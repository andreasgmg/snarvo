// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using Windows.UI;

namespace Snarvo.Editor;

/// <summary>The color and sizes the next annotation of a tool is created with.</summary>
public sealed class ToolStyle(Color color, float thickness, float fontSize)
{
    public Color Color { get; set; } = color;
    public float Thickness { get; set; } = thickness;
    public float FontSize { get; set; } = fontSize;

    /// <summary>Text tool only: draw new text as a label with a coloured background.</summary>
    public bool Label { get; set; }

    public ToolStyle Clone() => new(Color, Thickness, FontSize) { Label = Label };

    public static Dictionary<Tool, ToolStyle> CloneAll(IReadOnlyDictionary<Tool, ToolStyle> styles) =>
        styles.ToDictionary(kv => kv.Key, kv => kv.Value.Clone());

    // ── the palette: colours that work together and stand out against typical screenshots ──
    public static readonly Color Red = Color.FromArgb(255, 0xE0, 0x53, 0x3C);
    public static readonly Color Orange = Color.FromArgb(255, 0xF0, 0x8C, 0x00);
    public static readonly Color Yellow = Color.FromArgb(255, 0xFF, 0xD4, 0x3B);
    public static readonly Color Green = Color.FromArgb(255, 0x2F, 0x9E, 0x44);
    public static readonly Color Blue = Color.FromArgb(255, 0x1C, 0x7E, 0xD6);
    public static readonly Color Purple = Color.FromArgb(255, 0x70, 0x48, 0xE8);
    public static readonly Color Pink = Color.FromArgb(255, 0xD6, 0x33, 0x6C);
    public static readonly Color Ink = Color.FromArgb(255, 0x21, 0x25, 0x29);
    public static readonly Color Grey = Color.FromArgb(255, 0x86, 0x8E, 0x96);
    public static readonly Color White = Color.FromArgb(255, 0xFF, 0xFF, 0xFF);

    public static readonly Color[] Palette = [Red, Orange, Yellow, Green, Blue, Purple, Pink, Ink, Grey, White];

    /// <summary>Default colours of earlier versions, replaced by their palette counterparts.</summary>
    static readonly Dictionary<Color, Color> Retired = new()
    {
        [Color.FromArgb(255, 0xE5, 0x39, 0x35)] = Red,
        [Color.FromArgb(255, 0x1E, 0x88, 0xE5)] = Blue,
        [Color.FromArgb(255, 0xFF, 0xEB, 0x3B)] = Yellow,
        [Color.FromArgb(255, 0x42, 0x42, 0x42)] = Ink,
    };

    /// <summary>Saved styles plus defaults for any tool that was added after they were saved.</summary>
    public static Dictionary<Tool, ToolStyle> WithDefaults(IReadOnlyDictionary<Tool, ToolStyle>? saved)
    {
        var styles = CreateDefaults();
        if (saved is not null)
            foreach (var (tool, style) in saved)
                if (styles.ContainsKey(tool))
                {
                    var copy = style.Clone();
                    // Someone who never changed a colour gets the new palette's version of it.
                    if (Retired.TryGetValue(copy.Color, out var updated)) copy.Color = updated;
                    styles[tool] = copy;
                }
        return styles;
    }

    public static Dictionary<Tool, ToolStyle> CreateDefaults() => new()
    {
        [Tool.Arrow] = new(Red, 6, 24),
        [Tool.Line] = new(Red, 4, 24),
        [Tool.Rectangle] = new(Red, 4, 24),
        [Tool.Ellipse] = new(Red, 4, 24),
        [Tool.Highlight] = new(Yellow, 4, 24),
        [Tool.Pen] = new(Red, 4, 24),
        [Tool.Text] = new(Red, 4, 24) { Label = true },
        [Tool.Callout] = new(Blue, 4, 20),
        [Tool.Step] = new(Red, 4, 22),
        [Tool.Blur] = new(Red, 12, 24),
        [Tool.Pixelate] = new(Red, 12, 24),
        [Tool.Redact] = new(Ink, 4, 24),
        [Tool.Magnify] = new(Ink, 3, 24),
        [Tool.Spotlight] = new(Ink, 4, 24),
    };
}
