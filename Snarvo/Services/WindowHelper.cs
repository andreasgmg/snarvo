// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Snarvo.Native;
using Windows.Graphics;

namespace Snarvo.Services;

internal static class WindowHelper
{
    static readonly List<Window> Open = [];

    /// <summary>Icon, backdrop and theme every Snarvo window shares; also tracks it for theme changes.</summary>
    public static void Prepare(Window window)
    {
        window.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        window.AppWindow.SetIcon(App.IconPath);
        Open.Add(window);
        window.Closed += (_, _) => Open.Remove(window);
        ApplyTheme(window);
    }

    public static void ApplyThemeToAll()
    {
        foreach (var window in Open.ToList()) ApplyTheme(window);
    }

    static void ApplyTheme(Window window)
    {
        var theme = App.Instance?.Settings.Theme ?? AppTheme.System;
        if (window.Content is FrameworkElement root)
            root.RequestedTheme = theme switch
            {
                AppTheme.Light => ElementTheme.Light,
                AppTheme.Dark => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        window.AppWindow.TitleBar.PreferredTheme = theme switch
        {
            AppTheme.Light => TitleBarTheme.Light,
            AppTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
    }

    public static IntPtr Handle(Window window) => WinRT.Interop.WindowNative.GetWindowHandle(window);

    public static double Scale(Window window) => Win32.GetDpiForWindow(Handle(window)) / 96.0;

    /// <summary>
    /// Windows refuses focus stealing for apps that did not receive input, which is our case
    /// when a capture arrives via the clipboard. Briefly going top-most makes the window visible anyway.
    /// </summary>
    public static void BringToFront(Window window)
    {
        window.Activate();
        if (window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            if (presenter.State == OverlappedPresenterState.Minimized) presenter.Restore();
            presenter.IsAlwaysOnTop = true;
            var queue = window.DispatcherQueue;
            _ = Task.Delay(600).ContinueWith(_ => queue.TryEnqueue(() => presenter.IsAlwaysOnTop = false));
        }
        Win32.SetForegroundWindow(Handle(window));
    }

    /// <summary>Resizes (physical pixels) and centers the window on its display's work area.</summary>
    public static void CenterAndResize(Window window, int width, int height)
    {
        var area = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        width = Math.Min(width, (int)(area.Width * 0.94));
        height = Math.Min(height, (int)(area.Height * 0.94));
        window.AppWindow.MoveAndResize(new RectInt32(
            area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2,
            width, height));
    }
}
