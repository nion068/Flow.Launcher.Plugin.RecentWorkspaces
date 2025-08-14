using System;
using System.IO;

namespace Flow.Launcher.Plugin.RecentWorkspaces.Caching;

#nullable enable
internal sealed class FileTimestampCache<T>
{
    private readonly object _lockObj = new();
    private string? _cachedPath;
    private DateTime _cachedWriteTimeUtc;
    private T? _cachedValue;

    public T GetOrRefresh(string path, Func<string, T> buildFromPath)
    {
        DateTime writeTimeUtc;
        try { writeTimeUtc = File.GetLastWriteTimeUtc(path); }
        catch { writeTimeUtc = DateTime.MinValue; }

        lock (_lockObj)
        {
            if (string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase) && _cachedValue is not null && _cachedWriteTimeUtc == writeTimeUtc)
            {
                return _cachedValue;
            }
        }

        // Build outside lock to avoid blocking other readers
        T newValue = buildFromPath(path);

        lock (_lockObj)
        {
            // If another thread refreshed while we were building, return that
            if (string.Equals(_cachedPath, path, StringComparison.OrdinalIgnoreCase) && _cachedValue is not null && _cachedWriteTimeUtc == writeTimeUtc)
            {
                return _cachedValue;
            }

            _cachedPath = path;
            _cachedWriteTimeUtc = writeTimeUtc;
            _cachedValue = newValue;
            return newValue;
        }
    }
}


