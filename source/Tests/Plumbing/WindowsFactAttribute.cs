using System;
using System.Runtime.InteropServices;
using Xunit;

namespace Tests.Plumbing;

[AttributeUsage(AttributeTargets.Method)]
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) Skip = "This test only runs on Windows";
    }
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) Skip = "This test only runs on Windows";
    }
}