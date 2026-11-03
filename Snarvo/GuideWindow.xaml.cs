// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Snarvo.Services;
using Windows.Storage.Pickers;

namespace Snarvo;

public sealed class GuideStepItem(LibraryItem image) : INotifyPropertyChanged
{
    int _number;

    public LibraryItem Image { get; } = image;
    public string Text { get; set; } = "";

    public override string ToString() => Heading;

    public int Number
    {
        get => _number;
        set
        {
            if (_number == value) return;
            _number = value;
            PropertyChanged?.Invoke(this, new(nameof(Heading)));
        }
    }

    public string Heading => $"Steg {Number}";

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Turns a few screenshots into a step-by-step guide, saved as PDF or Word.</summary>
public sealed partial class GuideWindow : Window
{
    readonly ObservableCollection<GuideStepItem> _steps = [];
    string? _savedPath;
    string _savedSnapshot;
    bool _saving, _closeConfirmed;

    /// <param name="texts">Suggested text per capture, e.g. from step recording.</param>
    /// <param name="recorded">The steps were just recorded: nothing of them is saved anywhere yet.</param>
    /// <param name="deleteOnClose">Private mode: the captures only exist for this guide.</param>
    public GuideWindow(IEnumerable<string> ids, IReadOnlyDictionary<string, string>? texts = null,
        bool recorded = false, bool deleteOnClose = false)
    {
        InitializeComponent();
        Title = "Snarvo – Steg-för-steg-guide";
        WindowHelper.Prepare(this);

        // Oldest first: the order the steps were captured in.
        var idList = ids.Order(StringComparer.Ordinal).ToList();
        foreach (var id in idList)
        {
            var step = new GuideStepItem(new LibraryItem(id)) { Text = texts?.GetValueOrDefault(id) ?? "" };
            _steps.Add(step);
            _ = step.Image.LoadThumbnailAsync();
        }
        _steps.CollectionChanged += (_, _) => UpdateSteps();
        StepsList.ItemsSource = _steps;
        UpdateSteps();
        // A recorded guide is unsaved work from the start, so closing it asks first.
        _savedSnapshot = recorded ? "" : Snapshot();
        if (recorded)
            StatusText.Text = $"{idList.Count} steg inspelade. Texterna är förslag – skriv om dem som du vill." +
                              (deleteOnClose ? " Privat läge: bilderna raderas när guiden stängs." : "");
        if (deleteOnClose)
            Closed += (_, _) =>
            {
                foreach (var id in idList)
                    if (App.Instance?.IsOpenInEditor(id) != true) Library.Delete(id);
            };

        Library.Changed += OnLibraryChanged;
        Closed += (_, _) => Library.Changed -= OnLibraryChanged;
        AppWindow.Closing += AppWindow_Closing;

        double scale = WindowHelper.Scale(this);
        WindowHelper.CenterAndResize(this, (int)(980 * scale), (int)(860 * scale));
    }

    string GuideTitle => string.IsNullOrWhiteSpace(TitleBox.Text) ? "Guide" : TitleBox.Text.Trim();

    void UpdateSteps()
    {
        for (int i = 0; i < _steps.Count; i++) _steps[i].Number = i + 1;
        PdfButton.IsEnabled = WordButton.IsEnabled = HtmlButton.IsEnabled = _steps.Count > 0 && !_saving;
        if (_steps.Count == 0) StatusText.Text = "Guiden har inga steg. Markera skärmdumpar i biblioteket och välj Guide.";
    }

    /// <summary>Edits in the editor show up here; deleted screenshots drop out of the guide.</summary>
    void OnLibraryChanged() => DispatcherQueue.TryEnqueue(async () =>
    {
        foreach (var step in _steps.Where(s => !File.Exists(Library.OriginalPath(s.Image.Id))).ToList())
            _steps.Remove(step);
        foreach (var step in _steps.ToList())
            if (File.GetLastWriteTimeUtc(step.Image.ImagePath) != step.Image.Stamp)
                await step.Image.LoadThumbnailAsync();
    });

    static GuideStepItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as GuideStepItem;

