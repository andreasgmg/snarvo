// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using Microsoft.Graphics.Canvas;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace Snarvo.Services;

/// <summary>
/// Turns part of a video (e.g. a Snipping Tool screen recording) into a looping GIF.
/// Frames come straight from the decoder via a frame-server MediaPlayer, so they are exact
/// (the MediaComposition thumbnail API returns lossy JPEGs, whose noise bloats a GIF).
/// </summary>
public sealed class GifMaker : IDisposable
{
    static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(3);

    readonly MediaPlayer _player;
    readonly CanvasRenderTarget _frame;
    TaskCompletionSource<bool>? _waiting;

    GifMaker(MediaPlayer player, int width, int height, TimeSpan duration)
    {
        _player = player;
        Width = width;
        Height = height;
        Duration = duration;
        _frame = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), width, height, 96);
        _player.VideoFrameAvailable += (_, _) => _waiting?.TrySetResult(true);
    }

    public int Width { get; }
    public int Height { get; }
    public TimeSpan Duration { get; }

    public static int FrameCount(TimeSpan start, TimeSpan end, int fps) =>
        Math.Max(1, (int)Math.Floor((end - start).TotalSeconds * fps));

    public static async Task<GifMaker> OpenAsync(StorageFile video)
    {
        var player = new MediaPlayer
        {
            IsVideoFrameServerEnabled = true,
            IsMuted = true,
            AutoPlay = false,
        };
        var opened = new TaskCompletionSource<bool>();
        player.MediaOpened += (_, _) => opened.TrySetResult(true);
        player.MediaFailed += (_, e) => opened.TrySetException(new InvalidOperationException(e.ErrorMessage));
        player.Source = MediaSource.CreateFromStorageFile(video);
        await opened.Task;

        var session = player.PlaybackSession;
        return new GifMaker(player, (int)session.NaturalVideoWidth, (int)session.NaturalVideoHeight, session.NaturalDuration);
    }

    /// <summary>Decodes the frame shown at <paramref name="time"/> into the frame buffer.</summary>
    async Task SeekAsync(TimeSpan time)
    {
        _waiting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _player.PlaybackSession.Position = time;
        await Task.WhenAny(_waiting.Task, Task.Delay(FrameTimeout));
        _player.CopyFrameToVideoSurface(_frame);
    }

    public async Task CreateGifAsync(TimeSpan start, TimeSpan end, int fps, int maxWidth, string outputPath,
        IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        double scale = Math.Min(1.0, (double)maxWidth / Width);
        int width = Math.Max(2, (int)Math.Round(Width * scale));
        int height = Math.Max(2, (int)Math.Round(Height * scale));
        int frames = FrameCount(start, end, fps);

        var device = CanvasDevice.GetSharedDevice();
        using var scaled = new CanvasRenderTarget(device, width, height, 96);
        await using var file = File.Create(outputPath);
        var gif = new GifWriter(file, width, height);

        for (int i = 0; i < frames; i++)
        {
            cancel.ThrowIfCancellationRequested();
            await SeekAsync(start + TimeSpan.FromSeconds(i / (double)fps));

            using (var ds = scaled.CreateDrawingSession())
            {
                ds.DrawImage(_frame, new Rect(0, 0, width, height), _frame.Bounds, 1, CanvasImageInterpolation.HighQualityCubic);
            }
            // GIF delays are in 1/100 s; spread the rounding so the total length stays right.
            int delay = (int)(Math.Round((i + 1) * 100.0 / fps) - Math.Round(i * 100.0 / fps));
            var pixels = scaled.GetPixelBytes();
            await Task.Run(() => gif.AddFrame(pixels, delay), cancel);
            progress?.Report((i + 1) / (double)frames);
        }
        await Task.Run(gif.Finish, cancel);
    }

    public void Dispose()
    {
        _player.Dispose();
        _frame.Dispose();
    }
}
