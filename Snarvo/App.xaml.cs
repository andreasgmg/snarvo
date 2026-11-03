// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Windows.AppLifecycle;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Snarvo.Editor;
using Snarvo.Native;
using Snarvo.Services;
using Windows.ApplicationModel.DataTransfer;

namespace Snarvo;

public partial class App : Application
{
    public const string HotkeyText = "Ctrl+Alt+S";
    const int CaptureHotkey = 1;

    // Process names Snipping Tool has used across Windows 10/11 releases.
    static readonly string[] SnippingToolProcesses = ["SnippingTool", "ScreenClippingHost", "ScreenSketch"];

    public static App? Instance { get; private set; }
    public static string IconPath => Path.Combine(AppContext.BaseDirectory, "Assets", "Snarvo.ico");

    public AppSettings Settings { get; private set; } = new();

    /// <summary>Current colour and sizes per tool; one shared set so every editor window agrees.</summary>
    public Dictionary<Tool, ToolStyle> ToolStyles { get; private set; } = ToolStyle.CreateDefaults();

    readonly Dictionary<string, EditorWindow> _editors = new();
    DispatcherQueue _queue = null!;
    DispatcherQueueTimer _clipboardDebounce = null!;
    DispatcherQueueTimer _saveDebounce = null!;
    DispatcherQueueTimer _cleanupTimer = null!;
    TrayHost? _tray;
    LibraryWindow? _library;
    bool _captureRequested;    DateTime _captureDeadline;
    DateTime _ignoreClipboardUntil;
    string? _lastCaptureId;

    public App()
    {
        Instance = this;
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log(e.Exception);
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // The app lives in the tray, so closing the last window must not quit.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        _queue = DispatcherQueue.GetForCurrentThread();
        Settings = AppSettings.Load();
        AppSettings.MigrateAutostart();
        ToolStyles = ToolStyle.WithDefaults(Settings.ToolStyles);
        Settings.ToolStyles = ToolStyles;

        _saveDebounce = _queue.CreateTimer();
        _saveDebounce.Interval = TimeSpan.FromSeconds(1);
        _saveDebounce.IsRepeating = false;
        _saveDebounce.Tick += (_, _) => SaveSettings();

        _clipboardDebounce = _queue.CreateTimer();
        _clipboardDebounce.Interval = TimeSpan.FromMilliseconds(300);
        _clipboardDebounce.IsRepeating = false;
        _clipboardDebounce.Tick += async (_, _) => await ImportFromClipboardAsync();

        _tray = new TrayHost(IconPath, $"Snarvo – {HotkeyText} för ny skärmdump");
        _tray.Clicked += StartCapture;
        _tray.HotkeyPressed += _ => StartCapture();
        _tray.BalloonClicked += () =>
        {
            if (_lastCaptureId is { } id) _ = OpenEditorAsync(id);
            else ShowLibrary();
        };
        _tray.ClipboardUpdated += OnClipboardUpdated;
        _tray.MenuProvider = BuildTrayMenu;

        // Private captures left behind by a crash, old dragged-out files and captures past the
        // chosen age are removed first, and then once an hour while the app runs.
        Library.DeleteTemporary();
        CleanUp();
        _cleanupTimer = _queue.CreateTimer();
        _cleanupTimer.Interval = TimeSpan.FromHours(1);
        _cleanupTimer.Tick += (_, _) => CleanUp();
        _cleanupTimer.Start();

        // Keep the search index current: new captures and saved edits both change the library.
        Library.Changed += () => _queue.TryEnqueue(TextIndex.Update);
        TextIndex.Update();

        bool hotkeyOk = _tray.RegisterHotkey(CaptureHotkey, Win32.MOD_CONTROL | Win32.MOD_ALT, 'S');
        var commandLine = Environment.GetCommandLineArgs().Skip(1).ToList();
        bool background = commandLine.Contains("--background");
        var files = commandLine.Where(IsOpenableFile).ToList();

#if DEBUG
        // Developer aid: Snarvo.exe --record-test bild.png – records two steps from a picture
        // instead of the screen (no mouse hook), then opens the guide.
        if (commandLine.Count >= 2 && commandLine[0] == "--record-test")
        {
            _ = RecordTestAsync(commandLine[1]);
            return;
        }
#endif
        if (files.Count > 0)
        {
            foreach (var file in files) _ = OpenFileAsync(file);
        }
        else if (commandLine.Contains("--settings"))
        {
            ShowSettings();
        }
        else if (!background)
        {
            ShowLibrary();
            _tray.ShowBalloon("Snarvo körs i systemfältet",
                hotkeyOk
                    ? $"Tryck {HotkeyText} för en ny skärmdump, eller använd Win+Shift+S som vanligt."
                    : $"{HotkeyText} används redan av ett annat program. Klicka på ikonen för att ta en skärmdump.");
        }
        else if (!hotkeyOk)
        {
            _tray.ShowBalloon("Snarvo", $"{HotkeyText} används redan av ett annat program.");
        }

        // SnipPlus is this app's old name; two copies would both catch every capture.
        if (AppSettings.LegacyAppRunning())
            _tray.ShowBalloon("Den gamla SnipPlus körs fortfarande",
                "Avsluta den (högerklicka på dess ikon i systemfältet) och starta om Snarvo, så att bara en av dem tar emot skärmdumparna.");
    }

