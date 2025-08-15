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

            var providers = new IWorkspaceProvider[]
            {
                new CursorWorkspaceProvider(),
                new VisualStudioWorkspaceProvider(),
            };

            var tasks = providers.Select(p => p.GetWorkspaceFoldersAsync(cancellationToken)).ToArray();
            var resultsByProvider = await Task.WhenAll(tasks);

            var combined = new List<(IWorkspaceProvider Provider, string Path)>();
            for (int i = 0; i < providers.Length; i++)
            {
                foreach (var path in resultsByProvider[i])
                {
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    if (!File.Exists(path) && !Directory.Exists(path)) continue;
                    combined.Add((providers[i], path));
                }
            }

            // De-duplicate by path, keep first occurrence (Cursor preferred before VS)
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            combined = combined.Where(x => seen.Add(x.Path)).ToList();

            Logger.Write($"[RecentWorkspaces] Combined folders: {combined.Count}");
            foreach (var item in combined) Logger.Write($"[RecentWorkspaces] [{item.Provider.Name}] {item.Path}");

            var results = combined
                .Select(item => new Result
                {
                    Title = Path.GetFileName(item.Path),
                    SubTitle = item.Path,
                    IcoPath = item.Provider.GetIconPath(),
                    Action = _ => item.Provider.OpenWorkspace(item.Path)
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