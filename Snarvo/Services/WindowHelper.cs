// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Snarvo.Native;
using Windows.Graphics;

namespace Snarvo.Services;

internal static class WindowHelper
{
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
