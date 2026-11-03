// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Numerics;
using Snarvo.Services;
using Windows.Foundation;

namespace Snarvo.Editor;

/// <summary>Turns found sensitive text into covering, pixelating or blurring annotations.</summary>
public static class AutoHide
{
    /// <summary>
    /// One annotation per hit that is not hidden already. OCR boxes hug the letters tightly and
    /// often miss descenders, so each box is padded, most of all vertically.
    /// </summary>
    public static List<(SensitiveHit Hit, BoxAnnotation Box)> Create(IEnumerable<SensitiveHit> hits, HideMethod method,
        Vector2 imageSize, IEnumerable<Rect> alreadyCovered, IReadOnlyDictionary<Tool, ToolStyle> styles)
    {
        var image = new Rect(0, 0, imageSize.X, imageSize.Y);
        var covered = alreadyCovered.ToList();
        var tool = method switch { HideMethod.Pixelate => Tool.Pixelate, HideMethod.Blur => Tool.Blur, _ => Tool.Redact };
        var style = styles.TryGetValue(tool, out var s) ? s : ToolStyle.CreateDefaults()[tool];
        var created = new List<(SensitiveHit, BoxAnnotation)>();

        foreach (var hit in hits)
        {
            var b = hit.Bounds;
            // A solid box hides everything under it, so it needs less margin than pixels or blur.
            double padX = 4, padY = method == HideMethod.Cover ? Math.Max(3, b.Height * 0.22) : Math.Max(4, b.Height * 0.35);
            var area = Geo.Intersect(new Rect(b.X - padX, b.Y - padY, b.Width + 2 * padX, b.Height + 2 * padY), image);
            if (area.Width < 2 || area.Height < 2) continue;
            // Running it twice should not stack a second box on the same spot.
            var corner = new Vector2((float)area.Right, (float)area.Bottom);
            if (covered.Any(c => Geo.Inflate(c, 2).Contains(area.TopLeft()) && Geo.Inflate(c, 2).Contains(corner)))
                continue;

            BoxAnnotation box = tool switch
            {
                Tool.Pixelate => new BlurAnnotation { Pixelate = true },
                Tool.Blur => new BlurAnnotation(),
                _ => new RedactAnnotation(),
            };
            box.Start = area.TopLeft();
            box.End = corner;
            box.Color = style.Color;
            box.Thickness = style.Thickness;
            box.FontSize = style.FontSize;
            // Blocks and blur must be coarse relative to the text height, or short strings stay readable.
            if (tool == Tool.Pixelate) box.Thickness = Math.Max(box.Thickness, (float)area.Height / 2.5f);
            if (tool == Tool.Blur) box.Thickness = Math.Max(box.Thickness, (float)area.Height / 2f);

            covered.Add(area);
            created.Add((hit, box));
        }
        return created;
    }

    /// <summary>Areas already covered by blur, pixelation or solid boxes.</summary>
    public static IEnumerable<Rect> Covered(IEnumerable<Annotation> annotations) =>
        annotations.Where(a => a is BlurAnnotation or RedactAnnotation).Select(a => Geo.RectFrom(((BoxAnnotation)a).Start, ((BoxAnnotation)a).End));
}
