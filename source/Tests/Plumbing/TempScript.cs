using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Tests.Plumbing;

public static class TempScript
{
    // Some interactions such as stdout or encoding codepages require things that don't work with an inline cmd /c or bash -c command
    // This helper writes a script file into the temp directory so we can exercise more complex scenarios
    public static Handle Create(string cmd, string sh)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cmd");
            File.WriteAllText(tempFile, cmd);
            return new Handle(tempFile);
        }
        else
        {
            var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sh");
            File.WriteAllText(tempFile, sh.Replace("\r\n", "\n"));
            return new Handle(tempFile);
        }
    }

    public class Handle(string scriptPath) : IDisposable
    {
        public string ScriptPath { get; } = scriptPath;

        public void Dispose()
        {
            try
            {
                File.Delete(ScriptPath);
            }
            catch
            {
                // nothing to do if we can't delete the temp file
            }
        }

        // Returns the host application which will run the script. Either cmd.exe or bash
        public string GetHostExecutable()
            => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "bash";

        // Returns the command line args to get the host application to run the script
        // For cmd.exe, returns ["/c", ScriptPath] as it needs /c
        // For bash, returns [ScriptPath] as it doesn't need any preamble
        public string[] GetCommandArgs()
            // when running cmd.exe we need /c to tell it to run the script; bash doesn't want any preamble for a script file
            => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? ["/c", ScriptPath]
                : [ScriptPath];
    }
}