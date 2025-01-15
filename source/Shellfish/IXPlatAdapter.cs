using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;

namespace Octopus.Shellfish;

interface IXPlatAdapter
{
    void ConfigureStartInfoForUser(ProcessStartInfo startInfo, NetworkCredential runAs, IReadOnlyDictionary<string, string>? customEnvironmentVariables);
    void TryKillProcessAndChildrenRecursively(Process process);
    Encoding GetOemEncoding();
}