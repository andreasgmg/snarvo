// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Numerics;
using System.Text.Json.Serialization;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using Microsoft.UI.Text;
using Windows.Foundation;
using Windows.UI;

namespace Snarvo.Editor;

public enum Tool
{
    Select, Arrow, Line, Rectangle, Ellipse, Highlight, Pen,
    Text, Callout, Step,
    Blur, Pixelate, Magnify, Spotlight,
    Crop, CutOut,
    // Not on the tool rail: created by "Förenkla". Last, so stored tool numbers stay the same.
    Simplify,
    // New tools go after the existing ones for the same reason.
    Redact,
}

/// <summary>Everything an annotation may need while drawing besides its own data.</summary>
public sealed class RenderContext
{
    readonly Dictionary<StepAnnotation, int> _stepNumbers = new(ReferenceEqualityComparer.Instance);

    public RenderContext(CanvasBitmap baseImage, IEnumerable<Annotation> annotations)
    {
        Base = baseImage;
        Size = new((float)baseImage.Size.Width, (float)baseImage.Size.Height);
        int n = 0;
        foreach (var a in annotations)
            if (a is StepAnnotation step) _stepNumbers[step] = ++n;
    }

    public CanvasBitmap Base { get; }
    public Vector2 Size { get; }
    public Rect ImageRect => new(0, 0, Size.X, Size.Y);
    public int StepNumber(StepAnnotation step) => _stepNumbers.TryGetValue(step, out var n) ? n : 1;
}

/// <summary>Annotations whose text is typed in place.</summary>
public interface ITextAnnotation
{
    string Text { get; set; }
    float FontSize { get; }
    bool HideText { get; set; }
    Color TextColor { get; }
    Vector2 GetTextOrigin(ICanvasResourceCreator rc);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ArrowAnnotation), "arrow")]
[JsonDerivedType(typeof(LineAnnotation), "line")]
[JsonDerivedType(typeof(RectangleAnnotation), "rectangle")]
[JsonDerivedType(typeof(EllipseAnnotation), "ellipse")]
[JsonDerivedType(typeof(HighlightAnnotation), "highlight")]
[JsonDerivedType(typeof(PenAnnotation), "pen")]
[JsonDerivedType(typeof(TextAnnotation), "text")]
[JsonDerivedType(typeof(CalloutAnnotation), "callout")]
[JsonDerivedType(typeof(StepAnnotation), "step")]
[JsonDerivedType(typeof(BlurAnnotation), "blur")]
[JsonDerivedType(typeof(MagnifyAnnotation), "magnify")]
[JsonDerivedType(typeof(SpotlightAnnotation), "spotlight")]
[JsonDerivedType(typeof(BlockAnnotation), "block")]
[JsonDerivedType(typeof(RedactAnnotation), "redact")]
public abstract class Annotation
{
    public Color Color { get; set; } = Colors.Red;
    public float Thickness { get; set; } = 4;
    public float FontSize { get; set; } = 24;

    // Methods rather than properties so they never end up in the saved JSON.
    public abstract Tool GetTool();
    public virtual bool CastsShadow() => true;

    public abstract void Draw(CanvasDrawingSession ds, RenderContext ctx);
    public abstract Rect GetBounds(ICanvasResourceCreator rc);

    public virtual bool HitTest(ICanvasResourceCreator rc, Vector2 p, float tolerance) =>
        Geo.Inflate(GetBounds(rc), tolerance).Contains(p);

    /// <summary>Maps every stored point; used for moving, cropping and cut-outs.</summary>
    public abstract void TransformPoints(Func<Vector2, Vector2> map);

    public void Offset(Vector2 delta) => TransformPoints(p => p + delta);

    public virtual Vector2[] GetHandles(ICanvasResourceCreator rc) => [];
    public virtual void SetHandle(int index, Vector2 p) { }

    public virtual Annotation Clone() => (Annotation)MemberwiseClone();
}

/// <summary>Defined by a start and an end point (lines, arrows).</summary>
public abstract class ShapeAnnotation : Annotation
{
    public Vector2 Start { get; set; }
    public Vector2 End { get; set; }

