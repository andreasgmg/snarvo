// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Runtime.InteropServices;
using static Snarvo.Services.Localization;

namespace Snarvo.Native;

/// <summary>
/// Names what was clicked, through the accessibility interface Windows offers every program
/// (the same one screen readers use). Only the name and kind of the control are read – never
/// what is typed in it.
/// </summary>
internal static class Accessibility
{
    /// <summary>What was clicked: a suggested instruction and, when known, the control's area on screen.</summary>
    public sealed record ClickInfo(string Text, ScreenCapture.RECT? Bounds);

    // Only the members up to accLocation are declared; the order must match the real interface.
    [ComImport, Guid("618736E0-3C3D-11CF-810C-00AA00389B71"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    interface IAccessible
    {
        [return: MarshalAs(UnmanagedType.IDispatch)] object get_accParent();
        int get_accChildCount();
        [return: MarshalAs(UnmanagedType.IDispatch)] object get_accChild(object child);
        [return: MarshalAs(UnmanagedType.BStr)] string get_accName(object child);
        [return: MarshalAs(UnmanagedType.BStr)] string get_accValue(object child);
        [return: MarshalAs(UnmanagedType.BStr)] string get_accDescription(object child);
        object get_accRole(object child);
        object get_accState(object child);
        [return: MarshalAs(UnmanagedType.BStr)] string get_accHelp(object child);
        int get_accHelpTopic([MarshalAs(UnmanagedType.BStr)] out string helpFile, object child);
        [return: MarshalAs(UnmanagedType.BStr)] string get_accKeyboardShortcut(object child);
        object get_accFocus();
        object get_accSelection();
        [return: MarshalAs(UnmanagedType.BStr)] string get_accDefaultAction(object child);
        void accSelect(int flags, object child);
        void accLocation(out int left, out int top, out int width, out int height, object child);
    }

    [DllImport("oleacc.dll")]
    static extern int AccessibleObjectFromPoint(Win32.POINT point, [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible, out object child);

    const int ROLE_MENUITEM = 0x0C, ROLE_LINK = 0x1E, ROLE_LISTITEM = 0x22, ROLE_OUTLINEITEM = 0x24, ROLE_PAGETAB = 0x25,
        ROLE_TEXT = 0x2A, ROLE_PUSHBUTTON = 0x2B, ROLE_CHECKBUTTON = 0x2C, ROLE_RADIOBUTTON = 0x2D, ROLE_COMBOBOX = 0x2E,
        ROLE_SPLITBUTTON = 0x3E, ROLE_BUTTONDROPDOWN = 0x38, ROLE_BUTTONMENU = 0x39;

    /// <summary>
    /// A Swedish instruction for clicking at a point, such as "Klicka på knappen ”Spara”". Runs on its
    /// own thread with a time limit, so a program that does not answer cannot hold Snarvo up.
    /// </summary>
    public static Task<ClickInfo> DescribeClickAsync(Win32.POINT point)
    {
        var none = new ClickInfo("", null);
        var result = new TaskCompletionSource<ClickInfo>();
        var thread = new Thread(() =>
        {
            try { result.TrySetResult(Describe(point)); }
            catch { result.TrySetResult(none); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return Task.WhenAny(result.Task, Task.Delay(1500)).ContinueWith(t => result.Task.IsCompleted ? result.Task.Result : none);
    }

    static ClickInfo Describe(Win32.POINT point)
    {
        if (AccessibleObjectFromPoint(point, out var accessible, out var child) != 0 || accessible is null) return new("", null);
        string name = Clean(Try(() => accessible.get_accName(child)));
        int role = Try(() => accessible.get_accRole(child)) is int r ? r : 0;
        ScreenCapture.RECT? bounds = null;
        try
        {
            accessible.accLocation(out int left, out int top, out int width, out int height, child);
            if (width > 0 && height > 0)
                bounds = new ScreenCapture.RECT { Left = left, Top = top, Right = left + width, Bottom = top + height };
        }
        catch
        {
            // Some controls do not know where they are; the step then gets a ring instead.
        }
        Marshal.ReleaseComObject(accessible);
        return new(Instruction(name, role), bounds);
    }

    static string Instruction(string name, int role)
    {
        if (name.Length == 0) return "";
        var quoted = IsEnglish ? $"“{name}”" : $"”{name}”";
        return role switch
        {
            ROLE_PUSHBUTTON or ROLE_SPLITBUTTON or ROLE_BUTTONDROPDOWN or ROLE_BUTTONMENU => F("Klicka på knappen {0}.", quoted),
            ROLE_MENUITEM => F("Välj {0} i menyn.", quoted),
            ROLE_LINK => F("Klicka på länken {0}.", quoted),
            ROLE_PAGETAB => F("Öppna fliken {0}.", quoted),
            ROLE_CHECKBUTTON => F("Kryssa i {0}.", quoted),
            ROLE_RADIOBUTTON => F("Välj {0}.", quoted),
            ROLE_TEXT => F("Klicka i fältet {0}.", quoted),
            ROLE_COMBOBOX => F("Öppna listan {0}.", quoted),
            ROLE_LISTITEM or ROLE_OUTLINEITEM => F("Välj {0}.", quoted),
            _ => F("Klicka på {0}.", quoted),
        };
    }

    static T? Try<T>(Func<T> read)
    {
        try { return read(); }
        catch { return default; }
    }

    /// <summary>One line, without access-key ampersands, and not too long for a step.</summary>
    static string Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var text = string.Join(' ', name.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length > 60 ? text[..57].TrimEnd() + " …" : text;
    }
}
