using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Flow.Launcher.Plugin.RecentWorkspaces.Providers;

/// <summary>
/// Abstraction for a source of recent workspace folders.
/// </summary>
public interface IWorkspaceProvider
{
    /// <summary>
    /// Gets the provider display name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Returns absolute folder paths for recent workspaces.
    /// </summary>
    Task<IReadOnlyList<string>> GetWorkspaceFoldersAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Opens the specified workspace folder using the provider's application.
    /// </summary>
    /// <param name="folderPath">Absolute folder path.</param>
    /// <returns>True if launch succeeded.</returns>
    bool OpenWorkspace(string folderPath);
}


