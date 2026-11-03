// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Numerics;

namespace Snarvo.Editor;

/// <summary>Smoothing and shape recognition for pen strokes.</summary>
internal static class Freehand
{
    /// <summary>Points evenly spaced along the stroke, so later steps do not depend on mouse speed.</summary>
    public static List<Vector2> Resample(IReadOnlyList<Vector2> points, float spacing)
    {
        var result = new List<Vector2> { points[0] };
        if (points.Count < 2) return result;

        var previous = points[0];
        float carried = 0;
        for (int i = 1; i < points.Count; i++)
        {
            var next = points[i];
            float segment = Vector2.Distance(previous, next);
            while (carried + segment >= spacing)
            {
                float t = (spacing - carried) / segment;
                previous = Vector2.Lerp(previous, next, t);
                result.Add(previous);
                segment = Vector2.Distance(previous, next);
                carried = 0;
            }
            carried += segment;
            previous = next;
        }
        if (Vector2.Distance(result[^1], points[^1]) > spacing * 0.25f) result.Add(points[^1]);
        return result;
    }

    /// <summary>
    /// Evens out mouse jitter with a weighted moving average. The window shrinks towards the
    /// ends, so the stroke still starts and ends exactly where it was drawn.
    /// </summary>
    public static List<Vector2> Smooth(IReadOnlyList<Vector2> points)
    {
        if (points.Count < 3) return [.. points];
        var even = Resample(points, 2f);
        const int radius = 4;

        var result = new List<Vector2>(even.Count);
        for (int i = 0; i < even.Count; i++)
        {
            int r = Math.Min(radius, Math.Min(i, even.Count - 1 - i));
            Vector2 sum = Vector2.Zero;
            float weights = 0;
            for (int k = -r; k <= r; k++)
            {
                float w = r + 1 - Math.Abs(k);
                sum += even[i + k] * w;
                weights += w;
            }
            result.Add(sum / weights);
        }
        return result;
    }

    /// <summary>
    /// The straight line, ellipse or rectangle the stroke resembles, or null when it looks like
    /// none of them. Used when the user holds still at the end of a stroke.
    /// </summary>
    public static Annotation? Recognize(PenAnnotation pen)
    {
        var raw = pen.Points;
        if (raw.Count < 4) return null;

        float length = 0;
        for (int i = 1; i < raw.Count; i++) length += Vector2.Distance(raw[i - 1], raw[i]);
        if (length < 24) return null;

        var points = Resample(raw, Math.Max(1f, length / 160));
        Vector2 start = raw[0], end = raw[^1];
        float gap = Vector2.Distance(start, end);

        Annotation? shape = null;
        if (gap > 0.85f * length) shape = FitLine(points, start, end, gap);
        else if (gap < 0.3f * length) shape = FitClosed(points);
        if (shape is null) return null;

        shape.Color = pen.Color;
        shape.Thickness = pen.Thickness;
        shape.FontSize = pen.FontSize;
        return shape;
    }

    static LineAnnotation? FitLine(List<Vector2> points, Vector2 start, Vector2 end, float gap)
    {
        float worst = points.Max(p => Geo.DistanceToSegment(p, start, end));
        if (worst > Math.Max(4f, gap * 0.06f)) return null;

        // Nearly horizontal or vertical lines become exactly so.
        var d = end - start;
        float angle = MathF.Atan2(d.Y, d.X) * 180 / MathF.PI;
        float toAxis = MathF.Abs(angle - MathF.Round(angle / 90) * 90);
        if (toAxis < 5)
        {
            var mid = (start + end) / 2;
            if (MathF.Abs(d.X) > MathF.Abs(d.Y)) start.Y = end.Y = mid.Y;
            else start.X = end.X = mid.X;
        }
        return new LineAnnotation { Start = start, End = end };
    }

    static BoxAnnotation? FitClosed(List<Vector2> points)
    {
        // Trimmed extents, so the overlap where a closed stroke ends does not stretch the shape.
        static (float Low, float High) Extent(IEnumerable<float> values)
        {
            var sorted = values.Order().ToList();
            int cut = sorted.Count * 3 / 100;
            return (sorted[cut], sorted[sorted.Count - 1 - cut]);
        }
        var (left, right) = Extent(points.Select(p => p.X));
        var (top, bottom) = Extent(points.Select(p => p.Y));
        float a = (right - left) / 2, b = (bottom - top) / 2;
        if (a < 8 || b < 8) return null;
        var center = new Vector2(left + a, top + b);

        // Mean distance from the ideal outline, relative to the radius, for both candidates.
        float ellipseError = points.Average(p =>
        {
            var q = p - center;
            return MathF.Abs(MathF.Sqrt(q.X * q.X / (a * a) + q.Y * q.Y / (b * b)) - 1);
        });
        float rectangleError = points.Average(p =>
        {
            float dx = a - MathF.Abs(p.X - center.X), dy = b - MathF.Abs(p.Y - center.Y);
            return MathF.Abs(MathF.Abs(dx) < MathF.Abs(dy) ? dx / a : dy / b);
        });

        if (ellipseError <= rectangleError && ellipseError < 0.09f)
        {
            // An almost round ellipse was meant to be a circle.
            if (MathF.Abs(a - b) / MathF.Max(a, b) < 0.12f) a = b = (a + b) / 2;
            return new EllipseAnnotation { Start = center - new Vector2(a, b), End = center + new Vector2(a, b) };
        }
        if (rectangleError < ellipseError && rectangleError < 0.07f)
            return new RectangleAnnotation { Start = center - new Vector2(a, b), End = center + new Vector2(a, b) };
        return null;
    }
}