    /// <summary>
    /// Called on a background thread when someone launches Snarvo a second time:
    /// quit on --exit, open any image files it was given, otherwise show the library.
    /// </summary>
    public void OnRedirectedActivation(AppActivationArguments activation)
    {
        var commandLine = activation.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch
            ? launch.Arguments
            : "";
        var files = Regex.Matches(commandLine, "\"([^\"]+)\"|(\\S+)")
            .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value)
            .ToList();
        if (files.Contains("--exit"))
        {
            _queue?.TryEnqueue(() => _ = ExitAsync());
            return;
        }
        if (files.Contains("--settings"))
        {
            _queue?.TryEnqueue(() => ShowSettings());
            return;
        }
        files = files.Where(IsOpenableFile).ToList();
        _queue?.TryEnqueue(() =>
        {
            if (files.Count == 0) ShowLibrary();
            foreach (var file in files) _ = OpenFileAsync(file);
        });
    }

    IReadOnlyList<TrayMenuItem> BuildTrayMenu() =>
    [
        new($"Ny skärmdump\t{HotkeyText}", StartCapture),
        _recorder is null
            ? new("Spela in steg för en guide …", StartStepRecording, Enabled: Policy.StepRecorderAllowed)
            : new($"Stoppa inspelningen ({_recorder.Count} steg)", () => _ = FinishRecordingAsync(cancel: false)),
        new("Öppna bibliotek", ShowLibrary),
        new("Gör GIF av en inspelning …", () =>
        {
            ShowLibrary();
            if (_library is { } library) _ = VideoWindow.PickAndOpenAsync(library);
        }),
        TrayMenuItem.Separator,
        new(Policy.IsLocked(Policy.PrivateModeName) ? $"Privat läge ({Policy.LockedText.ToLowerInvariant()})" : "Privat läge – spara inte skärmdumpar",
            TogglePrivateMode, Settings.PrivateMode, Enabled: !Policy.IsLocked(Policy.PrivateModeName)),
        new("Inställningar …", () => ShowSettings()),
        TrayMenuItem.Separator,
        new("Om Snarvo", () => ShowSettings(about: true)),
        new("Avsluta Snarvo", () => _ = ExitAsync()),
    ];

    SettingsWindow? _settingsWindow;
    StepRecorder? _recorder;
    RecorderBar? _recorderBar;

    public bool IsRecordingSteps => _recorder is not null;

    /// <summary>Starts recording a guide: every click becomes a step, until the bar's Klar.</summary>
    public void StartStepRecording()
    {
        if (_recorder is not null)
        {
            if (_recorderBar is { } bar) WindowHelper.BringToFront(bar);
            return;
        }
        if (!Policy.StepRecorderAllowed)
        {
            _tray?.ShowBalloon("Stegregistrering är avstängd", "Din organisation har stängt av stegregistrering i Snarvo.");
            return;
        }
        var recorder = new StepRecorder(Settings.PrivateMode, Settings.StepRecorderHideSensitive, Settings.HideMethod, ToolStyles);
        if (!recorder.Start())
        {
            _tray?.ShowBalloon("Snarvo", "Kunde inte starta stegregistreringen.");
            return;
        }
        _recorder = recorder;
        _recorderBar = new RecorderBar(recorder);
        _recorderBar.Finish += () => _ = FinishRecordingAsync(cancel: false);
        _recorderBar.Cancel += () => _ = FinishRecordingAsync(cancel: true);
        _recorderBar.Activate();
        // The library would only be in the way of what is being shown.
        if (_library?.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.Minimize();
    }

#if DEBUG
    async Task RecordTestAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        using var stream = new MemoryStream(bytes).AsRandomAccessStream();
        using var bitmap = await Microsoft.Graphics.Canvas.CanvasBitmap.LoadAsync(Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice(), stream, 96);
        var pixels = bitmap.GetPixelBytes();
        int w = (int)bitmap.SizeInPixels.Width, h = (int)bitmap.SizeInPixels.Height;

        var recorder = new StepRecorder(Settings.PrivateMode, Settings.StepRecorderHideSensitive, Settings.HideMethod, ToolStyles);
        _recorder = recorder;
        _recorderBar = new RecorderBar(recorder);
        _recorderBar.Finish += () => _ = FinishRecordingAsync(cancel: false);
        _recorderBar.Cancel += () => _ = FinishRecordingAsync(cancel: true);
        _recorderBar.Activate();
        recorder.AddForTest(pixels, w, h, new System.Numerics.Vector2(w * 0.25f, h * 0.3f), "Klicka i fältet \u201DFörnamn\u201D.");
        await Task.Delay(400);
        recorder.AddForTest(pixels, w, h, new System.Numerics.Vector2(w * 0.3f, h * 0.86f), "Klicka på knappen \u201DSpara medlem\u201D.",
            new Native.ScreenCapture.RECT { Left = (int)(w * 0.22f), Top = (int)(h * 0.826f), Right = (int)(w * 0.38f), Bottom = (int)(h * 0.888f) });
    }
