// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Snarvo.Editor;
using Snarvo.Native;
using Windows.Graphics.DirectX;

namespace Snarvo.Services;

/// <summary>A recorded click: the capture in the library and a suggested instruction.</summary>
public sealed record RecordedStep(string Id, string Text);

/// <summary>
/// Records a guide while the user works: every left click takes a picture of the window that was
/// clicked, marks the spot with a ring and suggests a text from the clicked control's name.
///
/// Privacy: it only runs between Start and Stop, with a visible bar on screen. It sees mouse clicks
/// only – never keystrokes – and reads nothing but the name of the control under the pointer.
/// Clicks on Snarvo's own windows are ignored. In private mode the pictures are temporary.
/// </summary>
public sealed class StepRecorder
{
    public const int MaxSteps = 60;

    delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct MSLLHOOKSTRUCT
    {
        public Win32.POINT pt;
        public uint mouseData, flags, time;
        public IntPtr extraInfo;
    }

    const int WH_MOUSE_LL = 14, WM_LBUTTONDOWN = 0x0201;

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int id, LowLevelMouseProc proc, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    // Kept in a field: the delegate must live as long as the hook.
    readonly LowLevelMouseProc _proc;
    readonly bool _temporary, _hideSensitive;
    readonly HideMethod _hideMethod;
    readonly IReadOnlyDictionary<Tool, ToolStyle> _styles;
    readonly List<Task<RecordedStep?>> _steps = [];
    readonly SemaphoreSlim _oneAtATime = new(1, 1);
    IntPtr _hook;
    Win32.POINT _lastPoint;
    long _lastTick;

    public StepRecorder(bool temporary, bool hideSensitive, HideMethod hideMethod, IReadOnlyDictionary<Tool, ToolStyle> styles)
    {
        _proc = Hook;
        _temporary = temporary;
        _hideSensitive = hideSensitive;
        _hideMethod = hideMethod;
        _styles = styles;
    }

    public int Count => _steps.Count;
    public bool IsRecording => _hook != IntPtr.Zero;
    public bool IsFull => _steps.Count >= MaxSteps;

    /// <summary>Raised on the UI thread after each click that became a step.</summary>
    public event Action? StepAdded;

