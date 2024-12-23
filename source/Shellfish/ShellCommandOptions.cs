using System;

namespace Octopus.Shellfish;

public enum ShellCommandOptions
{
    /// <summary>
    /// Default value, equivalent to not specifying any options.
    /// </summary>
    None = 0,
    
    /// <summary>
    /// By default, if the CancellationToken is cancelled, the running process will be killed, and an OperationCanceledException
    /// will be thrown, like the vast majority of other .NET code.
    /// However, the legacy ShellExecutor API would swallow OperationCanceledException exceptions on cancellation, so this
    /// option exists to preserve that behaviour where necessary.
    /// </summary>
    DoNotThrowOnCancellation,
}