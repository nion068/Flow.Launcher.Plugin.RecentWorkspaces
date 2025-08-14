using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Flow.Launcher.Plugin.RecentWorkspaces.Helpers;
using Flow.Launcher.Plugin.RecentWorkspaces.Caching;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Flow.Launcher.Plugin.RecentWorkspaces.Providers;

#nullable enable
/// <summary>
/// Workspace provider for Microsoft Visual Studio (2022).
/// Extracts recent .sln files from private hives in LocalAppData.
/// </summary>
public sealed class VisualStudioWorkspaceProvider : IWorkspaceProvider
{
    private readonly ProcessHelper _processHelper;
    private static readonly FileTimestampCache<List<string>> Cache = new();

    /// <summary>
    /// Creates a Visual Studio workspace provider.
    /// </summary>
    public VisualStudioWorkspaceProvider() : this(new ProcessHelper(new DefaultProcessStarter())) { }

    /// <summary>
    /// Creates a Visual Studio workspace provider with a custom process helper (for testing).
    /// </summary>
    public VisualStudioWorkspaceProvider(ProcessHelper processHelper)
    {
        _processHelper = processHelper;
    }

    /// <inheritdoc />
    public string Name => "Visual Studio";

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetWorkspaceFoldersAsync(CancellationToken cancellationToken)
    {
        string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "VisualStudio");

        if (!Directory.Exists(baseDir))
        {
            Logger.Write("[RecentWorkspaces][VS] Base directory not found");
            return Array.Empty<string>();
        }

