using System;
using System.Diagnostics;
using System.IO;

namespace Flow.Launcher.Plugin.RecentWorkspaces;

#nullable enable
internal static class Logger
{
    private static bool _initialized;
    private static string? _logPath;

    public static bool Enabled { get; set; }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            _logPath = Path.Combine(Path.GetTempPath(), "RecentWorkspaces.log");

            bool hasListener = false;
            foreach (TraceListener listener in Trace.Listeners)
            {
                if (listener is TextWriterTraceListener tw && tw.Writer is StreamWriter sw && sw.BaseStream is FileStream fs && string.Equals(fs.Name, _logPath, StringComparison.OrdinalIgnoreCase))
                {
                    hasListener = true;
                    break;
                }
            }
            if (!hasListener)
            {
                Trace.Listeners.Add(new TextWriterTraceListener(_logPath));
                Trace.AutoFlush = true;
            }
        }
        catch
        {
            // ignore logger setup errors
        }
    }

    public static void Write(string message)
    {
        if (!Enabled) return;
        try
        {
            if (!_initialized) Initialize();
            Trace.WriteLine(message);
        }
        catch { }
    }
}


