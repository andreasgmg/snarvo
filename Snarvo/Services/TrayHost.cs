// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Diagnostics;
using System.Runtime.InteropServices;
using Snarvo.Native;

namespace Snarvo.Services;

public sealed record TrayMenuItem(string Text, Action? OnClick = null, bool Checked = false, bool Enabled = true)
{
    public static TrayMenuItem Separator { get; } = new("-");
    public bool IsSeparator => Text == "-";
}

/// <summary>
/// Hidden Win32 window that owns the notification-area icon, the global hotkey
/// and the clipboard listener. Lives on the UI thread, whose message loop pumps it.
/// </summary>
internal sealed class TrayHost : IDisposable
{
    const uint CallbackMessage = Win32.WM_APP + 1;

    readonly Win32.WndProc _wndProc;
    readonly IntPtr _hwnd;
    readonly IntPtr _icon;
    readonly uint _taskbarCreatedMessage;
    readonly string _tooltip;
    readonly HashSet<int> _hotkeys = [];
    bool _disposed;

    public event Action? Clicked;
    public event Action? BalloonClicked;
    public event Action<int>? HotkeyPressed;
    public event Action? ClipboardUpdated;

    public Func<IReadOnlyList<TrayMenuItem>>? MenuProvider { get; set; }

    public TrayHost(string iconPath, string tooltip)
    {
        _tooltip = tooltip;
        _wndProc = WndProc;
        var instance = Win32.GetModuleHandle(null);

        var wc = new Win32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = instance,
            lpszClassName = "SnarvoTrayHost",
        };
        Win32.RegisterClassEx(ref wc);
        _hwnd = Win32.CreateWindowEx(0, wc.lpszClassName, "Snarvo", 0, 0, 0, 0, 0,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);

        _taskbarCreatedMessage = Win32.RegisterWindowMessage("TaskbarCreated");
        _icon = Win32.LoadImage(IntPtr.Zero, iconPath, Win32.IMAGE_ICON,
            Win32.GetSystemMetrics(Win32.SM_CXSMICON), Win32.GetSystemMetrics(Win32.SM_CYSMICON), Win32.LR_LOADFROMFILE);

        AddIcon();
        Win32.AddClipboardFormatListener(_hwnd);
    }

    public bool RegisterHotkey(int id, uint modifiers, uint virtualKey)
    {
        if (_hotkeys.Remove(id)) Win32.UnregisterHotKey(_hwnd, id);
        if (!Win32.RegisterHotKey(_hwnd, id, modifiers | Win32.MOD_NOREPEAT, virtualKey)) return false;
        _hotkeys.Add(id);
        return true;
    }

    public void ShowBalloon(string title, string text)
    {
        var data = NewData();
        data.uFlags = Win32.NIF_INFO;
        data.szInfoTitle = title;
        data.szInfo = text;
        Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref data);
    }

    /// <summary>Process that currently owns the clipboard, if it can be determined.</summary>
    public static (int ProcessId, string? ProcessName) GetClipboardOwner()
    {
        var owner = Win32.GetClipboardOwner();
        if (owner == IntPtr.Zero) return (0, null);
        Win32.GetWindowThreadProcessId(owner, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return ((int)pid, process.ProcessName);
        }
        catch
        {
            return ((int)pid, null);
        }
    }

    void AddIcon()
    {
        var data = NewData();
        data.uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP | Win32.NIF_SHOWTIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon;
        data.szTip = _tooltip;
        Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref data);

        data.uVersion = Win32.NOTIFYICON_VERSION_4;
        Win32.Shell_NotifyIcon(Win32.NIM_SETVERSION, ref data);
    }

    Win32.NOTIFYICONDATA NewData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<Win32.NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1,
        szTip = "",
        szInfo = "",
        szInfoTitle = "",
    };

    IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == CallbackMessage)
            {
                // NOTIFYICON_VERSION_4: the event is in the low word of lParam.
                switch ((int)((long)lParam & 0xFFFF))
                {
                    case Win32.NIN_SELECT:
                    case Win32.NIN_KEYSELECT:
                        Clicked?.Invoke();
                        break;
                    case (int)Win32.WM_CONTEXTMENU:
                        ShowMenu();
                        break;
                    case Win32.NIN_BALLOONUSERCLICK:
                        BalloonClicked?.Invoke();
                        break;
                }
                return IntPtr.Zero;
            }
            if (msg == Win32.WM_HOTKEY && _hotkeys.Contains((int)wParam))
            {
                HotkeyPressed?.Invoke((int)wParam);
                return IntPtr.Zero;
            }
            if (msg == Win32.WM_CLIPBOARDUPDATE)
            {
                ClipboardUpdated?.Invoke();
                return IntPtr.Zero;
            }
            if (msg == _taskbarCreatedMessage)
            {
                // Explorer restarted; the icon must be added again.
                AddIcon();
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
        return Win32.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    void ShowMenu()
    {
        var items = MenuProvider?.Invoke();
        if (items is null || items.Count == 0) return;

        var menu = Win32.CreatePopupMenu();
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.IsSeparator)
                Win32.AppendMenu(menu, Win32.MF_SEPARATOR, UIntPtr.Zero, null);
            else
                Win32.AppendMenu(menu, Win32.MF_STRING | (item.Checked ? Win32.MF_CHECKED : 0) | (item.Enabled ? 0 : Win32.MF_GRAYED), (UIntPtr)(i + 1), item.Text);
        }

        Win32.GetCursorPos(out var pt);
        // Required so the menu closes when the user clicks elsewhere.
        Win32.SetForegroundWindow(_hwnd);
        int command = Win32.TrackPopupMenuEx(menu, Win32.TPM_RETURNCMD | Win32.TPM_RIGHTBUTTON, pt.X, pt.Y, _hwnd, IntPtr.Zero);
        Win32.PostMessage(_hwnd, Win32.WM_NULL, IntPtr.Zero, IntPtr.Zero);
        Win32.DestroyMenu(menu);

        if (command > 0) items[command - 1].OnClick?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var data = NewData();
        Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref data);
        Win32.RemoveClipboardFormatListener(_hwnd);
        foreach (var id in _hotkeys) Win32.UnregisterHotKey(_hwnd, id);
        Win32.DestroyWindow(_hwnd);
        if (_icon != IntPtr.Zero) Win32.DestroyIcon(_icon);
    }
}
