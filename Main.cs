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
public class RecentWorkspaces : IAsyncPlugin, IContextMenu
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

    private static IWorkspaceProvider[] DiscoverProviders()
    {
        var providerType = typeof(IWorkspaceProvider);

        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
            })
            .Where(t => providerType.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .Select(t =>
            {
                try { return Activator.CreateInstance(t) as IWorkspaceProvider; } catch { return null; }
            })
            .Where(p => p != null)
            .Cast<IWorkspaceProvider>()
            .ToArray();
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

            var providers = DiscoverProviders();
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

    /// <summary>
    /// Load context menus for the selected result.
    /// </summary>
    /// <param name="selectedResult"></param>
    /// <returns></returns>
    public List<Result> LoadContextMenus(Result selectedResult)
    {
        var providers = DiscoverProviders();

        var path = selectedResult.SubTitle;

        var cursorProvider = providers.FirstOrDefault(p => p is CursorWorkspaceProvider);
        var vscodeProvider = providers.FirstOrDefault(p => p is VSCodeWorkspaceProvider);
        var vsProvider = providers.FirstOrDefault(p => p is VisualStudioWorkspaceProvider);

        return new List<Result>
        {
            new()
            {
                Title = "Open in nvim",
                SubTitle = "Open the workspace in nvim",
                IcoPath = "Icons/nvim.ico",
                Action = _ =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo()
                        {
                            FileName = "pwsh",
                            Arguments = $"-NoExit -Command \"nvim .\"",
                            WorkingDirectory = path,
                            UseShellExecute = false
                        });
                        return true;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                },
                Glyph = new GlyphInfo(FontFamily: "/Resources/#Segoe Fluent Icons", Glyph: "\ue756"),

            },
            new()
            {
                Title = "Open in Cursor",
                SubTitle = "Open the workspace in Cursor",
                IcoPath = cursorProvider.GetIconPath(),
                Action = _ => cursorProvider.OpenWorkspace(path)
            },
            new()
            {
                Title = "Open in VS Code",
                SubTitle = "Open the workspace in VS Code",
                IcoPath = vscodeProvider.GetIconPath(),
                Action = _ => vscodeProvider.OpenWorkspace(path)
            },
            new()
            {
                Title = "Open in Visual Studio",
                SubTitle = "Open the workspace in Visual Studio",
                IcoPath = vsProvider.GetIconPath(),
                Action = _ => vsProvider.OpenWorkspace(path)
            }
        };
    }
}