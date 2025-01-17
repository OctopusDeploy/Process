using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Octopus.Shellfish.Plumbing;

static class ProcessIdentity
{
    public static string CurrentUserName => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? WindowsIdentity.GetCurrent().Name
        : Environment.UserName;
}