    protected Rect Box => Geo.RectFrom(Start, End);

    public override Rect GetBounds(ICanvasResourceCreator rc) => Box;

    public override void TransformPoints(Func<Vector2, Vector2> map)
    {
        Start = map(Start);
        End = map(End);
    }

    public override Vector2[] GetHandles(ICanvasResourceCreator rc) => [Start, End];

    public override void SetHandle(int index, Vector2 p)
    {
        if (index == 0) Start = p;
        else End = p;
    }

    public virtual bool IsTooSmall() => Vector2.Distance(Start, End) < 3;
}

/// <summary>A rectangular area with four corner handles.</summary>
public abstract class BoxAnnotation : ShapeAnnotation
{
    Vector2[] Corners()
    {
        var r = Box;
        return
        [
            new((float)r.Left, (float)r.Top), new((float)r.Right, (float)r.Top),
            new((float)r.Right, (float)r.Bottom), new((float)r.Left, (float)r.Bottom),
        ];
    }

    public override Vector2[] GetHandles(ICanvasResourceCreator rc) => Corners();

    public override void SetHandle(int index, Vector2 p)
    {
        Start = Corners()[(index + 2) % 4];
        End = p;
    }

    public override bool IsTooSmall() => Math.Abs(End.X - Start.X) < 3 || Math.Abs(End.Y - Start.Y) < 3;
}

public sealed class ArrowAnnotation : ShapeAnnotation
{
    public override Tool GetTool() => Tool.Arrow;

    public override void Draw(CanvasDrawingSession ds, RenderContext ctx)
    {
        var d = End - Start;
        float length = d.Length();
        if (length < 1) return;

        // A shaft that widens towards a large, swept-back head. The thin outline in a contrasting
        // colour keeps the arrow sharp on both light and dark parts of a screenshot.
        var u = d / length;
        var n = new Vector2(-u.Y, u.X);
        float headLength = Math.Min(Math.Max(Thickness * 4.2f, 16f), length * 0.6f);
        float headHalf = headLength * 0.62f;
        float tailHalf = Math.Max(1f, Thickness * 0.22f);
        float shaftHalf = Thickness * 0.55f;
        var barbBase = End - u * headLength;
        var notch = barbBase + u * (headLength * 0.22f);

        var path = new CanvasPathBuilder(ds);
        path.BeginFigure(Start + n * tailHalf);
        path.AddLine(notch + n * shaftHalf);
        path.AddLine(barbBase + n * headHalf);
        path.AddLine(End);
        path.AddLine(barbBase - n * headHalf);
        path.AddLine(notch - n * shaftHalf);
        path.AddLine(Start - n * tailHalf);
        path.EndFigure(CanvasFigureLoop.Closed);
        using var geometry = CanvasGeometry.CreatePath(path);
        ds.DrawGeometry(geometry, Geo.WithAlpha(Geo.Contrast(Color), 235), Outline(Thickness), Strokes.Round);
        ds.FillGeometry(geometry, Color);
    }

    /// <summary>Width of the contrasting edge around arrows and shapes; half of it shows outside.</summary>
    internal static float Outline(float thickness) => Math.Clamp(thickness * 0.5f, 2f, 5f);

    public override bool HitTest(ICanvasResourceCreator rc, Vector2 p, float tolerance) =>
        Geo.DistanceToSegment(p, Start, End) <= Thickness * 1.5f + tolerance;
}

public sealed class LineAnnotation : ShapeAnnotation
{
    public override Tool GetTool() => Tool.Line;

    public override void Draw(CanvasDrawingSession ds, RenderContext ctx)
    {
        ds.DrawLine(Start, End, Edge(Color), Thickness + ArrowAnnotation.Outline(Thickness), Strokes.Round);
        ds.DrawLine(Start, End, Color, Thickness, Strokes.Round);
    }

    internal static Color Edge(Color c) => Geo.WithAlpha(Geo.Contrast(c), 235);

