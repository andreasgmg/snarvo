// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

namespace Snarvo.Editor;

/// <summary>A named set of tool styles and frame effects ("mall").</summary>
public sealed class StylePreset
{
    public string Name { get; set; } = "";
    public Dictionary<Tool, ToolStyle> Styles { get; set; } = [];
    public FrameEffects Effects { get; set; } = new();
}
