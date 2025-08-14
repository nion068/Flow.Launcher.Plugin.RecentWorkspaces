using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.RecentWorkspaces.Providers;
using System.Threading;

namespace Flow.Launcher.Plugin.RecentWorkspaces;

/// <summary>
/// Flow Launcher plugin that displays recent workspaces.
/// </summary>
public class RecentWorkspaces : IAsyncPlugin
{
    private PluginInitContext _context;

    /// <summary>
    /// Initializes the plugin with the provided context.
    /// </summary>
    /// <param name="context">The Flow Launcher plugin initialization context.</param>
    public Task InitAsync(PluginInitContext context)
    {
        _context = context;
        // Toggle this to enable/disable logging
        Logger.Enabled = false;
        Logger.Write($"[RecentWorkspaces] Init at {DateTime.Now:O}");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns results for the given query.
    /// </summary>
    /// <param name="query">The user's query.</param>
    /// <param name="cancellationToken">Cancellation token propagated by Flow.</param>
    /// <returns>A list of results.</returns>
    public async Task<List<Result>> QueryAsync(Query query, CancellationToken cancellationToken)
    {
        if (query.ActionKeyword == string.Empty)
        {
            return new List<Result>();
        }

        try
        {
            Logger.Write($"[RecentWorkspaces] Query: '{query?.Search}'");
            var provider = new CursorWorkspaceProvider();

            var folders = (await provider.GetWorkspaceFoldersAsync(cancellationToken))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            Logger.Write($"[RecentWorkspaces] Found {folders.Count} folders");

            foreach (var f in folders) Logger.Write($"[RecentWorkspaces] Folder: {f}");

            var results =
                folders
                    .Select(path => new Result
                    {
                        Title = Path.GetFileName(path),
                        SubTitle = path,
                        IcoPath = "Icons/cursor.ico",
                        Action = _ => provider.OpenWorkspace(path)
                    })
                    .ToList();

            if (query.Search == string.Empty)
            {
                return results.Take(10).ToList();
            }

            return results
                .Where(r => _context.API.FuzzySearch(query.Search, r.Title).Score > 0)
                .ToList();
        }
        catch (Exception ex)
        {
            try { Logger.Write($"[RecentWorkspaces] Query error: {ex}"); } catch { }
            return new List<Result>();
        }
    }
}