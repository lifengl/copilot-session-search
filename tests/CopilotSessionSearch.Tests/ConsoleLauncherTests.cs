#nullable enable

using System.Diagnostics;
using CopilotSessionSearch.Models;
using CopilotSessionSearch.Services;

namespace CopilotSessionSearch.Tests;

public sealed class ConsoleLauncherTests
{
    [Fact]
    public void ResumeSessionUsesWindowsTerminalAndQuotedArguments()
    {
        var processLauncher = new RecordingProcessLauncher();
        var executablePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["copilot"] = @"Q:\tools with spaces\copilot.ps1",
            ["pwsh.exe"] = @"C:\Program Files\PowerShell\7\pwsh.exe",
            ["wt.exe"] = @"C:\Users\user\AppData\Local\Microsoft\WindowsApps\wt.exe",
        };
        var launcher = new ConsoleLauncher(
            processLauncher,
            command => executablePaths.GetValueOrDefault(command));
        string workingDirectory = Path.GetTempPath();
        SessionDescriptor session = CreateDescriptor(workingDirectory);

        launcher.ResumeSession(session);

        ProcessStartInfo startInfo = Assert.IsType<ProcessStartInfo>(processLauncher.StartInfo);
        Assert.Equal(executablePaths["wt.exe"], startInfo.FileName);
        Assert.Equal(workingDirectory, startInfo.WorkingDirectory);
        Assert.Contains("-d", startInfo.ArgumentList);
        Assert.Contains(executablePaths["pwsh.exe"], startInfo.ArgumentList);
        Assert.Contains(
            startInfo.ArgumentList,
            argument => argument.Contains(
                "--resume='session-id'",
                StringComparison.Ordinal));
        Assert.Contains(
            startInfo.ArgumentList,
            argument => argument.Contains(
                "'Q:\\tools with spaces\\copilot.ps1'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void ResumeSessionFallsBackToPowerShellAndUserProfile()
    {
        var processLauncher = new RecordingProcessLauncher();
        var executablePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["copilot"] = @"Q:\tools\copilot.ps1",
            ["powershell.exe"] = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
        };
        var launcher = new ConsoleLauncher(
            processLauncher,
            command => executablePaths.GetValueOrDefault(command));

        launcher.ResumeSession(CreateDescriptor(@"Q:\path-that-does-not-exist"));

        ProcessStartInfo startInfo = Assert.IsType<ProcessStartInfo>(processLauncher.StartInfo);
        Assert.Equal(executablePaths["powershell.exe"], startInfo.FileName);
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            startInfo.WorkingDirectory);
    }

    [Fact]
    public void ResumeSessionFailsClearlyWhenCopilotIsUnavailable()
    {
        var launcher = new ConsoleLauncher(
            new RecordingProcessLauncher(),
            _ => null);

        FileNotFoundException exception = Assert.Throws<FileNotFoundException>(
            () => launcher.ResumeSession(CreateDescriptor(Path.GetTempPath())));

        Assert.Contains("copilot", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SessionDescriptor CreateDescriptor(string workingDirectory)
    {
        return new SessionDescriptor(
            "session-id",
            "Session name",
            DateTimeOffset.Parse("2026-09-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-09-01T11:00:00Z"),
            workingDirectory,
            "owner/repository",
            "main");
    }

    private sealed class RecordingProcessLauncher : IProcessLauncher
    {
        public ProcessStartInfo? StartInfo { get; private set; }

        public void Start(ProcessStartInfo startInfo)
        {
            StartInfo = startInfo;
        }
    }
}
