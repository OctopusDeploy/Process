using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace Octopus.Shellfish.Windows;
#if NET5_0_OR_GREATER
[SupportedOSPlatform("Windows")]
#endif
class WindowsAdapter : IXPlatAdapter
{
    public void ConfigureStartInfoForUser(ProcessStartInfo startInfo, NetworkCredential runAs, IReadOnlyDictionary<string, string>? customEnvironmentVariables)
    {
#pragma warning disable PC001 // API not supported on all platforms
#pragma warning disable CA1416 // This call site is reachable on all platforms.
        startInfo.UserName = runAs.UserName;
        startInfo.Domain = runAs.Domain;
        startInfo.Password = runAs.SecurePassword;
        startInfo.LoadUserProfile = true;
#pragma warning restore CA1416
#pragma warning restore PC001 // API not supported on all platforms

        WindowStationAndDesktopAccess.GrantAccessToWindowStationAndDesktop(runAs.UserName, runAs.Domain);

        if (customEnvironmentVariables != null && customEnvironmentVariables.Any())
            WindowsEnvironmentVariableHelper.SetEnvironmentVariablesForTargetUser(startInfo, runAs, customEnvironmentVariables);
    }

    public void TryKillProcessAndChildrenRecursively(Process process)
        => TryKillProcessAndChildrenRecursively(process.Id);

    void TryKillProcessAndChildrenRecursively(int pid)
    {
        try
        {
            using (var searcher = new ManagementObjectSearcher("Select * From Win32_Process Where ParentProcessID=" + pid))
            {
                using (var moc = searcher.Get())
                {
                    foreach (var mo in moc.OfType<ManagementObject>())
                        TryKillProcessAndChildrenRecursively(Convert.ToInt32(mo["ProcessID"]));
                }
            }
        }
        catch (MarshalDirectiveException)
        {
            // This is a known framework bug: https://github.com/dotnet/runtime/issues/28840
            //
            // The ManagementObjectSearcher netcore3.1 is completely broken. It's possible to crash it just by creating
            // a new instance of ManagementScope. Once this framework bug is addressed, we should be able to remove
            // this catch block.
            //
            // Unfortunately, this means that we have no feasible way to recursively kill processes under netcore, so
            // we're left with just killing the top-level process and hoping that the others terminate soon.
        }

        try
        {
            var proc = Process.GetProcessById(pid);
            proc.Kill();
        }
        catch (ArgumentException)
        {
            // Shellfish already exited.
        }
    }

    public Encoding GetOemEncoding()
        => WindowsEncodingHelper.GetOemEncoding();
}