// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Snarvo.Services;
using Windows.Graphics;
using static Snarvo.Services.Localization;

namespace Snarvo;

/// <summary>
/// The small bar at the top of the screen while steps are recorded: it shows that recording is on,
/// how many steps there are, and finishes or cancels. It stays on top and is never recorded itself.
/// </summary>
public sealed class RecorderBar : Window
{
    readonly StepRecorder _recorder;
    readonly TextBlock _count;
    readonly TextBlock _hint;

    public event Action? Finish;
    public event Action? Cancel;

    public RecorderBar(StepRecorder recorder)
    {
        _recorder = recorder;
        Title = T("Snarvo – Spelar in steg");

        var dot = new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0xE0, 0x53, 0x3C)), VerticalAlignment = VerticalAlignment.Center };
        _count = new TextBlock { Text = T("Spelar in steg"), Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] };
        _hint = new TextBlock
        {
            Text = T("Klicka som vanligt – varje klick blir ett steg. Det du skriver spelas inte in."),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_count);
        text.Children.Add(_hint);

        var done = new Button { Content = T("Klar – gör guiden"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        done.Click += (_, _) => Finish?.Invoke();
        var cancel = new Button { Content = T("Avbryt") };
        cancel.Click += (_, _) => Cancel?.Invoke();
        AutomationProperties.SetHelpText(cancel, T("Avbryter och raderar de inspelade stegen"));

        var grid = new Grid { Padding = new Thickness(16, 10, 12, 10), ColumnSpacing = 12 };
        foreach (var column in new[] { GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = column });
        Grid.SetColumn(text, 1);
        Grid.SetColumn(done, 2);
        Grid.SetColumn(cancel, 3);
        grid.Children.Add(dot);
        grid.Children.Add(text);
        grid.Children.Add(done);
        grid.Children.Add(cancel);
        Content = grid;

        WindowHelper.Prepare(this);
        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(true, false);
        AppWindow.SetPresenter(presenter);

        // Top centre of the main screen, out of the way of most programs' own toolbars.
        double scale = WindowHelper.Scale(this);
        var area = DisplayArea.Primary.WorkArea;
        int width = (int)(640 * scale), height = (int)(66 * scale);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2, area.Y + (int)(10 * scale), width, height));

        _recorder.StepAdded += Update;
        Closed += (_, _) => _recorder.StepAdded -= Update;
    }

    void Update()
    {
        int n = _recorder.Count;
        _count.Text = n == 1 ? T("Spelar in – 1 steg") : F("Spelar in – {0} steg", n);
        if (_recorder.IsFull)
            _hint.Text = F("Högst {0} steg i en inspelning. Tryck Klar för att göra guiden.", StepRecorder.MaxSteps);
    }
}