    public override bool HitTest(ICanvasResourceCreator rc, Vector2 p, float tolerance) =>
        Geo.DistanceToSegment(p, Start, End) <= Thickness / 2 + tolerance;
}

public sealed class RectangleAnnotation : BoxAnnotation
{
    public override Tool GetTool() => Tool.Rectangle;

    public override void Draw(CanvasDrawingSession ds, RenderContext ctx)
    {
        ds.DrawRectangle(Box, LineAnnotation.Edge(Color), Thickness + ArrowAnnotation.Outline(Thickness), Strokes.Round);
        ds.DrawRectangle(Box, Color, Thickness, Strokes.Round);
    }

    public override bool HitTest(ICanvasResourceCreator rc, Vector2 p, float tolerance)
    {
        // Only the outline is clickable so things inside the frame stay reachable.
        float reach = Thickness / 2 + tolerance;
        var inner = Geo.Inflate(Box, -reach);
        return Geo.Inflate(Box, reach).Contains(p) && !(inner.Width > 0 && inner.Height > 0 && inner.Contains(p));
    }
}

public sealed class EllipseAnnotation : BoxAnnotation
{
    public override Tool GetTool() => Tool.Ellipse;

    public override void Draw(CanvasDrawingSession ds, RenderContext ctx)
    {
        var r = Box;
        float rx = (float)r.Width / 2, ry = (float)r.Height / 2;
        ds.DrawEllipse(r.Center(), rx, ry, LineAnnotation.Edge(Color), Thickness + ArrowAnnotation.Outline(Thickness), Strokes.Round);
        ds.DrawEllipse(r.Center(), rx, ry, Color, Thickness, Strokes.Round);
    }

    public override bool HitTest(ICanvasResourceCreator rc, Vector2 p, float tolerance)
    {
        var r = Box;
        float a = (float)r.Width / 2, b = (float)r.Height / 2;
        if (a < 2 || b < 2) return base.HitTest(rc, p, tolerance);
        var q = p - r.Center();
        float k = MathF.Sqrt(q.X * q.X / (a * a) + q.Y * q.Y / (b * b));
        return MathF.Abs(k - 1) * MathF.Min(a, b) <= Thickness / 2 + tolerance;
    }
}

public sealed class HighlightAnnotation : BoxAnnotation
{
    public override Tool GetTool() => Tool.Highlight;
    public override bool CastsShadow() => false;

    public override void Draw(CanvasDrawingSession ds, RenderContext ctx)
    {
        // Min blending behaves like a highlighter pen: dark text stays dark.
        var previous = ds.Blend;
        ds.Blend = CanvasBlend.Min;
        ds.FillRectangle(Box, Color);
        ds.Blend = previous;
    }
}

public sealed class PenAnnotation : Annotation
{
    public List<Vector2> Points { get; set; } = [];

    // The drawn points are kept as recorded; only the rendering is smoothed, so the stroke
    // looks the same while drawing and afterwards. Cached until the points change.
    List<Vector2>? _smoothed, _smoothedFrom;
    int _smoothedCount;

    public override Tool GetTool() => Tool.Pen;

    List<Vector2> SmoothedPoints()
    {
        if (_smoothed is null || !ReferenceEquals(_smoothedFrom, Points) || _smoothedCount != Points.Count)
        {
            _smoothed = Freehand.Smooth(Points);
            _smoothedFrom = Points;
            _smoothedCount = Points.Count;
        }
        return _smoothed;
    }

    public override void Draw(CanvasDrawingSession ds, RenderContext ctx)
    {
        if (Points.Count == 0) return;
        if (Points.Count == 1)
        {
            ds.FillCircle(Points[0], Thickness / 2, Color);
            return;
        }

        // Quadratic curves through the midpoints of the smoothed points give an even stroke.
        var points = SmoothedPoints();
        var path = new CanvasPathBuilder(ds);
        path.BeginFigure(points[0]);
        for (int i = 1; i < points.Count - 1; i++)
            path.AddQuadraticBezier(points[i], (points[i] + points[i + 1]) / 2);
        path.AddLine(points[^1]);
        path.EndFigure(CanvasFigureLoop.Open);
        using var geometry = CanvasGeometry.CreatePath(path);
        ds.DrawGeometry(geometry, Color, Thickness, Strokes.Round);
    }

