#region Copyright (c) Pixeval/Pixeval
// GPL v3 License
// 
// Pixeval/Pixeval
// Copyright (c) 2023 Pixeval/IOHelper.Imaging.cs
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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Microsoft.UI.Xaml.Media.Imaging;
using Pixeval.CoreApi.Net.Response;
using Pixeval.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using WinUI3Utilities;
using QRCoder;
using SixLabors.ImageSharp.Metadata;

namespace Pixeval.Util.IO;

public static partial class IoHelper
{
    public static async Task<SoftwareBitmapSource> GetSoftwareBitmapSourceAsync(this Stream stream, bool disposeOfImageStream)
    {
        // 此处Position可能为负数
        stream.Position = 0;

        var bitmap = await GetSoftwareBitmapFromStreamAsync(stream);
        if (disposeOfImageStream)
            await stream.DisposeAsync();
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(bitmap);
        return source;
    }

    public static async Task<BitmapImage> GetBitmapImageAsync(this Stream imageStream, bool disposeOfImageStream, int? desiredWidth = null)
    {
        var bitmapImage = new BitmapImage
        {
            DecodePixelType = DecodePixelType.Logical
        };
        if (desiredWidth is { } width)
            bitmapImage.DecodePixelWidth = width;
        await bitmapImage.SetSourceAsync(imageStream.AsRandomAccessStream());
        if (disposeOfImageStream)
            await imageStream.DisposeAsync();

        return bitmapImage;
    }

    /// <summary>
    /// Decodes the <paramref name="stream" /> to a <see cref="SoftwareBitmap" />
    /// </summary>
    public static async Task<SoftwareBitmap> GetSoftwareBitmapFromStreamAsync(Stream stream)
    {
        using var image = await Image.LoadAsync<Bgra32>(stream);
        var softwareBitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, image.Width, image.Height, BitmapAlphaMode.Premultiplied);
        var buffer = new byte[4 * image.Width * image.Height];
        image.CopyPixelDataTo(buffer);
        softwareBitmap.CopyFromBuffer(buffer.AsBuffer());
        return softwareBitmap;
        // BitmapDecoder Bug多
        // var decoder = await BitmapDecoder.CreateAsync(imageStream);
        // return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }

    /// <summary>
    /// Decodes the <paramref name="bytes" /> to a <see cref="SoftwareBitmap" />
    /// </summary>
    public static SoftwareBitmap CreateSoftwareBitmapFromBytes(byte[] bytes)
    {
        var image = Image.Identify(bytes);
        var softwareBitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, image.Width, image.Height, BitmapAlphaMode.Premultiplied);
        softwareBitmap.CopyFromBuffer(bytes.AsBuffer());
        return softwareBitmap;
    }

    public static SoftwareBitmap ToSoftwareBitmap(this Image<Bgra32> image)
    {
        var softwareBitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, image.Width, image.Height, BitmapAlphaMode.Premultiplied);
        var buffer = GC.AllocateUninitializedArray<byte>(image.Height * image.Width * 4);
        image.CopyPixelDataTo(buffer);
        softwareBitmap.CopyFromBuffer(buffer.AsBuffer());
        return softwareBitmap;
    }

    public static byte[] ToBytes(this SoftwareBitmap bitmap)
    {
        var buffer = GC.AllocateUninitializedArray<byte>(bitmap.PixelWidth * bitmap.PixelHeight * 4);
        bitmap.CopyToBuffer(buffer.AsBuffer());
        return buffer;
    }

    public static async Task<SoftwareBitmapSource> ToSourceAsync(this SoftwareBitmap bitmap)
    {
        var source = new SoftwareBitmapSource();
        await source.SetBitmapAsync(bitmap);
        return source;
    }

    public static async Task<Stream> GetFileThumbnailAsync(string path, uint size = 64)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, size);
        return thumbnail.AsStreamForRead();
    }

    public static async Task UgoiraSaveToFileAsync(this Image image, string path, UgoiraDownloadFormat? ugoiraDownloadFormat = null)
    {
        CreateParentDirectories(path);
        await using var fileStream = File.OpenWrite(path);
        _ = await UgoiraSaveToStreamAsync(image, fileStream, ugoiraDownloadFormat);
    }

    public static async Task<T> UgoiraSaveToStreamAsync<T>(this Image image, T destination, UgoiraDownloadFormat? ugoiraDownloadFormat = null) where T : Stream
    {
        return await Task.Run(async () =>
        {
            ugoiraDownloadFormat ??= App.AppViewModel.AppSettings.UgoiraDownloadFormat;
            await image.SaveAsync(destination,
                ugoiraDownloadFormat switch
                {
                    UgoiraDownloadFormat.Tiff => new TiffEncoder(),
                    UgoiraDownloadFormat.APng => new PngEncoder(),
                    UgoiraDownloadFormat.Gif => new GifEncoder(),
                    UgoiraDownloadFormat.WebPLossless => new WebpEncoder { FileFormat = WebpFileFormatType.Lossless },
                    UgoiraDownloadFormat.WebPLossy => new WebpEncoder { FileFormat = WebpFileFormatType.Lossy },
                    _ => ThrowHelper.ArgumentOutOfRange<UgoiraDownloadFormat?, IImageEncoder>(ugoiraDownloadFormat)
                });
            image.Dispose();
            destination.Position = 0;
            return destination;
        });
    }

    public static Image<Bgra32> ToImage(this SoftwareBitmap bitmap)
    {
        var buffer =
            GC.AllocateUninitializedArray<byte>(bitmap.PixelHeight * bitmap.PixelWidth * 4);
        bitmap.CopyToBuffer(buffer.AsBuffer());
        return Image.LoadPixelData<Bgra32>(buffer, bitmap.PixelWidth, bitmap.PixelHeight);
    }

    public static Image<Bgra32> ToUgoiraImage(this IEnumerable<SoftwareBitmap> bitmaps, IEnumerable<int> delays)
    {
        Image<Bgra32>? image = null;
        foreach (var (bitmap, delay) in bitmaps.Zip(delays))
        {
            image ??= new Image<Bgra32>(bitmap.PixelWidth, bitmap.PixelHeight);
            var image1 = bitmap.ToImage();
            var frame = image1.Frames.RootFrame;
            frame.Metadata.GetGifMetadata().FrameDelay = delay;
            image.Frames.AddFrame(frame);
        }
        Debug.Assert(image is not null);
        return image;
    }



    public static async Task IllustrationSaveToFileAsync(this Image image, string path, IllustrationDownloadFormat? illustrationDownloadFormat = null)
    {
        CreateParentDirectories(path);
        await using var fileStream = File.OpenWrite(path);
        _ = await IllustrationSaveToStreamAsync(image, fileStream, illustrationDownloadFormat);
    }

    public static async Task<Stream> IllustrationSaveToStreamAsync(this Stream stream, Stream? target = null, IllustrationDownloadFormat? illustrationDownloadFormat = null)
    {
        using var image = await Image.LoadAsync(stream);
        return await IllustrationSaveToStreamAsync(image, target ?? _recyclableMemoryStreamManager.GetStream(), illustrationDownloadFormat);
    }

    public static async Task<T> IllustrationSaveToStreamAsync<T>(this Image image, T destination, IllustrationDownloadFormat? illustrationDownloadFormat = null) where T : Stream
    {
        return await Task.Run(async () =>
        {
            illustrationDownloadFormat ??= App.AppViewModel.AppSettings.IllustrationDownloadFormat;
            await image.SaveAsync(destination,
                illustrationDownloadFormat switch
                {
                    IllustrationDownloadFormat.Jpg => new JpegEncoder(),
                    IllustrationDownloadFormat.Png => new PngEncoder(),
                    IllustrationDownloadFormat.Bmp => new BmpEncoder(),
                    IllustrationDownloadFormat.WebPLossless => new WebpEncoder { FileFormat = WebpFileFormatType.Lossless },
                    IllustrationDownloadFormat.WebPLossy => new WebpEncoder { FileFormat = WebpFileFormatType.Lossy },
                    _ => ThrowHelper.ArgumentOutOfRange<IllustrationDownloadFormat?, IImageEncoder>(illustrationDownloadFormat)
                });
            image.Dispose();
            destination.Position = 0;
            return destination;
        });
    }

    public static string GetUgoiraExtension(UgoiraDownloadFormat? ugoiraDownloadFormat = null)
    {
        ugoiraDownloadFormat ??= App.AppViewModel.AppSettings.UgoiraDownloadFormat;
        return ugoiraDownloadFormat switch
        {
            UgoiraDownloadFormat.Tiff or UgoiraDownloadFormat.APng or UgoiraDownloadFormat.Gif => "." + ugoiraDownloadFormat.ToString()!.ToLower(),
            UgoiraDownloadFormat.WebPLossless or UgoiraDownloadFormat.WebPLossy => ".webp",
            _ => ThrowHelper.ArgumentOutOfRange<UgoiraDownloadFormat?, string>(ugoiraDownloadFormat)
        };
    }

    public static string GetIllustrationExtension(IllustrationDownloadFormat? illustrationDownloadFormat = null)
    {
        illustrationDownloadFormat ??= App.AppViewModel.AppSettings.IllustrationDownloadFormat;
        return illustrationDownloadFormat switch
        {
            IllustrationDownloadFormat.Jpg or IllustrationDownloadFormat.Png or IllustrationDownloadFormat.Bmp => "." + illustrationDownloadFormat.ToString()!.ToLower(),
            IllustrationDownloadFormat.WebPLossless or IllustrationDownloadFormat.WebPLossy => ".webp",
            _ => ThrowHelper.ArgumentOutOfRange<IllustrationDownloadFormat?, string>(illustrationDownloadFormat)
        };
    }

    public static async Task<Image> GetImageFromZipStreamAsync(Stream zipStream, UgoiraMetadataResponse ugoiraMetadataResponse)
    {
        var bitmaps = await ReadSoftwareBitmapFromZipArchiveAsync(zipStream).ToArrayAsync();
        return bitmaps.ToUgoiraImage(ugoiraMetadataResponse.UgoiraMetadataInfo.Frames.Select(t => (int)t.Delay));
    }

    public static async Task<SoftwareBitmapSource> GenerateQrCodeForUrlAsync(string url)
    {
        var qrCodeGen = new QRCodeGenerator();
        var urlPayload = new PayloadGenerator.Url(url);
        var qrCodeData = qrCodeGen.CreateQrCode(urlPayload, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new BitmapByteQRCode(qrCodeData);
        var bytes = qrCode.GetGraphic(20);
        return await _recyclableMemoryStreamManager.GetStream(bytes).GetSoftwareBitmapSourceAsync(true);
    }

    public static async Task<SoftwareBitmapSource> GenerateQrCodeAsync(string content)
    {
        var qrCodeGen = new QRCodeGenerator();
        var qrCodeData = qrCodeGen.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new BitmapByteQRCode(qrCodeData);
        var bytes = qrCode.GetGraphic(20);
        return await _recyclableMemoryStreamManager.GetStream(bytes).GetSoftwareBitmapSourceAsync(true);
    }
}
