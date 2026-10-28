using System;
using System.IO;
using System.Windows;

namespace MiniHardwareWidget;

public partial class App : Application
{
    public static void LogDebug(string msg)
    {
        try
        {
            string path = Path.Combine(Path.GetTempPath(), "MiniHardwareWidget_debug.log");
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        }
        catch { }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDefaultDllDirectories(uint directoryFlags);
    private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;

    protected override void OnStartup(StartupEventArgs e)
    {
        // DevSecOps Hardening: Prevent DLL Search Order Hijacking
        try
        {
            SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        }
        catch { }

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