    public override Rect GetBounds(ICanvasResourceCreator rc)
    {
        if (Points.Count == 0) return new Rect(0, 0, 0, 0);
        float minX = Points.Min(p => p.X), minY = Points.Min(p => p.Y);
        float maxX = Points.Max(p => p.X), maxY = Points.Max(p => p.Y);
        return Geo.Inflate(new Rect(minX, minY, maxX - minX, maxY - minY), Thickness / 2);
    }

    public override bool HitTest(ICanvasResourceCreator rc, Vector2 p, float tolerance)
    {
        float reach = Thickness / 2 + tolerance;
        if (Points.Count == 1) return Vector2.Distance(p, Points[0]) <= reach;
        for (int i = 1; i < Points.Count; i++)
            if (Geo.DistanceToSegment(p, Points[i - 1], Points[i]) <= reach) return true;
        return false;
    }

    public override void TransformPoints(Func<Vector2, Vector2> map) => Points = Points.Select(map).ToList();

    public override Annotation Clone()
    {
        var copy = (PenAnnotation)MemberwiseClone();
        copy.Points = [.. Points];
        copy._smoothed = null;
        return copy;
    }
}

public sealed class TextAnnotation : Annotation, ITextAnnotation
{
    public Vector2 Position { get; set; }
    public string Text { get; set; } = "";

    /// <summary>Drawn as a label: contrasting text on a rounded box in the annotation colour.</summary>
    public bool Label { get; set; }

    [JsonIgnore] public bool HideText { get; set; }
    [JsonIgnore] public Color TextColor => Label ? Geo.Contrast(Color) : Color;

    public override Tool GetTool() => Tool.Text;
    public override bool CastsShadow() => Label;

    CanvasTextLayout CreateLayout(ICanvasResourceCreator rc)
    {
        using var format = Fonts.Create(FontSize);
        return new CanvasTextLayout(rc, Text.Length == 0 ? " " : Text, format, 0, 0);
    }

    Rect TextBox(CanvasTextLayout layout)
    {
        var b = layout.LayoutBounds;
        return new Rect(Position.X + b.X, Position.Y + b.Y, Math.Max(b.Width, FontSize * 0.6), b.Height);
    }

    Rect LabelBox(Rect text) => new(text.X - FontSize * 0.45, text.Y - FontSize * 0.12,
        text.Width + FontSize * 0.9, text.Height + FontSize * 0.24);

    public override void Draw(CanvasDrawingSession ds, RenderContext ctx)
    {
        if (Text.Length == 0 && !Label) return;
        using var layout = CreateLayout(ds);
        if (Label)
        {
            float radius = FontSize * 0.32f;
            ds.FillRoundedRectangle(LabelBox(TextBox(layout)), radius, radius, Color);
            if (!HideText) ds.DrawTextLayout(layout, Position, TextColor);
            return;
        }
        if (HideText) return;
        using var geometry = CanvasGeometry.CreateText(layout);
        // A contrasting outline keeps the text readable on any background.
        ds.DrawGeometry(geometry, Position, Geo.Contrast(Color), Math.Max(2f, FontSize / 6f), Strokes.Round);
        ds.FillGeometry(geometry, Position, Color);
    }

    public override Rect GetBounds(ICanvasResourceCreator rc)
    {
        using var layout = CreateLayout(rc);
        var box = TextBox(layout);
        return Label ? LabelBox(box) : box;
    }

    public Vector2 GetTextOrigin(ICanvasResourceCreator rc) => Position;

    public override void TransformPoints(Func<Vector2, Vector2> map) => Position = map(Position);
}

public sealed class CalloutAnnotation : Annotation, ITextAnnotation
{
    /// <summary>Where the tail points.</summary>
    public Vector2 Tip { get; set; }