    void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } step && _steps.IndexOf(step) is var i and > 0) _steps.Move(i, i - 1);
    }

    void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } step && _steps.IndexOf(step) is var i && i >= 0 && i < _steps.Count - 1) _steps.Move(i, i + 1);
    }

    void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } step) _steps.Remove(step);
    }

    void EditImage_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } step) _ = App.Instance?.OpenEditorAsync(step.Image.Id);
    }

    enum Format { Pdf, Word, Html }

    void SavePdf_Click(object sender, RoutedEventArgs e) => _ = SaveAsync(Format.Pdf);
    void SaveWord_Click(object sender, RoutedEventArgs e) => _ = SaveAsync(Format.Word);
    void SaveHtml_Click(object sender, RoutedEventArgs e) => _ = SaveAsync(Format.Html);

    async Task SaveAsync(Format format)
    {
        if (_saving || _steps.Count == 0) return;

        var name = string.Concat(GuideTitle.Split(Path.GetInvalidFileNameChars())).Trim();
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = name.Length > 0 ? name : "Guide",
        };
        var (kind, extension) = format switch
        {
            Format.Word => ("Word-dokument", ".docx"),
            Format.Html => ("Webbsida", ".html"),
            _ => ("PDF-dokument", ".pdf"),
        };
        picker.FileTypeChoices.Add(kind, new List<string> { extension });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelper.Handle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        _saving = true;
        UpdateSteps();
        StatusText.Text = "Skapar dokumentet …";
        try
        {
            var settings = App.Instance?.Settings;
            var look = settings is null ? GuideLook.Default
                : new GuideLook(settings.GuideAccent, settings.GuideLogo, settings.GuideOrganization);
            var guide = new Guide(GuideTitle, IntroBox.Text,
                _steps.Select(s => new GuideStep(s.Image.ImagePath, s.Text)).ToList(), look);
            switch (format)
            {
                case Format.Word: await GuideExport.SaveDocxAsync(guide, file.Path); break;
                case Format.Html: await GuideExport.SaveHtmlAsync(guide, file.Path); break;
                default: await GuideExport.SavePdfAsync(guide, file.Path); break;
            }

            _savedPath = file.Path;
            _savedSnapshot = Snapshot();
            StatusText.Text = $"Sparad: {file.Name} ({Math.Max(1, new FileInfo(file.Path).Length / 1024)} KB)";
            OpenButton.IsEnabled = RevealButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            StatusText.Text = "Kunde inte spara dokumentet. Är filen öppen i ett annat program?";
        }
        finally
        {
            _saving = false;
            UpdateSteps();
        }
    }

    // ───────────────────────────── look ─────────────────────────────

    static readonly (string Hex, string Name)[] Accents =
    [
        ("0078D4", "Blå"), ("1C7ED6", "Ljusblå"), ("2F9E44", "Grön"), ("C8432C", "Röd"),
        ("F08C00", "Orange"), ("7048E8", "Lila"), ("212529", "Svart"),
    ];

    bool _lookLoading;

    void LookFlyout_Opening(object sender, object e)
    {
        if (App.Instance?.Settings is not { } settings) return;
        _lookLoading = true;
        var logo = settings.GuideLogo;
        LogoText.Text = string.IsNullOrEmpty(logo) ? "Ingen logga. Den visas uppe till höger i guiden." : Path.GetFileName(logo);
        OrganizationBox.Text = settings.GuideOrganization ?? "";

        AccentSwatches.Children.Clear();
        foreach (var (hex, label) in Accents)
        {
            var color = Microsoft.UI.ColorHelper.FromArgb(255, Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..], 16));
            bool selected = string.Equals(settings.GuideAccent, hex, StringComparison.OrdinalIgnoreCase);
            var button = new Button
            {
                Width = 34, Height = 34, Padding = new Thickness(0),
                BorderThickness = new Thickness(selected ? 2 : 1),
                Content = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(color) },
                IsEnabled = !Policy.IsLocked(Policy.GuideAccentName),
            };
            if (selected) button.BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            ToolTipService.SetToolTip(button, label);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
            button.Click += (_, _) =>
            {
                settings.GuideAccent = hex;
                App.Instance?.SaveSettingsSoon();
                LookFlyout_Opening(sender, e);
            };
            AccentSwatches.Children.Add(button);
        }

        ChooseLogoButton.IsEnabled = RemoveLogoButton.IsEnabled = !Policy.IsLocked(Policy.GuideLogoName);
        RemoveLogoButton.IsEnabled &= !string.IsNullOrEmpty(logo);
        OrganizationBox.IsEnabled = !Policy.IsLocked(Policy.GuideOrganizationName);
        bool locked = Policy.IsLocked(Policy.GuideLogoName) || Policy.IsLocked(Policy.GuideAccentName) || Policy.IsLocked(Policy.GuideOrganizationName);
        LookPolicyText.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        LookPolicyText.Text = $"Delar av utseendet: {Policy.LockedText.ToLowerInvariant()}.";
        _lookLoading = false;
    }

    async void ChooseLogo_Click(object sender, RoutedEventArgs e)
    {
        if (App.Instance?.Settings is not { } settings) return;
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        foreach (var type in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" }) picker.FileTypeFilter.Add(type);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelper.Handle(this));
        if (await picker.PickSingleFileAsync() is not { } file) return;
        settings.GuideLogo = file.Path;
        App.Instance.SaveSettingsSoon();
        LookFlyout_Opening(sender, e);
    }

    void RemoveLogo_Click(object sender, RoutedEventArgs e)
    {
        if (App.Instance?.Settings is not { } settings) return;
        settings.GuideLogo = null;
        App.Instance.SaveSettingsSoon();
        LookFlyout_Opening(sender, e);
    }

    void OrganizationBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_lookLoading || App.Instance?.Settings is not { } settings) return;
        settings.GuideOrganization = OrganizationBox.Text.Trim().Length == 0 ? null : OrganizationBox.Text.Trim();
        App.Instance.SaveSettingsSoon();
    }

    void Open_Click(object sender, RoutedEventArgs e)
    {
        if (_savedPath is not null) Process.Start(new ProcessStartInfo(_savedPath) { UseShellExecute = true });
    }

    void Reveal_Click(object sender, RoutedEventArgs e)
    {
        if (_savedPath is not null) Process.Start("explorer.exe", $"/select,\"{_savedPath}\"");
    }

    // Everything the user typed or arranged, to tell whether it has been saved.
    string Snapshot() => string.Join("\u0001", new[] { TitleBox.Text, IntroBox.Text }
        .Concat(_steps.Select(s => s.Image.Id + "\u0002" + s.Text)));

    bool HasUnsavedText =>
        Snapshot() != _savedSnapshot && _steps.Count > 0 &&
        (TitleBox.Text.Trim().Length > 0 || IntroBox.Text.Trim().Length > 0 || _steps.Any(s => s.Text.Trim().Length > 0));

    async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed || !HasUnsavedText) return;
        args.Cancel = true;
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Stäng guiden?",
            Content = "Det du har skrivit är inte sparat i något dokument än.",
            PrimaryButtonText = "Stäng ändå",
            CloseButtonText = "Avbryt",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        _closeConfirmed = true;
        Close();
    }
}
