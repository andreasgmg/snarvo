// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Graphics.Canvas;
using Windows.Storage.Streams;

namespace Snarvo.Services;

/// <summary>
/// Every capture lives in the library folder as a small set of files sharing an id:
///   {id}.orig.png  – the untouched capture
///   {id}.base.png  – the capture after crop / cut-out (only when changed)
///   {id}.snip.json – annotations and frame effects, so edits stay editable
///   {id}.png       – the rendered result, used for thumbnails and sharing
///   {id}.txt       – text found in the result by OCR, for searching
/// </summary>
public static class Library
{
    const string IdFormat = "yyyyMMdd-HHmmss-fff";
    static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");
    static string? _lastHash;

    public static string Root { get; } = Path.Combine(AppSettings.DataFolder, "Library");

    public static event Action? Changed;

    public static string OriginalPath(string id) => Path.Combine(Root, id + ".orig.png");
    public static string BasePath(string id) => Path.Combine(Root, id + ".base.png");
    public static string DocumentPath(string id) => Path.Combine(Root, id + ".snip.json");
    public static string OutputPath(string id) => Path.Combine(Root, id + ".png");
    public static string TextPath(string id) => Path.Combine(Root, id + ".txt");
    static string TemporaryMarkerPath(string id) => Path.Combine(Root, id + ".temp");

    /// <summary>Where images dragged out or copied as files are written; other apps read them from here.</summary>
    public static string ShareFolder { get; } = Path.Combine(Path.GetTempPath(), "Snarvo");