    /// <summary>Center of the bubble.</summary>
    public Vector2 Center { get; set; }

    public string Text { get; set; } = "";

    [JsonIgnore] public bool HideText { get; set; }
    [JsonIgnore] public Color TextColor => Geo.Contrast(Color);

    float Padding => FontSize * 0.6f;

    public override Tool GetTool() => Tool.Callout;

    (CanvasTextLayout Layout, Rect Box) Measure(ICanvasResourceCreator rc)
    {
        using var format = Fonts.Create(FontSize);
        var layout = new CanvasTextLayout(rc, Text.Length == 0 ? " " : Text, format, 0, 0);
        var b = layout.LayoutBounds;
        double width = Math.Max(b.Width, FontSize * 3) + 2 * Padding;
        double height = Math.Max(b.Height, FontSize * 1.2) + 2 * Padding;
        return (layout, new Rect(Center.X - width / 2, Center.Y - height / 2, width, height));
    }

    public override void Draw(CanvasDrawingSession ds, RenderContext ctx)
    {
        var (layout, box) = Measure(ds);
        using (layout)
        {
            float radius = (float)Math.Min(FontSize * 0.7, box.Height / 2);
            using var body = CanvasGeometry.CreateRoundedRectangle(ds, box, radius, radius);
            if (box.Contains(Tip))
            {
                ds.FillGeometry(body, Color);
            }
            else
            {
                // A tail that starts wide inside the bubble and narrows to a rounded point.
                var dir = Vector2.Normalize(Tip - Center);
                var n = new Vector2(-dir.Y, dir.X);
                float baseHalf = (float)Math.Min(box.Width, box.Height) * 0.3f;
                float tipHalf = Math.Max(1f, FontSize * 0.06f);
                var path = new CanvasPathBuilder(ds);
                path.BeginFigure(Center + n * baseHalf);
                path.AddLine(Tip + n * tipHalf);
                path.AddArc(Tip - n * tipHalf, tipHalf, tipHalf, 0, CanvasSweepDirection.Clockwise, CanvasArcSize.Small);
                path.AddLine(Center - n * baseHalf);
                path.EndFigure(CanvasFigureLoop.Closed);
                using var tail = CanvasGeometry.CreatePath(path);
                using var shape = body.CombineWith(tail, Matrix3x2.Identity, CanvasGeometryCombine.Union);
                ds.FillGeometry(shape, Color);
            }
            if (!HideText && Text.Length > 0)
                ds.DrawTextLayout(layout, new Vector2((float)box.X + Padding, (float)box.Y + Padding), TextColor);
        }
    }

    public override Rect GetBounds(ICanvasResourceCreator rc)
    {
        var (layout, box) = Measure(rc);
        layout.Dispose();
        return Geo.Union(box, new Rect(Tip.X, Tip.Y, 0, 0));
    }

    public override bool HitTest(ICanvasResourceCreator rc, Vector2 p, float tolerance)
    {
        var (layout, box) = Measure(rc);
        layout.Dispose();
        return Geo.Inflate(box, tolerance).Contains(p) || Geo.DistanceToSegment(p, Center, Tip) <= 6 + tolerance;
    }

    public Vector2 GetTextOrigin(ICanvasResourceCreator rc)
    {
        var (layout, box) = Measure(rc);
        layout.Dispose();
        return new Vector2((float)box.X + Padding, (float)box.Y + Padding);
    }

    public override Vector2[] GetHandles(ICanvasResourceCreator rc) => [Tip];

    public override void SetHandle(int index, Vector2 p) => Tip = p;

    public override void TransformPoints(Func<Vector2, Vector2> map)
    {
        Tip = map(Tip);
        Center = map(Center);
    }
}

public sealed class StepAnnotation : Annotation
{
    public Vector2 Center { get; set; }

    float Radius => FontSize * 0.75f;

    public override Tool GetTool() => Tool.Step;

