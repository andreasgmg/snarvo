// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using static Snarvo.Services.Localization;

namespace Snarvo.Editor;

/// <summary>How big copied, exported and dragged images are.</summary>
public enum ExportSize
{
    Original,
    Percent75,
    Percent50,
    MaxWidth1920,
    MaxWidth1280,
    MaxWidth800,
}

public static class ExportSizes
{
    public static IReadOnlyList<ExportSize> All { get; } = Enum.GetValues<ExportSize>();

    public static string Label(this ExportSize size) => size switch
    {
        ExportSize.Percent75 => "75 %",
        ExportSize.Percent50 => "50 %",
        ExportSize.MaxWidth1920 => F("Max {0} px bred", 1920),
        ExportSize.MaxWidth1280 => F("Max {0} px bred", 1280),
        ExportSize.MaxWidth800 => F("Max {0} px bred", 800),
        _ => "100 %",
    };

    /// <summary>For the menu entry "Storlek: …": the label without the word for wide.</summary>
    public static string ShortLabel(this ExportSize size) => size switch
    {
        ExportSize.MaxWidth1920 => "Max 1920 px",
        ExportSize.MaxWidth1280 => "Max 1280 px",
        ExportSize.MaxWidth800 => "Max 800 px",
        _ => size.Label(),
    };

    /// <summary>Scale factor for an image of the given width; images are only ever made smaller.</summary>
    public static float ScaleFor(this ExportSize size, float width) => size switch
    {
        ExportSize.Percent75 => 0.75f,
        ExportSize.Percent50 => 0.5f,
        ExportSize.MaxWidth1920 => Math.Min(1f, 1920 / width),
        ExportSize.MaxWidth1280 => Math.Min(1f, 1280 / width),
        ExportSize.MaxWidth800 => Math.Min(1f, 800 / width),
        _ => 1f,
    };
}
