// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Snarvo.Editor;
using Snarvo.Services;
using static Snarvo.Services.Localization;

namespace Snarvo;

/// <summary>All of Snarvo's settings in one place. Changes apply and are saved at once.</summary>
public sealed partial class SettingsWindow : Window
{
    readonly AppSettings _settings;
    // Set while the controls are filled in, so that does not count as the user changing them.
    bool _loading;

    public SettingsWindow()
    {
        InitializeComponent();
        Title = T("Snarvo – Inställningar");
        Localization.Translate(Root);
        WindowHelper.Prepare(this);
        _settings = App.Instance!.Settings;

        double scale = WindowHelper.Scale(this);
        WindowHelper.CenterAndResize(this, (int)(820 * scale), (int)(860 * scale));

        Load();
        Activated += (_, e) =>
        {
            // The tray menu and the library change some of these too.
            if (e.WindowActivationState != WindowActivationState.Deactivated) Load();
        };
    }

    void Load()
    {
        _loading = true;
        AutostartSwitch.IsOn = AppSettings.StartWithWindows;
        AutoOpenSwitch.IsOn = _settings.AutoOpenEditor;
        CopyOnCloseSwitch.IsOn = _settings.CopyOnClose;
        OnlySnippingToolSwitch.IsOn = _settings.OnlySnippingTool;
        HotkeyText.Text = App.HotkeyText;
        PrivateModeSwitch.IsOn = _settings.PrivateMode;
        RecorderHideSwitch.IsOn = _settings.StepRecorderHideSensitive;
        if (!Policy.StepRecorderAllowed)
        {
            RecorderHideSwitch.IsEnabled = false;
            RecorderHideText.Text = F("Stegregistrering är avstängd. {0}.", T(Policy.LockedText));
        }
        HideMethodBox.SelectedIndex = (int)_settings.HideMethod;
        ThemeBox.SelectedIndex = (int)_settings.Theme;
        LabelSwitch.IsOn = App.Instance?.ToolStyles.TryGetValue(Tool.Text, out var text) == true && text.Label;
        DataFolderText.Text = Library.Root;
        VersionText.Text = $"Snarvo {typeof(App).Assembly.GetName().Version?.ToString(3)}";

        KeepDaysBox.Items.Clear();
        var choices = new List<int> { 0 };
        choices.AddRange(AppSettings.KeepDayChoices);
        // An organisation may enforce a period that is not one of the usual choices.
        if (!choices.Contains(_settings.KeepDays)) choices.Add(_settings.KeepDays);
        foreach (var days in choices)
            KeepDaysBox.Items.Add(new ComboBoxItem { Content = days == 0 ? T("Aldrig") : F("Efter {0}", App.DaysText(days)), Tag = days });
        KeepDaysBox.SelectedIndex = choices.IndexOf(_settings.KeepDays);

        Lock(PrivateModeSwitch, PrivateModeText, Policy.PrivateModeName);
        Lock(KeepDaysBox, KeepDaysText, Policy.KeepDaysName);
        Lock(OnlySnippingToolSwitch, OnlySnippingToolText, Policy.OnlySnippingToolName);
        Lock(HideMethodBox, HideMethodText, Policy.HideMethodName);
        PolicyBar.IsOpen = Policy.AnyLocked;
        _loading = false;
    }

    static void Lock(Control control, TextBlock description, string policy)
    {
        if (!Policy.IsLocked(policy)) return;
        control.IsEnabled = false;
        if (!description.Text.EndsWith(T(Policy.LockedText) + "."))
            description.Text += $" {T(Policy.LockedText)}.";
    }

    public void ShowAbout()
    {
        // After layout, so the header has a position to scroll to.
        DispatcherQueue.TryEnqueue(() =>
        {
            var point = AboutHeader.TransformToVisual(Scroller.Content as UIElement).TransformPoint(new Windows.Foundation.Point(0, 0));
            Scroller.ChangeView(null, point.Y, null);
        });
    }

    void Save() => App.Instance?.SaveSettingsSoon();

    void AutostartSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try
        {
            AppSettings.StartWithWindows = AutostartSwitch.IsOn;
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    void AutoOpenSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.AutoOpenEditor = AutoOpenSwitch.IsOn;
        Save();
    }

    void RecorderHideSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.StepRecorderHideSensitive = RecorderHideSwitch.IsOn;
        Save();
    }

    void CopyOnCloseSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.CopyOnClose = CopyOnCloseSwitch.IsOn;
        Save();
    }

    void OnlySnippingToolSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || Policy.IsLocked(Policy.OnlySnippingToolName)) return;
        _settings.OnlySnippingTool = OnlySnippingToolSwitch.IsOn;
        Save();
    }

    void PrivateModeSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || PrivateModeSwitch.IsOn == _settings.PrivateMode) return;
        App.Instance?.TogglePrivateMode();
    }

    async void KeepDaysBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || KeepDaysBox.SelectedItem is not ComboBoxItem { Tag: int days } || days == _settings.KeepDays) return;
        if (App.Instance is { } app) await app.SetKeepDaysAsync(Root.XamlRoot, days);
        Load();
    }

    void HideMethodBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || HideMethodBox.SelectedIndex < 0 || Policy.IsLocked(Policy.HideMethodName)) return;
        _settings.HideMethod = (HideMethod)HideMethodBox.SelectedIndex;
        Save();
    }

    void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedIndex < 0) return;
        _settings.Theme = (AppTheme)ThemeBox.SelectedIndex;
        Save();
        WindowHelper.ApplyThemeToAll();
    }

    void LabelSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading || App.Instance is not { } app || !app.ToolStyles.TryGetValue(Tool.Text, out var style)) return;
        style.Label = LabelSwitch.IsOn;
        Save();
    }

    async void ResetStyles_Click(object sender, RoutedEventArgs e)
    {
        if (App.Instance is not { } app) return;
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = T("Återställ färger och storlekar?"),
            Content = T("Alla verktyg får tillbaka sina ursprungliga färger och storlekar. Dina mallar och redan ritade bilder ändras inte."),
            PrimaryButtonText = T("Återställ"),
            CloseButtonText = T("Avbryt"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        // Changed in place: open editors share these objects.
        foreach (var (tool, style) in ToolStyle.CreateDefaults())
        {
            if (!app.ToolStyles.TryGetValue(tool, out var current)) continue;
            current.Color = style.Color;
            current.Thickness = style.Thickness;
            current.FontSize = style.FontSize;
            current.Label = style.Label;
        }
        Save();
        Load();
        ResetStylesText.Text = T("Återställt. Öppna editorfönster använder standardvärdena för nästa markering.");
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Library.Root);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Library.Root}\"") { UseShellExecute = true });
    }
}