    public override void Draw(CanvasDrawingSession ds, RenderContext ctx)
    {
        float r = Radius;
        float ring = Math.Max(1.5f, r * 0.12f);
        ds.FillCircle(Center, r + ring / 2, Geo.Contrast(Color));
        ds.FillCircle(Center, r - ring / 2, Color);
        using var format = new CanvasTextFormat
        {
            FontFamily = Fonts.Family,
            FontSize = r * 1.15f,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Center,
        };
        ds.DrawText(ctx.StepNumber(this).ToString(), new Rect(Center.X - r, Center.Y - r, 2 * r, 2 * r),
            Geo.Contrast(Color), format);
    }

    public override Rect GetBounds(ICanvasResourceCreator rc) =>
        new(Center.X - Radius, Center.Y - Radius, 2 * Radius, 2 * Radius);

    public override bool HitTest(ICanvasResourceCreator rc, Vector2 p, float tolerance) =>
        Vector2.Distance(p, Center) <= Radius + tolerance;

    public override void TransformPoints(Func<Vector2, Vector2> map) => Center = map(Center);
}

/// <summary>
/// Replaces a run of text with a plain rounded bar ("Förenkla"), so the eye goes to what matters.
/// The box covers the text with the surrounding background; the bar is drawn in a muted text colour.
/// </summary>
public sealed class BlockAnnotation : BoxAnnotation
{
    public Color Background { get; set; } = Colors.White;

    public override Tool GetTool() => Tool.Simplify;
    public override bool CastsShadow() => false;

    public override void Draw(CanvasDrawingSession ds, RenderContext ctx)
    {
        var r = Box;
        ds.FillRectangle(r, Background);
        // The box is the text padded by a quarter of its height on every side.
        float textHeight = (float)r.Height / 1.5f, inset = textHeight * 0.25f;
        float barHeight = Math.Max(2, textHeight * 0.55f);
        var bar = new Rect(r.X + inset, r.Y + (r.Height - barHeight) / 2, Math.Max(barHeight, r.Width - 2 * inset), barHeight);
        ds.FillRoundedRectangle(bar, barHeight / 2, barHeight / 2, Color);
    }
}

/// <summary>Hides sensitive content by blurring or pixelating the capture underneath.</summary>
public sealed class BlurAnnotation : BoxAnnotation
{
    public bool Pixelate { get; set; }

    public override Tool GetTool() => Pixelate ? Tool.Pixelate : Tool.Blur;
    public override bool CastsShadow() => false;

    public override void Draw(CanvasDrawingSession ds, RenderContext ctx)
    {
        var r = Geo.Intersect(Box, ctx.ImageRect);
        if (r.Width < 1 || r.Height < 1) return;

        if (Pixelate)
        {
            float block = Math.Max(2, Thickness);
            var origin = r.TopLeft();
            // Work on whole blocks: a partial last row/column of blocks would come out transparent
            // and let the original show through. Edge pixels are stretched so blocks past the image stay opaque.
            var whole = new Rect(r.X, r.Y, Math.Ceiling(r.Width / block) * block, Math.Ceiling(r.Height / block) * block);
            using var extended = new BorderEffect { Source = ctx.Base, ExtendX = CanvasEdgeBehavior.Clamp, ExtendY = CanvasEdgeBehavior.Clamp };
            // Blurring first means each block is a true average, so no letter shapes survive the pixelation.
            using var soften = new GaussianBlurEffect { Source = extended, BlurAmount = block / 2.5f, BorderMode = EffectBorderMode.Hard };
            using var crop = new CropEffect { Source = soften, SourceRectangle = whole, BorderMode = EffectBorderMode.Hard };
            using var down = new ScaleEffect
            {
                Source = crop, Scale = new Vector2(1 / block), CenterPoint = origin,
                InterpolationMode = CanvasImageInterpolation.Anisotropic, BorderMode = EffectBorderMode.Hard,
            };
            using var up = new ScaleEffect
            {
                Source = down, Scale = new Vector2(block), CenterPoint = origin,
                InterpolationMode = CanvasImageInterpolation.NearestNeighbor, BorderMode = EffectBorderMode.Hard,
            };
            using var clip = new CropEffect { Source = up, SourceRectangle = r, BorderMode = EffectBorderMode.Hard };
            ds.DrawImage(clip);
        }
        else
        {
            using var blur = new GaussianBlurEffect { Source = ctx.Base, BlurAmount = Math.Max(1, Thickness), BorderMode = EffectBorderMode.Hard };
            using var clip = new CropEffect { Source = blur, SourceRectangle = r, BorderMode = EffectBorderMode.Hard };
            ds.DrawImage(clip);
        }
    }
}

