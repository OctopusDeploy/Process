using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Octopus.Shellfish;
using Tests.Plumbing;
using Xunit;

namespace Tests;

// Cross-platform tests for ShellCommand which deal with std input
public class ShellCommandFixtureStdInput
{
    readonly CancellationTokenSource cancellationTokenSource = new(ShellCommandFixture.TestTimeout);
    CancellationToken CancellationToken => cancellationTokenSource.Token;

    [Theory]
    [InlineData(SyncBehaviour.Sync)]
    [InlineData(SyncBehaviour.Async)]
    public async Task ShouldWork(SyncBehaviour behaviour)
    {
        using var tempScript = TempScript.Create(
            """
            @echo off
            echo Enter First Name:
            set /p firstname=
            echo Hello '%firstname%'
            """,
            """
            echo "Enter First Name:"
            read firstname
            echo "Hello '$firstname'"
            """);

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        var executor = new ShellCommand(tempScript.GetHostExecutable())
            .WithArguments(tempScript.GetCommandArgs())
            .WithStdInSource("Bob") // it's going to ask us for the names, we need to answer back or the process will stall forever; we can preload this
            .WithStdOutTarget(stdOut)
            .WithStdErrTarget(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
        stdOut.ToString().Should().Be("Enter First Name:" + Environment.NewLine + "Hello 'Bob'" + Environment.NewLine);
    }

    [Theory]
    [InlineData(SyncBehaviour.Sync)]
    [InlineData(SyncBehaviour.Async)]
    public async Task MultipleInputItemsShouldWork(SyncBehaviour behaviour)
    {
        using var tempScript = TempScript.Create(
            """
            @echo off
            echo Enter First Name:
            set /p firstname=
            echo Enter Last Name:
            set /p lastname=
            echo Hello '%firstname%' '%lastname%'
            """,
            """
            echo "Enter First Name:"
            read firstname
            echo "Enter Last Name:"
            read lastname
            echo "Hello '$firstname' '$lastname'"
            """);

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        // it's going to ask us for the names, we need to answer back or the process will stall forever; we can preload this
        var stdIn = new TestInputSource();

        var executor = new ShellCommand(tempScript.GetHostExecutable())
            .WithArguments(tempScript.GetCommandArgs())
            .WithStdInSource(stdIn)
            .WithStdOutTarget(stdOut)
            .WithStdOutTarget(l =>
            {
                if (l.Contains("First")) stdIn.OnNext("Bob");
                if (l.Contains("Last")) stdIn.OnNext("Octopus");
            })
            .WithStdErrTarget(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
        stdOut.ToString().Should().Be("Enter First Name:" + Environment.NewLine + "Enter Last Name:" + Environment.NewLine + "Hello 'Bob' 'Octopus'" + Environment.NewLine);
    }

    [Theory]
    [InlineData(SyncBehaviour.Sync)]
    [InlineData(SyncBehaviour.Async)]
    public async Task ClosingStdInEarly(SyncBehaviour behaviour)
    {
        using var tempScript = TempScript.Create(
            """
            @echo off
            echo Enter First Name:
            set /p firstname=
            echo Enter Last Name:
            set /p lastname=
            echo Hello '%firstname%' '%lastname%'
            """,
            """
            echo "Enter First Name:"
            read firstname
            echo "Enter Last Name:"
            read lastname
            echo "Hello '$firstname' '$lastname'"
            """);

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        // it's going to ask us for the names, we need to answer back or the process will stall forever; we can preload this
        var stdIn = new TestInputSource();

        var executor = new ShellCommand(tempScript.GetHostExecutable())
            .WithArguments(tempScript.GetCommandArgs())
            .WithStdInSource(stdIn)
            .WithStdOutTarget(stdOut)
            .WithStdOutTarget(l =>
            {
                if (l.Contains("First")) stdIn.OnNext("Bob");
                if (l.Contains("Last")) stdIn.OnCompleted(); // shut it down
            })
            .WithStdErrTarget(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
        // When we close stdin the waiting process receives an EOF; Our trivial shell script interprets this as an empty string
        stdOut.ToString().Should().Be("Enter First Name:" + Environment.NewLine + "Enter Last Name:" + Environment.NewLine + "Hello 'Bob' ''" + Environment.NewLine);
    }

    [Theory]
    [InlineData(SyncBehaviour.Sync)]
    [InlineData(SyncBehaviour.Async)]
    public async Task ShouldReleaseInputSourceWhenProgramExits(SyncBehaviour behaviour)
    {
        using var tempScript = TempScript.Create(
            """
            @echo off
            echo Enter First Name:
            set /p firstname=
            echo Hello '%firstname%'
            """,
            """
            echo "Enter First Name:"
            read firstname
            echo "Hello '$firstname'"
            """);

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        // it's going to ask us for the names, we need to answer back or the process will stall forever; we can preload this
        var stdIn = new TestInputSource();

        var executor = new ShellCommand(tempScript.GetHostExecutable())
            .WithArguments(tempScript.GetCommandArgs())
            .WithStdInSource(stdIn)
            .WithStdOutTarget(stdOut)
            .WithStdOutTarget(l =>
            {
                stdIn.Subscriber.Should().NotBeNull("the shellcommand should still be subscribed to the input source while the process is running");

                // when we receive the first prompt, cancel and kill the process
                if (l.Contains("Enter First Name:")) stdIn.OnNext("Bob");
            })
            .WithStdErrTarget(stdErr);

        stdIn.Subscriber.Should().BeNull("the shellcommand should not subscribe to the input source until the process starts");

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(CancellationToken)
            : executor.Execute(CancellationToken);

        result.ExitCode.Should().Be(0, "the process should have run to completion");
        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
        stdOut.ToString().Should().Be("Enter First Name:" + Environment.NewLine + "Hello 'Bob'" + Environment.NewLine);

        stdIn.Subscriber.Should().BeNull("the shellcommand should have unsubscribed from the input source after the process exits");
    }

    [Theory]
    [InlineData(SyncBehaviour.Sync)]
    [InlineData(SyncBehaviour.Async)]
    public async Task ShouldBeCancellable(SyncBehaviour behaviour)
    {
        using var tempScript = TempScript.Create(
            """
            @echo off
            echo Enter Name:
            set /p name=
            echo Hello '%name%'
            """,
            """
            echo "Enter Name:"
            read name
            echo "Hello '$name'"
            """);

        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        // it's going to ask us for the name first, but we don't give it anything; the script should hang
        var stdIn = new TestInputSource();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken);

        var executor = new ShellCommand(tempScript.GetHostExecutable())
            .WithArguments(tempScript.GetCommandArgs())
            .WithStdInSource(stdIn)
            .WithStdOutTarget(stdOut)
            .WithStdOutTarget(l =>
            {
                // when we receive the first prompt, cancel and kill the process
                if (l.Contains("Enter Name:")) cts.Cancel();
            })
            .WithStdErrTarget(stdErr);

        var result = behaviour == SyncBehaviour.Async
            ? await executor.ExecuteAsync(cts.Token)
            : executor.Execute(cts.Token);

        // Our process was waiting on stdin and exits itself with code 0 when we close stdin,
        // but we cannot 100% guarantee it shuts down in time before we proceed to killing it; we could observe -1 too.
        // Whenever I've run this locally on windows or linux I always observe 0.
        result.ExitCode.Should().BeOneOf([0, -1], "The process should exit cleanly when stdin is closed, but we might kill depending on timing");
        stdErr.ToString().Should().BeEmpty("no messages should be written to stderr");
        stdOut.ToString()
            .Should()
            .BeOneOf([
                    "Enter Name:" + Environment.NewLine,
                    "Enter Name:" + Environment.NewLine + "Hello ''" + Environment.NewLine
                ],
                "When we cancel the process we close StdIn and it shuts down. The process observes the EOF as empty string and prints 'Hello ' but there is a benign race condition which means we may not observe this output. Test needs to handle both cases");
    }

    // If someone wants to have an interactive back-and-forth with a process, they 
    // can use a type like this to do it. We don't want to quite commit to putting it
    // in the public API though until we have a stronger use-case for it.
    class TestInputSource : IInputSource, IDisposable
    {
        // make the subscriber public so tests can verify it
        public IInputSourceObserver? Subscriber { get; private set; }

        public IDisposable Subscribe(IInputSourceObserver observer)
        {
            if (Subscriber != null) throw new InvalidOperationException("Only one subscriber is allowed");
            Subscriber = observer;
            return this;
        }

        public void OnNext(string line)
        {
            Subscriber?.OnNext(line);
        }

        public void OnCompleted()
        {
            Subscriber?.OnCompleted();
        }

        public void Dispose()
        {
            Subscriber = null;
        }
    }
}