        return await Task.Run(() =>
        {
            // Use directory timestamp as cache key surrogate for the instance hive set
            List<string> built = Cache.GetOrRefresh(baseDir, _ =>
            {
                var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (string instDir in GetVs2022InstanceDirs())
                {
                    string hivePath = Path.Combine(instDir, "privateregistry.bin");
                    if (File.Exists(hivePath)) TryReadVs17Hive(hivePath, results);

                    string appPriv = Path.Combine(instDir, "ApplicationPrivateSettings.xml");
                    if (File.Exists(appPriv)) TryParseApplicationPrivateSettings(appPriv, results);
                }

                var final = results
                    .Select(NormalizeToSlnOrEmpty)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Where(File.Exists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(p => new { Path = p, Time = SafeGetWriteTimeUtc(p) })
                    .OrderByDescending(x => x.Time)
                    .Select(x => x.Path)
                    .ToList();

                Logger.Write($"[RecentWorkspaces][VS] Cache built: {final.Count}");
                return final;
            });

            Logger.Write($"[RecentWorkspaces][VS] Using cached solutions: {built.Count}");

            return (IReadOnlyList<string>)built;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public bool OpenWorkspace(string folderPath)
    {
        // For VS, folderPath is a .sln path
        try
        {
            Logger.Write($"[RecentWorkspaces][VS] Launch request: {folderPath}");

            // Prefer 'devenv' on PATH
            if (_processHelper.TryStart("devenv", folderPath, Path.GetDirectoryName(folderPath))) return true;

            // Fallback: common install locations (VS Installer supports multiple paths; try minimal heuristics)
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var guesses = new[]
            {
                Path.Combine(programFilesX86, "Microsoft Visual Studio", "2022", "Community", "Common7", "IDE", "devenv.exe"),
                Path.Combine(programFilesX86, "Microsoft Visual Studio", "2022", "Professional", "Common7", "IDE", "devenv.exe"),
                Path.Combine(programFilesX86, "Microsoft Visual Studio", "2022", "Enterprise", "Common7", "IDE", "devenv.exe")
            };
            foreach (var exe in guesses)
            {
                if (File.Exists(exe) && _processHelper.TryStart(exe, folderPath, Path.GetDirectoryName(folderPath))) return true;
            }
        }
        catch (Exception ex)
        {
            Logger.Write($"[RecentWorkspaces][VS] Launch error: {ex}");
        }
        return false;
    }

    private static IEnumerable<string> GetVs2022InstanceDirs()
    {
        string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "VisualStudio");
        if (!Directory.Exists(baseDir)) yield break;

        foreach (string d in Directory.EnumerateDirectories(baseDir, "17.0_*").OrderByDescending(Directory.GetLastWriteTimeUtc))
            yield return d;
    }

    private static void TryParseApplicationPrivateSettings(string xmlPath, HashSet<string> sink)
    {
        try
        {
            string text = File.ReadAllText(xmlPath);
            CollectFromString(text, sink);
        }
        catch { }
    }

    private static readonly Regex WinPath = new(@"[A-Za-z]:\\[^:\*\?""<>|\r\n]+", RegexOptions.IgnoreCase);
    private static readonly Regex FileUri = new(@"file:///[A-Za-z]:/[^""\s>]+", RegexOptions.IgnoreCase);
    private static readonly Regex SlnExt = new(@"\.sln($|[\?""\s])", RegexOptions.IgnoreCase);

    private static void CollectFromString(string s, HashSet<string> sink)
    {
        foreach (Match m in FileUri.Matches(s)) sink.Add(UriToPath(m.Value));
        foreach (Match m in WinPath.Matches(s)) sink.Add(m.Value);
    }

    private static string UriToPath(string uri)
    {
        try { return new Uri(uri).LocalPath; } catch { return uri; }
    }

    private static string NormalizeToSlnOrEmpty(string p)
    {
        if (string.IsNullOrWhiteSpace(p)) return string.Empty;
        p = p.Trim().Trim('"');
        if (p.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)) p = UriToPath(p);
        if (SlnExt.IsMatch(p)) return p; // keep only .sln
        return string.Empty;
    }

    private static DateTime SafeGetWriteTimeUtc(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); } catch { return DateTime.MinValue; }
    }

    // P/Invoke for RegLoadAppKey to open privateregistry.bin without admin
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegLoadAppKey(string lpFile, out IntPtr phkResult, int samDesired, int dwOptions, int reserved);

    private const int KEY_READ = 0x20019; // STANDARD_RIGHTS_READ | KEY_QUERY_VALUE | KEY_ENUMERATE_SUB_KEYS
    private const int ERROR_SUCCESS = 0;

    private static void TryReadVs17Hive(string hiveFile, HashSet<string> sink)
    {
        if (!File.Exists(hiveFile)) return;

        IntPtr hKey;
        int rc = RegLoadAppKey(hiveFile, out hKey, KEY_READ, 0, 0);
        if (rc != ERROR_SUCCESS || hKey == IntPtr.Zero) return;

        using var safe = new SafeRegistryHandle(hKey, ownsHandle: true);
        using var root = RegistryKey.FromHandle(safe, RegistryView.Default);

        using var sw = root.OpenSubKey(@"Software\Microsoft\VisualStudio");
        if (sw is null) return;

        foreach (string sub in sw.GetSubKeyNames().Where(n => n.StartsWith("17.0_", StringComparison.OrdinalIgnoreCase)))
        {
            using var instKey = sw.OpenSubKey(sub);
            if (instKey is null) continue;

            foreach (string known in new[] { "MRUItems", "MRUItems\\Solution", "StartPage", "StartPage\\MRUItems", "FileMRUList", "ProjectMRUList" })
            {
                using var k = instKey.OpenSubKey(known);
                if (k != null) RecurseRegistryForPaths(k, sink);
            }

            RecurseRegistryForPaths(instKey, sink);
        }
    }

    private static void RecurseRegistryForPaths(RegistryKey key, HashSet<string> sink)
    {
        try
        {
            foreach (string name in key.GetValueNames())
            {
                object? v = key.GetValue(name);
                if (v is string s)
                {
                    CollectFromString(s, sink);
                }
                else if (v is string[] arr)
                {
                    foreach (string s2 in arr) CollectFromString(s2, sink);
                }
            }
            foreach (string sub in key.GetSubKeyNames())
            {
                using var child = key.OpenSubKey(sub);
                if (child != null) RecurseRegistryForPaths(child, sink);
            }
        }
        catch { }
    }
}