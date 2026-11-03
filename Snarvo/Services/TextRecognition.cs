// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Snarvo.Services;

/// <summary>OCR result plus the factor its coordinates must be divided by to get image coordinates.</summary>
public sealed record RecognizedText(OcrResult Result, float Scale)
{
    public string Text => string.Join(Environment.NewLine, Result.Lines.Select(l => l.Text));
}

public static class TextRecognition
{
    public static bool IsAvailable => OcrEngine.TryCreateFromUserProfileLanguages() is not null;

    /// <summary>
    /// Runs Windows OCR on whatever <paramref name="draw"/> paints in an area of <paramref name="size"/>.
    /// Returns null when no OCR language is installed.
    /// </summary>
    public static async Task<RecognizedText?> RecognizeAsync(Vector2 size, Action<CanvasDrawingSession> draw)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null) return null;

        // Screen text is recognised best at its native size; enlarging it adds misreads.
        // Only images beyond the engine's limit are shrunk.
        float scale = Math.Min(1f, OcrEngine.MaxImageDimension / Math.Max(size.X, size.Y));
        var device = CanvasDevice.GetSharedDevice();
        using var target = new CanvasRenderTarget(device, Math.Max(1, MathF.Floor(size.X * scale)), Math.Max(1, MathF.Floor(size.Y * scale)), 96);
        using (var ds = target.CreateDrawingSession())
        {
            ds.Clear(Colors.White);
            ds.Transform = Matrix3x2.CreateScale(scale);
            draw(ds);
        }
        using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(target, BitmapAlphaMode.Premultiplied);
        return new RecognizedText(await engine.RecognizeAsync(bitmap), scale);
    }
}
