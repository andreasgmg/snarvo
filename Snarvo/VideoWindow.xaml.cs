// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Snarvo.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.Storage;
using Windows.Storage.Pickers;
using static Snarvo.Services.Localization;

namespace Snarvo;

/// <summary>Trims a screen recording and saves it as a looping GIF.</summary>
public sealed partial class VideoWindow : Window
{
    static readonly int[] FrameRates = [10, 15, 20, 30];
    static readonly int[] Widths = [480, 640, 800, 1280, int.MaxValue];

    readonly StorageFile _video;
    TimeSpan _duration, _start, _end;
    int _videoWidth, _videoHeight;
    string? _savedGif;
    bool _saving;

    public VideoWindow(StorageFile video)
    {
        _video = video;
        InitializeComponent();
        Title = F("Snarvo – GIF av {0}", video.Name);
        Localization.Translate(Root);
        WindowHelper.Prepare(this);

        foreach (var fps in FrameRates) FpsBox.Items.Add(F("{0} bilder/s", fps));
        foreach (var width in Widths) WidthBox.Items.Add(width == int.MaxValue ? "Originalstorlek" : $"{width} px");
        FpsBox.SelectedIndex = 1;
        WidthBox.SelectedIndex = 2;

        // Starting the media pipeline before the first frame is drawn leaves the window blank
        // until it gets input, so the video is loaded once the content is on screen.
        Root.Loaded += (_, _) =>
        {
            Player.Source = MediaSource.CreateFromStorageFile(video);
            Player.MediaPlayer.MediaOpened += (player, _) => DispatcherQueue.TryEnqueue(() =>
            {
                var session = player.PlaybackSession;
                _duration = session.NaturalDuration;
                _videoWidth = (int)session.NaturalVideoWidth;
                _videoHeight = (int)session.NaturalVideoHeight;
                _end = _duration;
                UpdateTexts();
            });
        };
        Closed += (_, _) => Player.MediaPlayer?.Pause();

        double scale = WindowHelper.Scale(this);
        WindowHelper.CenterAndResize(this, (int)(980 * scale), (int)(760 * scale));
        UpdateTexts();
    }

    int Fps => FrameRates[Math.Max(0, FpsBox.SelectedIndex)];
    int MaxWidth => Widths[Math.Max(0, WidthBox.SelectedIndex)];
    TimeSpan Position => Player.MediaPlayer.PlaybackSession.Position;

    static string Format(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds / 100}";

    void UpdateTexts()
    {
        if (_duration == TimeSpan.Zero)
        {
            TrimText.Text = T("Läser in videon …");
            return;
        }
        TrimText.Text = $"{Format(_start)} – {Format(_end)}  ({(_end - _start).TotalSeconds:0.0} s av {_duration.TotalSeconds:0.0} s)";

        int frames = GifMaker.FrameCount(_start, _end, Fps);
        double scale = Math.Min(1.0, (double)MaxWidth / Math.Max(1, _videoWidth));
        EstimateText.Text = F("{0} bildrutor, {1} × {2} px", frames, Math.Round(_videoWidth * scale), Math.Round(_videoHeight * scale));
    }

    void Settings_Changed(object sender, SelectionChangedEventArgs e) => UpdateTexts();

    void SetStart_Click(object sender, RoutedEventArgs e)
    {
        _start = Position;
        if (_start >= _end) _end = _duration;
        UpdateTexts();
    }

    void SetEnd_Click(object sender, RoutedEventArgs e)
    {
        if (Position <= _start)
        {
            StatusText.Text = T("Slutet måste ligga efter starten.");
            return;
        }
        _end = Position;
        UpdateTexts();
    }

    void ResetTrim_Click(object sender, RoutedEventArgs e)
    {
        _start = TimeSpan.Zero;
        _end = _duration;
        UpdateTexts();
    }

    async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_saving || _duration == TimeSpan.Zero) return;

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(_video.Name),
        };
        picker.FileTypeChoices.Add("GIF-animation", new List<string> { ".gif" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelper.Handle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        _saving = true;
        SaveButton.IsEnabled = false;
        Progress.Value = 0;
        Progress.Visibility = Visibility.Visible;
        StatusText.Text = T("Skapar GIF …");
        Player.MediaPlayer.Pause();
        try
        {
            using var maker = await GifMaker.OpenAsync(_video);
            var progress = new Progress<double>(p => Progress.Value = p);
            await maker.CreateGifAsync(_start, _end, Fps, MaxWidth, file.Path, progress);

            _savedGif = file.Path;
            StatusText.Text = F("Sparad: {0} ({1} KB)", file.Name, new FileInfo(file.Path).Length / 1024);
            CopyFileButton.IsEnabled = RevealButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            StatusText.Text = T("Kunde inte skapa GIF:en.");
        }
        finally
        {
            _saving = false;
            SaveButton.IsEnabled = true;
            Progress.Visibility = Visibility.Collapsed;
        }
    }

    async void CopyFile_Click(object sender, RoutedEventArgs e)
    {
        if (_savedGif is null) return;
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetStorageItems([await StorageFile.GetFileFromPathAsync(_savedGif)]);
        App.Instance?.IgnoreClipboardFor(TimeSpan.FromSeconds(2));
        Clipboard.SetContent(package);
        Clipboard.Flush();
        StatusText.Text = T("GIF:en är kopierad som fil.");
    }

    void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (_savedGif is not null) Process.Start("explorer.exe", $"/select,\"{_savedGif}\"");
    }

    /// <summary>Asks for a video (Snipping Tool saves recordings under Videos) and opens it.</summary>
    public static async Task PickAndOpenAsync(Window owner)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.VideosLibrary };
        foreach (var type in new[] { ".mp4", ".mov", ".wmv", ".avi", ".mkv" }) picker.FileTypeFilter.Add(type);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelper.Handle(owner));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        WindowHelper.BringToFront(new VideoWindow(file));
    }
}
