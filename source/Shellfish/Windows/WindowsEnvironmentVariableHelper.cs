using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;

namespace Octopus.Shellfish.Windows;
#if NET5_0_OR_GREATER
[SupportedOSPlatform("Windows")]
#endif
static class WindowsEnvironmentVariableHelper
{
    static readonly object EnvironmentVariablesCacheLock = new();
    static IDictionary mostRecentMachineEnvironmentVariables = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Machine);
    static readonly Dictionary<string, Dictionary<string, string>> EnvironmentVariablesForUserCache = new();

    internal static void SetEnvironmentVariablesForTargetUser(ProcessStartInfo startInfo, NetworkCredential runAs, IReadOnlyDictionary<string, string> customEnvironmentVariables)
    {
        // Double check before we go doing p/invoke gymnastics
        if (customEnvironmentVariables == null || !customEnvironmentVariables.Any()) return;

        // If ProcessStartInfo.enviromentVariables (field) is null, the new process will build its environment variables from scratch
        // This will be the system environment variables, plus the user's profile variables (if the user profile is loaded)
        // However, if the ProcessStartInfo.environmentVariables (field) is not null, these environment variables will be used instead
        // As soon as we touch ProcessStartInfo.EnvironmentVariables (property) it lazy loads the environment variables for the current process
        // which in turn means the launched process will get the environment variables for the wrong user profile!

        // See https://msdn.microsoft.com/en-us/library/windows/desktop/ms682425(v=vs.85).aspx (CreateProcess) used when ProcessStartInfo.Username is not set
        // See https://msdn.microsoft.com/en-us/library/windows/desktop/ms682431(v=vs.85).aspx (CreateProcessWithLogonW) used when ProcessStartInfo.Username is set

        // Start by getting the environment variables for the target user (as if they started a process themselves)
        // This will get the system environment variables along with the user's profile variables
        var targetUserEnvironmentVariables = GetTargetUserEnvironmentVariables(runAs);

        // Now copy in the extra environment variables we want to propagate from this process
        foreach (var variable in customEnvironmentVariables)
            targetUserEnvironmentVariables[variable.Key] = variable.Value;

        // Starting from a clean slate, copy the resulting environment variables into the ProcessStartInfo
        startInfo.EnvironmentVariables.Clear();
        foreach (var variable in targetUserEnvironmentVariables)
            startInfo.EnvironmentVariables[variable.Key] = variable.Value;
    }

    static Dictionary<string, string> GetTargetUserEnvironmentVariables(NetworkCredential runAs)
    {
        var cacheKey = $"{runAs.Domain}\\{runAs.UserName}";

        // Start with a pessimistic lock until such a time where we want more throughput for concurrent processes
        // In the real world we shouldn't have too many processes wanting to run concurrently
        lock (EnvironmentVariablesCacheLock)
        {
            // If the machine environment variables have changed we should invalidate the entire cache
            InvalidateEnvironmentVariablesForUserCacheIfMachineEnvironmentVariablesHaveChanged();

            // Otherwise the cache will generally be valid, except for the (hopefully) rare case where a variable was added/changed for the specific user
            if (EnvironmentVariablesForUserCache.TryGetValue(cacheKey, out var cached))
                return cached;

            Dictionary<string, string> targetUserEnvironmentVariables;
            using (var token = AccessToken.Logon(runAs.UserName, runAs.Password, runAs.Domain))
            using (UserProfile.Load(token))
            {
                targetUserEnvironmentVariables = EnvironmentBlock.GetEnvironmentVariablesForUser(token, false);
            }

            // Cache the target user's environment variables so we don't have to load them every time
            // The downside is that once we target a certain user account, their variables are snapshotted in time
            EnvironmentVariablesForUserCache[cacheKey] = targetUserEnvironmentVariables;
            return targetUserEnvironmentVariables;
        }
    }

    static void InvalidateEnvironmentVariablesForUserCacheIfMachineEnvironmentVariablesHaveChanged()
    {
        var currentMachineEnvironmentVariables = Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Machine);
        var machineEnvironmentVariablesHaveChanged =
#pragma warning disable DE0006 // API is deprecated
            !currentMachineEnvironmentVariables.Cast<DictionaryEntry>()
                .OrderBy(e => e.Key)
                .SequenceEqual(mostRecentMachineEnvironmentVariables.Cast<DictionaryEntry>().OrderBy(e => e.Key));
#pragma warning restore DE0006 // API is deprecated

        if (machineEnvironmentVariablesHaveChanged)
        {
            mostRecentMachineEnvironmentVariables = currentMachineEnvironmentVariables;
            EnvironmentVariablesForUserCache.Clear();
        }
    }
}