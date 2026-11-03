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

    public GuideWindow(IEnumerable<string> ids)
    {
        InitializeComponent();
        Title = "Snarvo – Steg-för-steg-guide";
        WindowHelper.Prepare(this);

        // Oldest first: the order the steps were captured in.
        foreach (var id in ids.Order(StringComparer.Ordinal))
        {
            var step = new GuideStepItem(new LibraryItem(id));
            _steps.Add(step);
            _ = step.Image.LoadThumbnailAsync();
        }
        _steps.CollectionChanged += (_, _) => UpdateSteps();
        StepsList.ItemsSource = _steps;
        UpdateSteps();
        _savedSnapshot = Snapshot();

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
        PdfButton.IsEnabled = WordButton.IsEnabled = _steps.Count > 0 && !_saving;
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

    void SavePdf_Click(object sender, RoutedEventArgs e) => _ = SaveAsync(word: false);
    void SaveWord_Click(object sender, RoutedEventArgs e) => _ = SaveAsync(word: true);

    async Task SaveAsync(bool word)
    {
        if (_saving || _steps.Count == 0) return;

        var name = string.Concat(GuideTitle.Split(Path.GetInvalidFileNameChars())).Trim();
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = name.Length > 0 ? name : "Guide",
        };
        picker.FileTypeChoices.Add(word ? "Word-dokument" : "PDF-dokument", new List<string> { word ? ".docx" : ".pdf" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelper.Handle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        _saving = true;
        UpdateSteps();
        StatusText.Text = "Skapar dokumentet …";
        try
        {
            var guide = new Guide(GuideTitle, IntroBox.Text,
                _steps.Select(s => new GuideStep(s.Image.ImagePath, s.Text)).ToList());
            if (word) await GuideExport.SaveDocxAsync(guide, file.Path);
            else await GuideExport.SavePdfAsync(guide, file.Path);

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
        Snapshot() != _savedSnapshot &&
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
