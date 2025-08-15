using System;
using System.Diagnostics;
using System.IO;

#nullable enable

namespace Flow.Launcher.Plugin.RecentWorkspaces.Helpers;

/// <summary>
/// Utility to start external processes with minimal boilerplate and testability hooks.
/// </summary>
public sealed class ProcessHelper
{
    private readonly IProcessStarter _starter;

    /// <summary>
    /// Creates a new instance of <see cref="ProcessHelper"/>.
    /// </summary>
    /// <param name="starter">Abstraction over process starting, enabling tests.</param>
    public ProcessHelper(IProcessStarter starter)
    {
        _starter = starter;
    }

    /// <summary>
    /// Starts a process with the given executable and a single quoted argument.
    /// </summary>
    /// <param name="fileName">Executable path or app name on PATH.</param>
    /// <param name="argument">Argument to pass (typically a folder path).</param>
    /// <param name="workingDirectory">Optional working directory; defaults to the argument value.</param>
    /// <returns>True if the process was started successfully; otherwise false.</returns>
    public bool TryStart(string fileName, string argument, string? workingDirectory = null)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = '"' + argument + '"',
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = workingDirectory ?? argument
            };
            _starter.Start(psi);
            Logger.Write($"[RecentWorkspaces] Started: {fileName} {psi.Arguments}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Write($"[RecentWorkspaces] Start failed for '{fileName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Opens a document or folder via the shell association (e.g., .sln with Visual Studio).
    /// </summary>
    public bool TryShellOpen(string path, string? workingDirectory = null)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open",
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(path) ?? Environment.CurrentDirectory
            };
            _starter.Start(psi);
            Logger.Write($"[RecentWorkspaces] Shell open: {path}");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Write($"[RecentWorkspaces] Shell open failed for '{path}': {ex.Message}");
            return false;
        }
    }
}

/// <summary>
/// Abstraction over <see cref="Process.Start(ProcessStartInfo)"/> to allow unit testing.
/// </summary>
public interface IProcessStarter
{
    /// <summary>
    /// Starts a process described by the given <see cref="ProcessStartInfo"/>.
    /// </summary>
    void Start(ProcessStartInfo psi);
}

/// <summary>
/// Default implementation that calls <see cref="Process.Start(ProcessStartInfo)"/>.
/// </summary>
public sealed class DefaultProcessStarter : IProcessStarter
{
    /// <inheritdoc />
    public void Start(ProcessStartInfo psi)
    {
        using var _ = Process.Start(psi);
    }
}


