#region Copyright (c) Pixeval/Pixeval
// GPL v3 License
// 
// Pixeval/Pixeval
// Copyright (c) 2023 Pixeval/ImageViewerPageViewModel.cs
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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.System;
using Windows.System.UserProfile;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Pixeval.Attributes;
using Pixeval.Controls;
using Pixeval.Controls.MarkupExtensions;
using Pixeval.Database;
using Pixeval.Database.Managers;
using Pixeval.Util;
using Pixeval.Util.IO;
using Pixeval.Util.UI;
using Pixeval.Utilities;
using Pixeval.Utilities.Threading;
using Pixeval.AppManagement;
using Pixeval.CoreApi.Global.Enum;
using Pixeval.CoreApi.Net.Response;
using Pixeval.Download;
using Pixeval.Util.ComponentModels;

namespace Pixeval.Pages.IllustrationViewer;

public partial class ImageViewerPageViewModel : UiObservableObject, IDisposable
{
    public enum LoadingPhase
    {
        [LocalizedResource(typeof(ImageViewerPageResources), nameof(ImageViewerPageResources.CheckingCache))]
        CheckingCache,

        [LocalizedResource(typeof(ImageViewerPageResources), nameof(ImageViewerPageResources.LoadingFromCache))]
        LoadingFromCache,

        [LocalizedResource(typeof(ImageViewerPageResources), nameof(ImageViewerPageResources.DownloadingUgoiraZipFormatted), DownloadingUgoiraZip)]
        DownloadingUgoiraZip,

        [LocalizedResource(typeof(ImageViewerPageResources), nameof(ImageViewerPageResources.MergingUgoiraFrames))]
        MergingUgoiraFrames,

        [LocalizedResource(typeof(ImageViewerPageResources), nameof(ImageViewerPageResources.DownloadingImageFormatted), DownloadingImage)]
        DownloadingImage,

        [LocalizedResource(typeof(ImageViewerPageResources), nameof(ImageViewerPageResources.LoadingImage))]
        LoadingImage
    }

    private bool _disposed;

    [ObservableProperty]
    private bool _isMirrored;

    [ObservableProperty]
    private bool _isPlaying = true;

    [ObservableProperty]
    private double _loadingProgress;

    [ObservableProperty]
    private string? _loadingText;

    [ObservableProperty]
    private IReadOnlyList<int>? _msIntervals;

    [ObservableProperty]
    private IReadOnlyList<Stream>? _originalImageSources;

    [ObservableProperty]
    private int _rotationDegree;

