// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Snarvo.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;

namespace Snarvo;

public sealed class LibraryItem(string id)
{
    public string Id { get; } = id;
    public string Label { get; } = Library.DisplayName(id);
    public BitmapImage Thumbnail { get; } = new() { DecodePixelWidth = 440 };
    public DateTime Stamp { get; set; }

    /// <summary>Text found in the image by OCR; empty until it has been indexed.</summary>
    public string Text { get; set; } = TextIndex.Read(id);

    /// <summary>True when every word of the query occurs in the image text or the date.</summary>
    public bool Matches(string[] words) =>
        words.All(w => Text.Contains(w, StringComparison.CurrentCultureIgnoreCase)
                    || Label.Contains(w, StringComparison.CurrentCultureIgnoreCase));

    public string ImagePath => File.Exists(Library.OutputPath(Id)) ? Library.OutputPath(Id) : Library.OriginalPath(Id);

    /// <summary>What screen readers announce for the item (otherwise the class name).</summary>
    public override string ToString() => $"Skärmdump {Label}";

    public async Task LoadThumbnailAsync()
    {
        try
        {
            Stamp = File.GetLastWriteTimeUtc(ImagePath);
            // Read into memory so the file is not kept locked while the editor saves over it.
            var bytes = await File.ReadAllBytesAsync(ImagePath);
            using var stream = new MemoryStream(bytes).AsRandomAccessStream();
            await Thumbnail.SetSourceAsync(stream);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }
}

public sealed partial class LibraryWindow : Window
{
    // Every capture, newest first; _items is the part of it that matches the search and is shown.
    readonly List<LibraryItem> _all = [];
    readonly ObservableCollection<LibraryItem> _items = [];
    LibraryItem? _contextItem;
    bool _refreshQueued;

    public LibraryWindow()
    {
        InitializeComponent();
        Title = "Snarvo – Bibliotek";
        WindowHelper.Prepare(this);

        EmptyHint.Text = $"Ta en skärmdump med {App.HotkeyText}, klicka på Snarvo-ikonen i systemfältet, " +
                         "eller använd Win+Shift+S som vanligt – den hamnar här automatiskt.";

        Items.ItemsSource = _items;
        BuildRetentionMenu();
        Library.Changed += OnLibraryChanged;
        TextIndex.Indexed += OnTextIndexed;
        Closed += (_, _) =>
        {
            Library.Changed -= OnLibraryChanged;
            TextIndex.Indexed -= OnTextIndexed;
        };

        double scale = WindowHelper.Scale(this);
        WindowHelper.CenterAndResize(this, (int)(1080 * scale), (int)(720 * scale));
        _ = RefreshAsync();
    }

    void OnLibraryChanged()
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        DispatcherQueue.TryEnqueue(async () =>
        {
            _refreshQueued = false;
            await RefreshAsync();
        });
    }

    void OnTextIndexed(string id)
    {
        var item = _all.FirstOrDefault(i => i.Id == id);
        if (item is null) return;
        item.Text = TextIndex.Read(id);
        if (SearchBox.Text.Length > 0) ApplyFilter();
    }

    async Task RefreshAsync()
    {
        var ids = Library.List();
        var existing = _all.ToDictionary(i => i.Id);

        var toLoad = new List<LibraryItem>();
        _all.Clear();
        foreach (var id in ids)
        {
            if (existing.TryGetValue(id, out var item))
            {
                if (File.GetLastWriteTimeUtc(item.ImagePath) != item.Stamp) toLoad.Add(item);
            }
            else
            {
                item = new LibraryItem(id);
                toLoad.Add(item);
            }
            _all.Add(item);
        }

        EmptyState.Visibility = ids.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilter();

        foreach (var item in toLoad) await item.LoadThumbnailAsync();
    }

    void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => ApplyFilter();

