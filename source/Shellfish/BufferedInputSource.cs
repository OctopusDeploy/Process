using System;

namespace Octopus.Shellfish;

// Note: It may be tempting to write a BufferedInputSource which buffers multiple lines,
// however if we write multiple lines the process on the other end may only be expecting one.
// Rather than buffering on the other end, it could drop the data and lead to a lockup if
// it later asks for a second input line. This is really only useful for one-shot inputs, not interactive things
class BufferedInputSource(string inputLine) : IInputSource, IDisposable
{
    public IDisposable Subscribe(IInputSourceObserver observer)
    {
        observer.OnNext(inputLine);
        observer.OnCompleted();
        return this;
    }

    public void Dispose()
    {
        // nothing to do here
    }
}

public static partial class ShellCommandExtensionMethods
{
    public static ShellCommand WithStdInSource(this ShellCommand shellCommand, string inputLine)
        => shellCommand.WithStdInSource(new BufferedInputSource(inputLine));
}