using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Octopus.Shellfish;

using static ShellCommandExecutorHelpers;

/// <summary>
/// A fluent-builder style utility designed to easily let you execute shell commands.
/// </summary>
public class ShellCommand
{
    readonly string executable;
    List<string>? argumentList;
    string? argumentString;
    string? workingDirectory;
    IReadOnlyDictionary<string, string>? environmentVariables;
    NetworkCredential? windowsCredential;
    Encoding? outputEncoding;

    List<IOutputTarget>? stdOutTargets;
    List<IOutputTarget>? stdErrTargets;
    IInputSource? stdInSource;

    public ShellCommand(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable)) throw new ArgumentException("Executable must be a valid non-whitespace string.", nameof(executable));
        this.executable = executable;
    }

    /// <summary>
    /// Allows you to specify the working directory for the process.
    /// If you don't specify one, the process' Current Directory is used.
    /// </summary>
    /// <remarks>
    /// Internally ShellCommand sets UseShellExecute=false, so if unset "the current directory is understood to contain the executable."
    /// https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.workingdirectory?view=net-8.0
    /// </remarks>
    public ShellCommand WithWorkingDirectory(string workingDir)
    {
        workingDirectory = workingDir;
        return this;
    }

    /// <summary>
    /// Allows you to supply a list of individual string arguments.
    /// Arguments will be quoted and escaped if necessary, so you can freely pass arguments with spaces or special characters.
    /// </summary>
    public ShellCommand WithArguments(IEnumerable<string> argList)
    {
        argumentList ??= new List<string>();
        argumentList.Clear();
        argumentList.AddRange(argList);
        return this;
    }

    /// <summary>
    /// Allows you to supply a single argument string which will be used directly.
    /// No quoting or escaping of any spaces or special characters will be performed.
    /// </summary>
    public ShellCommand WithArguments(string argString)
    {
        argumentString = argString;
        return this;
    }

    /// <summary>
    /// Allows you to set additional environment variables for the launched process.
    /// </summary>
    /// <remarks>
    /// The C# IDictionary type does not implement IReadOnlyDictionary. If you have an IDictionary, you will
    /// need to copy it into something IReadOnlyDictionary compatible, such as new Dictionary&lt;string, string&gt;(yourDictionary).
    /// </remarks>
    public ShellCommand WithEnvironmentVariables(IReadOnlyDictionary<string, string> dictionary)
    {
        environmentVariables = dictionary;
        return this;
    }

    /// <summary>
    /// Runs the command as the given user.
    /// Currently only supported on Windows
    /// </summary>
    /// <param name="credential">The credential representing the user account.</param>
#if NET5_0_OR_GREATER
    [SupportedOSPlatform("Windows")]
