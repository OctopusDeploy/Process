using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using Octopus.Shellfish.Nix;
using Octopus.Shellfish.Plumbing;
using Octopus.Shellfish.Windows;

namespace Octopus.Shellfish;

public static class ShellExecutor
{
    static readonly IXPlatAdapter XPlatAdapter = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? new WindowsAdapter()
        : new NixAdapter();

    public static int ExecuteCommand(
        string executable,
        string arguments,
        string workingDirectory,
        Action<string> debug,
        Action<string> info,
        Action<string> error,
        NetworkCredential? runAs = null,
        IDictionary<string, string>? customEnvironmentVariables = null,
        CancellationToken cancel = default)
    {
        if (executable == null)
            throw new ArgumentNullException(nameof(executable));
        if (arguments == null)
            throw new ArgumentNullException(nameof(arguments));
        if (workingDirectory == null)
            throw new ArgumentNullException(nameof(workingDirectory));
        if (debug == null)
            throw new ArgumentNullException(nameof(debug));
        if (info == null)
            throw new ArgumentNullException(nameof(info));
        if (error == null)
            throw new ArgumentNullException(nameof(error));

        customEnvironmentVariables = customEnvironmentVariables == null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(customEnvironmentVariables);

        void WriteData(Action<string> action, ManualResetEventSlim resetEvent, DataReceivedEventArgs e)
        {
            try
            {
                if (e.Data == null)
                {
                    resetEvent.Set();
                    return;
                }

                action(e.Data);
            }
            catch (Exception ex)
            {
                try
                {
                    error($"Error occurred handling message: {ex.PrettyPrint()}");
                }
                catch
                {
                    // Ignore
                }
            }
        }

        try
        {
            // We need to be careful to make sure the message is accurate otherwise people could wrongly assume the exe is in the working directory when it could be somewhere completely different!
            var executableDirectoryName = Path.GetDirectoryName(executable);
            debug($"Executable directory is {executableDirectoryName}");

            var exeInSamePathAsWorkingDirectory = string.Equals(executableDirectoryName?.TrimEnd('\\', '/'), workingDirectory.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
            var exeFileNameOrFullPath = exeInSamePathAsWorkingDirectory ? Path.GetFileName(executable) : executable;
            debug($"Executable name or full path: {exeFileNameOrFullPath}");

            var encoding = XPlatAdapter.GetOemEncoding();
            var hasCustomEnvironmentVariables = customEnvironmentVariables.Any();

            bool runAsSameUser;
            string runningAs;
            if (runAs == null)
            {
                debug("No user context provided. Running as current user.");
                runAsSameUser = true;
                runningAs = $@"{ProcessIdentity.CurrentUserName}";
            }
            else
            {
                runAsSameUser = false;
                runningAs = $@"{runAs.Domain ?? Environment.MachineName}\{runAs.UserName}";
                debug($"Different user context provided. Running as {runningAs}");
            }

            var customEnvironmentVarsMessage = hasCustomEnvironmentVariables
                ? runAsSameUser
                    ? $"the same environment variables as the launching process plus {customEnvironmentVariables.Count} custom variable(s)"
                    : $"that user's environment variables plus {customEnvironmentVariables.Count} custom variable(s)"
                : runAsSameUser
                    ? "the same environment variables as the launching process"
                    : "that user's default environment variables";

            debug($"Starting {exeFileNameOrFullPath} in working directory '{workingDirectory}' using '{encoding.EncodingName}' encoding running as '{runningAs}' with {customEnvironmentVarsMessage}");

            using (var outputResetEvent = new ManualResetEventSlim(false))
            using (var errorResetEvent = new ManualResetEventSlim(false))
            using (var process = new Process())
            {
                process.StartInfo.FileName = executable;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.WorkingDirectory = workingDirectory;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;

                if (runAs == null)
                    RunAsSameUser(process.StartInfo, customEnvironmentVariables);
                else
                    XPlatAdapter.ConfigureStartInfoForUser(process.StartInfo, runAs, new Dictionary<string, string>(customEnvironmentVariables));

                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    process.StartInfo.StandardOutputEncoding = encoding;
                    process.StartInfo.StandardErrorEncoding = encoding;
                }

                process.OutputDataReceived += (sender, e) =>
                {
                    WriteData(info, outputResetEvent, e);
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    WriteData(error, errorResetEvent, e);
                };

                process.Start();

                var running = true;

                using (cancel.Register(() =>
                       {
                           if (running) DoOurBestToCleanUp(process, error);
                       }))
                {
                    if (cancel.IsCancellationRequested)
                        DoOurBestToCleanUp(process, error);

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    process.WaitForExit();

                    SafelyCancelRead(process.CancelErrorRead, debug);
                    SafelyCancelRead(process.CancelOutputRead, debug);

                    SafelyWaitForAllOutput(outputResetEvent, cancel, debug);
                    SafelyWaitForAllOutput(errorResetEvent, cancel, debug);

                    var exitCode = SafelyGetExitCode(process);
                    debug($"Shellfish {exeFileNameOrFullPath} in {workingDirectory} exited with code {exitCode}");

                    running = false;
                    return exitCode;
                }
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Error when attempting to execute {executable}: {ex.Message}", ex);
        }
    }

    static int SafelyGetExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException ex)
            when (ex.Message is "No process is associated with this object." || ex.Message is "Process was not started by this object, so requested information cannot be determined.")
        {
            return -1;
        }
    }

    static void SafelyWaitForAllOutput(ManualResetEventSlim outputResetEvent,
        CancellationToken cancel,
        Action<string> debug)
    {
        try
        {
            //5 seconds is a bit arbitrary, but the process should have already exited by now, so unwise to wait too long
            outputResetEvent.Wait(TimeSpan.FromSeconds(5), cancel);
        }
        catch (OperationCanceledException ex)
        {
            debug($"Swallowing {ex.GetType().Name} while waiting for last of the process output.");
        }
    }

    static void SafelyCancelRead(Action action, Action<string> debug)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException ex)
        {
            debug($"Swallowing {ex.GetType().Name} calling {action.Method.Name}.");
        }
    }

    public static void ExecuteCommandWithoutWaiting(
        string executable,
        string arguments,
        string workingDirectory,
        NetworkCredential? runAs = null,
        IDictionary<string, string>? customEnvironmentVariables = null)
    {
        customEnvironmentVariables = customEnvironmentVariables == null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(customEnvironmentVariables);

        try
        {
            using (var process = new Process())
            {
                process.StartInfo.FileName = executable;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.WorkingDirectory = workingDirectory;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;

                if (runAs == null)
                    RunAsSameUser(process.StartInfo, customEnvironmentVariables);
                else
                    XPlatAdapter.ConfigureStartInfoForUser(process.StartInfo, runAs, new Dictionary<string, string>(customEnvironmentVariables));

                process.Start();
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Error when attempting to execute {executable}: {ex.Message}", ex);
        }
    }

    static void RunAsSameUser(ProcessStartInfo processStartInfo, IDictionary<string, string>? customEnvironmentVariables)
    {
        // Accessing the ProcessStartInfo.EnvironmentVariables dictionary will pre-load the environment variables for the current process
        // Then we'll add/overwrite with the customEnvironmentVariables
        if (customEnvironmentVariables != null && customEnvironmentVariables.Any())
            foreach (var variable in customEnvironmentVariables)
                processStartInfo.EnvironmentVariables[variable.Key] = variable.Value;
    }

    static void DoOurBestToCleanUp(Process process, Action<string> error)
    {
        try
        {
            XPlatAdapter.TryKillProcessAndChildrenRecursively(process);
        }
        catch (Exception hitmanException)
        {
            error($"Failed to kill the launched process and its children: {hitmanException}");
            try
            {
                process.Kill();
            }
            catch (Exception killProcessException)
            {
                error($"Failed to kill the launched process: {killProcessException}");
            }
        }
    }
}