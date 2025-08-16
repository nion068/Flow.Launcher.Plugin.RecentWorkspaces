using System;
using System.Collections.Generic;
using System.IO;
using Flow.Launcher.Plugin.RecentWorkspaces.Caching;
using Flow.Launcher.Plugin.RecentWorkspaces.Helpers;
using Flow.Launcher.Plugin.RecentWorkspaces.Commons;
using System.Threading;
using System.Threading.Tasks;

namespace Flow.Launcher.Plugin.RecentWorkspaces.Providers;

#nullable enable
/// <summary>
/// Reads recent workspace folders from Cursor's storage.json.
/// </summary>
public class VSCodeWorkspaceProvider : IWorkspaceProvider
{
    private static readonly FileTimestampCache<List<string>> Cache = new();
    private readonly ProcessHelper _processHelper;

    /// <summary>
    /// Creates a Cursor workspace provider with default process starter.
    /// </summary>
    public VSCodeWorkspaceProvider() : this(new ProcessHelper(new DefaultProcessStarter())) { }

    /// <summary>
    /// Creates a Cursor workspace provider with a custom process helper (useful for testing).
    /// </summary>
    /// <param name="processHelper">Process helper to start external processes.</param>
    public VSCodeWorkspaceProvider(ProcessHelper processHelper)
    {
        _processHelper = processHelper;
    }

    /// <inheritdoc/>
    public string Name => "VS Code";

    /// <inheritdoc />
    public string GetIconPath() => "Icons/vscode.ico";

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetWorkspaceFoldersAsync(CancellationToken cancellationToken)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (string.IsNullOrEmpty(appData))
        {
            Logger.Write("[RecentWorkspaces][VSCode] APPDATA not found");
            return Array.Empty<string>();
        }

        string storagePath = Path.Combine(appData, "Code", "User", "globalStorage", "storage.json");
        Logger.Write($"[RecentWorkspaces][VSCode] storage.json path: {storagePath}");

        if (!File.Exists(storagePath))
        {
            Logger.Write("[RecentWorkspaces][VSCode] storage.json not found");
            return Array.Empty<string>();
        }

        List<string> list = await Task.Run(() => Cache.GetOrRefresh(storagePath, path =>
        {
            var collected = VSCodium.ExtractPathsFromStorage(path, VSCodium.TryConvertFileUriToWindowsPath, Logger.Write);
            var ordered = VSCodium.OrderByLastWriteDesc(collected);
            Logger.Write($"[RecentWorkspaces][VSCode] Cache built (ordered): {ordered.Count}");
            return ordered;
        }), cancellationToken);

        Logger.Write($"[RecentWorkspaces][VSCode] Using cached folders: {list.Count}");
        return list;
    }

    /// <summary>
    /// Opens the specified folder with VSCode editor.
    /// </summary>
    /// <param name="folderPath">Absolute folder path.</param>
    /// <returns>true if process start succeeded.</returns>
    public bool OpenWorkspace(string folderPath)
    {
        try
        {
            Logger.Write($"[RecentWorkspaces][VSCode] Launch request: {folderPath}");
            if (_processHelper.TryStart("code", folderPath)) return true;

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string exe1 = Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe");
            if (File.Exists(exe1) && _processHelper.TryStart(exe1, folderPath)) return true;
        }
        catch (Exception ex)
        {
            Logger.Write($"[RecentWorkspaces][VSCode] Launch error: {ex}");
        }
        return false;
    }
}