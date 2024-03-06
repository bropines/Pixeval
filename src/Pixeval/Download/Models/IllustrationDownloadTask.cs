#region Copyright (c) Pixeval/Pixeval
// GPL v3 License
// 
// Pixeval/Pixeval
// Copyright (c) 2023 Pixeval/IllustrationDownloadTask.cs
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
using System.IO;
using System.Threading.Tasks;
using Pixeval.Controls;
using Pixeval.CoreApi.Net.Response;
using Pixeval.Database;
using Pixeval.Util;
using Pixeval.Util.IO;
using Pixeval.Utilities;
using Pixeval.Utilities.Threading;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Pixeval.Download.Models;

public class IllustrationDownloadTask(DownloadHistoryEntry entry, IllustrationItemViewModel illustration)
    : IllustrationDownloadTaskBase(entry)
{
    public string Url => Urls[0];

    public IllustrationItemViewModel IllustrationViewModel { get; protected set; } = illustration;

    public override async Task DownloadAsync(Func<string, IProgress<double>?, CancellationHandle?, Task<Result<Image<Bgra32>>>> downloadImageAsync)
    {
        await DownloadAsyncCore(downloadImageAsync, Url, Destination);
    }

    protected virtual async Task DownloadAsyncCore(Func<string, IProgress<double>?, CancellationHandle?, Task<Result<Image<Bgra32>>>> downloadImageAsync, string url, string destination)
    {
        if (!App.AppViewModel.AppSettings.OverwriteDownloadedFile && File.Exists(destination))
            return;

        if (App.AppViewModel.AppSettings.UseFileCache && App.AppViewModel.Cache.TryGet(await IllustrationViewModel.GetIllustrationOriginalImageCacheKeyAsync(), out var image))
        {

            await SaveImageAsync(image, destination);
            return;
        }

        if (await downloadImageAsync(url, this, CancellationHandle) is Result<Image<Bgra32>>.Success result)
        {
            using var image1 = result.Value;
            await SaveImageAsync(image1, destination);
        }
    }

    protected virtual async Task SaveImageAsync(Image<Bgra32> image, string destination)
    {
        image.SetTags(IllustrationViewModel.Illustrate);
        await image.IllustrationSaveToFileAsync(destination);
    }
}
