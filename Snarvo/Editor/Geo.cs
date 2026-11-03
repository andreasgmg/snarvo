// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Numerics;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Text;
using Windows.Foundation;
using Windows.UI;

namespace Snarvo.Editor;

internal static class Geo
{
    public static Rect RectFrom(Vector2 a, Vector2 b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    public static Rect Inflate(Rect r, double d)
    {
        double w = Math.Max(0, r.Width + 2 * d), h = Math.Max(0, r.Height + 2 * d);
        return new(r.X + r.Width / 2 - w / 2, r.Y + r.Height / 2 - h / 2, w, h);
    }

    public static Rect Intersect(Rect a, Rect b)
    {
        double x1 = Math.Max(a.Left, b.Left), y1 = Math.Max(a.Top, b.Top);
        double x2 = Math.Min(a.Right, b.Right), y2 = Math.Min(a.Bottom, b.Bottom);
        return x2 <= x1 || y2 <= y1 ? new Rect(x1, y1, 0, 0) : new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    public static Rect Union(Rect a, Rect b)
    {
        double x1 = Math.Min(a.Left, b.Left), y1 = Math.Min(a.Top, b.Top);
        return new(x1, y1, Math.Max(a.Right, b.Right) - x1, Math.Max(a.Bottom, b.Bottom) - y1);
    }

    public static bool Contains(this Rect r, Vector2 p) =>
        p.X >= r.Left && p.X <= r.Right && p.Y >= r.Top && p.Y <= r.Bottom;

    public static Vector2 Center(this Rect r) => new((float)(r.X + r.Width / 2), (float)(r.Y + r.Height / 2));

    public static Vector2 TopLeft(this Rect r) => new((float)r.X, (float)r.Y);

    public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float lengthSquared = ab.LengthSquared();
        if (lengthSquared < 1e-4f) return Vector2.Distance(p, a);
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / lengthSquared, 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }

    /// <summary>Snaps lines to 45° steps, or boxes to squares.</summary>
    public static Vector2 Constrain(Vector2 start, Vector2 p, bool square)
    {
        var d = p - start;
        if (square)
        {
            float s = Math.Max(Math.Abs(d.X), Math.Abs(d.Y));
            return start + new Vector2(MathF.CopySign(s, d.X), MathF.CopySign(s, d.Y));
        }
        double step = Math.PI / 4;
        double angle = Math.Round(Math.Atan2(d.Y, d.X) / step) * step;
        float length = d.Length();
        return start + new Vector2((float)Math.Cos(angle) * length, (float)Math.Sin(angle) * length);
    }

    /// <summary>White on dark colors, near-black on light ones.</summary>
    public static Color Contrast(Color c)
    {
        double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
        return luminance > 0.62 ? Color.FromArgb(255, 25, 25, 25) : Color.FromArgb(255, 255, 255, 255);
    }

    public static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
}

internal static class Strokes
{
    public static readonly CanvasStrokeStyle Round = new()
    {
        StartCap = CanvasCapStyle.Round,
        EndCap = CanvasCapStyle.Round,
        DashCap = CanvasCapStyle.Round,
        LineJoin = CanvasLineJoin.Round,
    };

    public static readonly CanvasStrokeStyle Dash = new() { DashStyle = CanvasDashStyle.Dash };
}

internal static class Fonts
{
    public const string Family = "Segoe UI";

    public static CanvasTextFormat Create(float size) => new()
    {
        FontFamily = Family,
        FontSize = size,
        FontWeight = FontWeights.SemiBold,
        WordWrapping = CanvasWordWrapping.NoWrap,
    };
}