#endif
    public ShellCommand WithCredentials(NetworkCredential credential)
    {
        windowsCredential = credential;
        return this;
    }

    /// <summary>
    /// This controls the value of process.StartInfo.StandardOutputEncoding and process.StartInfo.StandardErrorEncoding.
    /// It can be used if you are running a process that outputs text in a nonstandard encoding that System.Diagnostics.Process
    /// can't handle automatically
    /// </summary>
    /// <remarks>
    /// The old ShellExecutor API would always set the Output Encoding on windows to the "OEM" encoding using
    /// XPlatAdapter.GetOemEncoding() to determine what that was.
    ///
    /// This traces back to an old bug (in powershell, possibly?), which I could not reproduce on a modern Windows 11 system.
    /// As it does not appear to be required anymore, we no longer set the OEM encoding by default, but this lets you set it manually, should you have some other reason.
    ///   https://github.com/OctopusDeploy/Issues/issues/748
    ///   https://github.com/OctopusDeploy/OctopusDeploy/commit/a223657f4d5d64bde8a638322d3f6f2f7b188169
    /// </remarks>
    public ShellCommand WithOutputEncoding(Encoding encoding)
    {
        outputEncoding = encoding;
        return this;
    }

    /// <summary>
    /// Adds an output target for the standard output stream of the process.
    /// Typically, an extension method like WithStdOutTarget(StringBuilder) or WithStdOutTarget(Action&lt;string&gt;) would be used over this.
    /// </summary>
    public ShellCommand WithStdOutTarget(IOutputTarget target)
    {
        stdOutTargets ??= new List<IOutputTarget>();
        stdOutTargets.Add(target);
        return this;
    }

    // Experimental: We are not 100% sure this is the right way to implement stdin
    internal ShellCommand WithStdInSource(IInputSource source)
    {
        stdInSource = source;
        return this;
    }

    /// <summary>
    /// Adds an output target for the standard error stream of the process.
    /// Typically, an extension method like WithStdErrTarget(StringBuilder) or WithStdErrTarget(Action&lt;string&gt;) would be used over this.
    /// </summary>
    public ShellCommand WithStdErrTarget(IOutputTarget target)
    {
        stdErrTargets ??= new List<IOutputTarget>();
        stdErrTargets.Add(target);
        return this;
    }

    /// <summary>
    /// Launches the process and synchronously waits for it to exit.
    /// </summary>
    public ShellCommandResult Execute(CancellationToken cancellationToken = default)
    {
        using var process = new Process();
        ConfigureProcess(process, out var shouldBeginOutputRead, out var shouldBeginErrorRead, out var shouldBeginInput);

        var exitedEvent = AttachProcessExitedManualResetEvent(process, cancellationToken);
        process.Start();

        var closeStdInDisposable = BeginIoStreams(process, shouldBeginOutputRead, shouldBeginErrorRead, shouldBeginInput);

        try
        {
            exitedEvent?.Wait(cancellationToken);

            // Either: AttachProcessExitedManualResetEvent determined there was no cancellation to consider and exitedEvent is null.
            // We can offload all the work to process.WaitForExit();
            //
            // Or: ExitEvent was signalled, but we still want to call process.WaitForExit; it waits for the StdErr and StdOut streams to flush.
            // in a way that can we cannot easily do ourselves.  https://github.com/microsoft/referencesource/blob/51cf7850defa8a17d815b4700b67116e3fa283c2/System/services/monitoring/system/diagnosticts/Process.cs#L2453
            // It should return very quickly.
            if (!cancellationToken.IsCancellationRequested) process.WaitForExit(); // note we CANNOT pass a timeout here as otherwise the WaitForExit implementation will not wait for the streams to flush
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation is not necessarily an error, and some processes will gracefully exit themselves when stdin is closed.
            // We can observe a valid 0 exit code if that happens.
            // If the process does not close itself, we'll proceed to killing it.
            closeStdInDisposable?.Dispose();
            closeStdInDisposable = null;

            if (shouldBeginOutputRead) process.CancelOutputRead();
            if (shouldBeginErrorRead) process.CancelErrorRead();

            // The legacy ShellExecutor would unconditionally kill the process upon cancellation.
            // We keep that default as it is the safest option for compatibility
            TryKillProcessAndChildrenRecursively(process);

            // Do not rethrow; The legacy ShellExecutor didn't throw an OperationCanceledException if CancellationToken was signaled.
            // This is a bit nonstandard for .NET, but we keep it as the default for compatibility.
        }
        finally
        {
            closeStdInDisposable?.Dispose();
        }

        return new ShellCommandResult(SafelyGetExitCode(process));
    }

    /// <summary>
    /// Launches the process and asynchronously waits for it to exit.
    /// </summary>
    public async Task<ShellCommandResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        using var process = new Process();
        ConfigureProcess(process, out var shouldBeginOutputRead, out var shouldBeginErrorRead, out var shouldBeginInput);

        var exitedTask = AttachProcessExitedTask(process, cancellationToken);
        process.Start();

        var closeStdInDisposable = BeginIoStreams(process, shouldBeginOutputRead, shouldBeginErrorRead, shouldBeginInput);

        try
        {
            // tests deadlock on linux if we ConfigureAwait(false) here
            await exitedTask.ConfigureAwait(true);

            if (!cancellationToken.IsCancellationRequested) await FinalWaitForExitAsync(process, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation is not necessarily an error, and some processes will gracefully exit themselves when stdin is closed.
            // We can observe a valid 0 exit code if that happens.
            // If the process does not close itself, we'll proceed to killing it.
            closeStdInDisposable?.Dispose();
            closeStdInDisposable = null;

            if (shouldBeginOutputRead) process.CancelOutputRead();
            if (shouldBeginErrorRead) process.CancelErrorRead();

            // The legacy ShellExecutor would unconditionally kill the process upon cancellation.
            // We keep that default as it is the safest option for compatibility
            TryKillProcessAndChildrenRecursively(process);

            // Do not rethrow; The legacy ShellExecutor didn't throw an OperationCanceledException if CancellationToken was signaled.
            // This is a bit nonstandard for .NET, but we keep it as the default for compatibility.
        }
        finally
        {
            closeStdInDisposable?.Dispose();
        }

        return new ShellCommandResult(SafelyGetExitCode(process));
    }

    // sets standard flags on the Process that apply for both Execute and ExecuteAsync
    void ConfigureProcess(Process process, out bool shouldBeginOutputRead, out bool shouldBeginErrorRead, out bool shouldBeginInput)
    {
        process.StartInfo.FileName = executable;

        if (argumentString is not null && argumentList is { Count: > 0 }) throw new InvalidOperationException("Cannot specify both raw arguments and arguments");

        if (argumentString is not null)
        {
            process.StartInfo.Arguments = argumentString;
        }
        else if (argumentList is { Count: > 0 })
        {
#if NET5_0_OR_GREATER
            // Prefer ArgumentList if we're on net5.0 or greater. Our polyfill should have the same behaviour, but
            // If we stick with the CLR we will pick up optimizations and bugfixes going forward
            foreach (var arg in argumentList) process.StartInfo.ArgumentList.Add(arg);
#else
            process.StartInfo.Arguments = PasteArguments.JoinArguments(argumentList);
#endif
        }

        if (workingDirectory is not null) process.StartInfo.WorkingDirectory = workingDirectory;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;

        if (outputEncoding is not null)
        {
            process.StartInfo.StandardOutputEncoding = outputEncoding;
            process.StartInfo.StandardErrorEncoding = outputEncoding;
        }

        if (windowsCredential is not null)
        {
            XPlatAdapter.ConfigureStartInfoForUser(process.StartInfo, windowsCredential, environmentVariables);
        }
        else // exec as the current user
        {
            // Accessing the ProcessStartInfo.EnvironmentVariables dictionary will preload the environment variables for the current process
            // Then we'll add/overwrite with the customEnvironmentVariables
            if (environmentVariables is { Count: > 0 })
                foreach (var kvp in environmentVariables)
                    process.StartInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
        }

        shouldBeginOutputRead = shouldBeginErrorRead = false;
        if (stdOutTargets is { Count: > 0 })
        {
            process.StartInfo.RedirectStandardOutput = true;
            shouldBeginOutputRead = true;

            var targets = stdOutTargets.ToArray();
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return; // don't pass nulls along to the targets, it's an edge case that happens when the process exits

                foreach (var target in targets) target.WriteLine(e.Data);
            };
        }

        if (stdErrTargets is { Count: > 0 })
        {
            process.StartInfo.RedirectStandardError = true;
            shouldBeginErrorRead = true;

            var targets = stdErrTargets.ToArray();
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return; // don't pass nulls along to the targets, it's an edge case that happens when the process exits

                foreach (var target in targets) target.WriteLine(e.Data);
            };
        }

        shouldBeginInput = false;
        if (stdInSource is not null)
        {
            process.StartInfo.RedirectStandardInput = true;
            shouldBeginInput = true;
        }
    }

    // Common code for Execute and ExecuteAsync to handle stdin and stdout streaming
    IDisposable? BeginIoStreams(Process process,
        bool shouldBeginOutputRead,
        bool shouldBeginErrorRead,
        bool shouldBeginInput)
    {
        if (shouldBeginOutputRead) process.BeginOutputReadLine();
        if (shouldBeginErrorRead) process.BeginErrorReadLine();

        if (shouldBeginInput && stdInSource != null)
        {
            var inputQueue = new InputQueue(process.StandardInput);
            var unsubscribe = stdInSource.Subscribe(inputQueue);
            return new StopInputStreamDisposable(inputQueue, unsubscribe);
        }

        return null;
    }

    static async Task FinalWaitForExitAsync(Process process, CancellationToken cancellationToken)
    {
#if NET5_0_OR_GREATER // WaitForExitAsync was added in net5. It handles the buffer flushing scenario so we can simply call it.
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
#else
        // Compatibility shim for netstandard2.0
        // Similarly to the sync version, We want to wait for the StdErr and StdOut streams to flush but cannot easily
        // do this ourselves. https://github.com/dotnet/runtime/blob/e03b9a4692a15eb3ffbb637439241e8f8e5ca95f/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.cs#L1565
        await Task.CompletedTask;
        process.WaitForExit(); // note we CANNOT pass a timeout here as otherwise the WaitForExit implementation will not wait for the streams to flush
#endif
    }

    sealed class StopInputStreamDisposable(InputQueue inputQueue, IDisposable additionalDisposable) : IDisposable
    {
        public void Dispose()
        {
            // tell the input queue to shut itself down
            inputQueue.OnCompleted();
            // do whatever other work might be attached
            additionalDisposable.Dispose();
        }
    }
}