using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using Flow.Launcher.Plugin.RecentWorkspaces.Caching;
using Flow.Launcher.Plugin.RecentWorkspaces.Helpers;
using System.Threading;
using System.Threading.Tasks;

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
            JsonNode? root;
            try
            {
                using FileStream fs = File.OpenRead(path);
                root = JsonNode.Parse(fs);
            }
            catch (Exception ex)
            {
                Logger.Write($"[RecentWorkspaces][Cursor] Failed reading/parsing storage.json: {ex}");
                return new List<string>();
            }

            if (root is null)
            {
                Logger.Write("[RecentWorkspaces][Cursor] Parsed root is null");
                return new List<string>();
            }

            var collected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // backupWorkspaces.folders[*].folderUri
            var backupFolders = root["backupWorkspaces"]?["folders"]?.AsArray();
            Logger.Write($"[RecentWorkspaces][Cursor] backupWorkspaces.folders count: {backupFolders?.Count}");
            foreach (var folder in (IEnumerable<JsonNode?>)(backupFolders ?? new JsonArray()))
            {
                var folderUri = folder?["folderUri"]?.GetValue<string>();

                if (string.IsNullOrWhiteSpace(folderUri))
                {
                    Logger.Write("[RecentWorkspaces][Cursor] Skipping empty folderUri");
                    continue;
                }

                var localPath = TryConvertFileUriToPath(folderUri);

                if (!string.IsNullOrWhiteSpace(localPath))
                {
                    Logger.Write($"[RecentWorkspaces][Cursor] backup folder: {localPath}");
                    collected.Add(localPath!);
                }
                else
                {
                    Logger.Write($"[RecentWorkspaces][Cursor] Could not convert: {folderUri}");
                }
            }

            // profileAssociations.workspaces keys
            var workspacesNode = root["profileAssociations"]?["workspaces"] as JsonObject;

            Logger.Write($"[RecentWorkspaces][Cursor] profileAssociations.workspaces present: {workspacesNode is not null}");

            if (workspacesNode is null)
            {
                return collected.ToList();
            }

            foreach (KeyValuePair<string, JsonNode?> kv in workspacesNode)
            {
                var localPath = TryConvertFileUriToPath(kv.Key);

                if (!string.IsNullOrWhiteSpace(localPath))
                {
                    Logger.Write($"[RecentWorkspaces][Cursor] workspace key path: {localPath}");
                    collected.Add(localPath!);
                }
                else
                {
                    Logger.Write($"[RecentWorkspaces][Cursor] Could not convert workspace key: {kv.Key}");
                }
            }

            var built = collected.ToList();
            var ordered = built
                .Select(p => new { Path = p, Time = SafeGetDirWriteTimeUtc(p) })
                .OrderByDescending(x => x.Time)
                .Select(x => x.Path)
                .ToList();

            Logger.Write($"[RecentWorkspaces][Cursor] Cache built (ordered): {ordered.Count}");

            return ordered;
        }), cancellationToken);

        Logger.Write($"[RecentWorkspaces][Cursor] Using cached folders: {list.Count}");
        return list;
    }

    private static DateTime SafeGetDirWriteTimeUtc(string path)
    {
        try
        {
            if (Directory.Exists(path)) return Directory.GetLastWriteTimeUtc(path);
            if (File.Exists(path)) return File.GetLastWriteTimeUtc(path);
        }
        catch { }
        return DateTime.MinValue;
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

    private static string? TryConvertFileUriToPath(string uri)
    {
        try
        {
            if (uri.StartsWith("vscode-remote://", StringComparison.OrdinalIgnoreCase))
            {
                return null; // Skip remote URIs for now
            }

            if (!uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                // Some entries might already be plain paths
                return Directory.Exists(uri) || File.Exists(uri) ? uri : null;
            }

            var parsed = new Uri(uri);

            string localPath = parsed.LocalPath;

            // On Windows, Uri.LocalPath returns percent-decoded, but ensure consistency
            localPath = HttpUtility.UrlDecode(localPath);

            if (localPath.Length >= 3 && localPath[0] == '/' && char.IsLetter(localPath[1]) && localPath[2] == ':')
            {
                localPath = localPath.Substring(1);
            }

            if (localPath.Length >= 2 && localPath[1] == ':')
            {
                localPath = char.ToUpperInvariant(localPath[0]) + localPath.Substring(1);
            }

            localPath = localPath.Replace('/', '\\');
            try { localPath = Path.GetFullPath(localPath); } catch { }

            Logger.Write($"[RecentWorkspaces][Cursor] Converted '{uri}' -> '{localPath}'");
            return localPath;
        }
        catch
        {
            return null;
        }
    }
}


