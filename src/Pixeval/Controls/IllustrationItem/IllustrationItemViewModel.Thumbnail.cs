#region Copyright

// GPL v3 License
// 
// Pixeval/Pixeval
// Copyright (c) 2024 Pixeval/IllustrationItemViewModel.Thumbnail.cs
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
using Windows.Graphics.Imaging;
using Microsoft.UI.Xaml.Media.Imaging;
using Pixeval.AppManagement;
using Pixeval.Util;
using Pixeval.Util.IO;
using Pixeval.Utilities;
using Pixeval.Utilities.Threading;

namespace Pixeval.Controls;

public partial class IllustrationItemViewModel
{
    /// <summary>
    /// 缩略图图片
    /// </summary>
    public SoftwareBitmapSource? ThumbnailSource => ThumbnailSourceRef?.Value;

    /// <summary>
    /// 缩略图文件流
    /// </summary>
    public SoftwareBitmap? Thumbnail { get; set; }

    private SharedRef<SoftwareBitmapSource>? _thumbnailSourceRef;

    public SharedRef<SoftwareBitmapSource>? ThumbnailSourceRef
    {
        get => _thumbnailSourceRef;
        set
        {
            if (Equals(_thumbnailSourceRef, value))
                return;
            _thumbnailSourceRef = value;
            OnPropertyChanged(nameof(ThumbnailSource));
        }
    }

    private CancellationHandle LoadingThumbnailCancellationHandle { get; } = new();

    /// <summary>
    /// 是否正在加载缩略图
    /// </summary>
    protected bool LoadingThumbnail { get; set; }

    /// <summary>
    /// 当控件需要显示图片时，调用此方法加载缩略图
    /// </summary>
    /// <param name="key">使用<see cref="IDisposable"/>对象，防止复用本对象的时候，本对象持有对<paramref name="key"/>的引用，导致<paramref name="key"/>无法释放</param>
    /// <returns>缩略图首次加载完成则返回<see langword="true"/>，之前已加载、正在加载或加载失败则返回<see langword="false"/></returns>
    public virtual async Task<bool> TryLoadThumbnailAsync(IDisposable key)
    {
        if (ThumbnailSourceRef is not null)
        {
            _ = ThumbnailSourceRef.MakeShared(key);

            // 之前已加载
            return false;
        }

        if (LoadingThumbnail)
        {
            // 已有别的线程正在加载缩略图
            return false;
        }

        LoadingThumbnail = true;
        if (App.AppViewModel.AppSettings.UseFileCache && App.AppViewModel.Cache.TryGet(await this.GetIllustrationThumbnailCacheKeyAsync(), out var image))
        {
            ThumbnailSourceRef = new SharedRef<SoftwareBitmapSource>(await image.ToSoftwareBitmap().ToSourceAsync(), key);

            // 读取缓存并加载完成
            LoadingThumbnail = false;
            OnPropertyChanged(nameof(ThumbnailSource));
            return true;
        }

        if (await GetThumbnailAsync() is { } s)
        {
            if (App.AppViewModel.AppSettings.UseFileCache)
            {
                App.AppViewModel.Cache.Add(await this.GetIllustrationThumbnailCacheKeyAsync(), s.ToImage(), TimeSpan.FromDays(10));
            }
            ThumbnailSourceRef = new SharedRef<SoftwareBitmapSource>(await s.ToSourceAsync(), key);

            // 获取并加载完成
            LoadingThumbnail = false;
            return true;
        }

        // 加载失败
        LoadingThumbnail = false;
        return false;
    }

    /// <summary>
    /// 当控件不显示，或者Unload时，调用此方法以尝试释放内存
    /// </summary>
    public void UnloadThumbnail(object key)
    {
        if (LoadingThumbnail)
        {
            LoadingThumbnailCancellationHandle.Cancel();
            LoadingThumbnail = false;
            return;
        }

        if (App.AppViewModel.AppSettings.UseFileCache)
            return;

        if (!ThumbnailSourceRef?.TryDispose(key) ?? true)
            return;

        ThumbnailSourceRef = null;
    }

    /// <summary>
    /// 直接获取对应缩略图
    /// </summary>
    public async Task<SoftwareBitmap?> GetThumbnailAsync()
    {

        if (Illustrate.GetThumbnailUrl() is { } url)
        {
            switch (await App.AppViewModel.MakoClient.DownloadStreamAsync(url, cancellationHandle: LoadingThumbnailCancellationHandle))
            {
                case Result<Stream>.Success(var stream):
                    return await IoHelper.GetSoftwareBitmapFromStreamAsync(stream);
                case Result<Stream>.Failure(OperationCanceledException):
                    LoadingThumbnailCancellationHandle.Reset();
                    return null;
            }
        }

        return await AppInfo.GetNotAvailableImageAsync();
    }

    /// <summary>
    /// 强制释放所有缩略图
    /// </summary>
    private void DisposeInternal()
    {
        ThumbnailSourceRef?.DisposeForce();
    }

    public override void Dispose()
    {
        DisposeInternal();
        GC.SuppressFinalize(this);
    }
}
