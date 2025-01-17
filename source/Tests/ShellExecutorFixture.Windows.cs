using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using FluentAssertions;
using Tests.Plumbing;
using Xunit;

namespace Tests;

public class ShellExecutorFixtureWindows(WindowsUserClassFixture fx) : IClassFixture<WindowsUserClassFixture>
{
    static readonly string Command = "cmd.exe";
    static readonly string CommandParam = "/c";

    // Mimic the cancellation behaviour from LoggedTest in Octopus Server; we can't reference it in this assembly
    static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(45);

    readonly CancellationTokenSource cancellationTokenSource = new(TestTimeout);

    readonly TestUserPrincipal user = fx.User;
    CancellationToken CancellationToken => cancellationTokenSource.Token;

    [WindowsFact]
    public void DebugLogging_ShouldContainDiagnosticsInfo_DifferentUser()
    {
        var arguments = $"{CommandParam} \"echo %userdomain%\\%username%\"";
        // Target the CommonApplicationData folder since this is a place the particular user can get to
        var workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var networkCredential = user.GetCredential();
        var customEnvironmentVariables = new Dictionary<string, string>();

        var exitCode = ShellExecutorFixture.Execute(Command,
            arguments,
            workingDirectory,
            out var debugMessages,
            out var infoMessages,
            out var errorMessages,
            networkCredential,
            customEnvironmentVariables,
            CancellationToken);

        exitCode.Should().Be(0, "the process should have run to completion");
        debugMessages.ToString()
            .Should()
            .ContainEquivalentOf(Command, "the command should be logged")
            .And.ContainEquivalentOf($@"{user.DomainName}\{user.UserName}", "the custom user details should be logged")
            .And.ContainEquivalentOf(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "the working directory should be logged");
        infoMessages.ToString().Should().ContainEquivalentOf($@"{user.DomainName}\{user.UserName}");
        errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
    }

    [WindowsFact]
    public void RunningAsDifferentUser_ShouldCopySpecialEnvironmentVariables()
    {
        var arguments = $"{CommandParam} \"echo {EchoEnvironmentVariable("customenvironmentvariable")}\"";
        // Target the CommonApplicationData folder since this is a place the particular user can get to
        var workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var networkCredential = user.GetCredential();
        var customEnvironmentVariables = new Dictionary<string, string>
        {
            { "customenvironmentvariable", "customvalue" }
        };

        var exitCode = ShellExecutorFixture.Execute(Command,
            arguments,
            workingDirectory,
            out _,
            out var infoMessages,
            out var errorMessages,
            networkCredential,
            customEnvironmentVariables,
            CancellationToken);

        exitCode.Should().Be(0, "the process should have run to completion");
        infoMessages.ToString().Should().ContainEquivalentOf("customvalue", "the environment variable should have been copied to the child process");
        errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
    }

    [WindowsFact]
    public void RunningAsDifferentUser_ShouldWorkLotsOfTimes()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(240));

        for (var i = 0; i < 20; i++)
        {
            var arguments = $"{CommandParam} \"echo {EchoEnvironmentVariable("customenvironmentvariable")}%\"";
            // Target the CommonApplicationData folder since this is a place the particular user can get to
            var workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var networkCredential = user.GetCredential();
            var customEnvironmentVariables = new Dictionary<string, string>
            {
                { "customenvironmentvariable", $"customvalue-{i}" }
            };

            var exitCode = ShellExecutorFixture.Execute(Command,
                arguments,
                workingDirectory,
                out _,
                out var infoMessages,
                out var errorMessages,
                networkCredential,
                customEnvironmentVariables,
                cts.Token);

            exitCode.Should().Be(0, "the process should have run to completion");
            infoMessages.ToString().Should().ContainEquivalentOf($"customvalue-{i}", "the environment variable should have been copied to the child process");
            errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
        }
    }

    [WindowsFact]
    public void RunningAsDifferentUser_CanWriteToItsOwnTempPath()
    {
        var arguments = $"{CommandParam} \"echo hello > %temp%hello.txt\"";
        // Target the CommonApplicationData folder since this is a place the particular user can get to
        var workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var networkCredential = user.GetCredential();
        var customEnvironmentVariables = new Dictionary<string, string>();

        var exitCode = ShellExecutorFixture.Execute(Command,
            arguments,
            workingDirectory,
            out _,
            out _,
            out var errorMessages,
            networkCredential,
            customEnvironmentVariables,
            CancellationToken);

        exitCode.Should().Be(0, "the process should have run to completion after writing to the temp folder for the other user");
        errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
    }

    [WindowsTheory]
    [InlineData("powershell.exe", "-command \"Write-Host $env:userdomain\\$env:username\"")]
    public void RunAsCurrentUser_PowerShell_ShouldWork(string command, string arguments)
    {
        var workingDirectory = "";
        var networkCredential = default(NetworkCredential);
        var customEnvironmentVariables = new Dictionary<string, string>();

        var exitCode = ShellExecutorFixture.Execute(command,
            arguments,
            workingDirectory,
            out _,
            out var infoMessages,
            out var errorMessages,
            networkCredential,
            customEnvironmentVariables,
            CancellationToken);

        exitCode.Should().Be(0, "the process should have run to completion");
        errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
        infoMessages.ToString().Should().ContainEquivalentOf($@"{Environment.UserDomainName}\{Environment.UserName}");
    }

    [WindowsTheory]
    [InlineData("cmd.exe", "/c \"echo %userdomain%\\%username%\"")]
    [InlineData("powershell.exe", "-command \"Write-Host $env:userdomain\\$env:username\"")]
    public void RunAsDifferentUser_ShouldWork(string command, string arguments)
    {
        // Target the CommonApplicationData folder since this is a place the particular user can get to
        var workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var networkCredential = user.GetCredential();
        var customEnvironmentVariables = new Dictionary<string, string>();

        var exitCode = ShellExecutorFixture.Execute(command,
            arguments,
            workingDirectory,
            out _,
            out var infoMessages,
            out var errorMessages,
            networkCredential,
            customEnvironmentVariables,
            CancellationToken);

        exitCode.Should().Be(0, "the process should have run to completion");
        errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
        infoMessages.ToString().Should().ContainEquivalentOf($@"{user.DomainName}\{user.UserName}");
    }

    static string EchoEnvironmentVariable(string varName) => $"%{varName}%";
}