    public static DateTime CreatedAt(string id) =>
        DateTime.TryParseExact(id, IdFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : DateTime.MinValue;

    public static string DisplayName(string id) => CreatedAt(id).ToString("d MMM yyyy HH:mm:ss", Swedish);

    public static IReadOnlyList<string> List()
    {
        if (!Directory.Exists(Root)) return [];
        return Directory.GetFiles(Root, "*.orig.png")
            .Select(f => Path.GetFileName(f)[..^".orig.png".Length])
            .Where(id => !IsTemporary(id))
            .OrderByDescending(id => id, StringComparer.Ordinal)
            .ToList();
    }

    // ── private mode: captures that only live while their editor is open ──

    /// <summary>Temporary captures are hidden from the library and deleted when their editor closes.</summary>
    public static bool IsTemporary(string id) => File.Exists(TemporaryMarkerPath(id));

    public static void MarkTemporary(string id) => File.WriteAllText(TemporaryMarkerPath(id), "");

    /// <summary>Moves a temporary capture into the library for good.</summary>
    public static void Keep(string id)
    {
        File.Delete(TemporaryMarkerPath(id));
        NotifyChanged();
    }

    /// <summary>Deletes temporary captures left behind, e.g. after a crash. Returns how many.</summary>
    public static int DeleteTemporary(Func<string, bool>? skip = null)
    {
        if (!Directory.Exists(Root)) return 0;
        int count = 0;
        foreach (var marker in Directory.GetFiles(Root, "*.temp"))
        {
            var id = Path.GetFileNameWithoutExtension(marker);
            if (skip?.Invoke(id) == true) continue;
            Delete(id);
            count++;
        }
        return count;
    }

    // ── automatic clean-up ──

    /// <summary>Deletes library captures taken more than the given number of days ago. Returns how many.</summary>
    public static int DeleteOlderThan(int days, Func<string, bool>? skip = null)
    {
        var limit = DateTime.Now.AddDays(-days);
        int count = 0;
        foreach (var id in List())
        {
            var created = CreatedAt(id);
            if (created == DateTime.MinValue || created >= limit || skip?.Invoke(id) == true) continue;
            Delete(id);
            count++;
        }
        return count;
    }

    public static int CountOlderThan(int days)
    {
        var limit = DateTime.Now.AddDays(-days);
        return List().Count(id => CreatedAt(id) is var created && created != DateTime.MinValue && created < limit);
    }

    /// <summary>Removes images that were dragged out or copied as files more than <paramref name="age"/> ago.</summary>
    public static void CleanShareFolder(TimeSpan age)
    {
        try
        {
            if (!Directory.Exists(ShareFolder)) return;
            foreach (var file in Directory.GetFiles(ShareFolder))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < DateTime.Now - age) File.Delete(file);
                }
                catch (IOException)
                {
                    // Still being read by the app it was dropped into; next time.
                }
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    /// <summary>
    /// Adds an image to the library. Returns null when it is identical to the previous import
    /// (Snipping Tool sometimes puts the same capture on the clipboard twice).
    /// </summary>
    public static async Task<string?> ImportAsync(IRandomAccessStream stream, bool forceOpaque, bool temporary = false)
    {
        Directory.CreateDirectory(Root);
        var device = CanvasDevice.GetSharedDevice();
        var bitmap = await CanvasBitmap.LoadAsync(device, stream, 96);

        var pixels = bitmap.GetPixelBytes();
        var hash = Convert.ToHexString(SHA256.HashData(pixels));
        if (hash == _lastHash)
        {
            bitmap.Dispose();
            return null;
        }
        _lastHash = hash;

        if (forceOpaque)
        {
            // Clipboard bitmaps often arrive as 32-bit DIBs with a zero alpha channel.
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            var opaque = CanvasBitmap.CreateFromBytes(device, pixels, (int)bitmap.SizeInPixels.Width,
                (int)bitmap.SizeInPixels.Height, bitmap.Format, 96, CanvasAlphaMode.Premultiplied);
            bitmap.Dispose();
            bitmap = opaque;
        }

        using (bitmap)
            return await AddBitmapAsync(bitmap, temporary);
    }

    /// <summary>
    /// Joins captures into one new capture, side by side or stacked, oldest first.
    /// Smaller images are centred; the whole is shrunk if it would exceed the GPU's bitmap limit.
    /// </summary>
    public static async Task<string> CombineAsync(IEnumerable<string> ids, bool sideBySide)
    {
        const float Gap = 16;
        var device = CanvasDevice.GetSharedDevice();
        var images = new List<CanvasBitmap>();
        try
        {
            foreach (var id in ids.OrderBy(id => id, StringComparer.Ordinal))
            {
                var path = File.Exists(OutputPath(id)) ? OutputPath(id) : OriginalPath(id);
                var bytes = await File.ReadAllBytesAsync(path);
                using var stream = new MemoryStream(bytes).AsRandomAccessStream();
                images.Add(await CanvasBitmap.LoadAsync(device, stream, 96));
            }

            float along = images.Sum(i => (float)(sideBySide ? i.Size.Width : i.Size.Height)) + Gap * (images.Count - 1);
            float across = images.Max(i => (float)(sideBySide ? i.Size.Height : i.Size.Width));
            float width = sideBySide ? along : across, height = sideBySide ? across : along;
            float scale = Math.Min(1f, (device.MaximumBitmapSizeInPixels - 256) / Math.Max(width, height));

            using var target = new CanvasRenderTarget(device, MathF.Ceiling(width * scale), MathF.Ceiling(height * scale), 96);
            using (var ds = target.CreateDrawingSession())
            {
                ds.Clear(Microsoft.UI.Colors.White);
                ds.Transform = System.Numerics.Matrix3x2.CreateScale(scale);
                float at = 0;
                foreach (var image in images)
                {
                    float w = (float)image.Size.Width, h = (float)image.Size.Height;
                    if (sideBySide) ds.DrawImage(image, at, (across - h) / 2);
                    else ds.DrawImage(image, (across - w) / 2, at);
                    at += (sideBySide ? w : h) + Gap;
                }
            }
            return await AddBitmapAsync(target);
        }
        finally
        {
            foreach (var image in images) image.Dispose();
        }
    }

    public static async Task<string> AddBitmapAsync(CanvasBitmap bitmap, bool temporary = false)
    {
        Directory.CreateDirectory(Root);
        var id = NewId();
        // Marked before the image exists, so the library and text index never pick it up.
        if (temporary) MarkTemporary(id);
        await bitmap.SaveAsync(OriginalPath(id), CanvasBitmapFileFormat.Png);
        File.Copy(OriginalPath(id), OutputPath(id), overwrite: true);
        NotifyChanged();
        return id;
    }

    public static async Task<string?> ImportFileAsync(string path, bool temporary = false)
    {
        await using var file = File.OpenRead(path);
        using var stream = file.AsRandomAccessStream();
        _lastHash = null;
        return await ImportAsync(stream, forceOpaque: false, temporary);
    }

    public static void Delete(string id)
    {
        // The marker goes last: if anything else fails, the capture is still cleaned up next start.
        foreach (var path in new[] { OriginalPath(id), BasePath(id), DocumentPath(id), OutputPath(id), TextPath(id), TemporaryMarkerPath(id) })
        {
            if (path == TemporaryMarkerPath(id) && File.Exists(OriginalPath(id))) break;
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { App.Log(ex); }
        }
        NotifyChanged();
    }

    public static void NotifyChanged() => Changed?.Invoke();

    static string NewId()
    {
        var id = DateTime.Now.ToString(IdFormat, CultureInfo.InvariantCulture);
        while (File.Exists(OriginalPath(id)))
        {
            Thread.Sleep(2);
            id = DateTime.Now.ToString(IdFormat, CultureInfo.InvariantCulture);
        }
        return id;
    }
}
