using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Web;

namespace Flow.Launcher.Plugin.RecentWorkspaces.Commons;

#nullable enable
/// <summary>
/// Utilities for VSCode-like editors (Cursor, VS Code, VSCodium) that store recents in storage.json.
/// </summary>
public static class VSCodium
{
    /// <summary>
    /// Parses a VSCode-style storage.json and extracts recent workspace paths.
    /// </summary>
    /// <param name="storageJsonPath">Absolute path to storage.json.</param>
    /// <param name="uriToPath">Optional converter for file:// URIs to local paths. Defaults to Windows converter.</param>
    /// <param name="log">Optional logger callback for diagnostics.</param>
    /// <returns>List of absolute workspace paths (may include folders or files depending on caller).</returns>
    public static List<string> ExtractPathsFromStorage(string storageJsonPath, Func<string, string?>? uriToPath = null, Action<string>? log = null)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(storageJsonPath))
        {
            log?.Invoke($"[VSCodiumHelper] storage.json missing: {storageJsonPath}");
            return new List<string>();
        }

        JsonNode? root;
        try
        {
            using FileStream fs = File.OpenRead(storageJsonPath);
            root = JsonNode.Parse(fs);
        }
        catch (Exception ex)
        {
            log?.Invoke($"[VSCodiumHelper] Failed to parse: {ex.Message}");
            return new List<string>();
        }
        if (root is null) return new List<string>();

        uriToPath ??= TryConvertFileUriToWindowsPath;

        // backupWorkspaces.folders[*].folderUri
        var backupFolders = root["backupWorkspaces"]?["folders"]?.AsArray();
        log?.Invoke($"[VSCodiumHelper] backup folders: {backupFolders?.Count}");
        foreach (var folder in (IEnumerable<JsonNode?>)(backupFolders ?? new JsonArray()))
        {
            var folderUri = folder?["folderUri"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(folderUri)) continue;
            var localPath = uriToPath(folderUri);
            if (!string.IsNullOrWhiteSpace(localPath)) results.Add(localPath!);
        }

        // profileAssociations.workspaces keys
        var workspacesNode = root["profileAssociations"]?["workspaces"] as JsonObject;
        if (workspacesNode is not null)
        {
            foreach (var kv in workspacesNode)
            {
                var localPath = uriToPath(kv.Key);
                if (!string.IsNullOrWhiteSpace(localPath)) results.Add(localPath!);
            }
        }

        return results.ToList();
    }

    /// <summary>
    /// Orders paths by their last write time (most recent first).
    /// </summary>
    public static List<string> OrderByLastWriteDesc(IEnumerable<string> paths)
    {
        return paths
            .Select(p => new { Path = p, Time = SafeGetWriteTimeUtc(p) })
            .OrderByDescending(x => x.Time)
            .Select(x => x.Path)
            .ToList();
    }

    /// <summary>
    /// Safely gets last write time in UTC for files or directories; returns MinValue on error.
    /// </summary>
    public static DateTime SafeGetWriteTimeUtc(string path)
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
    /// Converts VSCode-style file URIs to Windows paths; returns null for unsupported/remote URIs.
    /// </summary>
    public static string? TryConvertFileUriToWindowsPath(string uri)
    {
        try
        {
            if (uri.StartsWith("vscode-remote://", StringComparison.OrdinalIgnoreCase)) return null;
            if (!uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                return Directory.Exists(uri) || File.Exists(uri) ? uri : null;
            }

            var parsed = new Uri(uri);
            string localPath = parsed.LocalPath;
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
            return localPath;
        }
        catch { return null; }
    }
}


