using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;

namespace Octopus.Shellfish.Nix;

class NixAdapter : IXPlatAdapter
{
    public void ConfigureStartInfoForUser(ProcessStartInfo startInfo, NetworkCredential runAs, IReadOnlyDictionary<string, string>? customEnvironmentVariables)
        => throw new PlatformNotSupportedException("NetCore on Linux or Mac does not support running a process as a different user.");

    public void TryKillProcessAndChildrenRecursively(Process process)
    {
        var messages = new List<string>();
        var result = ShellExecutor.ExecuteCommand(
            "/bin/bash",
            $"-c \"kill -TERM {process.Id}\"",
            Environment.CurrentDirectory,
            m => { },
            m => { },
            m => messages.Add(m)
        );

        if (result != 0)
            throw new ShellExecutionException(result, messages);

        //process.Kill() doesnt seem to work in netcore 2.2 there have been some improvments in netcore 3.0 as well as also allowing to kill child processes
        //https://github.com/dotnet/corefx/pull/34147
        //In netcore 2.2 if the process is terminated we still get stuck on process.WaitForExit(); we need to manually check to see if the process has exited and then close it.
        if (process.HasExited)
            process.Close();
    }

    public Encoding GetOemEncoding()
        => Encoding.UTF8;
}