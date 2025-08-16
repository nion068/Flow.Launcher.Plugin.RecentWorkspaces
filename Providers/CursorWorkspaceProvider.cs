using System;
using System.Collections.Generic;
using System.IO;
using Flow.Launcher.Plugin.RecentWorkspaces.Caching;
using Flow.Launcher.Plugin.RecentWorkspaces.Commons;
using System.Threading;
using System.Threading.Tasks;
using Flow.Launcher.Plugin.RecentWorkspaces.Helpers;

namespace Flow.Launcher.Plugin.RecentWorkspaces.Providers;

#nullable enable
/// <summary>
/// Reads recent workspace folders from Cursor's storage.json.
/// </summary>
public class CursorWorkspaceProvider : IWorkspaceProvider
{
    private static readonly FileTimestampCache<List<string>> Cache = new();
    private readonly ProcessHelper _processHelper;

    /// <summary>
    /// Creates a Cursor workspace provider with default process starter.
    /// </summary>
    public CursorWorkspaceProvider() : this(new ProcessHelper(new DefaultProcessStarter())) { }

    /// <summary>
    /// Creates a Cursor workspace provider with a custom process helper (useful for testing).
    /// </summary>
    /// <param name="processHelper">Process helper to start external processes.</param>
    public CursorWorkspaceProvider(ProcessHelper processHelper)
    {
        _processHelper = processHelper;
    }

    /// <inheritdoc/>
    public string Name => "Cursor";

    /// <inheritdoc />
    public string GetIconPath() => "Icons/cursor.ico";

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetWorkspaceFoldersAsync(CancellationToken cancellationToken)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (string.IsNullOrEmpty(appData))
        {
            Logger.Write("[RecentWorkspaces][Cursor] APPDATA not found");
            return Array.Empty<string>();
        }

        string storagePath = Path.Combine(appData, "Cursor", "User", "globalStorage", "storage.json");
        Logger.Write($"[RecentWorkspaces][Cursor] storage.json path: {storagePath}");

        if (!File.Exists(storagePath))
        {
            Logger.Write("[RecentWorkspaces][Cursor] storage.json not found");
            return Array.Empty<string>();
        }

        List<string> list = await Task.Run(() => Cache.GetOrRefresh(storagePath, path =>
        {
            var collected = VSCodium.ExtractPathsFromStorage(path, VSCodium.TryConvertFileUriToWindowsPath, Logger.Write);
            var ordered = VSCodium.OrderByLastWriteDesc(collected);
            Logger.Write($"[RecentWorkspaces][Cursor] Cache built (ordered): {ordered.Count}");
            return ordered;
        }), cancellationToken);

        Logger.Write($"[RecentWorkspaces][Cursor] Using cached folders: {list.Count}");
        return list;
    }

    /// <summary>
    /// Opens the specified folder with Cursor editor.
    /// </summary>
    /// <param name="folderPath">Absolute folder path.</param>
    /// <returns>true if process start succeeded.</returns>
    public bool OpenWorkspace(string folderPath)
    {
        try
        {
            Logger.Write($"[RecentWorkspaces][Cursor] Launch request: {folderPath}");
            if (_processHelper.TryStart("cursor", folderPath)) return true;

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string exe1 = Path.Combine(localAppData, "Programs", "Cursor", "Cursor.exe");
            if (File.Exists(exe1) && _processHelper.TryStart(exe1, folderPath)) return true;

            string exe2 = Path.Combine(localAppData, "Cursor", "Cursor.exe");
            if (File.Exists(exe2) && _processHelper.TryStart(exe2, folderPath)) return true;
        }
        catch (Exception ex)
        {
            Logger.Write($"[RecentWorkspaces][Cursor] Launch error: {ex}");
        }
        return false;
    }
}