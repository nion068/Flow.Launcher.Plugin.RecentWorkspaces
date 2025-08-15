## Flow.Launcher.Plugin.RecentWorkspaces

A Flow Launcher plugin that lists and opens your recent development workspaces.

### Features
- Combines results from multiple providers:
  - Cursor: recent folders from `%APPDATA%/Cursor/User/globalStorage/storage.json`
  - Visual Studio 2022: recent `.sln` files from instance hives under `%LOCALAPPDATA%/Microsoft/VisualStudio/17.0_*`
- Fast: async I/O and cached results (file timestamp-based)
- One-press launch into the right app (Cursor, Visual Studio)
- Provider-specific icons

### Install (local dev)
1) Build the project:
   - Visual Studio: build the solution
   - CLI: `dotnet publish -c Debug -r win-x64`
2) Copy the published folder `Flow.Launcher.Plugin.RecentWorkspaces/bin/Debug/win-x64/publish` into Flow Launcher plugins directory (or symlink it):
   - `%APPDATA%/FlowLauncher/Plugins/Flow.Launcher.Plugin.RecentWorkspaces`
3) Restart Flow Launcher.

### Usage
- Invoke the plugin keyword (as configured in `plugin.json`) and type to filter.
- Press Enter on a result to launch the workspace in the associated app.

### Providers
- `CursorWorkspaceProvider`
  - Reads Cursor `storage.json`, extracts recent folders, normalizes Windows paths, orders by last write time
  - Icon: `Icons/cursor.ico`
- `VisualStudioWorkspaceProvider`
  - Scans VS 2022 instance directories (`17.0_*`), parses `privateregistry.bin` and optional `ApplicationPrivateSettings.xml`
  - Keeps only existing `.sln` files, ordered by last write time
  - Icon: `Icons/vs.ico`

### Caching
- Both providers use a simple file timestamp cache to avoid repeated heavy I/O:
  - Cursor: keyed by `storage.json` last write time
  - Visual Studio: keyed by the `%LOCALAPPDATA%/Microsoft/VisualStudio` directory timestamp

### Logging
- Toggle in `RecentWorkspaces.InitAsync`:
  - `Logger.Enabled = false;` (set to `true` to enable)
- Logs go to `%TEMP%/RecentWorkspaces.log`.

### Icons
- Add provider icons to the `Icons/` folder. Already referenced:
  - `Icons/cursor.ico`
  - `Icons/vs.ico`

### Extend
- To add a provider, implement `IWorkspaceProvider`:
  - `string Name { get; }`
  - `string GetIconPath()`
  - `Task<IReadOnlyList<string>> GetWorkspaceFoldersAsync(CancellationToken)`
  - `bool OpenWorkspace(string path)`
  - Register it in `Main.cs` with the others.

### License
MIT