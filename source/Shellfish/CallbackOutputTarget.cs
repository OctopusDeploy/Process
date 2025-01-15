using System;

namespace Octopus.Shellfish;

class CallbackOutputTarget(Action<string> callback) : IOutputTarget
{
    readonly Action<string> callback = callback;

    public void WriteLine(string line) => callback(line);
}

public static partial class ShellCommandExtensionMethods
{
    public static ShellCommand WithStdOutTarget(this ShellCommand shellCommand, Action<string> callback)
        => shellCommand.WithStdOutTarget(new CallbackOutputTarget(callback));

    public static ShellCommand WithStdErrTarget(this ShellCommand shellCommand, Action<string> callback)
        => shellCommand.WithStdErrTarget(new CallbackOutputTarget(callback));
}