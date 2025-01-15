using System;
using System.Linq;
using System.Reflection;
using Assent;
using Octopus.Shellfish;
using Xunit;

namespace Tests;

public class PublicSurfaceArea
{
    [Fact]
    public void TheLibraryOnlyExposesWhatWeWantItToExpose()
    {
        var assembly = typeof(ShellExecutor).Assembly;
        var publicMembers =
            from t in assembly.GetExportedTypes()
            from m in t.GetMembers(BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Static | BindingFlags.Instance)
            where !(m is MethodBase method && method.IsSpecialName)
            select $"{m.DeclaringType?.FullName}.{m.Name}";

        this.Assent(string.Join("\r\n", publicMembers));
    }
}