#endif

    async Task FinishRecordingAsync(bool cancel)
    {
        if (_recorder is not { } recorder) return;
        _recorder = null;
        _recorderBar?.Close();
        _recorderBar = null;
        try
        {
            if (cancel)
            {
                await recorder.CancelAsync();
                return;
            }
            var steps = await recorder.StopAsync();
            if (steps.Count == 0)
            {
                _tray?.ShowBalloon("Inga steg spelades in", "Klicka i det program du vill visa medan inspelningen pågår.");
                return;
            }
            var guide = new GuideWindow(steps.Select(s => s.Id), steps.ToDictionary(s => s.Id, s => s.Text),
                recorded: true, deleteOnClose: Settings.PrivateMode);
            WindowHelper.BringToFront(guide);
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    public void ShowSettings(bool about = false)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        if (about) _settingsWindow.ShowAbout();
        WindowHelper.BringToFront(_settingsWindow);
    }

    public static string DaysText(int days) => days == 1 ? "1 dag" : $"{days} dagar";

    /// <summary>
    /// Changes how long captures are kept. Shortening it deletes old captures straight away,
    /// so that is confirmed first. Returns false when the user cancels.
    /// </summary>
    public async Task<bool> SetKeepDaysAsync(XamlRoot root, int days)
    {
        if (Policy.IsLocked(Policy.KeepDaysName)) return false;
        if (days > 0)
        {
            int old = Library.CountOlderThan(days);
            var dialog = new ContentDialog
            {
                XamlRoot = root,
                Title = old == 0 ? "Slå på automatisk rensning?" : old == 1 ? "Radera 1 skärmdump?" : $"Radera {old} skärmdumpar?",
                Content = (old == 0 ? "" : $"{(old == 1 ? "1 skärmdump är" : $"{old} skärmdumpar är")} äldre än {DaysText(days)} och raderas direkt. ") +
                          $"Därefter raderas skärmdumpar automatiskt när de blir {DaysText(days)} gamla. " +
                          "Bilder som är öppna i editorn raderas först när de har stängts. Det går inte att ångra.",
                PrimaryButtonText = old == 0 ? "Slå på" : "Radera och slå på",
                CloseButtonText = "Avbryt",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return false;
        }
        Settings.KeepDays = days;
        SaveSettingsSoon();
        CleanUp();
        if (root != _library?.Content.XamlRoot) _library?.OnKeepDaysChanged();
        return true;
    }

    /// <summary>Applies the automatic clean-up setting. Captures open in an editor wait until next time.</summary>
    public void CleanUp()
    {
        try
        {
            Library.CleanShareFolder(TimeSpan.FromDays(1));
            if (Settings.KeepDays > 0)
            {
                int deleted = Library.DeleteOlderThan(Settings.KeepDays, skip: _editors.ContainsKey);
                if (deleted > 0) Log($"Automatisk rensning: raderade {deleted} skärmdumpar äldre än {Settings.KeepDays} dagar.");
            }
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    public void TogglePrivateMode()
    {
        if (Policy.IsLocked(Policy.PrivateModeName)) return;
        Settings.PrivateMode = !Settings.PrivateMode;
        SaveSettings();
        Library.NotifyChanged(); // the library window shows the mode
        _tray?.ShowBalloon(Settings.PrivateMode ? "Privat läge på" : "Privat läge av",
            Settings.PrivateMode
                ? "Nya skärmdumpar öppnas i editorn och raderas när du stänger den. De sparas inte i biblioteket."
                : "Nya skärmdumpar sparas i biblioteket igen.");
    }

    /// <summary>Saves settings shortly, so dragging a slider does not write the file on every step.</summary>
    public void SaveSettingsSoon()
    {
        _saveDebounce.Stop();
        _saveDebounce.Start();
    }

    void SaveSettings()
    {
        try
        {
            Settings.Save();
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    /// <summary>Starts Snipping Tool's capture overlay; the result arrives via the clipboard.</summary>
    public void StartCapture()
    {
        _captureRequested = true;
        _captureDeadline = DateTime.Now.AddMinutes(2);
        try
        {
            Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log(ex);
            _tray?.ShowBalloon("Snarvo", "Kunde inte starta Skärmklippverktyget.");
        }
    }

    /// <summary>Our own copies must not be re-imported as new captures.</summary>
    public void IgnoreClipboardFor(TimeSpan duration) => _ignoreClipboardUntil = DateTime.Now + duration;

    void OnClipboardUpdated()
    {
        if (DateTime.Now < _ignoreClipboardUntil) return;

        var (pid, name) = TrayHost.GetClipboardOwner();
        if (pid == Environment.ProcessId) return;

        bool fromSnippingTool = name is not null &&
            SnippingToolProcesses.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase));
        bool requested = _captureRequested && DateTime.Now < _captureDeadline;
        if (Settings.OnlySnippingTool && !fromSnippingTool && !requested) return;

        // Clipboard owners often update in several steps; wait for them to finish.
        _clipboardDebounce.Stop();
        _clipboardDebounce.Start();
    }

    async Task ImportFromClipboardAsync()
    {
        bool requested = _captureRequested && DateTime.Now < _captureDeadline;
        string? id;
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Bitmap)) return;
            var reference = await content.GetBitmapAsync();
            using var stream = await reference.OpenReadAsync();
            id = await Library.ImportAsync(stream, forceOpaque: true, temporary: Settings.PrivateMode);
        }
        catch (Exception ex)
        {
            Log(ex);
            return;
        }
        if (id is null) return;

        _captureRequested = false;
        // In private mode the editor is the only place the capture exists, so it always opens.
        if (requested || Settings.AutoOpenEditor || Settings.PrivateMode)
        {
            await OpenEditorAsync(id);
        }
        else
        {
            _lastCaptureId = id;
            _tray?.ShowBalloon("Skärmdumpen sparades i Snarvo", "Klicka här för att redigera den.");
        }
    }

    public bool IsOpenInEditor(string id) => _editors.ContainsKey(id);

    public async Task OpenEditorAsync(string id)
    {
        try
        {
            if (_editors.TryGetValue(id, out var existing))
            {
                WindowHelper.BringToFront(existing);
                return;
            }
            var document = await SnipDocument.LoadAsync(id);
            var editor = new EditorWindow(document);
            _editors[id] = editor;
            editor.Closed += (_, _) => _editors.Remove(id);
            WindowHelper.BringToFront(editor);
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff"];
    static readonly string[] VideoExtensions = [".mp4", ".mov", ".wmv", ".avi", ".mkv"];

    static bool HasExtension(string path, string[] extensions) =>
        extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    // The command line also holds the path of Snarvo.exe itself, which exists but is neither.
    static bool IsOpenableFile(string path) =>
        File.Exists(path) && (HasExtension(path, ImageExtensions) || HasExtension(path, VideoExtensions));

    /// <summary>Images go to the library and editor; videos open the GIF maker.</summary>
    async Task OpenFileAsync(string path)
    {
        try
        {
            if (HasExtension(path, VideoExtensions))
            {
                WindowHelper.BringToFront(new VideoWindow(await Windows.Storage.StorageFile.GetFileFromPathAsync(path)));
                return;
            }
            var id = await Library.ImportFileAsync(path, temporary: Settings.PrivateMode);
            if (id is not null) await OpenEditorAsync(id);
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    public void ShowLibrary()
    {
        if (_library is null)
        {
            _library = new LibraryWindow();
            _library.Closed += (_, _) => _library = null;
        }
        WindowHelper.BringToFront(_library);
    }

    async Task ExitAsync()
    {
        // Steps recorded so far stay in the library (or are removed with the other private captures).
        if (_recorder is { } recorder)
        {
            _recorder = null;
            _recorderBar?.Close();
            await recorder.StopAsync();
        }
        foreach (var editor in _editors.Values.ToList())
        {
            await editor.SaveIfDirtyAsync();
            editor.Close();
        }
        _library?.Close();
        Library.DeleteTemporary();
        _saveDebounce.Stop();
        _cleanupTimer.Stop();
        SaveSettings();
        _tray?.Dispose();
        Exit();
    }

    public static void Log(Exception ex) => Log(ex.ToString());

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.DataFolder);
            File.AppendAllText(Path.Combine(AppSettings.DataFolder, "error.log"), $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