    /// <summary>Shows the matching items, editing the visible list in place so thumbnails do not flicker.</summary>
    void ApplyFilter()
    {
        var words = SearchBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var visible = words.Length == 0 ? _all : _all.Where(i => i.Matches(words)).ToList();

        for (int i = _items.Count - 1; i >= 0; i--)
            if (!visible.Contains(_items[i])) _items.RemoveAt(i);
        for (int i = 0; i < visible.Count; i++)
        {
            if (i < _items.Count && ReferenceEquals(_items[i], visible[i])) continue;
            int current = _items.IndexOf(visible[i]);
            if (current >= 0) _items.Move(current, i);
            else _items.Insert(i, visible[i]);
        }

        Subtitle.Text = words.Length > 0
            ? $"{visible.Count} av {_all.Count} skärmdumpar"
            : _all.Count == 1 ? "1 skärmdump" : $"{_all.Count} skärmdumpar";
        if (App.Instance?.Settings is { } settings)
        {
            if (settings.KeepDays > 0) Subtitle.Text += $" · raderas efter {DaysText(settings.KeepDays)}";
            if (settings.PrivateMode) Subtitle.Text += " · privat läge, nya sparas inte";
        }
        NoResults.Text = $"Ingen skärmdump innehåller \"{SearchBox.Text.Trim()}\". Texten i nya bilder läses in några sekunder efter att de tagits.";
        NoResults.Visibility = words.Length > 0 && visible.Count == 0 && _all.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Like Explorer: a click selects (Ctrl/Shift for several, e.g. to combine), a double-click opens.
    void Items_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is LibraryItem item)
            _ = App.Instance?.OpenEditorAsync(item.Id);
    }

    void Items_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            foreach (var item in Items.SelectedItems.OfType<LibraryItem>())
                _ = App.Instance?.OpenEditorAsync(item.Id);
        }
        else if (e.Key == VirtualKey.Delete)
        {
            e.Handled = true;
            _ = DeleteAsync(Items.SelectedItems.OfType<LibraryItem>().ToList());
        }
    }

    void Item_ContextRequested(UIElement sender, ContextRequestedEventArgs args) =>
        _contextItem = (sender as FrameworkElement)?.DataContext as LibraryItem;

    void MenuEdit_Click(object sender, RoutedEventArgs e)
    {
        if (_contextItem is { } item) _ = App.Instance?.OpenEditorAsync(item.Id);
    }

    async void MenuCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_contextItem is not { } item) return;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(item.ImagePath);
            var package = new DataPackage();
            package.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
            package.SetStorageItems([file]);
            App.Instance?.IgnoreClipboardFor(TimeSpan.FromSeconds(2));
            Clipboard.SetContent(package);
            Clipboard.Flush();
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    void MenuReveal_Click(object sender, RoutedEventArgs e)
    {
        if (_contextItem is { } item)
            Process.Start("explorer.exe", $"/select,\"{item.ImagePath}\"");
    }

    void MenuDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_contextItem is not { } item) return;
        var selected = Items.SelectedItems.OfType<LibraryItem>().ToList();
        _ = DeleteAsync(selected.Contains(item) ? selected : [item]);
    }

    async Task DeleteAsync(IReadOnlyList<LibraryItem> items)
    {
        if (items.Count == 0) return;
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = items.Count == 1 ? "Ta bort skärmdumpen?" : $"Ta bort {items.Count} skärmdumpar?",
            Content = "Det går inte att ångra.",
            PrimaryButtonText = "Ta bort",
            CloseButtonText = "Avbryt",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        foreach (var item in items) Library.Delete(item.Id);
    }

    void Capture_Click(object sender, RoutedEventArgs e) => App.Instance?.StartCapture();
    void Video_Click(object sender, RoutedEventArgs e) => _ = VideoWindow.PickAndOpenAsync(this);

    void Items_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CombineButton.IsEnabled = Items.SelectedItems.Count >= 2;
        GuideButton.IsEnabled = Items.SelectedItems.Count >= 1;
    }

    void Guide_Click(object sender, RoutedEventArgs e)
    {
        var ids = Items.SelectedItems.OfType<LibraryItem>().Select(i => i.Id).ToList();
        if (ids.Count > 0) WindowHelper.BringToFront(new GuideWindow(ids));
    }

    void CombineSideBySide_Click(object sender, RoutedEventArgs e) => _ = CombineAsync(sideBySide: true);
    void CombineStacked_Click(object sender, RoutedEventArgs e) => _ = CombineAsync(sideBySide: false);

    async Task CombineAsync(bool sideBySide)
    {
        var ids = Items.SelectedItems.OfType<LibraryItem>().Select(i => i.Id).ToList();
        if (ids.Count < 2) return;
        try
        {
            var id = await Library.CombineAsync(ids, sideBySide);
            if (App.Instance is { } app) await app.OpenEditorAsync(id);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    void Items_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var paths = e.Items.OfType<LibraryItem>().Select(i => i.ImagePath).ToList();
        if (paths.Count == 0)
        {
            e.Cancel = true;
            return;
        }
        e.Data.RequestedOperation = DataPackageOperation.Copy;
        // Files are resolved only when the drop target asks for them.
        e.Data.SetDataProvider(StandardDataFormats.StorageItems, async request =>
        {
            var deferral = request.GetDeferral();
            try
            {
                var files = new List<IStorageItem>();
                foreach (var path in paths) files.Add(await StorageFile.GetFileFromPathAsync(path));
                request.SetData(files);
            }
            catch (Exception ex)
            {
                App.Log(ex);
            }
            finally
            {
                deferral.Complete();
            }
        });
    }

    async void Open_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        foreach (var type in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" })
            picker.FileTypeFilter.Add(type);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelper.Handle(this));

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        try
        {
            var id = await Library.ImportFileAsync(file.Path, temporary: App.Instance?.Settings.PrivateMode == true);
            if (id is not null && App.Instance is { } app) await app.OpenEditorAsync(id);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    // ───────────────────────────── automatic clean-up ─────────────────────────────

    static string DaysText(int days) => App.DaysText(days);

    void BuildRetentionMenu()
    {
        RetentionMenu.Items.Clear();
        var current = App.Instance?.Settings.KeepDays ?? 0;
        RetentionButton.IsEnabled = !Policy.IsLocked(Policy.KeepDaysName);
        foreach (var days in new[] { 0 }.Concat(AppSettings.KeepDayChoices.Reverse()))
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = days == 0 ? "Behåll alla skärmdumpar" : $"Radera skärmdumpar äldre än {DaysText(days)}",
                GroupName = "Retention",
                IsChecked = days == current,
            };
            item.Click += (_, _) => _ = SetKeepDaysAsync(days);
            RetentionMenu.Items.Add(item);
        }
        ToolTipService.SetToolTip(RetentionButton, current == 0
            ? "Automatisk rensning: av – alla skärmdumpar sparas tills du tar bort dem"
            : $"Automatisk rensning: skärmdumpar raderas när de är {DaysText(current)} gamla");
    }

    /// <summary>Turns on clean-up after asking, since it deletes old captures straight away.</summary>
    async Task SetKeepDaysAsync(int days)
    {
        if (App.Instance is not { } app) return;
        await app.SetKeepDaysAsync(Root.XamlRoot, days);
        BuildRetentionMenu();
        ApplyFilter();
    }

    /// <summary>The settings window changed the clean-up period.</summary>
    public void OnKeepDaysChanged()
    {
        BuildRetentionMenu();
        ApplyFilter();
    }

    void Settings_Click(object sender, RoutedEventArgs e) => App.Instance?.ShowSettings();

    void Folder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Library.Root);
        Process.Start("explorer.exe", $"\"{Library.Root}\"");
    }
}