/// <summary>
/// Covers content with a solid box. Unlike blurring or pixelation nothing of what is underneath
/// survives, so it cannot be reconstructed from the image.
/// </summary>
public sealed class RedactAnnotation : BoxAnnotation
{
    public override Tool GetTool() => Tool.Redact;
    public override bool CastsShadow() => false;

    public override void Draw(CanvasDrawingSession ds, RenderContext ctx)
    {
        var r = Box;
        if (r.Width < 1 || r.Height < 1) return;
        var solid = Geo.WithAlpha(Color, 255);
        float radius = (float)Math.Min(4, Math.Min(r.Width, r.Height) / 4);
        ds.FillRoundedRectangle(r, radius, radius, solid);

        // A small lock tells the reader the content was hidden on purpose.
        float size = (float)Math.Min(r.Height * 0.55, 22);
        if (size < 10 || r.Width < size * 2.2) return;
        using var format = new CanvasTextFormat
        {
            FontFamily = "Segoe MDL2 Assets",
            FontSize = size,
            HorizontalAlignment = CanvasHorizontalAlignment.Center,
            VerticalAlignment = CanvasVerticalAlignment.Center,
        };
        ds.DrawText("\uE72E", r, Geo.WithAlpha(Geo.Contrast(solid), 140), format);
    }
}

/// <summary>A round loupe that shows the area under it enlarged.</summary>
public sealed class MagnifyAnnotation : BoxAnnotation
{
    public float Zoom { get; set; } = 2;

    public override Tool GetTool() => Tool.Magnify;

    public override void Draw(CanvasDrawingSession ds, RenderContext ctx)
    {
        var r = Box;
        if (r.Width < 2 || r.Height < 2) return;
        var c = r.Center();
        float rx = (float)r.Width / 2, ry = (float)r.Height / 2;
        var source = new Rect(c.X - rx / Zoom, c.Y - ry / Zoom, r.Width / Zoom, r.Height / Zoom);

        using var lens = CanvasGeometry.CreateEllipse(ds, c, rx, ry);
        using (ds.CreateLayer(1f, lens))
        {
            ds.FillRectangle(r, Colors.White);
            ds.DrawImage(ctx.Base, r, source, 1f, CanvasImageInterpolation.HighQualityCubic);
        }
        ds.DrawEllipse(c, rx, ry, Color, Thickness);
    }
}

/// <summary>Dims everything except the chosen area.</summary>
public sealed class SpotlightAnnotation : BoxAnnotation
{
    public override Tool GetTool() => Tool.Spotlight;
    public override bool CastsShadow() => false;

    public override void Draw(CanvasDrawingSession ds, RenderContext ctx)
    {
        using var everything = CanvasGeometry.CreateRectangle(ds, ctx.ImageRect);
        using var hole = CanvasGeometry.CreateRoundedRectangle(ds, Box, 6, 6);
        using var dimmed = everything.CombineWith(hole, Matrix3x2.Identity, CanvasGeometryCombine.Exclude);
        ds.FillGeometry(dimmed, Color.FromArgb(150, 0, 0, 0));
    }

    public override bool HitTest(ICanvasResourceCreator rc, Vector2 p, float tolerance)
    {
        // Only the edge, so annotations inside the spotlight can still be clicked.
        var inner = Geo.Inflate(Box, -tolerance * 2);
        return Geo.Inflate(Box, tolerance).Contains(p) && !(inner.Width > 0 && inner.Height > 0 && inner.Contains(p));
    }
}
