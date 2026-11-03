// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Snarvo.Services;

/// <summary>
/// Snarvo is written in Swedish; on a Windows set to another language it shows English instead.
/// Texts are looked up by their Swedish wording in <see cref="English"/>, so the Swedish source
/// stays readable. A text without a translation is shown in Swedish.
/// The environment variable SNARVO_LANG (sv or en) overrides the choice, for testing.
/// </summary>
public static partial class Localization
{
    public static bool IsEnglish { get; } = Environment.GetEnvironmentVariable("SNARVO_LANG") is { Length: > 0 } forced
        ? !forced.StartsWith("sv", StringComparison.OrdinalIgnoreCase)
        : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName != "sv";

    /// <summary>Dates and numbers: Swedish style, or the user's own culture in English.</summary>
    public static CultureInfo Culture { get; } = IsEnglish ? CultureInfo.CurrentCulture : CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>The text in the user's language.</summary>
    public static string T(string swedish)
    {
        if (!IsEnglish || string.IsNullOrEmpty(swedish)) return swedish;
        if (English.TryGetValue(swedish, out var english)) return english;
#if DEBUG
        if (Missing.Add(swedish)) System.Diagnostics.Debug.WriteLine($"Ingen översättning: {swedish}");
#endif
        return swedish;
    }

    /// <summary>A format string in the user's language, filled in: F("Dolde {0} uppgifter", n).</summary>
    public static string F(string swedishFormat, params object?[] args) => string.Format(Culture, T(swedishFormat), args);

#if DEBUG
    static readonly HashSet<string> Missing = [];
#endif

    /// <summary>
    /// Translates the texts of a XAML tree written in Swedish: text, content, headers, placeholders,
    /// tooltips, accessible names and the menus and flyouts attached to buttons.
    /// </summary>
    public static void Translate(object? node)
    {
        if (!IsEnglish || node is null) return;
        switch (node)
        {
            case string:
                return;
            case TextBlock text:
                text.Text = T(text.Text);
                break;
            case TextBox box:
                box.PlaceholderText = T(box.PlaceholderText);
                if (box.Header is string header) box.Header = T(header);
                break;
            case AutoSuggestBox suggest:
                suggest.PlaceholderText = T(suggest.PlaceholderText);
                break;
            case ToggleSwitch toggle:
                if (toggle.OnContent is string on) toggle.OnContent = T(on);
                if (toggle.OffContent is string off) toggle.OffContent = T(off);
                if (toggle.Header is string toggleHeader) toggle.Header = T(toggleHeader);
                break;
            case Slider slider:
                if (slider.Header is string sliderHeader) slider.Header = T(sliderHeader);
                break;
            case InfoBar info:
                info.Title = T(info.Title);
                info.Message = T(info.Message);
                break;
            case MenuFlyoutSubItem sub:
                sub.Text = T(sub.Text);
                foreach (var item in sub.Items) Translate(item);
                break;
            case MenuFlyoutItem item:
                item.Text = T(item.Text);
                break;
            case MenuFlyout menu:
                foreach (var item in menu.Items) Translate(item);
                return;
            case Flyout flyout:
                Translate(flyout.Content);
                return;
        }

        if (node is DependencyObject d)
        {
            if (ToolTipService.GetToolTip(d) is string tip) ToolTipService.SetToolTip(d, T(tip));
            var name = AutomationProperties.GetName(d);
            if (!string.IsNullOrEmpty(name)) AutomationProperties.SetName(d, T(name));
            var help = AutomationProperties.GetHelpText(d);
            if (!string.IsNullOrEmpty(help)) AutomationProperties.SetHelpText(d, T(help));
        }

        switch (node)
        {
            case Panel panel:
                foreach (var child in panel.Children) Translate(child);
                break;
            case Border border:
                Translate(border.Child);
                break;
            case UserControl user:
                Translate(user.Content);
                break;
            case ItemsControl items:
                foreach (var item in items.Items) Translate(item);
                break;
            case ContentControl content:
                if (content.Content is string s) content.Content = T(s);
                else Translate(content.Content);
                // DropDownButton is a Button; SplitButton has a flyout of its own.
                if (content is Button { Flyout: { } buttonFlyout }) Translate(buttonFlyout);
                if (content is SplitButton { Flyout: { } splitFlyout }) Translate(splitFlyout);
                break;
            case ContentPresenter presenter:
                Translate(presenter.Content);
                break;
        }
    }

    /// <summary>For list items made from a template: translates each one as it appears.</summary>
    public static void TranslateItems(ListViewBase list)
    {
        if (!IsEnglish) return;
        list.ContainerContentChanging += (_, args) =>
        {
            if (!args.InRecycleQueue) Translate(args.ItemContainer.ContentTemplateRoot);
        };
    }
}
