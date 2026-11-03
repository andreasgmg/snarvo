// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.UI;
using Windows.Foundation;
using Windows.UI;

namespace Snarvo.Editor;

/// <summary>Draws a document the same way on screen and when exporting.</summary>
public static class Renderer
{
    const float ShadowMargin = 36;
    static readonly Color AnnotationShadow = Color.FromArgb(110, 0, 0, 0);
    static readonly Color FrameShadow = Color.FromArgb(140, 0, 0, 0);

    /// <summary>Where the image's top-left corner ends up once border and shadow are added.</summary>
    public static Vector2 ContentOffset(SnipDocument doc)
    {
        float o = doc.Effects.BorderWidth + (doc.Effects.Shadow ? ShadowMargin : 0);
        return new Vector2(o, o);
    }

    public static Vector2 FinalSize(SnipDocument doc) => doc.Size + 2 * ContentOffset(doc);

    /// <summary>The capture plus annotations, in image coordinates.</summary>
    public static void DrawContent(CanvasDrawingSession ds, ICanvasResourceCreator rc, SnipDocument doc)
    {
        ds.DrawImage(doc.Base, 0, 0);
        var ctx = new RenderContext(doc.Base, doc.Annotations);
        foreach (var annotation in doc.Annotations)
        {
            if (!annotation.CastsShadow())
            {
                annotation.Draw(ds, ctx);
                continue;
            }
            using var layer = new CanvasCommandList(rc);
            using (var lds = layer.CreateDrawingSession())
                annotation.Draw(lds, ctx);
            using var shadow = new ShadowEffect { Source = layer, BlurAmount = 2.5f, ShadowColor = AnnotationShadow };
            ds.DrawImage(shadow, 1.5f, 2f);
            ds.DrawImage(layer);
        }
    }

    /// <summary>The content wrapped in the document's frame effects, at the final size.</summary>
    public static void DrawFramed(CanvasDrawingSession ds, ICanvasResourceCreator rc, SnipDocument doc)
    {
        var effects = doc.Effects;
        if (effects.IsEmpty)
        {
            DrawContent(ds, rc, doc);
            return;
        }

        using var content = new CanvasCommandList(rc);
        using (var cds = content.CreateDrawingSession())
            DrawContent(cds, rc, doc);

        var size = doc.Size;
        float border = effects.BorderWidth;
        float radius = effects.CornerRadius;

        using var framed = new CanvasCommandList(rc);
        using (var fds = framed.CreateDrawingSession())
        {
            if (border > 0)
            {
                float outerRadius = radius > 0 ? radius + border : 0;
                fds.FillRoundedRectangle(0, 0, size.X + 2 * border, size.Y + 2 * border, outerRadius, outerRadius, effects.BorderColor);
            }
            if (radius > 0)
            {
                using var clip = CanvasGeometry.CreateRoundedRectangle(rc, border, border, size.X, size.Y, radius, radius);
                using (fds.CreateLayer(1f, clip))
                    fds.DrawImage(content, border, border);
            }
            else
            {
                fds.DrawImage(content, border, border);
            }
        }

        float margin = effects.Shadow ? ShadowMargin : 0;
        if (effects.Shadow)
        {
            using var shadow = new ShadowEffect { Source = framed, BlurAmount = 9, ShadowColor = FrameShadow };
            ds.DrawImage(shadow, margin + 2, margin + 6);
        }
        ds.DrawImage(framed, margin, margin);
    }

    /// <summary>The final image resized by <paramref name="scale"/>, for copying and exporting.</summary>
    public static CanvasRenderTarget RenderScaled(SnipDocument doc, float scale, Color? background = null)
    {
        var full = RenderFinal(doc, background);
        if (scale >= 0.999f) return full;

        using (full)
        {
            var device = CanvasDevice.GetSharedDevice();
            var size = full.Size;
            var target = new CanvasRenderTarget(device,
                Math.Max(1, MathF.Round((float)size.Width * scale)), Math.Max(1, MathF.Round((float)size.Height * scale)), 96);
            using var ds = target.CreateDrawingSession();
            ds.Clear(background ?? Colors.Transparent);
            // Drawing through a scale transform would sample sparsely and make text jagged.
            ds.DrawImage(full, new Rect(0, 0, target.Size.Width, target.Size.Height), full.Bounds, 1, CanvasImageInterpolation.HighQualityCubic);
            return target;
        }
    }

    public static CanvasRenderTarget RenderFinal(SnipDocument doc, Color? background = null)
    {
        var device = CanvasDevice.GetSharedDevice();
        var size = FinalSize(doc);
        var target = new CanvasRenderTarget(device, MathF.Ceiling(size.X), MathF.Ceiling(size.Y), 96);
        using var ds = target.CreateDrawingSession();
        ds.Clear(background ?? Colors.Transparent);
        DrawFramed(ds, device, doc);
        return target;
    }
}
