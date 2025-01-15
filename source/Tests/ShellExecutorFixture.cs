using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using FluentAssertions;
using Octopus.Shellfish;
using Octopus.Shellfish.Plumbing;
using Octopus.Shellfish.Windows;
using Xunit;

namespace Tests;

public class ShellExecutorFixture
{
    // ReSharper disable InconsistentNaming
    const int SIG_TERM = 143;
    const int SIG_KILL = 137;

    static readonly string Command = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "bash";
    static readonly string CommandParam = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "/c" : "-c";

    // Mimic the cancellation behaviour from LoggedTest in Octopus Server; we can't reference it in this assembly
    static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(45);

    readonly CancellationTokenSource cancellationTokenSource = new(TestTimeout);
    CancellationToken CancellationToken => cancellationTokenSource.Token;

    [Fact]
    public void ExitCode_ShouldBeReturned()
    {
        var arguments = $"{CommandParam} \"exit 99\"";
        var workingDirectory = "";
        var networkCredential = default(NetworkCredential);
        IDictionary<string, string>? customEnvironmentVariables = null;

        var exitCode = Execute(Command,
            arguments,
            workingDirectory,
            out var debugMessages,
            out var infoMessages,
            out var errorMessages,
            networkCredential,
            customEnvironmentVariables,
            CancellationToken);

        var expectedEncoding = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? WindowsEncodingHelper.GetOemEncoding() : Encoding.UTF8;

        exitCode.Should().Be(99, "our custom exit code should be reflected");

        debugMessages.ToString().Should().ContainEquivalentOf($"Starting {Command} in working directory '' using '{expectedEncoding.EncodingName}' encoding running as '{ProcessIdentity.CurrentUserName}'");
        errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
        infoMessages.ToString().Should().BeEmpty("no messages should be written to stdout");
    }

    [Fact]
    public void DebugLogging_ShouldContainDiagnosticsInfo_ForDefault()
    {
        var arguments = $"{CommandParam} \"echo hello\"";
        var workingDirectory = "";
        var networkCredential = default(NetworkCredential);
        var customEnvironmentVariables = new Dictionary<string, string>();

        var exitCode = Execute(Command,
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
            .And.ContainEquivalentOf(ProcessIdentity.CurrentUserName, "the current user details should be logged");
        infoMessages.ToString().Should().ContainEquivalentOf("hello");
        errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
    }

    [Fact]
    public void RunningAsSameUser_ShouldCopySpecialEnvironmentVariables()
    {
        var arguments = $"{CommandParam} \"echo {EchoEnvironmentVariable("customenvironmentvariable")}\"";
        var workingDirectory = "";
        var networkCredential = default(NetworkCredential);
        var customEnvironmentVariables = new Dictionary<string, string>
        {
            { "customenvironmentvariable", "customvalue" }
        };

        var exitCode = Execute(Command,
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

    [Fact]
    public void CancellationToken_ShouldForceKillTheProcess()
    {
        // Terminate the process after a very short time so the test doesn't run forever
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        // Starting a new instance of cmd.exe will run indefinitely waiting for user input
        var arguments = "";
        var workingDirectory = "";
        var networkCredential = default(NetworkCredential);
        var customEnvironmentVariables = new Dictionary<string, string>();

        var exitCode = Execute(Command,
            arguments,
            workingDirectory,
            out _,
            out var infoMessages,
            out var errorMessages,
            networkCredential,
            customEnvironmentVariables,
            cts.Token);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            exitCode.Should().BeLessOrEqualTo(0, "the process should have been terminated");
            infoMessages.ToString().Should().ContainEquivalentOf("Microsoft Windows", "the default command-line header would be written to stdout");
        }
        else
        {
            exitCode.Should().BeOneOf(SIG_KILL, SIG_TERM, 0, -1);
        }

        errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
    }

    [Fact]
    public void EchoHello_ShouldWriteToStdOut()
    {
        var arguments = $"{CommandParam} \"echo hello\"";
        var workingDirectory = "";
        var networkCredential = default(NetworkCredential);
        var customEnvironmentVariables = new Dictionary<string, string>();

        var exitCode = Execute(Command,
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
        infoMessages.ToString().Should().ContainEquivalentOf("hello");
    }

    [Fact]
    public void EchoError_ShouldWriteToStdErr()
    {
        var arguments = $"{CommandParam} \"echo Something went wrong! 1>&2\"";
        var workingDirectory = "";
        var networkCredential = default(NetworkCredential);
        var customEnvironmentVariables = new Dictionary<string, string>();

        var exitCode = Execute(Command,
            arguments,
            workingDirectory,
            out _,
            out var infoMessages,
            out var errorMessages,
            networkCredential,
            customEnvironmentVariables,
            CancellationToken);

        exitCode.Should().Be(0, "the process should have run to completion");
        infoMessages.ToString().Should().BeEmpty("no messages should be written to stdout");
        errorMessages.ToString().Should().ContainEquivalentOf("Something went wrong!");
    }

    [Fact]
    public void RunAsCurrentUser_ShouldWork()
    {
        var arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $"{CommandParam} \"echo {EchoEnvironmentVariable("username")}\""
            : $"{CommandParam} \"whoami\"";
        var workingDirectory = "";
        var networkCredential = default(NetworkCredential);
        var customEnvironmentVariables = new Dictionary<string, string>();

        var exitCode = Execute(Command,
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
        infoMessages.ToString().Should().ContainEquivalentOf($@"{Environment.UserName}");
    }

    static string EchoEnvironmentVariable(string varName)
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? $"%{varName}%" : $"${varName}";

    public static int Execute(
        string command,
        string arguments,
        string workingDirectory,
        out StringBuilder debugMessages,
        out StringBuilder infoMessages,
        out StringBuilder errorMessages,
        NetworkCredential? networkCredential,
        IDictionary<string, string>? customEnvironmentVariables,
        CancellationToken cancel
    )
    {
        var debug = new StringBuilder();
        var info = new StringBuilder();
        var error = new StringBuilder();
        var exitCode = ShellExecutor.ExecuteCommand(
            command,
            arguments,
            workingDirectory,
            x =>
            {
                Console.WriteLine($"{DateTime.UtcNow} DBG: {x}");
                debug.Append(x);
            },
            x =>
            {
                Console.WriteLine($"{DateTime.UtcNow} INF: {x}");
                info.Append(x);
            },
            x =>
            {
                Console.WriteLine($"{DateTime.UtcNow} ERR: {x}");
                error.Append(x);
            },
            networkCredential,
            customEnvironmentVariables,
            cancel);

        debugMessages = debug;
        infoMessages = info;
        errorMessages = error;

        return exitCode;
    }
}