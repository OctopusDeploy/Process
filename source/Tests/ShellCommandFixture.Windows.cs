using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Octopus.Shellfish;
using Tests.Plumbing;
using Xunit;

// We disable this because we want to test the synchronous version of the method as well as the async version using the same test method
// ReSharper disable MethodHasAsyncOverload

namespace Tests;

// Windows-specific tests for ShellCommandExecutor
#if NET5_0_OR_GREATER
[SupportedOSPlatform("Windows")]
#endif
public class ShellCommandFixtureWindows(WindowsUserClassFixture fx) : IClassFixture<WindowsUserClassFixture>
{
    readonly TestUserPrincipal user = fx.User;

    // If unspecified, ShellCommand will default to the current directory, which our temporary user may not have access to.
    // Our tests that run as a different user need to set a different working directory or they may fail.
    readonly string commonAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    readonly CancellationTokenSource cancellationTokenSource = new(ShellCommandFixture.TestTimeout);
#if NET5_0_OR_GREATER
    static ShellCommandFixtureWindows()
    {
        // Our "WithOutputEncoding" test fails on .NET Core without this
        // Refer https://nicolaiarocci.com/how-to-read-windows-1252-encoded-files-with-.netcore-and-.net5-/
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
#endif
    CancellationToken CancellationToken => cancellationTokenSource.Token;

    [WindowsTheory]
    [InlineData("cmd.exe", "/c \"echo %userdomain%\\%username%\"", SyncBehaviour.Sync)]
    [InlineData("cmd.exe", "/c \"echo %userdomain%\\%username%\"", SyncBehaviour.Async)]
    [InlineData("powershell.exe", "-command \"Write-Host $env:userdomain\\$env:username\"", SyncBehaviour.Sync)]
    [InlineData("powershell.exe", "-command \"Write-Host $env:userdomain\\$env:username\"", SyncBehaviour.Async)]
    public async Task RunAsCurrentUser_CmdAndPowerShell_ShouldWork(string command, string arguments, SyncBehaviour behaviour)
    {
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var executor = new ShellCommand(command)
            .WithArguments(arguments)
            .WithStdOutTarget(stdOut)
            .WithStdErrTarget(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
        stdOut.ToString().Should().ContainEquivalentOf($@"{Environment.UserDomainName}\{Environment.UserName}");
    }

    [WindowsTheory]
    [InlineData(SyncBehaviour.Sync)]
    [InlineData(SyncBehaviour.Async)]
    public async Task SettingOutputEncodingShouldAllowUsToReadWeirdText(SyncBehaviour behaviour)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cmd");
        // codepage 932 is Shift-JIS
        File.WriteAllText(tempFile,
            """
            @ECHO OFF
            CHCP 932
            ECHO ㈱髙
            """);
        try
        {
            var stdOut = new StringBuilder();
            var stdErr = new StringBuilder();

            var executor = new ShellCommand("cmd.exe")
                .WithArguments("/c " + tempFile)
                .WithStdOutTarget(stdOut)
                .WithStdErrTarget(stdErr);

            var result = behaviour == SyncBehaviour.Async
                ? await executor.ExecuteAsync(CancellationToken)
                : executor.Execute(CancellationToken);

            result.ExitCode.Should().Be(0, "the process should have run to completion");
            stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
            stdOut.ToString().Should().Be("Active code page: 932" + Environment.NewLine + "πê\u2592Θ\u00bdüE" + Environment.NewLine);

            // Now try again with the encoding set to Shift-JIS, it should work
            stdOut.Clear();
            stdErr.Clear();
            executor.WithOutputEncoding(Encoding.GetEncoding(932));

            var resultFixed = behaviour == SyncBehaviour.Async
                ? await executor.ExecuteAsync(CancellationToken)
                : executor.Execute(CancellationToken);

            resultFixed.ExitCode.Should().Be(0, "the process should have run to completion");
            stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
            stdOut.ToString().Should().Be("Active code page: 932" + Environment.NewLine + "繹ｱ鬮・" + Environment.NewLine);
        }
        finally
        {
            try
            {
                File.Delete(tempFile);
            }
            catch (Exception)
            {
                // can't do much, just leave the tempfile
            }
        }
    }

    [WindowsTheory]
    [InlineData("cmd.exe", "/c \"echo %userdomain%\\%username%\"", SyncBehaviour.Sync)]
    [InlineData("cmd.exe", "/c \"echo %userdomain%\\%username%\"", SyncBehaviour.Async)]
    [InlineData("powershell.exe", "-command \"Write-Host $env:userdomain\\$env:username\"", SyncBehaviour.Sync)]
    [InlineData("powershell.exe", "-command \"Write-Host $env:userdomain\\$env:username\"", SyncBehaviour.Async)]
    public async Task RunAsDifferentUser_ShouldWork(string command, string arguments, SyncBehaviour behaviour)
    {
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var executor = new ShellCommand(command)
            .WithArguments(arguments)
            .WithCredentials(user.GetCredential())
            .WithWorkingDirectory(commonAppDataPath)
            .WithStdOutTarget(stdOut)
            .WithStdErrTarget(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
        stdOut.ToString().Should().ContainEquivalentOf($@"{user.DomainName}\{user.UserName}");
    }

    [WindowsTheory]
    [InlineData(SyncBehaviour.Sync)]
    [InlineData(SyncBehaviour.Async)]
    public async Task RunningAsDifferentUser_ShouldCopySpecialEnvironmentVariables(SyncBehaviour behaviour)
    {
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var executor = new ShellCommand("cmd.exe")
            .WithArguments($"/c \"echo {EchoEnvironmentVariable("customenvironmentvariable")}\"")
            .WithCredentials(user.GetCredential())
            .WithWorkingDirectory(commonAppDataPath)
            .WithEnvironmentVariables(new Dictionary<string, string>
            {
                { "customenvironmentvariable", "customvalue" }
            })
            .WithStdOutTarget(stdOut)
            .WithStdErrTarget(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
        stdOut.ToString().Should().ContainEquivalentOf("customvalue", "the environment variable should have been copied to the child process");
    }

    [WindowsTheory]
    [InlineData(SyncBehaviour.Sync)]
    [InlineData(SyncBehaviour.Async)]
    public async Task RunningAsDifferentUser_ShouldWorkLotsOfTimes(SyncBehaviour behaviour)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(240));

        var executor = new ShellCommand("cmd.exe")
            .WithArguments($"/c \"echo {EchoEnvironmentVariable("customenvironmentvariable")}%\"")
            .WithCredentials(user.GetCredential())
            .WithWorkingDirectory(commonAppDataPath);

        for (var i = 0; i < 20; i++)
        {
            var stdOut = new StringBuilder();
            var stdErr = new StringBuilder();

            // the executor is mutable so this overwrites the config each time
            executor
                .WithStdOutTarget(stdOut)
                .WithStdErrTarget(stdErr)
                .WithEnvironmentVariables(new Dictionary<string, string>
                {
                    { "customenvironmentvariable", $"customvalue-{i}" }
                });

            var result = behaviour == SyncBehaviour.Async
                ? await executor.ExecuteAsync(CancellationToken)
                : executor.Execute(CancellationToken);

            result.ExitCode.Should().Be(0, "the process should have run to completion");
            stdOut.ToString().Should().ContainEquivalentOf($"customvalue-{i}", "the environment variable should have been copied to the child process");
            stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
        }
    }

    [WindowsTheory]
    [InlineData(SyncBehaviour.Sync)]
    [InlineData(SyncBehaviour.Async)]
    public async Task RunningAsDifferentUser_CanWriteToItsOwnTempPath(SyncBehaviour behaviour)
    {
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var uniqueString = Guid.NewGuid().ToString("N");

        var executor = new ShellCommand("cmd.exe")
            // Prove we can write to the temp folder by reading the contents back and echoing them into our test 
            .WithArguments($"/c \"echo {uniqueString} > %temp%\\{uniqueString}.txt && type %temp%\\{uniqueString}.txt\"")
            .WithCredentials(user.GetCredential())
            .WithWorkingDirectory(commonAppDataPath)
            .WithStdOutTarget(stdOut)
            .WithStdErrTarget(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion after writing to the temp folder for the other user");
        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
        stdOut.ToString().Should().Contain(uniqueString);
    }

    static string EchoEnvironmentVariable(string varName) => $"%{varName}%";
}