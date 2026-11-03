// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Snarvo.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace Snarvo.Editor;

public sealed partial class EditorWindow : Window
{
    sealed record ToolInfo(Tool Tool, string Glyph, string Name, VirtualKey Key, string Hint);

    static readonly ToolInfo[] Tools =
    [
        new(Tool.Select, "\uE7C9", "Markera", VirtualKey.V,
            "Klicka för att markera, dra för att flytta. Delete tar bort, piltangenterna knuffar. Dubbelklicka på text för att ändra den."),
        new(Tool.Arrow, "\uE72A", "Pil", VirtualKey.A, "Dra för att rita en pil. Håll Shift för raka vinklar."),
        new(Tool.Line, "\uE738", "Linje", VirtualKey.L, "Dra för att rita en linje. Håll Shift för raka vinklar."),
        new(Tool.Rectangle, "\uE739", "Rektangel", VirtualKey.R, "Dra för att rita en rektangel, eller klicka på ett ord för att ringa in det (Shift+klick: hela raden)."),
        new(Tool.Ellipse, "\uEA3A", "Ellips", VirtualKey.E, "Dra för att rita en ellips. Håll Shift för en cirkel."),
        new(Tool.Highlight, "\uE7E6", "Överstrykning", VirtualKey.H, "Klicka på ett ord för att stryka över det (Shift+klick: hela raden), eller dra över ett område."),
        new(Tool.Pen, "\uE70F", "Penna", VirtualKey.P,
            "Rita på fri hand. Håll musen still en stund i slutet så blir en cirkel, rektangel eller linje perfekt."),
        new(Tool.Text, "\uE8D2", "Text", VirtualKey.T, "Klicka där texten ska stå och skriv. Klicka utanför, Esc eller Ctrl+Enter när du är klar."),
        new(Tool.Callout, "\uE8BD", "Pratbubbla", VirtualKey.C, "Dra från det du vill peka på till där bubblan ska ligga, och skriv sedan."),
        new(Tool.Step, "", "Numrerade steg", VirtualKey.N, "Klicka för att placera steg 1, 2, 3 … Numren uppdateras automatiskt om du tar bort ett."),
        new(Tool.Redact, "\uE72E", "Täck över", VirtualKey.D,
            "Dra över känslig information, eller klicka på ett ord, för att täcka det helt. Säkrast – inget av det som stod där går att återskapa."),
        new(Tool.Blur, "\uED1A", "Oskärpa", VirtualKey.B, "Dra över känslig information för att göra den suddig."),
        new(Tool.Pixelate, "\uE80A", "Pixla", VirtualKey.X, "Dra över känslig information för att pixla den."),
        new(Tool.Magnify, "\uE71E", "Förstoringsglas", VirtualKey.M, "Dra för att placera ett förstoringsglas som visar området under det i dubbel storlek."),
        new(Tool.Spotlight, "\uEA80", "Strålkastare", VirtualKey.S, "Dra runt det viktiga – resten av bilden tonas ner."),
        new(Tool.Crop, "\uE7A8", "Beskär", VirtualKey.K, "Dra för att välja vad som ska behållas och släpp för att beskära – eller ta bort enfärgade kanter automatiskt (Ctrl+Shift+K)."),
        new(Tool.CutOut, "\uE8C6", "Klipp ut remsa", VirtualKey.U,
            "Dra uppifrån och ner för att ta bort en vågrät remsa, eller i sidled för att ta bort en lodrät. Resten fogas ihop."),
    ];

    static readonly Color Accent = Color.FromArgb(255, 0, 120, 212);

    static readonly Color[] Palette = ToolStyle.Palette;

    static readonly Color[] BorderPalette = [ToolStyle.White, ToolStyle.Ink, ToolStyle.Grey, ToolStyle.Blue, ToolStyle.Red];

    enum Drag { None, Create, Move, Handle, Region, Pen, Snapped }

    readonly SnipDocument _doc;
    readonly CanvasDevice _device = CanvasDevice.GetSharedDevice();
    readonly Dictionary<Tool, ToolStyle> _styles = App.Instance?.ToolStyles ?? ToolStyle.CreateDefaults();
    readonly Dictionary<Tool, ToggleButton> _toolButtons = new();
    readonly Stack<DocState> _undo = new();
    readonly Stack<DocState> _redo = new();
    readonly DispatcherQueueTimer _statusTimer;
    readonly DispatcherQueueTimer _snapTimer;

    Tool _tool;
    Annotation? _selected;
    Vector2 _contentOffset;
    bool _dirty;
    // Edited since the image was last copied, dragged out or saved somewhere; copied on close.
    bool _unshared;
    bool _ready;
    bool _suppressStyleEvents;
    bool _pickingColor;

    // Pointer gesture in progress.
    Drag _drag;
    int _handle;
    Vector2 _dragStart;
    Vector2 _lastPoint;
    DocState? _pending;
    bool _changed;
    Rect? _region;
    Vector2 _snapAnchor;

    // Slider / colour changes are merged into one undo step per selection, effects per flyout opening.
    Annotation? _styleSession;
    bool _effectsSession;

    // In-place text editing.
    ITextAnnotation? _editing;
    TextBox? _textBox;
    DocState? _textUndo;
    string _textBefore = "";
    bool _editingIsNew;