    [ObservableProperty]
    private float _scale = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFit))]
    private ZoomableImageMode _showMode;

    /// <summary>
    /// <see cref="ShowMode"/> is <see cref="ZoomableImageMode.Fit"/> or not
    /// </summary>
    public bool IsFit => ShowMode is ZoomableImageMode.Fit;

    private bool _loadSuccessfully;

    public bool LoadSuccessfully
    {
        get => _loadSuccessfully;
        private set
        {
            if (value == _loadSuccessfully)
                return;
            _loadSuccessfully = value;
            OnPropertyChanged();
        }
    }

    public ImageViewerPageViewModel(IllustrationItemViewModel illustrationViewModel, FrameworkElement frameworkElement) : base(frameworkElement)
    {
        IllustrationViewModel = illustrationViewModel;
        _ = LoadImage();

        InitializeCommands();
        // 此时IsPlaying为true，IsFit为true
        PlayGifCommand.GetPlayCommand(true);
        RestoreResolutionCommand.GetResolutionCommand(true);
    }

    public async Task<Stream?> GetOriginalImageSourceAsync(IProgress<int>? progress = null, Stream? destination = null)
    {
        if (OriginalImageSources is null)
            return destination;

        if (IllustrationViewModel.IsUgoira)
            return await OriginalImageSources.UgoiraSaveToStreamAsync(MsIntervals ?? [], destination, progress);

        if (OriginalImageSources is [var stream, ..])
        {
            stream.Position = 0;
            return stream;
        }

        return destination;
    }

    public async Task<StorageFile> SaveToFolderAsync(AppKnownFolders appKnownFolder)
    {
        var name = Path.GetFileName(App.AppViewModel.AppSettings.DefaultDownloadPathMacro);
        var path = IoHelper.NormalizePathSegment(new IllustrationMetaPathParser().Reduce(name, IllustrationViewModel));
        var file = await appKnownFolder.CreateFileAsync(path);
        await using var target = await file.OpenStreamForWriteAsync();
        _ = await GetOriginalImageSourceAsync(null, target);
        return file;
    }

    public CancellationHandle ImageLoadingCancellationHandle { get; } = new();

    public IllustrationItemViewModel IllustrationViewModel { get; }

    public void Dispose()
    {
        _disposed = true;
        DisposeInternal();
        GC.SuppressFinalize(this);
    }

    private void AdvancePhase(LoadingPhase phase)
    {
        LoadingText = phase.GetLocalizedResource() switch
        {
            { FormatKey: LoadingPhase } attr => attr.GetLocalizedResourceContent()?.Format((int)LoadingProgress),
            var attr => attr?.GetLocalizedResourceContent()
        };
    }

    private async Task LoadImage()
    {
        if (!LoadSuccessfully || _disposed)
        {
            _disposed = false;
            _ = LoadThumbnailAsync();
            AddHistory();
            await LoadOriginalImageAsync();
            IllustrationViewModel.UnloadThumbnail(this);
        }

        return;

        async Task LoadThumbnailAsync()
        {
            _ = await IllustrationViewModel.TryLoadThumbnailAsync(this);
            OriginalImageSources ??= [IllustrationViewModel.ThumbnailStream!];
        }

        void AddHistory()
        {
            using var scope = App.AppViewModel.AppServicesScope;
            var manager = scope.ServiceProvider.GetRequiredService<BrowseHistoryPersistentManager>();
            _ = manager.Delete(x => x.Id == IllustrationViewModel.Id && x.Type == SimpleWorkType.IllustAndManga);
            manager.Insert(new BrowseHistoryEntry { Id = IllustrationViewModel.Id, Type = SimpleWorkType.IllustAndManga });
        }

        async Task<Stream?> GetStreamAsync((UgoiraMetadataResponse Metadata, string UgoiraUrl)? ugoiraParameter)
        {
            var cacheKey = await IllustrationViewModel.GetIllustrationOriginalImageCacheKeyAsync();

            if (App.AppViewModel.AppSettings.UseFileCache && await App.AppViewModel.Cache.ExistsAsync(cacheKey))
            {
                AdvancePhase(LoadingPhase.LoadingFromCache);

                // 就算已经有该Key，只要取不出值，就重新加载
                if (await App.AppViewModel.Cache.TryGetAsync<Stream>(cacheKey) is { } stream)
                    return stream;
            }
            if (ugoiraParameter is { Metadata: var metadata, UgoiraUrl: var ugoiraUrl })
            {
                var downloadRes = await App.AppViewModel.MakoClient.DownloadStreamAsync(ugoiraUrl, new Progress<double>(d =>
                {
                    LoadingProgress = d;
                    AdvancePhase(LoadingPhase.DownloadingUgoiraZip);
                }), ImageLoadingCancellationHandle);
                if (downloadRes is Result<Stream>.Success(var zipStream))
                {
                    if (App.AppViewModel.AppSettings.UseFileCache)
                    {
                        await App.AppViewModel.Cache.AddAsync(cacheKey, zipStream, TimeSpan.FromDays(1));
                        zipStream.Position = 0;
                    }
                    return zipStream;
                }
            }
            else
            {
                var url = IllustrationViewModel.OriginalStaticUrl!;
                AdvancePhase(LoadingPhase.DownloadingImage);
                var result = await App.AppViewModel.MakoClient.DownloadStreamAsync(url, new Progress<double>(d =>
                {
                    LoadingProgress = d;
                    AdvancePhase(LoadingPhase.DownloadingImage);
                }), ImageLoadingCancellationHandle);
                if (result is Result<Stream>.Success(var s))
                {
                    if (App.AppViewModel.AppSettings.UseFileCache)
                    {
                        await App.AppViewModel.Cache.AddAsync(cacheKey, s, TimeSpan.FromDays(1));
                        s.Position = 0;
                    }
                    return s;
                }
            }

            return null;
        }

        async Task LoadOriginalImageAsync()
        {
            var ugoiraParameter = null as (UgoiraMetadataResponse Metadata, string Url)?;
            if (IllustrationViewModel.IsUgoira)
                ugoiraParameter = await IllustrationViewModel.GetUgoiraOriginalUrlAsync();

            var stream = await GetStreamAsync(ugoiraParameter);
            if (stream is not null)
            {
                if (IllustrationViewModel.IsUgoira)
                {
                    AdvancePhase(LoadingPhase.MergingUgoiraFrames);
                    OriginalImageSources = await IoHelper.ReadZipArchiveEntriesAsync(stream, true);
                    MsIntervals = ugoiraParameter!.Value.Metadata.UgoiraMetadataInfo.Frames.Select(x => (int)x.Delay).ToArray();
                }
                else
                {
                    OriginalImageSources = [stream];
                }
            }

            LoadSuccessfully = true;

            if (OriginalImageSources is not null && !_disposed)
                UpdateCommandCanExecute();
        }
    }

    private void PlayGifCommandOnExecuteRequested(XamlUICommand sender, ExecuteRequestedEventArgs args)
    {
        IsPlaying = !IsPlaying;
        PlayGifCommand.GetPlayCommand(IsPlaying);
    }

    public void RestoreResolutionCommandOnExecuteRequested(XamlUICommand sender, ExecuteRequestedEventArgs args)
    {
        ShowMode = IsFit ? ZoomableImageMode.Original : ZoomableImageMode.Fit;
        RestoreResolutionCommand.GetResolutionCommand(IsFit);
    }

    private void SetAsBackgroundCommandOnExecuteRequested(XamlUICommand sender, ExecuteRequestedEventArgs args)
    {
        SetPersonalization(UserProfilePersonalizationSettings.Current.TrySetWallpaperImageAsync);
    }

    private void SetAsLockScreenCommandOnExecuteRequested(XamlUICommand sender, ExecuteRequestedEventArgs args)
    {
        SetPersonalization(UserProfilePersonalizationSettings.Current.TrySetLockScreenImageAsync);
    }

    private async void SetPersonalization(Func<StorageFile, IAsyncOperation<bool>> operation)
    {
        if (OriginalImageSources is not [not null, ..])
            return;

        var file = await SaveToFolderAsync(AppKnownFolders.SavedWallPaper);
        _ = await operation(file);

        ToastNotificationHelper.ShowTextToastNotification(
            EntryViewerPageResources.SetAsSucceededTitle,
            EntryViewerPageResources.SetAsBackgroundSucceededTitle);
    }

    private void ShareCommandExecuteRequested(XamlUICommand sender, ExecuteRequestedEventArgs args)
    {
        Window.ShowShareUi();
    }

    private void InitializeCommands()
    {
        SaveCommand.CanExecuteRequested += LoadingCompletedCanExecuteRequested;
        SaveCommand.ExecuteRequested += (_, _) => IllustrationViewModel.SaveCommand.Execute(DownloadParameter);

        SaveAsCommand.CanExecuteRequested += LoadingCompletedCanExecuteRequested;
        SaveAsCommand.ExecuteRequested += (_, _) => IllustrationViewModel.SaveAsCommand.Execute((Window, (Func<IProgress<int>?, Task<Stream?>>)(p => GetOriginalImageSourceAsync(p))));

        CopyCommand.CanExecuteRequested += LoadingCompletedCanExecuteRequested;
        CopyCommand.ExecuteRequested += (_, _) => IllustrationViewModel.CopyCommand.Execute(DownloadParameter);

        PlayGifCommand.CanExecuteRequested += (_, e) => e.CanExecute = IllustrationViewModel.IsUgoira && LoadSuccessfully;
        PlayGifCommand.ExecuteRequested += PlayGifCommandOnExecuteRequested;

        // 相当于鼠标滚轮滚动10次，方便快速缩放
        ZoomOutCommand.CanExecuteRequested += LoadingCompletedCanExecuteRequested;
        ZoomOutCommand.ExecuteRequested += (_, _) => Scale = ZoomableImage.Zoom(-1200, Scale);

        ZoomInCommand.CanExecuteRequested += LoadingCompletedCanExecuteRequested;
        ZoomInCommand.ExecuteRequested += (_, _) => Scale = ZoomableImage.Zoom(1200, Scale);

        RotateClockwiseCommand.CanExecuteRequested += LoadingCompletedCanExecuteRequested;
        RotateClockwiseCommand.ExecuteRequested += (_, _) => RotationDegree += 90;

        RotateCounterclockwiseCommand.CanExecuteRequested += LoadingCompletedCanExecuteRequested;
        RotateCounterclockwiseCommand.ExecuteRequested += (_, _) => RotationDegree -= 90;

        MirrorCommand.CanExecuteRequested += LoadingCompletedCanExecuteRequested;
        MirrorCommand.ExecuteRequested += (_, _) => IsMirrored = !IsMirrored;

        RestoreResolutionCommand.CanExecuteRequested += LoadingCompletedCanExecuteRequested;
        RestoreResolutionCommand.ExecuteRequested += RestoreResolutionCommandOnExecuteRequested;

        ShareCommand.CanExecuteRequested += LoadingCompletedCanExecuteRequested;
        ShareCommand.ExecuteRequested += ShareCommandExecuteRequested;

        SetAsLockScreenCommand.CanExecuteRequested += IsNotUgoiraAndLoadingCompletedCanExecuteRequested;
        SetAsLockScreenCommand.ExecuteRequested += SetAsLockScreenCommandOnExecuteRequested;

        SetAsBackgroundCommand.CanExecuteRequested += IsNotUgoiraAndLoadingCompletedCanExecuteRequested;
        SetAsBackgroundCommand.ExecuteRequested += SetAsBackgroundCommandOnExecuteRequested;
    }

    private void UpdateCommandCanExecute()
    {
        SaveCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        PlayGifCommand.NotifyCanExecuteChanged();
        RestoreResolutionCommand.NotifyCanExecuteChanged();
        ZoomInCommand.NotifyCanExecuteChanged();
        ZoomOutCommand.NotifyCanExecuteChanged();
        RotateClockwiseCommand.NotifyCanExecuteChanged();
        RotateCounterclockwiseCommand.NotifyCanExecuteChanged();
        MirrorCommand.NotifyCanExecuteChanged();
        ShareCommand.NotifyCanExecuteChanged();
    }

    private void LoadingCompletedCanExecuteRequested(XamlUICommand _, CanExecuteRequestedEventArgs args) => args.CanExecute = LoadSuccessfully;

    private void IsNotUgoiraAndLoadingCompletedCanExecuteRequested(XamlUICommand sender, CanExecuteRequestedEventArgs args) => args.CanExecute = !IllustrationViewModel.IsUgoira && LoadSuccessfully;

    public (FrameworkElement FrameworkElement, Func<IProgress<int>?, Task<Stream?>>) DownloadParameter => (FrameworkElement, p => GetOriginalImageSourceAsync(p));

    public XamlUICommand SaveCommand { get; } = EntryItemResources.Save.GetCommand(
        FontIconSymbol.SaveE74E, VirtualKeyModifiers.Control, VirtualKey.S);

    public XamlUICommand SaveAsCommand { get; } = EntryItemResources.SaveAs.GetCommand(
        FontIconSymbol.SaveAsE792, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, VirtualKey.S);

    public XamlUICommand CopyCommand { get; } = EntryItemResources.Copy.GetCommand(
        FontIconSymbol.CopyE8C8, VirtualKeyModifiers.Control, VirtualKey.C);

    public XamlUICommand PlayGifCommand { get; } = "".GetCommand(FontIconSymbol.StopE71A);

    public XamlUICommand ZoomOutCommand { get; } = EntryViewerPageResources.ZoomOut.GetCommand(
        FontIconSymbol.ZoomOutE71F, VirtualKey.Subtract);

    public XamlUICommand ZoomInCommand { get; } = EntryViewerPageResources.ZoomIn.GetCommand(
        FontIconSymbol.ZoomInE8A3, VirtualKey.Add);

    public XamlUICommand RotateClockwiseCommand { get; } = EntryViewerPageResources.RotateClockwise.GetCommand(
        FontIconSymbol.RotateE7AD, VirtualKeyModifiers.Control, VirtualKey.R);

    public XamlUICommand RotateCounterclockwiseCommand { get; } = EntryViewerPageResources.RotateCounterclockwise.GetCommand(
            null!, VirtualKeyModifiers.Control, VirtualKey.L);

    public XamlUICommand MirrorCommand { get; } = EntryViewerPageResources.Mirror.GetCommand(
            FontIconSymbol.CollatePortraitF57C, VirtualKeyModifiers.Control, VirtualKey.M);

    public XamlUICommand RestoreResolutionCommand { get; } = "".GetCommand(FontIconSymbol.WebcamE8B8);

    public StandardUICommand ShareCommand { get; } = new(StandardUICommandKind.Share);

    public XamlUICommand SetAsCommand { get; } = EntryViewerPageResources.SetAs.GetCommand(FontIconSymbol.PersonalizeE771);

    public XamlUICommand SetAsLockScreenCommand { get; } = new() { Label = EntryViewerPageResources.LockScreen };

    public XamlUICommand SetAsBackgroundCommand { get; } = new() { Label = EntryViewerPageResources.Background };

    private void DisposeInternal()
    {
        IllustrationViewModel.UnloadThumbnail(this);
        // if the loading task is null or hasn't been completed yet, the 
        // OriginalImageSources would be the thumbnail source, its disposal may 
        // cause the IllustrationGrid shows weird result such as an empty content
        var temp = OriginalImageSources;
        OriginalImageSources = null;

        if (LoadSuccessfully && temp is not null)
            foreach (var originalImageSource in temp)
            {
                originalImageSource?.Dispose();
            }

        LoadSuccessfully = false;
    }
}
