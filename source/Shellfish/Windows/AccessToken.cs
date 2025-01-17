using System;
using System.Runtime.InteropServices;

namespace Octopus.Shellfish.Windows;

class AccessToken : IDisposable
{
    public enum LogonProvider
    {
        Default = 0,
        WinNT40 = 2,
        WinNT50 = 3
    }

    public enum LogonType
    {
        Interactive = 2,
        Network = 3,
        Batch = 4,
        Service = 5,
        Unlock = 7,
        NetworkClearText = 8,
        NewCredentials = 9
    }

    AccessToken(string username, SafeAccessTokenHandle handle)
    {
        Username = username;
        Handle = handle;
    }

    public string Username { get; }
    public SafeAccessTokenHandle Handle { get; }

    public static AccessToken Logon(string username,
        string password,
        string domain = ".",
        LogonType logonType = LogonType.Network,
        LogonProvider logonProvider = LogonProvider.Default)
    {
        // See https://msdn.microsoft.com/en-us/library/windows/desktop/aa378184(v=vs.85).aspx
        var hToken = IntPtr.Zero;
        Win32Helper.Invoke(() => LogonUser(username,
                domain,
                password,
                LogonType.Network,
                LogonProvider.Default,
                out hToken),
            $"Logon failed for the user '{username}'");

        return new AccessToken(username, new SafeAccessTokenHandle(hToken));
    }

    public void Dispose()
    {
        Handle?.Dispose();
    }

    [DllImport("advapi32.dll", SetLastError = true)]
#pragma warning disable PC003 // Native API not available in UWP
    static extern bool LogonUser(string username,
        string domain,
        string password,
        LogonType logonType,
        LogonProvider logonProvider,
        out IntPtr hToken);
#pragma warning restore PC003 // Native API not available in UWP
}