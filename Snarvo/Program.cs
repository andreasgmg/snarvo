// Copyright (c) 2026 Andreas Glans
// SPDX-License-Identifier: EUPL-1.2

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Snarvo;

public static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Only one Snarvo may own the tray icon, hotkey and clipboard listener.
        // A second launch just asks the running instance to show its library,
        // or to quit when it was started with --exit (the installer does that before updating).
        var key = AppInstance.FindOrRegisterForKey("Snarvo.Main");
        if (!key.IsCurrent)
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            Task.Run(() => key.RedirectActivationToAsync(activation).AsTask()).Wait();
            return 0;
        }
        if (args.Contains("--exit")) return 0;

        // Lets the installer and uninstaller see that Snarvo is running.
        using var running = new Mutex(false, "Snarvo.Running");

        key.Activated += (_, activation) => App.Instance?.OnRedirectedActivation(activation);

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }
}
