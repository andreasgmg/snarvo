// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Text.Json;
using Microsoft.Win32;
using Snarvo.Editor;

namespace Snarvo.Services;

/// <summary>How "Dölj känsligt" and "Dölj ord eller namn" hide what they find.</summary>
public enum HideMethod { Cover, Pixelate, Blur }

public enum AppTheme { System, Light, Dark }

public sealed class AppSettings
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunValue = "Snarvo";

    // Colours and vectors are structs with public fields.
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IncludeFields = true };

    /// <summary>
    /// Settings, library and error log. The environment variable SNARVO_DATA moves it elsewhere,
    /// e.g. for testing without touching the real library.
    /// </summary>
    public static string DataFolder { get; } =
        Environment.GetEnvironmentVariable("SNARVO_DATA") is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : ResolveDataFolder();

    // The app was called SnipPlus before; its library and settings move along on first start.
    const string LegacyName = "SnipPlus";

    static string ResolveDataFolder()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var current = Path.Combine(root, "Snarvo");
        var legacy = Path.Combine(root, LegacyName);
        if (Directory.Exists(current) || !Directory.Exists(legacy)) return current;
        try
        {
            Directory.Move(legacy, current);
            return current;
        }
        catch (Exception)
        {
            // Something holds a file open (the old app still running?): keep using the old
            // folder rather than starting with an empty library, and try again next start.
            return legacy;
        }
    }

    /// <summary>Moves "start with Windows" from the old name to the new one.</summary>
    public static void MigrateAutostart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(LegacyName) is null) return;
            key.DeleteValue(LegacyName, throwOnMissingValue: false);
            key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --background");
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    /// <summary>True while the old SnipPlus app is still running next to this one.</summary>
    public static bool LegacyAppRunning()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName(LegacyName);
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }

    static string FilePath => Path.Combine(DataFolder, "settings.json");

    /// <summary>Open the editor for every Snipping Tool capture, not only ones started from Snarvo.</summary>
    public bool AutoOpenEditor { get; set; }

    /// <summary>Copy the edited image when its editor closes, unless it was already copied or shared.</summary>
    public bool CopyOnClose { get; set; } = true;

    /// <summary>
    /// Private mode: new captures are not kept in the library. They open in the editor and are
    /// deleted from disk when it closes, unless "Behåll i biblioteket" is chosen.
    /// </summary>
    public bool PrivateMode { get; set; }

    /// <summary>Delete library captures older than this many days; 0 keeps them for ever.</summary>
    public int KeepDays { get; set; }

    public static readonly int[] KeepDayChoices = [1, 7, 30];

    /// <summary>Ignore images copied by other apps than Snipping Tool.</summary>
    public bool OnlySnippingTool { get; set; } = true;

    /// <summary>Last used colour and sizes per tool, shared by all editor windows.</summary>
    public Dictionary<Tool, ToolStyle>? ToolStyles { get; set; }

    public List<StylePreset> Presets { get; set; } = [];

    /// <summary>Folder that "Spara i mappen" writes to without asking, e.g. one synced by OneDrive.</summary>
    public string? QuickSaveFolder { get; set; }

    /// <summary>Size of copied, exported and dragged images.</summary>
    public ExportSize ExportSize { get; set; }

    /// <summary>Solid boxes by default: pixelated and blurred text can sometimes be reconstructed.</summary>
    public HideMethod HideMethod { get; set; } = HideMethod.Cover;

    /// <summary>Step recording hides personal data in each picture straight away.</summary>
    public bool StepRecorderHideSensitive { get; set; } = true;

    /// <summary>Guides: heading colour (RRGGBB), logo file and organisation name.</summary>
    public string GuideAccent { get; set; } = "0078D4";
    public string? GuideLogo { get; set; }
    public string? GuideOrganization { get; set; }

    /// <summary>Light or dark windows; follows Windows unless chosen.</summary>
    public AppTheme Theme { get; set; }

    /// <summary>Preset whose frame effects new captures start with, if any.</summary>
    public string? DefaultPreset { get; set; }

    public static AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            if (File.Exists(FilePath))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
        Policy.Apply(settings);
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    public static bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is not null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
                key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --background");
            else
                key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }
}
