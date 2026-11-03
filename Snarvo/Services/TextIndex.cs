// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Numerics;
using Microsoft.Graphics.Canvas;

namespace Snarvo.Services;

/// <summary>
/// Runs OCR on library images in the background and stores the text next to them, so the
/// library can be searched. An image is (re)indexed when its rendered result is newer than its text.
/// </summary>
public static class TextIndex
{
    static bool _running;
    static bool _runAgain;

    /// <summary>Raised on the UI thread with the id of an image whose text was (re)indexed.</summary>
    public static event Action<string>? Indexed;

    public static string Read(string id)
    {
        try
        {
            return File.Exists(Library.TextPath(id)) ? File.ReadAllText(Library.TextPath(id)) : "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Indexes everything that needs it. Calls while running schedule one more pass.</summary>
    public static async void Update()
    {
        if (_running)
        {
            _runAgain = true;
            return;
        }
        _running = true;
        try
        {
            do
            {
                _runAgain = false;
                foreach (var id in Library.List())
                {
                    if (!NeedsIndexing(id)) continue;
                    if (!await IndexAsync(id)) return; // no OCR language installed
                }
            }
            while (_runAgain);
        }
        finally
        {
            _running = false;
        }
    }

    static bool NeedsIndexing(string id)
    {
        var image = Library.OutputPath(id);
        var text = Library.TextPath(id);
        return File.Exists(image) && (!File.Exists(text) || File.GetLastWriteTimeUtc(text) < File.GetLastWriteTimeUtc(image));
    }

    static async Task<bool> IndexAsync(string id)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(Library.OutputPath(id));
            using var stream = new MemoryStream(bytes).AsRandomAccessStream();
            using var bitmap = await CanvasBitmap.LoadAsync(CanvasDevice.GetSharedDevice(), stream, 96);
            var size = new Vector2((float)bitmap.Size.Width, (float)bitmap.Size.Height);
            var recognized = await TextRecognition.RecognizeAsync(size, ds => ds.DrawImage(bitmap));
            if (recognized is null) return false;

            // The image may have been deleted while OCR was running.
            if (!File.Exists(Library.OriginalPath(id))) return true;
            await File.WriteAllTextAsync(Library.TextPath(id), recognized.Text);
            Indexed?.Invoke(id);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
        return true;
    }
}
