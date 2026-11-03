// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

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
        ExportSize.MaxWidth1920 => "Max 1920 px bred",
        ExportSize.MaxWidth1280 => "Max 1280 px bred",
        ExportSize.MaxWidth800 => "Max 800 px bred",
        _ => "100 %",
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
