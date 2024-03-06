#region Copyright (c) Pixeval/Pixeval
// GPL v3 License
// 
// Pixeval/Pixeval
// Copyright (c) 2023 Pixeval/IOHelper.cs
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
#endregion

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage;
using Windows.Storage.Streams;
using Pixeval.Download.Models;
using Pixeval.Utilities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace Pixeval.Util.IO;

public static partial class IoHelper
{
    public static async Task<string> Sha1Async(this Stream stream)
    {
        using var sha1 = SHA1.Create();
        var result = await sha1.ComputeHashAsync(stream);
        stream.Position = 0; // reset the stream
        return result.Select(b => b.ToString("X2")).Aggregate((acc, str) => acc + str);
    }

    public static string NormalizePath(string path)
    {
        return Path.GetFullPath(Path.GetInvalidPathChars().Aggregate(path, (s, c) => s.Replace(c.ToString(), string.Empty)));
    }

    public static string NormalizePathSegment(string path)
    {
        return Path.GetInvalidFileNameChars().Aggregate(path, (s, c) => s.Replace(c.ToString(), string.Empty));
    }

    public static void CreateParentDirectories(string fullPath)
    {
        var directory = Path.GetDirectoryName(fullPath);
        _ = Directory.CreateDirectory(directory!);
    }

    public static async Task ClearDirectoryAsync(this StorageFolder dir)
    {
        await Task.WhenAll((await dir.GetItemsAsync()).Select(f => f.DeleteAsync().AsTask()));
    }

    public static async Task<IRandomAccessStream> GetRandomAccessStreamFromByteArrayAsync(byte[] byteArray)
    {
        var stream = new InMemoryRandomAccessStream();
        using var dataWriter = new DataWriter(stream.GetOutputStreamAt(0));
        dataWriter.WriteBytes(byteArray);
        _ = await dataWriter.StoreAsync();
        _ = dataWriter.DetachStream();
        return stream;
    }

    public static async Task<IImageFormat> DetectImageFormat(this IRandomAccessStream randomAccessStream)
    {
        await using var stream = randomAccessStream.AsStream();
        return await Image.DetectFormatAsync(stream);
    }

    public static async Task<string> ToBase64StringAsync(this IRandomAccessStream randomAccessStream)
    {
        var array = ArrayPool<byte>.Shared.Rent((int)randomAccessStream.Size);
        var buffer = await randomAccessStream.ReadAsync(array.AsBuffer(), (uint)randomAccessStream.Size, InputStreamOptions.None);
        ArrayPool<byte>.Shared.Return(array);
        return Convert.ToBase64String(buffer.ToArray());
    }

    public static async Task<string> GenerateBase64UrlForImageAsync(this IRandomAccessStream randomAccessStream)
    {
        var base64Str = await randomAccessStream.ToBase64StringAsync();
        var format = await randomAccessStream.DetectImageFormat();
        return $"data:image/{format?.Name.ToLower()},{base64Str}";
    }

    public static async Task WriteBytesAsync(this Stream stream, byte[] bytes)
    {
        await stream.WriteAsync(bytes);
    }

    public static async Task WriteBytesAsync(this StorageStreamTransaction storageStreamTransaction, byte[] bytes)
    {
        _ = await storageStreamTransaction.Stream.WriteAsync(CryptographicBuffer.CreateFromByteArray(bytes));
    }

    public static IAsyncAction WriteStringAsync(this StorageFile storageFile, string str)
    {
        return storageFile.WriteBytesAsync(str.GetBytes());
    }

    public static IAsyncAction WriteBytesAsync(this StorageFile storageFile, byte[] bytes)
    {
        return FileIO.WriteBytesAsync(storageFile, bytes);
    }

    public static async Task<StorageFile> GetOrCreateFileAsync(this StorageFolder folder, string itemName)
    {
        return await folder.TryGetItemAsync(itemName) as StorageFile ?? await folder.CreateFileAsync(itemName, CreationCollisionOption.ReplaceExisting);
    }

    public static async Task<StorageFolder> GetOrCreateFolderAsync(this StorageFolder folder, string folderName)
    {
        return await folder.TryGetItemAsync(folderName) as StorageFolder ?? await folder.CreateFolderAsync(folderName, CreationCollisionOption.ReplaceExisting);
    }

    public static async Task<string?> ReadStringAsync(this StorageFile storageFile, Encoding? encoding = null)
    {
        return (await storageFile.ReadBytesAsync())?.GetString(encoding);
    }

    public static async Task<byte[]?> ReadBytesAsync(this StorageFile? file)
    {
        if (file is null)
        {
            return null;
        }

        using var stream = await file.OpenReadAsync();
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        _ = await reader.LoadAsync((uint)stream.Size);
        var bytes = new byte[stream.Size];
        reader.ReadBytes(bytes);
        return bytes;
    }

    public static Task<HttpResponseMessage> PostFormAsync(this HttpClient httpClient, string url, params (string? Key, string? Value)[] parameters)
    {
        var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(parameters.Select(tuple => new KeyValuePair<string?, string?>(tuple.Key, tuple.Value)))
            {
                Headers =
                {
                    ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded")
                }
            }
        };
        return httpClient.SendAsync(httpRequestMessage);
    }

    public static async IAsyncEnumerable<SoftwareBitmap> ReadSoftwareBitmapFromZipArchiveAsync(Stream zipStream)
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        foreach (var zipArchiveEntry in archive.Entries)
        {
            await using var stream = zipArchiveEntry.Open();
            var bytes = GC.AllocateUninitializedArray<byte>((int)stream.Length);
            yield return CreateSoftwareBitmapFromBytes(bytes);
        }
    }

    public static async Task SaveToFileAsync(this IRandomAccessStream stream, StorageFile file)
    {
        stream.Seek(0);
        await stream.AsStreamForRead().CopyToAsync(await file.OpenStreamForWriteAsync());
    }

    public static async Task DeleteIllustrationTaskAsync(IllustrationDownloadTaskBase task)
    {
        try
        {
            if (task is MangaDownloadTask)
                await (await StorageFolder.GetFolderFromPathAsync(Path.GetDirectoryName(task.Destination))).DeleteAsync(StorageDeleteOption.Default);
            else
                await (await StorageFile.GetFileFromPathAsync(task.Destination)).DeleteAsync(StorageDeleteOption.Default);
        }
        catch
        {
            // ignored
        }
    }
}