    /// <summary>Must be called on a thread with a message loop (the UI thread).</summary>
    public bool Start()
    {
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, Win32.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) App.Log($"Stegregistrering: SetWindowsHookEx misslyckades ({Marshal.GetLastWin32Error()}).");
        return _hook != IntPtr.Zero;
    }

    /// <summary>Stops listening and returns the finished steps, oldest first.</summary>
    public async Task<IReadOnlyList<RecordedStep>> StopAsync()
    {
        Unhook();
        var steps = await Task.WhenAll(_steps);
        return steps.OfType<RecordedStep>().ToList();
    }

    /// <summary>Stops and deletes everything recorded.</summary>
    public async Task CancelAsync()
    {
        foreach (var step in await StopAsync()) Library.Delete(step.Id);
    }

    void Unhook()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    IntPtr Hook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && wParam == WM_LBUTTONDOWN && !IsFull)
        {
            try
            {
                OnClick(Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam).pt);
            }
            catch (Exception ex)
            {
                App.Log(ex);
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    /// <summary>
    /// Runs inside the mouse hook, before the click reaches the program, so the picture shows the
    /// screen as it was when the user clicked. Only the quick screen copy happens here.
    /// </summary>
    void OnClick(Win32.POINT point)
    {
        // The second click of a double-click is the same step.
        long now = Environment.TickCount64;
        bool sameSpot = Math.Abs(point.X - _lastPoint.X) <= 6 && Math.Abs(point.Y - _lastPoint.Y) <= 6;
        if (sameSpot && now - _lastTick < 500) return;
        _lastPoint = point;
        _lastTick = now;

        var root = ScreenCapture.RootWindowAt(point);
        if (root != IntPtr.Zero && ScreenCapture.ProcessOf(root) == Environment.ProcessId) return;

        var area = ScreenCapture.AreaFor(point, root);
        if (area.Width <= 0 || area.Height <= 0) return;
        var pixels = ScreenCapture.Copy(area);
        var info = Accessibility.DescribeClickAsync(point);
        var click = new Vector2(point.X - area.Left, point.Y - area.Top);

        // On the UI thread like the editor: Win2D and OCR misbehave on the thread pool. Mostly waiting anyway.
        _steps.Add(SaveStepAsync(pixels, area, click, info));
        StepAdded?.Invoke();
    }

#if DEBUG
    /// <summary>Developer aid: a step from a given picture instead of the screen, without any mouse hook.</summary>
    internal void AddForTest(byte[] pixels, int width, int height, Vector2 click, string text, ScreenCapture.RECT? control = null)
    {
        var area = new ScreenCapture.RECT { Right = width, Bottom = height };
        _steps.Add(SaveStepAsync(pixels, area, click, Task.FromResult(new Accessibility.ClickInfo(text, control))));
        StepAdded?.Invoke();
    }
#endif

    async Task<RecordedStep?> SaveStepAsync(byte[] pixels, ScreenCapture.RECT area, Vector2 click, Task<Accessibility.ClickInfo> clickInfo)
    {
        int width = area.Width, height = area.Height;
        // One at a time, so the library ids (which are timestamps) follow the click order.
        await _oneAtATime.WaitAsync();
        try
        {
            var device = CanvasDevice.GetSharedDevice();
            string id;
            using (var bitmap = CanvasBitmap.CreateFromBytes(device, pixels, width, height,
                       DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Premultiplied))
                id = await Library.AddBitmapAsync(bitmap, _temporary);

            var doc = await SnipDocument.LoadAsync(id);
            if (_hideSensitive)
            {
                var recognized = await TextRecognition.RecognizeAsync(doc.Size, ds => ds.DrawImage(doc.Base));
                if (recognized is not null)
                {
                    var hits = SensitiveInfo.Find(recognized.Result, recognized.Scale);
                    foreach (var (_, box) in AutoHide.Create(hits, _hideMethod, doc.Size, [], _styles))
                        doc.Annotations.Add(box);
                }
            }

            // What was clicked, on top of everything else: a frame around the control when its
            // area is known and sensible, otherwise a ring around the spot.
            var info = await clickInfo;
            var style = _styles.TryGetValue(Tool.Rectangle, out var s) ? s : ToolStyle.CreateDefaults()[Tool.Rectangle];
            BoxAnnotation mark;
            if (ControlFrame(info.Bounds, area, click) is { } frame)
            {
                mark = new RectangleAnnotation { Start = frame.Start, End = frame.End };
            }
            else
            {
                const float Radius = 30;
                mark = new EllipseAnnotation { Start = click - new Vector2(Radius), End = click + new Vector2(Radius) };
            }
            mark.Color = style.Color;
            mark.Thickness = Math.Max(3, style.Thickness);
            mark.FontSize = style.FontSize;
            doc.Annotations.Add(mark);
            await doc.SaveAsync();
            return new RecordedStep(id, info.Text);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return null;
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <summary>
    /// The clicked control's area in the picture, a little larger, if it is a sensible thing to frame:
    /// it contains the click and is not most of the window (as a whole web page would be).
    /// </summary>
    static (Vector2 Start, Vector2 End)? ControlFrame(ScreenCapture.RECT? bounds, ScreenCapture.RECT area, Vector2 click)
    {
        if (bounds is not { } b) return null;
        const float Pad = 5;
        var start = new Vector2(b.Left - area.Left - Pad, b.Top - area.Top - Pad);
        var end = new Vector2(b.Right - area.Left + Pad, b.Bottom - area.Top + Pad);
        var size = end - start;
        bool containsClick = click.X >= start.X && click.X <= end.X && click.Y >= start.Y && click.Y <= end.Y;
        bool small = size.X * size.Y <= area.Width * area.Height * 0.25f && size.X < area.Width * 0.9f && size.Y < area.Height * 0.6f;
        if (!containsClick || !small || size.X < 12 || size.Y < 12) return null;
        start = Vector2.Max(start, new Vector2(2));
        end = Vector2.Min(end, new Vector2(area.Width - 2, area.Height - 2));
        return (start, end);
    }
}
