// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using Microsoft.Win32;

namespace Snarvo.Services;

/// <summary>
/// Settings an organisation enforces through Group Policy or Intune: DWORD values under
/// HKLM\Software\Policies\Snarvo (whole computer), or HKCU\Software\Policies\Snarvo (one user).
/// A value set for the computer wins. Only administrators can write these keys, so a user
/// cannot switch an enforced setting off. The ADMX template in the policy folder describes them.
/// </summary>
public static class Policy
{
    const string KeyPath = @"Software\Policies\Snarvo";

    public const string PrivateModeName = "PrivateMode";
    public const string KeepDaysName = "KeepDays";
    public const string OnlySnippingToolName = "OnlySnippingTool";
    public const string HideMethodName = "HideMethod";
    public const string StepRecorderName = "AllowStepRecorder";

    static readonly Dictionary<string, int> Values = Read();

    static Dictionary<string, int> Read()
    {
        var values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // User first, so the computer-wide value overwrites it.
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = hive.OpenSubKey(KeyPath);
                if (key is null) continue;
                foreach (var name in key.GetValueNames())
                    if (key.GetValue(name) is int value) values[name] = value;
            }
            catch (Exception ex)
            {
                App.Log(ex);
            }
        }
        return values;
    }

    public static bool IsLocked(string name) => Values.ContainsKey(name);

    public static bool AnyLocked => Values.Count > 0;

    /// <summary>Step recording is allowed unless the organisation has turned it off.</summary>
    public static bool StepRecorderAllowed => !Values.TryGetValue(StepRecorderName, out var v) || v != 0;

    /// <summary>Overrides the user's own settings with the enforced ones.</summary>
    public static void Apply(AppSettings settings)
    {
        if (Values.TryGetValue(PrivateModeName, out var privateMode)) settings.PrivateMode = privateMode != 0;
        if (Values.TryGetValue(KeepDaysName, out var days)) settings.KeepDays = Math.Clamp(days, 0, 3650);
        if (Values.TryGetValue(OnlySnippingToolName, out var only)) settings.OnlySnippingTool = only != 0;
        if (Values.TryGetValue(HideMethodName, out var method) && Enum.IsDefined(typeof(HideMethod), method))
            settings.HideMethod = (HideMethod)method;
    }

    public const string LockedText = "Styrs av din organisation";
}
