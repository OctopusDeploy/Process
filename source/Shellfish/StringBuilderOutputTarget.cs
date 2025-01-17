using System;
using System.Text;

namespace Octopus.Shellfish;

class StringBuilderOutputTarget(StringBuilder stringBuilder) : IOutputTarget
{
    readonly StringBuilder stringBuilder = stringBuilder;

    public void WriteLine(string line) => stringBuilder.AppendLine(line);
}

public static partial class ShellCommandExtensionMethods
{
    public static ShellCommand WithStdOutTarget(this ShellCommand shellCommand, StringBuilder stringBuilder)
        => shellCommand.WithStdOutTarget(new StringBuilderOutputTarget(stringBuilder));

    public static ShellCommand WithStdErrTarget(this ShellCommand shellCommand, StringBuilder stringBuilder)
        => shellCommand.WithStdErrTarget(new StringBuilderOutputTarget(stringBuilder));
}