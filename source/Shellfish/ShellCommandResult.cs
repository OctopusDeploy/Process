using System;

namespace Octopus.Shellfish;

/// <summary>
/// Holds the result of a shell command execution.
/// </summary>
public class ShellCommandResult(int exitCode)
{
    /// <summary>
    /// The shell command exit code
    /// </summary>
    public int ExitCode { get; } = exitCode;
}