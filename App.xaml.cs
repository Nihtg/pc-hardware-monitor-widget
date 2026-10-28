using System;
using System.IO;
using System.Windows;

namespace MiniHardwareWidget;

public partial class App : Application
{
    public static void LogDebug(string msg)
    {
#if DEBUG
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_run.log");
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
#endif
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        LogDebug("App.OnStartup BEGIN");

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            LogFatal(args.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            LogFatal(args.Exception);
            args.SetObserved();
        };

        DispatcherUnhandledException += (s, args) =>
        {
            LogFatal(args.Exception);
            args.Handled = true;
        };

        base.OnStartup(e);
        LogDebug("App.OnStartup END");
    }

    private static void LogFatal(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            LogDebug($"FATAL EXCEPTION: {ex}");
        }
        catch { }
    }
}