    public EditorWindow(SnipDocument document)
    {
        _doc = document;
        InitializeComponent();

        UpdateTitle();
        WindowHelper.Prepare(this);

        _statusTimer = DispatcherQueue.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromSeconds(3);
        _statusTimer.IsRepeating = false;
        _statusTimer.Tick += (_, _) => HintText.Text = CurrentToolInfo.Hint;

        // Holding still at the end of a pen stroke turns it into the shape it resembles.
        _snapTimer = DispatcherQueue.CreateTimer();
        _snapTimer.Interval = TimeSpan.FromMilliseconds(500);
        _snapTimer.IsRepeating = false;
        _snapTimer.Tick += (_, _) => SnapStroke();

        BuildToolRail();
        BuildSwatches();
        RebuildPresetsMenu();
        BuildExportSizeMenu();
        UpdateShareMenu();

        Root.PreviewKeyDown += Root_PreviewKeyDown;
        Root.SizeChanged += (_, e) => UpdateCompactToolbar(e.NewSize.Width);
        Root.Loaded += (_, _) =>
        {
            ZoomToFit();
            Scroller.Focus(FocusState.Programmatic);
        };
        Closed += Window_Closed;
        // Another editor window may have saved or removed presets meanwhile.
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated) return;
            RebuildPresetsMenu();
            UpdateExportSizeLabel();
            UpdateShareMenu();
        };

        // A fresh capture starts with the default preset's frame (border, shadow …).
        if (_doc.IsNew && DefaultPreset is { } preset && !preset.Effects.IsEmpty)
        {
            _doc.Effects = preset.Effects.Clone();
            _dirty = _unshared = true;
        }

        UpdateBoardSize();
        SizeWindowToImage();
        _ready = true;
        SetTool(Tool.Arrow);
        UpdateUndoButtons();
    }

    ToolInfo CurrentToolInfo => Tools.First(t => t.Tool == _tool);
    float Zoom => Scroller.ZoomFactor;
    float Tolerance => 5f / Zoom;
    double RasterScale => Root.XamlRoot?.RasterizationScale ?? WindowHelper.Scale(this);

    // ───────────────────────────── setup ─────────────────────────────

    void BuildToolRail()
    {
        foreach (var info in Tools)
        {
            object content = info.Tool == Tool.Step
                ? new TextBlock { Text = "①", FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center }
                : new FontIcon { Glyph = info.Glyph, FontSize = 16 };
            var button = new ToggleButton
            {
                Width = 40,
                Height = 40,
                Padding = new Thickness(0),
                Content = content,
            };
            ToolTipService.SetToolTip(button, $"{info.Name} ({info.Key})");
            AutomationProperties.SetName(button, info.Name);
            button.Click += (_, _) => SetTool(info.Tool);
            ToolRail.Children.Add(button);
            _toolButtons[info.Tool] = button;

            if (info.Tool is Tool.Select or Tool.Pen or Tool.Step or Tool.Spotlight)
            {
                ToolRail.Children.Add(new Border
                {
                    Height = 1,
                    Margin = new Thickness(6, 4, 6, 4),
                    Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
                });
            }
        }
    }

    void BuildSwatches()
    {
        foreach (var color in Palette)
        {
            var button = SwatchButton(color);
            button.Click += (_, _) => ChangeColor(color);
            Swatches.Children.Add(button);
        }
        foreach (var color in BorderPalette)
        {
            var button = SwatchButton(color);
            button.Click += (_, _) => ChangeEffects(e => e.BorderColor = color);
            BorderSwatches.Children.Add(button);
        }
    }

    static Button SwatchButton(Color color) => new()
    {
        Width = 34,
        Height = 34,
        Padding = new Thickness(0),
        Content = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(color),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
        },
    };

    void SizeWindowToImage()
    {
        // Show the image at 100 % (one image pixel per screen pixel) when it fits.
        double scale = WindowHelper.Scale(this);
        var size = Renderer.FinalSize(_doc);
        int width = (int)(size.X + (60 + 48 + 24) * scale);
        int height = (int)(size.Y + (52 + 36 + 48 + 48) * scale);
        width = Math.Max(width, (int)(1040 * scale));
        height = Math.Max(height, (int)(640 * scale));
        WindowHelper.CenterAndResize(this, width, height);
    }

    // ───────────────────────────── tools & style ─────────────────────────────

    void SetTool(Tool tool)
    {
        CommitTextEdit();
        _tool = tool;
        foreach (var (key, button) in _toolButtons) button.IsChecked = key == tool;
        if (tool != Tool.Select) Select(null);
        HintText.Text = CurrentToolInfo.Hint;
        Host.SetCursor(DefaultCursor());
        UpdateStylePanel();
    }

    /// <summary>In narrow windows the action buttons drop their labels; tooltips still name them.</summary>
    void UpdateCompactToolbar(double width)
    {
        // The text tools show more style controls on the left, so the labels need a wider window.
        double needed = FontPanel.Visibility == Visibility.Visible ? 1380 : 1180;
        var visibility = width < needed ? Visibility.Collapsed : Visibility.Visible;
        foreach (var child in ActionBar.Children)
        {
            var content = child switch
            {
                ContentControl control => control.Content,
                Border border => border.Child,
                _ => null,
            };
            if (content is StackPanel panel && child != CopyButton)
                foreach (var label in panel.Children.OfType<TextBlock>())
                    label.Visibility = visibility;
        }
    }

    InputSystemCursorShape DefaultCursor() => _tool switch
    {
        Tool.Select => InputSystemCursorShape.Arrow,
        Tool.Text => InputSystemCursorShape.IBeam,
        _ => InputSystemCursorShape.Cross,
    };

    void Select(Annotation? annotation)
    {
        _selected = annotation;
        _styleSession = null;
        UpdateStylePanel();
        Board.Invalidate();
    }

    void UpdateStylePanel()
    {
        var kind = _selected?.GetTool() ?? _tool;
        _styles.TryGetValue(kind, out var style);
        bool hasStyle = style is not null;

        ColorButton.Visibility = ToVisibility(hasStyle && kind is not (Tool.Blur or Tool.Pixelate or Tool.Spotlight));
        ThicknessPanel.Visibility = ToVisibility(hasStyle && kind is Tool.Arrow or Tool.Line or Tool.Rectangle or Tool.Ellipse
            or Tool.Pen or Tool.Magnify or Tool.Blur or Tool.Pixelate);
        ThicknessLabel.Text = kind switch { Tool.Blur => "Styrka", Tool.Pixelate => "Blockstorlek", _ => "Tjocklek" };
        FontPanel.Visibility = ToVisibility(hasStyle && kind is Tool.Text or Tool.Callout or Tool.Step);
        LabelToggle.Visibility = ToVisibility(hasStyle && kind == Tool.Text);
        FontLabel.Text = kind == Tool.Step ? "Storlek" : "Textstorlek";
        AutoTrimButton.Visibility = ToVisibility(_selected is null && kind == Tool.Crop);
        if (Root.ActualWidth > 0) UpdateCompactToolbar(Root.ActualWidth);
        if (style is null) return;

        var color = _selected?.Color ?? style.Color;
        _suppressStyleEvents = true;
        ColorSwatch.Background = new SolidColorBrush(color);
        Picker.Color = color;
        ThicknessSlider.Value = _selected?.Thickness ?? style.Thickness;
        FontSlider.Value = _selected?.FontSize ?? style.FontSize;
        LabelToggle.IsChecked = _selected is TextAnnotation selectedText ? selectedText.Label : style.Label;
        _suppressStyleEvents = false;
    }

    static Visibility ToVisibility(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    void LabelToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_suppressStyleEvents) return;
        bool label = LabelToggle.IsChecked == true;
        if (_styles.TryGetValue(Tool.Text, out var style))
        {
            style.Label = label;
            App.Instance?.SaveSettingsSoon();
        }
        if (_selected is TextAnnotation text && text.Label != label) ChangeSelected(a => ((TextAnnotation)a).Label = label);
    }

    void ChangeColor(Color color)
    {
        _suppressStyleEvents = true;
        ColorSwatch.Background = new SolidColorBrush(color);
        Picker.Color = color;
        _suppressStyleEvents = false;

        var kind = _selected?.GetTool() ?? _tool;
        if (_styles.TryGetValue(kind, out var style))
        {
            style.Color = color;
            App.Instance?.SaveSettingsSoon();
        }
        if (_selected is not null && _selected.Color != color) ChangeSelected(a => a.Color = color);
    }

    void Eyedropper_Click(object sender, RoutedEventArgs e)
    {
        ColorButton.Flyout.Hide();
        _pickingColor = true;
        Host.SetCursor(InputSystemCursorShape.Cross);
        HintText.Text = "Klicka i bilden för att hämta en färg. Esc avbryter.";
    }

    /// <summary>Takes the colour under the pointer as it looks on screen, annotations included.</summary>
    void PickColor(Vector2 p)
    {
        _pickingColor = false;
        Host.SetCursor(DefaultCursor());
        if (p.X < 0 || p.Y < 0 || p.X >= _doc.Size.X || p.Y >= _doc.Size.Y) return;

        Color color;
        using (var pixel = new CanvasRenderTarget(_device, 1, 1, 96))
        {
            using (var ds = pixel.CreateDrawingSession())
            {
                ds.Clear(Colors.White);
                ds.Transform = Matrix3x2.CreateTranslation(-MathF.Floor(p.X), -MathF.Floor(p.Y));
                Renderer.DrawContent(ds, _device, _doc);
            }
            color = pixel.GetPixelColors()[0];
        }
        color.A = 255;
        ChangeColor(color);

        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        var package = new DataPackage();
        package.SetText(hex);
        App.Instance?.IgnoreClipboardFor(TimeSpan.FromSeconds(2));
        Clipboard.SetContent(package);
        Clipboard.Flush();
        ShowStatus($"Färgen {hex} är vald och kopierad.");
    }

    void Picker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (_suppressStyleEvents || !_ready) return;
        ChangeColor(args.NewColor);
    }

    void ThicknessSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressStyleEvents || !_ready) return;
        float value = (float)e.NewValue;
        if (_styles.TryGetValue(_selected?.GetTool() ?? _tool, out var style))
        {
            style.Thickness = value;
            App.Instance?.SaveSettingsSoon();
        }
        if (_selected is not null && _selected.Thickness != value) ChangeSelected(a => a.Thickness = value);
    }

    void FontSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressStyleEvents || !_ready) return;
        float value = (float)e.NewValue;
        if (_styles.TryGetValue(_selected?.GetTool() ?? _tool, out var style))
        {
            style.FontSize = value;
            App.Instance?.SaveSettingsSoon();
        }
        if (_selected is not null && _selected.FontSize != value) ChangeSelected(a => a.FontSize = value);
    }

    /// <summary>Applies a property change to the selection; consecutive changes share one undo step.</summary>
    void ChangeSelected(Action<Annotation> change)
    {
        if (_selected is null) return;
        if (!ReferenceEquals(_styleSession, _selected))
        {
            PushUndo(_doc.Capture());
            _styleSession = _selected;
        }
        change(_selected);
        _dirty = _unshared = true;
        Board.Invalidate();
    }

    // ───────────────────────────── presets ─────────────────────────────

    static AppSettings? Settings => App.Instance?.Settings;

    static StylePreset? DefaultPreset =>
        Settings?.Presets.FirstOrDefault(p => p.Name == Settings.DefaultPreset);

    /// <summary>
    /// Rebuilt after every change rather than while opening: a flyout filled during Opening
    /// is measured while still empty and shows up misplaced and clipped.
    /// </summary>
    void RebuildPresetsMenu()
    {
        var menu = PresetsMenu;
        menu.Items.Clear();
        var settings = Settings;
        if (settings is null) return;

        if (settings.Presets.Count == 0)
            menu.Items.Add(new MenuFlyoutItem { Text = "Inga mallar än", IsEnabled = false });

        foreach (var preset in settings.Presets)
        {
            bool isDefault = preset.Name == settings.DefaultPreset;
            var sub = new MenuFlyoutSubItem { Text = isDefault ? $"{preset.Name}  (standard)" : preset.Name };

            var apply = new MenuFlyoutItem { Text = "Använd" };
            apply.Click += (_, _) => ApplyPreset(preset);
            var makeDefault = new ToggleMenuFlyoutItem { Text = "Standard för nya skärmdumpar", IsChecked = isDefault };
            makeDefault.Click += (_, _) =>
            {
                settings.DefaultPreset = isDefault ? null : preset.Name;
                App.Instance?.SaveSettingsSoon();
                RebuildPresetsMenu();
            };
            var delete = new MenuFlyoutItem { Text = "Ta bort" };
            delete.Click += (_, _) =>
            {
                settings.Presets.Remove(preset);
                if (isDefault) settings.DefaultPreset = null;
                App.Instance?.SaveSettingsSoon();
                RebuildPresetsMenu();
                ShowStatus($"Mallen \"{preset.Name}\" togs bort.");
            };

            sub.Items.Add(apply);
            sub.Items.Add(makeDefault);
            sub.Items.Add(new MenuFlyoutSeparator());
            sub.Items.Add(delete);
            menu.Items.Add(sub);
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        var save = new MenuFlyoutItem { Text = "Spara nuvarande stil som mall…", Icon = new FontIcon { Glyph = "" } };
        save.Click += (_, _) => _ = SavePresetAsync();
        menu.Items.Add(save);
    }

    void ApplyPreset(StylePreset preset)
    {
        CommitTextEdit();
        foreach (var (tool, style) in preset.Styles)
            if (_styles.ContainsKey(tool)) _styles[tool] = style.Clone();
        App.Instance?.SaveSettingsSoon();

        PushUndo(_doc.Capture());
        _doc.Effects = preset.Effects.Clone();
        _dirty = _unshared = true;
        UpdateBoardSize();
        UpdateStylePanel();
        ShowStatus($"Mallen \"{preset.Name}\" används. Nya annoteringar får dess stil.");
    }

    async Task SavePresetAsync()
    {
        var settings = Settings;
        if (settings is null) return;

        var name = new TextBox { PlaceholderText = "T.ex. Supportärende", Text = $"Mall {settings.Presets.Count + 1}" };
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Spara som mall",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Mallen sparar färg och storlek för alla verktyg, plus ram, rundade hörn och skugga.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    name,
                },
            },
            PrimaryButtonText = "Spara",
            CloseButtonText = "Avbryt",
            DefaultButton = ContentDialogButton.Primary,
        };
        name.Loaded += (_, _) => name.SelectAll();
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var title = name.Text.Trim();
        if (title.Length == 0) return;
        settings.Presets.RemoveAll(p => p.Name == title);
        settings.Presets.Add(new StylePreset
        {
            Name = title,
            Styles = ToolStyle.CloneAll(_styles),
            Effects = _doc.Effects.Clone(),
        });
        App.Instance?.SaveSettingsSoon();
        RebuildPresetsMenu();
        ShowStatus($"Mallen \"{title}\" är sparad.");
    }

    // ───────────────────────────── effects ─────────────────────────────

    void EffectsFlyout_Opening(object? sender, object e)
    {
        _effectsSession = false;
        _suppressStyleEvents = true;
        ShadowToggle.IsOn = _doc.Effects.Shadow;
        BorderSlider.Value = _doc.Effects.BorderWidth;
        RadiusSlider.Value = _doc.Effects.CornerRadius;
        _suppressStyleEvents = false;
    }

    void ShadowToggle_Toggled(object sender, RoutedEventArgs e) => ChangeEffects(fx => fx.Shadow = ShadowToggle.IsOn);
    void BorderSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e) => ChangeEffects(fx => fx.BorderWidth = (float)e.NewValue);
    void RadiusSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e) => ChangeEffects(fx => fx.CornerRadius = (float)e.NewValue);

    void ChangeEffects(Action<FrameEffects> change)
    {
        if (_suppressStyleEvents || !_ready) return;
        if (!_effectsSession)
        {
            PushUndo(_doc.Capture());
            _effectsSession = true;
        }
        change(_doc.Effects);
        _dirty = _unshared = true;
        UpdateBoardSize();
        Board.Invalidate();
    }

    // ───────────────────────────── pointer input ─────────────────────────────

    Vector2 ToDocument(Point p) => new Vector2((float)p.X, (float)p.Y) - _contentOffset;

    Vector2 ClampToImage(Vector2 p) => Vector2.Clamp(p, Vector2.Zero, _doc.Size);

    void Board_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Board);
        if (!point.Properties.IsLeftButtonPressed) return;
        e.Handled = true;

        CommitTextEdit();
        Scroller.Focus(FocusState.Pointer);

        if (_pickingColor || IsKeyDown(VirtualKey.Menu))
        {
            PickColor(ToDocument(point.Position));
            return;
        }

        var p = ToDocument(point.Position);
        _dragStart = _lastPoint = p;
        _changed = false;
        _drag = Drag.None;

        if (_tool is Tool.Crop or Tool.CutOut)
        {
            var start = ClampToImage(p);
            _dragStart = _lastPoint = start;
            _region = Geo.RectFrom(start, start);
            StartDrag(Drag.Region, e);
            return;
        }

        if (_selected is not null)
        {
            int handle = HitHandle(_selected, p);
            if (handle >= 0)
            {
                BeginChange();
                _handle = handle;
                StartDrag(Drag.Handle, e);
                return;
            }
            if (_tool != Tool.Text && _selected.HitTest(_device, p, Tolerance))
            {
                BeginChange();
                StartDrag(Drag.Move, e);
                return;
            }
        }

        switch (_tool)
        {
            case Tool.Select:
                var hit = HitTest(p);
                Select(hit);
                if (hit is not null)
                {
                    BeginChange();
                    StartDrag(Drag.Move, e);
                }
                break;

            case Tool.Text:
                if (HitTest(p) is ITextAnnotation existing)
                {
                    Select((Annotation)existing);
                    BeginTextEdit(existing, isNew: false, before: null);
                }
                else
                {
                    var before = _doc.Capture();
                    var text = (TextAnnotation)CreateAnnotation(Tool.Text, p);
                    text.Position = p - new Vector2(0, text.FontSize * 0.7f);
                    _doc.Annotations.Add(text);
                    Select(text);
                    BeginTextEdit(text, isNew: true, before);
                }
                break;

            case Tool.Step:
                BeginChange();
                var step = CreateAnnotation(Tool.Step, p);
                _doc.Annotations.Add(step);
                Select(step);
                _changed = true;
                StartDrag(Drag.Move, e);
                break;

            case Tool.Pen:
                BeginChange();
                var pen = CreateAnnotation(Tool.Pen, p);
                _doc.Annotations.Add(pen);
                Select(pen);
                _changed = true;
                StartDrag(Drag.Pen, e);
                _snapAnchor = p;
                _snapTimer.Start();
                break;

            default:
                BeginChange();
                var shape = CreateAnnotation(_tool, p);
                _doc.Annotations.Add(shape);
                Select(shape);
                _changed = true;
                StartDrag(Drag.Create, e);
                break;
        }
        Board.Invalidate();
    }

    void StartDrag(Drag drag, PointerRoutedEventArgs e)
    {
        _drag = drag;
        Board.CapturePointer(e.Pointer);
    }

    void Board_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var p = ToDocument(e.GetCurrentPoint(Board).Position);
        if (_drag == Drag.None)
        {
            UpdateHoverCursor(p);
            return;
        }

        bool shift = IsKeyDown(VirtualKey.Shift);
        switch (_drag)
        {
            case Drag.Create when _selected is CalloutAnnotation callout:
                callout.Center = p;
                break;
            case Drag.Create when _selected is ShapeAnnotation shape:
                shape.End = shift ? Geo.Constrain(shape.Start, p, shape is BoxAnnotation) : p;
                break;
            case Drag.Handle:
                _selected!.SetHandle(_handle, p);
                break;
            case Drag.Move:
                _selected!.Offset(p - _lastPoint);
                break;
            case Drag.Pen:
                var pen = (PenAnnotation)_selected!;
                if (Vector2.Distance(pen.Points[^1], p) >= 1.5f) pen.Points.Add(p);
                // Mouse jitter must not count as moving on.
                if (Vector2.Distance(_snapAnchor, p) > 3 / Zoom)
                {
                    _snapAnchor = p;
                    _snapTimer.Stop();
                    _snapTimer.Start();
                }
                break;
            case Drag.Snapped:
                break;
            case Drag.Region:
                p = ClampToImage(p);
                _region = Geo.RectFrom(_dragStart, p);
                break;
        }
        _changed = true;
        _lastPoint = p;
        Board.Invalidate();
    }

    void Board_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_drag == Drag.None) return;
        Board.ReleasePointerCapture(e.Pointer);
        FinishDrag();
    }

    void Board_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_drag != Drag.None) FinishDrag();
    }

    void FinishDrag()
    {
        var drag = _drag;
        _drag = Drag.None;
        _snapTimer.Stop();

        switch (drag)
        {
            case Drag.Create when _selected is CalloutAnnotation callout:
                if (Vector2.Distance(callout.Center, callout.Tip) < 12)
                    callout.Center = callout.Tip + new Vector2(callout.FontSize * 4, -callout.FontSize * 3);
                var before = _pending;
                _pending = null;
                BeginTextEdit(callout, isNew: true, before);
                Board.Invalidate();
                return;

            case Drag.Create when _selected is ShapeAnnotation shape && shape.IsTooSmall():
                // A click without dragging should not leave an invisible shape behind.
                _doc.Annotations.Remove(shape);
                Select(null);
                _changed = false;
                // With the marking and hiding tools a click on a word marks exactly that word.
                if (shape.GetTool() is Tool.Rectangle or Tool.Highlight or Tool.Redact or Tool.Blur or Tool.Pixelate)
                    _ = MarkTextAtAsync(shape.GetTool(), shape.Start, wholeLine: IsKeyDown(VirtualKey.Shift));
                break;

            case Drag.Region:
                var region = _region;
                _region = null;
                if (region is { } r)
                {
                    if (_tool == Tool.Crop) ApplyCrop(r);
                    else ApplyCutOut(_dragStart, _lastPoint);
                }
                Board.Invalidate();
                return;
        }

        CommitChange();
        UpdateStylePanel();
        Board.Invalidate();
    }

    void SnapStroke()
    {
        if (_drag != Drag.Pen || _selected is not PenAnnotation pen) return;
        int index = _doc.Annotations.IndexOf(pen);
        if (index < 0 || Freehand.Recognize(pen) is not { } shape) return;

        _doc.Annotations[index] = shape;
        _drag = Drag.Snapped;
        Select(shape);
    }

    void Board_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var p = ToDocument(e.GetPosition(Board));
        if (HitTest(p) is ITextAnnotation text)
        {
            Select((Annotation)text);
            BeginTextEdit(text, isNew: false, before: null);
        }
    }

    void UpdateHoverCursor(Vector2 p)
    {
        var shape = DefaultCursor();
        if (_pickingColor || IsKeyDown(VirtualKey.Menu))
            shape = InputSystemCursorShape.Cross;
        else if (_tool is not (Tool.Crop or Tool.CutOut))
        {
            if (_selected is not null && HitHandle(_selected, p) >= 0)
                shape = InputSystemCursorShape.SizeAll;
            else if (_selected is not null && _tool != Tool.Text && _selected.HitTest(_device, p, Tolerance))
                shape = InputSystemCursorShape.SizeAll;
            else if (_tool == Tool.Select && HitTest(p) is not null)
                shape = InputSystemCursorShape.SizeAll;
        }
        Host.SetCursor(shape);
    }

    Annotation? HitTest(Vector2 p)
    {
        for (int i = _doc.Annotations.Count - 1; i >= 0; i--)
            if (_doc.Annotations[i].HitTest(_device, p, Tolerance)) return _doc.Annotations[i];
        return null;
    }

    int HitHandle(Annotation annotation, Vector2 p)
    {
        var handles = annotation.GetHandles(_device);
        for (int i = 0; i < handles.Length; i++)
            if (Vector2.Distance(handles[i], p) <= 8 / Zoom) return i;
        return -1;
    }

    Annotation CreateAnnotation(Tool tool, Vector2 p)
    {
        Annotation annotation = tool switch
        {
            Tool.Arrow => new ArrowAnnotation { Start = p, End = p },
            Tool.Line => new LineAnnotation { Start = p, End = p },
            Tool.Rectangle => new RectangleAnnotation { Start = p, End = p },
            Tool.Ellipse => new EllipseAnnotation { Start = p, End = p },
            Tool.Highlight => new HighlightAnnotation { Start = p, End = p },
            Tool.Pen => new PenAnnotation { Points = [p] },
            Tool.Text => new TextAnnotation { Position = p },
            Tool.Callout => new CalloutAnnotation { Tip = p, Center = p },
            Tool.Step => new StepAnnotation { Center = p },
            Tool.Blur => new BlurAnnotation { Start = p, End = p },
            Tool.Pixelate => new BlurAnnotation { Start = p, End = p, Pixelate = true },
            Tool.Redact => new RedactAnnotation { Start = p, End = p },
            Tool.Magnify => new MagnifyAnnotation { Start = p, End = p },
            Tool.Spotlight => new SpotlightAnnotation { Start = p, End = p },
            _ => throw new ArgumentOutOfRangeException(nameof(tool)),
        };
        var style = _styles[tool];
        annotation.Color = style.Color;
        annotation.Thickness = style.Thickness;
        annotation.FontSize = style.FontSize;
        if (annotation is TextAnnotation text) text.Label = style.Label;
        return annotation;
    }

    static bool IsKeyDown(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    // ───────────────────────────── text editing ─────────────────────────────

    void BeginTextEdit(ITextAnnotation annotation, bool isNew, DocState? before)
    {
        CommitTextEdit();
        _editing = annotation;
        _editingIsNew = isNew;
        _textUndo = before ?? _doc.Capture();
        _textBefore = annotation.Text;
        annotation.HideText = true;

        var transparent = new SolidColorBrush(Colors.Transparent);
        var foreground = new SolidColorBrush(annotation.TextColor);
        var box = new TextBox
        {
            // AcceptsReturn must be set before Text, or everything after the first line is dropped.
            AcceptsReturn = true,
            Text = annotation.Text,
            FontFamily = new FontFamily(Fonts.Family),
            FontWeight = FontWeights.SemiBold,
            FontSize = annotation.FontSize,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            MinWidth = 12,
            MinHeight = 0,
            Background = transparent,
            Foreground = foreground,
        };
        // The default template paints a white field while focused; keep it see-through.
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
            box.Resources[key] = transparent;
        foreach (var key in new[] { "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
            box.Resources[key] = transparent;
        foreach (var key in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" })
            box.Resources[key] = foreground;

        box.TextChanged += (_, _) =>
        {
            annotation.Text = box.Text;
            PositionTextBox();
            Board.Invalidate();
        };
        box.LostFocus += (_, _) => CommitTextEdit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == VirtualKey.Escape || (e.Key == VirtualKey.Enter && IsKeyDown(VirtualKey.Control)))
            {
                e.Handled = true;
                CommitTextEdit();
                Scroller.Focus(FocusState.Programmatic);
            }
        };
        box.Loaded += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };

        _textBox = box;
        Overlay.Children.Add(box);
        PositionTextBox();
        Board.Invalidate();
    }

    void PositionTextBox()
    {
        if (_editing is null || _textBox is null) return;
        var origin = _editing.GetTextOrigin(_device) + _contentOffset;
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(_textBox, origin.X);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(_textBox, origin.Y);
    }

    void CommitTextEdit()
    {
        if (_editing is not { } annotation) return;
        _editing = null;

        if (_textBox is not null)
        {
            Overlay.Children.Remove(_textBox);
            _textBox = null;
        }
        annotation.HideText = false;

        if (string.IsNullOrWhiteSpace(annotation.Text))
        {
            // Empty text is removed; if it existed before, that removal is undoable.
            _doc.Annotations.Remove((Annotation)annotation);
            if (!_editingIsNew) MarkChanged(_textUndo!);
            Select(null);
        }
        else if (_editingIsNew || annotation.Text != _textBefore)
        {
            MarkChanged(_textUndo!);
        }
        _textUndo = null;
        Board.Invalidate();
    }

    // ───────────────────────────── undo ─────────────────────────────

    void BeginChange()
    {
        _pending = _doc.Capture();
        _changed = false;
        _styleSession = null;
    }

    void CommitChange()
    {
        if (_changed && _pending is not null) MarkChanged(_pending);
        _pending = null;
    }

    void MarkChanged(DocState before)
    {
        PushUndo(before);
        _dirty = _unshared = true;
    }

    void PushUndo(DocState state)
    {
        _undo.Push(state);
        _redo.Clear();
        UpdateUndoButtons();
    }

    void Undo()
    {
        CommitTextEdit();
        if (_undo.Count == 0) return;
        _redo.Push(_doc.Capture());
        _doc.Restore(_undo.Pop());
        AfterRestore();
    }

    void Redo()
    {
        CommitTextEdit();
        if (_redo.Count == 0) return;
        _undo.Push(_doc.Capture());
        _doc.Restore(_redo.Pop());
        AfterRestore();
    }

    void AfterRestore()
    {
        _dirty = _unshared = true;
        _styleSession = null;
        _effectsSession = false;
        Select(null);
        UpdateBoardSize();
        UpdateUndoButtons();
    }

    void UpdateUndoButtons()
    {
        UndoButton.IsEnabled = _undo.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;
    }

    void DeleteSelected()
    {
        if (_selected is null) return;
        PushUndo(_doc.Capture());
        _doc.Annotations.Remove(_selected);
        _dirty = _unshared = true;
        Select(null);
    }

    // ───────────────────────────── crop & cut-out ─────────────────────────────

    void ApplyCrop(Rect r)
    {
        r = Geo.Intersect(r, new Rect(0, 0, _doc.Size.X, _doc.Size.Y));
        int x = (int)Math.Round(r.X), y = (int)Math.Round(r.Y);
        int w = (int)Math.Round(r.Width), h = (int)Math.Round(r.Height);
        if (w < 3 || h < 3) return;

        PushUndo(_doc.Capture());
        var cropped = new CanvasRenderTarget(_device, w, h, 96);
        using (var ds = cropped.CreateDrawingSession())
        {
            ds.Clear(Colors.Transparent);
            ds.DrawImage(_doc.Base, -x, -y);
        }
        _doc.Base = cropped;
        foreach (var a in _doc.Annotations) a.Offset(new Vector2(-x, -y));
        AfterBaseChanged();
        SetTool(Tool.Select);
    }

    void AutoTrim_Click(object sender, RoutedEventArgs e) => AutoTrim();

    /// <summary>
    /// Crops away plain single-coloured borders (Snagit's "auto trim"), keeping a small margin.
    /// Each side is trimmed while its lines have the colour of that side's outermost line, so
    /// borders of different colours (white above, grey below …) all go. Annotations are never cut.
    /// </summary>
    void AutoTrim()
    {
        CommitTextEdit();
        const int Tolerance = 24, Margin = 6;
        var size = _doc.Base.SizeInPixels;
        int width = (int)size.Width, height = (int)size.Height;
        var pixels = _doc.Base.GetPixelColors();

        static int Distance(Color a, Color b) =>
            Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) + Math.Abs(a.A - b.A);

        // True when every pixel of row y (or column x) between from and to is close to the reference.
        bool RowMatches(int y, int from, int to, Color reference)
        {
            for (int x = from; x < to; x++)
                if (Distance(pixels[y * width + x], reference) > Tolerance) return false;
            return true;
        }
        bool ColumnMatches(int x, int from, int to, Color reference)
        {
            for (int y = from; y < to; y++)
                if (Distance(pixels[y * width + x], reference) > Tolerance) return false;
            return true;
        }

        // Repeated until nothing changes, so stacked bands (white, then a grey strip) all go.
        int top = 0, bottom = height, left = 0, right = width;
        bool trimmed;
        do
        {
            int before = top + left + (height - bottom) + (width - right);
            var reference = pixels[top * width + (left + right) / 2];
            while (top < bottom - 1 && RowMatches(top, left, right, reference)) top++;
            reference = pixels[(bottom - 1) * width + (left + right) / 2];
            while (bottom - 1 > top && RowMatches(bottom - 1, left, right, reference)) bottom--;
            reference = pixels[(top + bottom) / 2 * width + left];
            while (left < right - 1 && ColumnMatches(left, top, bottom, reference)) left++;
            reference = pixels[(top + bottom) / 2 * width + right - 1];
            while (right - 1 > left && ColumnMatches(right - 1, top, bottom, reference)) right--;
            trimmed = top + left + (height - bottom) + (width - right) > before;
        }
        while (trimmed);

        if (right - left <= 1 || bottom - top <= 1)
        {
            ShowStatus("Bilden har bara en färg – det finns inget att beskära fram.");
            return;
        }

        var image = new Rect(0, 0, width, height);
        var keep = Geo.Inflate(new Rect(left, top, right - left, bottom - top), Margin);
        foreach (var annotation in _doc.Annotations)
            keep = Geo.Union(keep, annotation.GetBounds(_device));
        keep = Geo.Intersect(keep, image);
        if (keep.Width >= width - 1 && keep.Height >= height - 1)
        {
            ShowStatus("Det finns inga enfärgade kanter att beskära bort.");
            return;
        }

        ApplyCrop(keep);
        ShowStatus($"Beskar bort kanterna: {width} × {height} → {(int)Math.Round(keep.Width)} × {(int)Math.Round(keep.Height)} px. Ctrl+Z ångrar.");
    }

    /// <summary>Removes a horizontal or vertical strip and joins the remaining parts.</summary>
    void ApplyCutOut(Vector2 a, Vector2 b)
    {
        int width = (int)_doc.Size.X, height = (int)_doc.Size.Y;
        var d = b - a;
        bool rows = Math.Abs(d.Y) >= Math.Abs(d.X);

        int from = (int)MathF.Floor(rows ? Math.Min(a.Y, b.Y) : Math.Min(a.X, b.X));
        int to = (int)MathF.Ceiling(rows ? Math.Max(a.Y, b.Y) : Math.Max(a.X, b.X));
        int extent = rows ? height : width;
        from = Math.Clamp(from, 0, extent);
        to = Math.Clamp(to, 0, extent);
        int band = to - from;
        if (band < 2 || band >= extent - 1) return;

        PushUndo(_doc.Capture());
        var result = rows
            ? new CanvasRenderTarget(_device, width, height - band, 96)
            : new CanvasRenderTarget(_device, width - band, height, 96);
        using (var ds = result.CreateDrawingSession())
        {
            ds.Clear(Colors.Transparent);
            var exact = CanvasImageInterpolation.NearestNeighbor;
            if (rows)
            {
                if (from > 0) ds.DrawImage(_doc.Base, new Rect(0, 0, width, from), new Rect(0, 0, width, from), 1, exact);
                if (to < height) ds.DrawImage(_doc.Base, new Rect(0, from, width, height - to), new Rect(0, to, width, height - to), 1, exact);
            }
            else
            {
                if (from > 0) ds.DrawImage(_doc.Base, new Rect(0, 0, from, height), new Rect(0, 0, from, height), 1, exact);
                if (to < width) ds.DrawImage(_doc.Base, new Rect(from, 0, width - to, height), new Rect(to, 0, width - to, height), 1, exact);
            }
        }
        _doc.Base = result;

        Func<Vector2, Vector2> map = rows
            ? p => p.Y >= to ? new Vector2(p.X, p.Y - band) : p.Y > from ? new Vector2(p.X, from) : p
            : p => p.X >= to ? new Vector2(p.X - band, p.Y) : p.X > from ? new Vector2(from, p.Y) : p;
        foreach (var annotation in _doc.Annotations) annotation.TransformPoints(map);
        AfterBaseChanged();
    }

    void AfterBaseChanged()
    {
        _dirty = _unshared = true;
        Select(null);
        UpdateBoardSize();
    }

    // ───────────────────────────── drawing ─────────────────────────────

    void UpdateBoardSize()
    {
        var size = Renderer.FinalSize(_doc);
        Board.Width = Overlay.Width = size.X;
        Board.Height = Overlay.Height = size.Y;
        _contentOffset = Renderer.ContentOffset(_doc);
        SizeText.Text = $"{(int)_doc.Size.X} × {(int)_doc.Size.Y} px";
        UpdateExportSizeLabel();
        PositionTextBox();
        Board.Invalidate();
    }

    // A virtual control only renders the visible regions, so very large images fit.
    void Board_RegionsInvalidated(CanvasVirtualControl sender, CanvasRegionsInvalidatedEventArgs args)
    {
        foreach (var region in args.InvalidatedRegions)
        {
            using var ds = sender.CreateDrawingSession(region);
            DrawBoard(ds, sender);
        }
    }

    void DrawBoard(CanvasDrawingSession ds, ICanvasResourceCreator sender)
    {
        Renderer.DrawFramed(ds, sender, _doc);

        ds.Transform = Matrix3x2.CreateTranslation(_contentOffset);
        if (_selected is not null && _drag != Drag.Pen) DrawSelection(ds, _selected);
        if (_region is { } region) DrawRegion(ds, region);
        ds.Transform = Matrix3x2.Identity;
    }

    void DrawSelection(CanvasDrawingSession ds, Annotation annotation)
    {
        float z = Zoom;
        if (annotation is not (ArrowAnnotation or LineAnnotation))
        {
            var bounds = Geo.Inflate(annotation.GetBounds(_device), 4 / z);
            ds.DrawRectangle(bounds, Colors.White, 1 / z);
            ds.DrawRectangle(bounds, Accent, 1 / z, Strokes.Dash);
        }
        foreach (var handle in annotation.GetHandles(_device))
        {
            ds.FillCircle(handle, 5 / z, Colors.White);
            ds.DrawCircle(handle, 5 / z, Accent, 1.5f / z);
        }
    }

    void DrawRegion(CanvasDrawingSession ds, Rect r)
    {
        float z = Zoom;
        var image = new Rect(0, 0, _doc.Size.X, _doc.Size.Y);
        if (_tool == Tool.Crop)
        {
            using var everything = CanvasGeometry.CreateRectangle(_device, image);
            using var keep = CanvasGeometry.CreateRectangle(_device, r);
            using var outside = everything.CombineWith(keep, Matrix3x2.Identity, CanvasGeometryCombine.Exclude);
            ds.FillGeometry(outside, Color.FromArgb(150, 0, 0, 0));
            ds.DrawRectangle(r, Colors.White, 1.5f / z);
            ds.DrawRectangle(r, Accent, 1.5f / z, Strokes.Dash);
            DrawLabel(ds, $"{(int)Math.Round(r.Width)} × {(int)Math.Round(r.Height)}", r.TopLeft(), z);
        }
        else
        {
            var d = _lastPoint - _dragStart;
            bool rows = Math.Abs(d.Y) >= Math.Abs(d.X);
            var band = rows ? new Rect(0, r.Y, image.Width, r.Height) : new Rect(r.X, 0, r.Width, image.Height);
            var red = Color.FromArgb(255, 0xE5, 0x39, 0x35);
            ds.FillRectangle(band, Geo.WithAlpha(red, 100));
            ds.DrawRectangle(band, red, 1.5f / z, Strokes.Dash);
            DrawLabel(ds, $"Tar bort {(int)Math.Round(rows ? r.Height : r.Width)} px", band.TopLeft(), z);
        }
    }

    static void DrawLabel(CanvasDrawingSession ds, string text, Vector2 anchor, float zoom)
    {
        using var format = new CanvasTextFormat { FontFamily = Fonts.Family, FontSize = 12 / zoom, WordWrapping = CanvasWordWrapping.NoWrap };
        using var layout = new CanvasTextLayout(ds, text, format, 0, 0);
        float pad = 4 / zoom;
        var b = layout.LayoutBounds;
        var position = new Vector2(anchor.X + pad, anchor.Y + pad);
        ds.FillRoundedRectangle(new Rect(position.X - pad, position.Y - pad, b.Width + 2 * pad, b.Height + 2 * pad),
            3 / zoom, 3 / zoom, Color.FromArgb(200, 0, 0, 0));
        ds.DrawTextLayout(layout, position, Colors.White);
    }

    // ───────────────────────────── zoom ─────────────────────────────

    void ZoomToFit()
    {
        var size = Renderer.FinalSize(_doc);
        double width = Scroller.ViewportWidth, height = Scroller.ViewportHeight;
        if (width <= 0 || height <= 0) return;
        double fitWidth = width / (size.X + 48);
        // Very tall images are read top to bottom: fit the width rather than shrinking to nothing.
        double fit = size.Y > size.X * 2.5 ? fitWidth : Math.Min(fitWidth, height / (size.Y + 48));
        double zoom = Math.Max(0.1, Math.Min(fit, 1 / RasterScale));
        Scroller.ChangeView(0, 0, (float)zoom, true);
    }

    void ZoomToActual() => Scroller.ChangeView(null, null, (float)(1 / RasterScale), true);

    void Scroller_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        ZoomText.Text = $"{Math.Round(Zoom * RasterScale * 100)} %";
        Board.Invalidate();
    }

    void ZoomFit_Click(object sender, RoutedEventArgs e) => ZoomToFit();
    void ZoomActual_Click(object sender, RoutedEventArgs e) => ZoomToActual();

    // ───────────────────────────── keyboard ─────────────────────────────

    void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_editing is not null) return;
        var focused = FocusManager.GetFocusedElement(Root.XamlRoot);
        if (focused is TextBox or Slider or ToggleSwitch) return;

        if (IsKeyDown(VirtualKey.Control))
        {
            bool shift = IsKeyDown(VirtualKey.Shift);
            switch (e.Key)
            {
                case VirtualKey.Z when shift:
                case VirtualKey.Y:
                    Redo();
                    break;
                case VirtualKey.Z:
                    Undo();
                    break;
                case VirtualKey.C when shift:
                    _ = CopyAsFileAsync();
                    break;
                case VirtualKey.C:
                    _ = CopyAsync();
                    break;
                case VirtualKey.K when shift:
                    AutoTrim();
                    break;
                case VirtualKey.H when shift:
                    _ = HideWordsAsync();
                    break;
                case VirtualKey.H:
                    _ = HideSensitiveAsync();
                    break;
                case VirtualKey.S when shift:
                    _ = ExportAsync();
                    break;
                case VirtualKey.S:
                    _ = SaveAsync();
                    break;
                case VirtualKey.Number0:
                    ZoomToFit();
                    break;
                case VirtualKey.Number1:
                    ZoomToActual();
                    break;
                default:
                    return;
            }
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Delete:
            case VirtualKey.Back:
                DeleteSelected();
                break;
            case VirtualKey.Escape:
                if (_pickingColor)
                {
                    _pickingColor = false;
                    Host.SetCursor(DefaultCursor());
                    HintText.Text = CurrentToolInfo.Hint;
                }
                else if (_selected is not null) Select(null);
                else if (_tool is Tool.Crop or Tool.CutOut) SetTool(Tool.Select);
                break;
            case VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down when _selected is not null:
                float step = IsKeyDown(VirtualKey.Shift) ? 10 : 1;
                var delta = e.Key switch
                {
                    VirtualKey.Left => new Vector2(-step, 0),
                    VirtualKey.Right => new Vector2(step, 0),
                    VirtualKey.Up => new Vector2(0, -step),
                    _ => new Vector2(0, step),
                };
                ChangeSelected(a => a.Offset(delta));
                break;
            default:
                var info = Tools.FirstOrDefault(t => t.Key == e.Key);
                if (info is null) return;
                SetTool(info.Tool);
                break;
        }
        e.Handled = true;
    }

    // ───────────────────────────── commands ─────────────────────────────

    void Undo_Click(object sender, RoutedEventArgs e) => Undo();
    void Redo_Click(object sender, RoutedEventArgs e) => Redo();
    void Copy_Click(object sender, RoutedEventArgs e) => _ = CopyAsync();
    void Save_Click(object sender, RoutedEventArgs e) => _ = SaveAsync();
    void Export_Click(object sender, RoutedEventArgs e) => _ = ExportAsync();
    void Ocr_Click(object sender, RoutedEventArgs e) => _ = OcrAsync();
    void HideSensitive_Click(object sender, RoutedEventArgs e) => _ = HideSensitiveAsync();
    void HideSensitive_SplitClick(SplitButton sender, SplitButtonClickEventArgs e) => _ = HideSensitiveAsync();
    void HideWords_Click(object sender, RoutedEventArgs e) => _ = HideWordsAsync();

    void HideMethod_Click(object sender, RoutedEventArgs e)
    {
        if (App.Instance is not { } app || sender is not RadioMenuFlyoutItem { Tag: string tag }) return;
        app.Settings.HideMethod = Enum.Parse<HideMethod>(tag);
        app.SaveSettingsSoon();
    }

    void HideMenu_Opening(object sender, object e)
    {
        var method = (App.Instance?.Settings.HideMethod ?? HideMethod.Cover).ToString();
        foreach (var item in new[] { HideCoverItem, HidePixelateItem, HideBlurItem })
            item.IsChecked = (string)item.Tag == method;
    }
    void Simplify_Click(object sender, RoutedEventArgs e) => _ = SimplifyAsync();
    void Library_Click(object sender, RoutedEventArgs e) => App.Instance?.ShowLibrary();

    /// <summary>Drag-and-drop to other apps: the image is offered as a PNG file and as a bitmap.</summary>
    async void DragOut_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            CommitTextEdit();
            var folder = Library.ShareFolder;
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"Skärmdump {_doc.Id}.png");

            using (var image = RenderForSharing())
            {
                await image.SaveAsync(path, CanvasBitmapFileFormat.Png);
                args.DragUI.SetContentFromSoftwareBitmap(await CreateDragPreviewAsync(image));
            }

            var file = await StorageFile.GetFileFromPathAsync(path);
            args.Data.SetStorageItems([file]);
            args.Data.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
            args.Data.RequestedOperation = DataPackageOperation.Copy;
            args.AllowedOperations = DataPackageOperation.Copy;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            args.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    async Task<SoftwareBitmap> CreateDragPreviewAsync(CanvasBitmap image)
    {
        float scale = (float)Math.Min(1, 220 / Math.Max(image.Size.Width, image.Size.Height));
        using var preview = new CanvasRenderTarget(_device,
            Math.Max(1, (float)image.Size.Width * scale), Math.Max(1, (float)image.Size.Height * scale), 96);
        using (var ds = preview.CreateDrawingSession())
        {
            ds.Clear(Colors.Transparent);
            ds.DrawImage(image, new Rect(0, 0, preview.Size.Width, preview.Size.Height), image.Bounds);
        }
        return await SoftwareBitmap.CreateCopyFromSurfaceAsync(preview, BitmapAlphaMode.Premultiplied);
    }

    void ShowStatus(string message)
    {
        HintText.Text = message;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    // ───────────────────────────── export size ─────────────────────────────

    ExportSize CurrentExportSize => Settings?.ExportSize ?? ExportSize.Original;

    /// <summary>The image as it should leave Snarvo: copied, exported or dragged out.</summary>
    CanvasRenderTarget RenderForSharing(Color? background = null)
    {
        _unshared = false;
        return Renderer.RenderScaled(_doc, CurrentExportSize.ScaleFor(Renderer.FinalSize(_doc).X), background);
    }

    void BuildExportSizeMenu()
    {
        foreach (var size in ExportSizes.All)
        {
            var item = new RadioMenuFlyoutItem { Text = size.Label(), GroupName = "ExportSize", IsChecked = size == CurrentExportSize };
            item.Click += (_, _) =>
            {
                if (Settings is { } settings) settings.ExportSize = size;
                App.Instance?.SaveSettingsSoon();
                UpdateExportSizeLabel();
            };
            ExportSizeMenu.Items.Add(item);
        }
        UpdateExportSizeLabel();
    }

    void UpdateExportSizeLabel()
    {
        var size = CurrentExportSize;
        var scaled = Vector2.Round(Renderer.FinalSize(_doc) * size.ScaleFor(Renderer.FinalSize(_doc).X));
        ExportSizeMenu.Text = $"Storlek: {(size == ExportSize.Original ? "100 %" : size.Label().Replace(" bred", ""))}";
        ToolTipService.SetToolTip(ExportSizeMenu,
            $"Storlek när bilden kopieras, exporteras eller dras ut: {size.Label()} ({scaled.X:0} × {scaled.Y:0} px)");
        foreach (var item in ExportSizeMenu.Items.OfType<RadioMenuFlyoutItem>())
            item.IsChecked = item.Text == size.Label();
    }

    // ───────────────────────────── quick share ─────────────────────────────

    void UpdateShareMenu()
    {
        var folder = Settings?.QuickSaveFolder;
        bool hasFolder = folder is not null && Directory.Exists(folder);
        QuickSaveItem.Text = hasFolder ? $"Spara i {Path.GetFileName(folder!.TrimEnd('\\'))}" : "Spara i mapp …";
        ToolTipService.SetToolTip(QuickSaveItem, hasFolder ? folder : "Välj en mapp en gång – sedan sparas bilder dit direkt");
        OpenQuickSaveFolderItem.IsEnabled = hasFolder;
        CopyOnCloseItem.IsChecked = Settings?.CopyOnClose ?? true;
        KeepItem.Visibility = ToVisibility(Library.IsTemporary(_doc.Id));
    }

    void UpdateTitle() => Title = Library.IsTemporary(_doc.Id)
        ? $"Snarvo – {Library.DisplayName(_doc.Id)} – privat, raderas när fönstret stängs"
        : $"Snarvo – {Library.DisplayName(_doc.Id)}";

    /// <summary>Private mode: this capture should stay after all.</summary>
    void Keep_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Library.Keep(_doc.Id);
            _dirty = true; // so the edits so far are written when the window closes
            UpdateTitle();
            UpdateShareMenu();
            ShowStatus("Bilden sparas i biblioteket när du stänger fönstret.");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowStatus("Kunde inte flytta bilden till biblioteket.");
        }
    }

    void CopyOnClose_Click(object sender, RoutedEventArgs e)
    {
        if (Settings is not { } settings) return;
        settings.CopyOnClose = CopyOnCloseItem.IsChecked;
        App.Instance?.SaveSettingsSoon();
        ShowStatus(settings.CopyOnClose
            ? "Bilden kopieras automatiskt när du stänger editorn."
            : "Bilden kopieras inte längre när du stänger editorn.");
    }

    void QuickSave_Click(object sender, RoutedEventArgs e) => _ = QuickSaveAsync(chooseFolder: false);
    void ChooseQuickSaveFolder_Click(object sender, RoutedEventArgs e) => _ = QuickSaveAsync(chooseFolder: true);
    void CopyAsFile_Click(object sender, RoutedEventArgs e) => _ = CopyAsFileAsync();

    void OpenQuickSaveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Settings?.QuickSaveFolder is { } folder && Directory.Exists(folder))
            System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\"");
    }

    async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelper.Handle(this));
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    /// <summary>Saves straight into the chosen folder; the folder is only asked for the first time.</summary>
    async Task QuickSaveAsync(bool chooseFolder)
    {
        CommitTextEdit();
        var settings = Settings;
        if (settings is null) return;

        var folder = settings.QuickSaveFolder;
        if (chooseFolder || folder is null || !Directory.Exists(folder))
        {
            folder = await PickFolderAsync();
            if (folder is null) return;
            settings.QuickSaveFolder = folder;
            App.Instance?.SaveSettingsSoon();
            UpdateShareMenu();
            if (chooseFolder)
            {
                ShowStatus($"Bilder sparas nu i {folder}.");
                return;
            }
        }

        try
        {
            var stamp = Library.CreatedAt(_doc.Id);
            var name = $"Skärmdump {(stamp == DateTime.MinValue ? DateTime.Now : stamp):yyyy-MM-dd HH.mm.ss}";
            var path = Path.Combine(folder, name + ".png");
            for (int n = 2; File.Exists(path); n++) path = Path.Combine(folder, $"{name} ({n}).png");

            using var image = RenderForSharing();
            await image.SaveAsync(path, CanvasBitmapFileFormat.Png);
            ShowStatus($"Sparad som {Path.GetFileName(path)} i {folder}.");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowStatus("Kunde inte spara i mappen.");
        }
    }

    /// <summary>Puts the image on the clipboard as a PNG file: pastes into Explorer, attaches in chat apps.</summary>
    async Task CopyAsFileAsync()
    {
        CommitTextEdit();
        try
        {
            var folder = Library.ShareFolder;
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"Skärmdump {_doc.Id}.png");
            using (var image = RenderForSharing())
                await image.SaveAsync(path, CanvasBitmapFileFormat.Png);

            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetStorageItems([await StorageFile.GetFileFromPathAsync(path)]);
            App.Instance?.IgnoreClipboardFor(TimeSpan.FromSeconds(2));
            Clipboard.SetContent(package);
            Clipboard.Flush();
            ShowStatus("Bilden är kopierad som fil – klistra in i Utforskaren eller som bilaga.");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowStatus("Kunde inte kopiera som fil.");
        }
    }

    /// <summary>Copies the image; <paramref name="quiet"/> when the window is already closing.</summary>
    async Task CopyAsync(bool quiet = false)
    {
        CommitTextEdit();
        try
        {
            var stream = new InMemoryRandomAccessStream();
            string size;
            using (var image = RenderForSharing())
            {
                await image.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                size = $"{image.SizeInPixels.Width} × {image.SizeInPixels.Height} px";
            }
            stream.Seek(0);

            var package = new DataPackage();
            package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
            App.Instance?.IgnoreClipboardFor(TimeSpan.FromSeconds(2));
            Clipboard.SetContent(package);
            Clipboard.Flush();
            if (!quiet) ShowStatus($"Bilden är kopierad ({size}) – klistra in den var du vill.");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            if (!quiet) ShowStatus("Kunde inte kopiera bilden.");
        }
    }

    async Task SaveAsync()
    {
        CommitTextEdit();
        try
        {
            await _doc.SaveAsync();
            _dirty = false;
            ShowStatus("Sparad i biblioteket.");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowStatus("Kunde inte spara.");
        }
    }

    public async Task SaveIfDirtyAsync()
    {
        CommitTextEdit();
        if (!_dirty) return;
        try
        {
            await _doc.SaveAsync();
            _dirty = false;
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    async Task ExportAsync()
    {
        CommitTextEdit();
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SuggestedFileName = $"Skärmdump {_doc.Id}",
        };
        picker.FileTypeChoices.Add("PNG-bild", new List<string> { ".png" });
        picker.FileTypeChoices.Add("JPEG-bild", new List<string> { ".jpg" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHelper.Handle(this));

        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        try
        {
            bool jpeg = file.FileType.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || file.FileType.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
            using var image = RenderForSharing(jpeg ? Colors.White : null);
            using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            stream.Size = 0;
            await image.SaveAsync(stream, jpeg ? CanvasBitmapFileFormat.Jpeg : CanvasBitmapFileFormat.Png, 0.92f);
            ShowStatus($"Exporterad till {file.Path}");
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowStatus("Kunde inte exportera bilden.");
        }
    }

    async Task OcrAsync()
    {
        CommitTextEdit();
        string text;
        try
        {
            var recognized = await TextRecognition.RecognizeAsync(_doc.Size, ds => Renderer.DrawContent(ds, _device, _doc));
            if (recognized is null)
            {
                await ShowNoOcrLanguageAsync();
                return;
            }
            text = recognized.Text;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowStatus("Textigenkänningen misslyckades.");
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            await ShowMessageAsync("Textigenkänning", "Ingen text hittades i bilden.");
            return;
        }

        var box = new TextBox
        {
            // AcceptsReturn must be set before Text, or everything after the first line is dropped.
            AcceptsReturn = true,
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Height = 320,
            Width = 480,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Text i bilden",
            Content = box,
            PrimaryButtonText = "Kopiera",
            CloseButtonText = "Stäng",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var package = new DataPackage();
            package.SetText(box.Text);
            App.Instance?.IgnoreClipboardFor(TimeSpan.FromSeconds(2));
            Clipboard.SetContent(package);
            Clipboard.Flush();
            ShowStatus("Texten är kopierad.");
        }
    }

    /// <summary>Finds e-mail addresses, phone numbers, passwords, keys etc. with OCR and hides them.</summary>
    async Task HideSensitiveAsync()
    {
        CommitTextEdit();
        IReadOnlyList<SensitiveHit> hits;
        try
        {
            // Only the capture itself: the found rectangles must line up with it.
            var recognized = await TextRecognition.RecognizeAsync(_doc.Size, ds => ds.DrawImage(_doc.Base));
            if (recognized is null)
            {
                await ShowNoOcrLanguageAsync();
                return;
            }
            hits = SensitiveInfo.Find(recognized.Result, recognized.Scale);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowStatus("Kunde inte söka efter känslig information.");
            return;
        }

        var added = HideHits(hits);
        if (added.Count == 0)
        {
            ShowStatus(hits.Count == 0 ? "Hittade ingen känslig information." : "Allt känsligt är redan dolt.");
            return;
        }
        var summary = string.Join(", ", added.GroupBy(h => h.Kind).Select(g => $"{g.Count()} {g.Key}"));
        ShowStatus($"Dolde {added.Count} uppgifter: {summary}. Ctrl+Z ångrar, eller markera och ta bort enskilda.");
    }

    // OCR of the current capture, reused for clicks on text until the capture is cropped or cut.
    RecognizedText? _wordsOcr;
    CanvasBitmap? _wordsOcrBase;

    /// <summary>Marks (or hides) the word under a click, or its whole line with Shift.</summary>
    async Task MarkTextAtAsync(Tool tool, Vector2 point, bool wholeLine)
    {
        try
        {
            if (_wordsOcr is null || !ReferenceEquals(_wordsOcrBase, _doc.Base))
            {
                var bitmap = _doc.Base;
                var recognized = await TextRecognition.RecognizeAsync(_doc.Size, ds => ds.DrawImage(bitmap));
                if (recognized is null) return;
                _wordsOcr = recognized;
                _wordsOcrBase = bitmap;
            }
            if (!ReferenceEquals(_wordsOcrBase, _doc.Base)) return;

            float scale = _wordsOcr.Scale;
            Rect? found = null;
            foreach (var line in _wordsOcr.Result.Lines)
            {
                Rect? lineBounds = null;
                bool hit = false;
                foreach (var word in line.Words)
                {
                    var r = word.BoundingRect;
                    var box = new Rect(r.X / scale, r.Y / scale, r.Width / scale, r.Height / scale);
                    lineBounds = lineBounds is { } lb ? Geo.Union(lb, box) : box;
                    if (Geo.Inflate(box, 3).Contains(point))
                    {
                        hit = true;
                        if (!wholeLine) found = box;
                    }
                }
                if (hit && wholeLine) found = lineBounds;
                if (found is not null) break;
            }
            if (found is not { } text)
            {
                ShowStatus("Ingen text där du klickade. Dra för att markera ett område.");
                return;
            }

            // A little air around the letters; more for the hiding tools, which must cover descenders too.
            bool hiding = tool is Tool.Redact or Tool.Blur or Tool.Pixelate;
            double padX = hiding ? 4 : 3, padY = Math.Max(2, text.Height * (hiding ? 0.25 : 0.18));
            var area = Geo.Intersect(new Rect(text.X - padX, text.Y - padY, text.Width + 2 * padX, text.Height + 2 * padY),
                new Rect(0, 0, _doc.Size.X, _doc.Size.Y));
            var before = _doc.Capture();
            var mark = (BoxAnnotation)CreateAnnotation(tool, area.TopLeft());
            mark.End = new Vector2((float)area.Right, (float)area.Bottom);
            if (tool == Tool.Pixelate) mark.Thickness = Math.Max(mark.Thickness, (float)area.Height / 2.5f);
            _doc.Annotations.Add(mark);
            MarkChanged(before);
            Select(mark);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    // Remembered for the session, so the same customer name is quick to hide in the next capture too.
    static string _lastHiddenTerms = "";

    /// <summary>Hides every place where the given words or names occur.</summary>
    async Task HideWordsAsync()
    {
        CommitTextEdit();
        var box = new TextBox { Text = _lastHiddenTerms, PlaceholderText = "T.ex. Anna Svensson, Acme AB", Width = 400 };
        AutomationProperties.SetName(box, "Ord eller namn");
        box.Loaded += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = "Alla ställen där det står i bilden döljs. Skilj flera ord eller namn åt med komma.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400,
        });
        panel.Children.Add(box);
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "Dölj ord eller namn",
            Content = panel,
            PrimaryButtonText = "Dölj",
            CloseButtonText = "Avbryt",
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var terms = box.Text.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => string.Join(' ', t.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (terms.Count == 0) return;
        _lastHiddenTerms = box.Text.Trim();

        IReadOnlyList<SensitiveHit> hits;
        try
        {
            var recognized = await TextRecognition.RecognizeAsync(_doc.Size, ds => ds.DrawImage(_doc.Base));
            if (recognized is null)
            {
                await ShowNoOcrLanguageAsync();
                return;
            }
            hits = SensitiveInfo.FindTerms(recognized.Result, recognized.Scale, terms);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowStatus("Kunde inte läsa texten i bilden.");
            return;
        }

        var quoted = string.Join(", ", terms.Select(t => $"\"{t}\""));
        if (hits.Count == 0)
        {
            ShowStatus($"Hittade inte {quoted} i bilden. Texten läses med OCR – prova gärna en kortare del, t.ex. bara efternamnet.");
            return;
        }
        var added = HideHits(hits);
        ShowStatus(added.Count == 0
            ? $"{quoted} är redan dolt överallt."
            : $"Dolde {added.Count} {(added.Count == 1 ? "ställe" : "ställen")} med {quoted}. Ctrl+Z ångrar.");
    }

    /// <summary>
    /// Hides the hits that are not hidden already, as one undo step, in the way chosen under
    /// "Dölj känsligt" (solid boxes unless changed); returns those it added.
    /// </summary>
    List<SensitiveHit> HideHits(IEnumerable<SensitiveHit> hits)
    {
        var method = App.Instance?.Settings.HideMethod ?? HideMethod.Cover;
        var created = AutoHide.Create(hits, method, _doc.Size, AutoHide.Covered(_doc.Annotations), _styles);
        if (created.Count > 0)
        {
            var before = _doc.Capture();
            foreach (var (_, box) in created) _doc.Annotations.Add(box);
            MarkChanged(before);
            Select(null);
        }
        return created.Select(c => c.Hit).ToList();
    }

    /// <summary>Replaces the text in the capture with plain bars, so the annotations stand out (Snagit's "Simplify").</summary>
    async Task SimplifyAsync()
    {
        CommitTextEdit();
        RecognizedText? recognized;
        try
        {
            recognized = await TextRecognition.RecognizeAsync(_doc.Size, ds => ds.DrawImage(_doc.Base));
        }
        catch (Exception ex)
        {
            App.Log(ex);
            ShowStatus("Kunde inte läsa texten i bilden.");
            return;
        }
        if (recognized is null)
        {
            await ShowNoOcrLanguageAsync();
            return;
        }

        var image = new Rect(0, 0, _doc.Size.X, _doc.Size.Y);
        var existing = _doc.Annotations.OfType<BlockAnnotation>().Select(b => b.GetBounds(_device)).ToList();
        DocState? before = null;
        int added = 0;
        foreach (var run in TextRuns(recognized))
        {
            var box = Geo.Intersect(Geo.Inflate(run, run.Height * 0.25), image);
            if (box.Width < 3 || box.Height < 3) continue;
            // Running it twice should not stack a second block on the same text.
            if (existing.Any(e => e.Contains(box.Center()))) continue;

            var (background, ink) = SampleColors(box);
            before ??= _doc.Capture();
            _doc.Annotations.Add(new BlockAnnotation
            {
                Start = box.TopLeft(),
                End = new Vector2((float)box.Right, (float)box.Bottom),
                Background = background,
                Color = Mix(background, ink, 0.3f),
            });
            added++;
        }

        if (added == 0)
        {
            ShowStatus(existing.Count > 0 ? "Texten är redan förenklad." : "Hittade ingen text att förenkla.");
            return;
        }
        MarkChanged(before!);
        // Ready to click a block away: the text that matters should stay readable.
        SetTool(Tool.Select);
        Select(null);
        ShowStatus($"Förenklade {added} textrader. Klicka på ett block och tryck Delete för att visa texten igen. Ctrl+Z ångrar alltihop.");
    }

    /// <summary>Recognised words grouped per line, split where a wide gap separates columns.</summary>
    static IEnumerable<Rect> TextRuns(RecognizedText recognized)
    {
        foreach (var line in recognized.Result.Lines)
        {
            Rect? run = null;
            foreach (var word in line.Words.OrderBy(w => w.BoundingRect.X))
            {
                var r = word.BoundingRect;
                var w = new Rect(r.X / recognized.Scale, r.Y / recognized.Scale, r.Width / recognized.Scale, r.Height / recognized.Scale);
                if (run is { } current && w.Left - current.Right > Math.Max(current.Height, w.Height) * 1.5)
                {
                    yield return current;
                    run = null;
                }
                run = run is { } c ? Geo.Union(c, w) : w;
            }
            if (run is { } last) yield return last;
        }
    }

    /// <summary>The background is the commonest colour along the box edge; the ink, the pixels furthest from it.</summary>
    (Color Background, Color Ink) SampleColors(Rect box)
    {
        var size = _doc.Base.SizeInPixels;
        int x = Math.Clamp((int)box.X, 0, (int)size.Width - 1), y = Math.Clamp((int)box.Y, 0, (int)size.Height - 1);
        int w = Math.Clamp((int)Math.Ceiling(box.Width), 1, (int)size.Width - x);
        int h = Math.Clamp((int)Math.Ceiling(box.Height), 1, (int)size.Height - y);
        var pixels = _doc.Base.GetPixelColors(x, y, w, h);

        var edge = new List<Color>();
        for (int i = 0; i < w; i++)
        {
            edge.Add(pixels[i]);
            edge.Add(pixels[(h - 1) * w + i]);
        }
        for (int j = 0; j < h; j++)
        {
            edge.Add(pixels[j * w]);
            edge.Add(pixels[j * w + w - 1]);
        }
        var background = edge.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
        background.A = 255;

        static int Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
        int furthest = pixels.Max(p => Distance(p, background));
        if (furthest < 30) return (background, background);
        var ink = pixels.Where(p => Distance(p, background) >= furthest * 0.6).ToList();
        return (background, Color.FromArgb(255,
            (byte)ink.Average(p => p.R), (byte)ink.Average(p => p.G), (byte)ink.Average(p => p.B)));
    }

    static Color Mix(Color a, Color b, float t) => Color.FromArgb(255,
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    Task ShowNoOcrLanguageAsync() => ShowMessageAsync("Textigenkänning",
        "Inget språk för textigenkänning (OCR) är installerat. Lägg till ett språk under Inställningar → Tid och språk.");

    async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "OK",
        };
        await dialog.ShowAsync();
    }

    async void Window_Closed(object sender, WindowEventArgs args)
    {
        // Edit, close, paste: no need to remember Ctrl+C. Skipped when it was already shared,
        // so something copied elsewhere since then is not overwritten for nothing.
        CommitTextEdit();
        if (_unshared && Settings?.CopyOnClose != false) await CopyAsync(quiet: true);

        // Private mode: nothing of this capture stays on disk.
        if (Library.IsTemporary(_doc.Id))
        {
            Library.Delete(_doc.Id);
            return;
        }
        await SaveIfDirtyAsync();
    }
}
