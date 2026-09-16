#nullable enable

using System.Diagnostics;
using System.IO;
using CopilotSessionSearch.Models;

namespace CopilotSessionSearch.Services;

public sealed class ConsoleLauncher : IConsoleLauncher
{
    private readonly IProcessLauncher _processLauncher;
    private readonly Func<string, string?> _executableLocator;

    public ConsoleLauncher()
        : this(new ProcessLauncher(), FindExecutable)
    {
    }

    public ConsoleLauncher(
        IProcessLauncher processLauncher,
        Func<string, string?> executableLocator)
    {
        ArgumentNullException.ThrowIfNull(processLauncher);
        ArgumentNullException.ThrowIfNull(executableLocator);

        _processLauncher = processLauncher;
        _executableLocator = executableLocator;
    }

    public void ResumeSession(SessionDescriptor session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId);

        string workingDirectory = ResolveWorkingDirectory(session.WorkingDirectory);
        string copilotPath = FindRequiredExecutable("copilot");
        string? powerShellPath = _executableLocator("pwsh.exe")
            ?? _executableLocator("powershell.exe");

        if (powerShellPath is null)
        {
            throw new FileNotFoundException(
                "PowerShell 7 or Windows PowerShell is required to resume a session.");
        }

        string command = $"& {QuotePowerShell(copilotPath)} --resume={QuotePowerShell(session.SessionId)}";
        string? windowsTerminalPath = _executableLocator("wt.exe");
        ProcessStartInfo startInfo = windowsTerminalPath is not null
            ? CreateWindowsTerminalStartInfo(
                windowsTerminalPath,
                powerShellPath,
                workingDirectory,
                command)
            : CreatePowerShellStartInfo(powerShellPath, workingDirectory, command);

        _processLauncher.Start(startInfo);
    }

    private string FindRequiredExecutable(string commandName)
    {
        string? executable = _executableLocator(commandName)
            ?? _executableLocator(commandName + ".exe")
            ?? _executableLocator(commandName + ".cmd")
            ?? _executableLocator(commandName + ".bat")
            ?? _executableLocator(commandName + ".ps1");

        return executable
            ?? throw new FileNotFoundException(
                $"The '{commandName}' command was not found on PATH.");
    }

    private static ProcessStartInfo CreateWindowsTerminalStartInfo(
        string windowsTerminalPath,
        string powerShellPath,
        string workingDirectory,
        string command)
    {
        var startInfo = CreateBaseStartInfo(windowsTerminalPath, workingDirectory);
        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add(workingDirectory);
        startInfo.ArgumentList.Add(powerShellPath);
        startInfo.ArgumentList.Add("-NoExit");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);
        return startInfo;
    }

    private static ProcessStartInfo CreatePowerShellStartInfo(
        string powerShellPath,
        string workingDirectory,
        string command)
    {
        var startInfo = CreateBaseStartInfo(powerShellPath, workingDirectory);
        startInfo.ArgumentList.Add("-NoExit");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);
        return startInfo;
    }

    private static ProcessStartInfo CreateBaseStartInfo(
        string fileName,
        string workingDirectory)
    {
        return new ProcessStartInfo
        {
            CreateNoWindow = false,
            FileName = fileName,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
    }

    private static string ResolveWorkingDirectory(string? workingDirectory)
    {
        if (!string.IsNullOrWhiteSpace(workingDirectory)
            && Directory.Exists(workingDirectory))
        {
            return workingDirectory;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private static string QuotePowerShell(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static string? FindExecutable(string commandName)
    {
        if (Path.IsPathFullyQualified(commandName))
        {
            return File.Exists(commandName)
                ? commandName
                : null;
        }

        string[] extensions = Path.HasExtension(commandName)
            ? [string.Empty]
            : GetExecutableExtensions();

        foreach (string directory in GetPathDirectories())
        {
            foreach (string extension in extensions)
            {
                string candidate = Path.Combine(directory, commandName + extension);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string[] GetExecutableExtensions()
    {
        string? pathExtensions = Environment.GetEnvironmentVariable("PATHEXT");
        IEnumerable<string> extensions = string.IsNullOrWhiteSpace(pathExtensions)
            ? [".exe", ".cmd", ".bat"]
            : pathExtensions.Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return extensions
            .Append(".ps1")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> GetPathDirectories()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
        {
            return [];
        }

        return path.Split(
            Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => directory.Trim('"'));
    }
}
