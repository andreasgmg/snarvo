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

    public ToolStyle Clone() => new(Color, Thickness, FontSize);

    public static Dictionary<Tool, ToolStyle> CloneAll(IReadOnlyDictionary<Tool, ToolStyle> styles) =>
        styles.ToDictionary(kv => kv.Key, kv => kv.Value.Clone());

    /// <summary>Saved styles plus defaults for any tool that was added after they were saved.</summary>
    public static Dictionary<Tool, ToolStyle> WithDefaults(IReadOnlyDictionary<Tool, ToolStyle>? saved)
    {
        var styles = CreateDefaults();
        if (saved is not null)
            foreach (var (tool, style) in saved)
                if (styles.ContainsKey(tool)) styles[tool] = style.Clone();
        return styles;
    }

    public static Dictionary<Tool, ToolStyle> CreateDefaults()
    {
        var red = Color.FromArgb(255, 0xE5, 0x39, 0x35);
        var blue = Color.FromArgb(255, 0x1E, 0x88, 0xE5);
        var yellow = Color.FromArgb(255, 0xFF, 0xEB, 0x3B);
        var graphite = Color.FromArgb(255, 0x42, 0x42, 0x42);
        return new()
        {
            [Tool.Arrow] = new(red, 5, 24),
            [Tool.Line] = new(red, 4, 24),
            [Tool.Rectangle] = new(red, 4, 24),
            [Tool.Ellipse] = new(red, 4, 24),
            [Tool.Highlight] = new(yellow, 4, 24),
            [Tool.Pen] = new(red, 4, 24),
            [Tool.Text] = new(red, 4, 28),
            [Tool.Callout] = new(blue, 4, 20),
            [Tool.Step] = new(red, 4, 22),
            [Tool.Blur] = new(red, 12, 24),
            [Tool.Pixelate] = new(red, 12, 24),
            [Tool.Magnify] = new(graphite, 3, 24),
            [Tool.Spotlight] = new(graphite, 4, 24),
        };
    }
}
