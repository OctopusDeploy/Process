using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Octopus.Shellfish.Windows;
#if NET5_0_OR_GREATER
[SupportedOSPlatform("Windows")]
#endif
class UserProfile : IDisposable
{
    readonly AccessToken token;
    readonly SafeRegistryHandle userProfile;

    UserProfile(AccessToken token, SafeRegistryHandle userProfile)
    {
        this.token = token;
        this.userProfile = userProfile;
    }

    public static UserProfile Load(AccessToken token)
    {
        var userProfile = new PROFILEINFO
        {
            lpUserName = token.Username
        };
        userProfile.dwSize = Marshal.SizeOf(userProfile);

        // See https://msdn.microsoft.com/en-us/library/windows/desktop/bb762281(v=vs.85).aspx
        Win32Helper.Invoke(() => LoadUserProfile(token.Handle, ref userProfile),
            $"Failed to load user profile for user '{token.Username}'");

        return new UserProfile(token, new SafeRegistryHandle(userProfile.hProfile, false));
    }

    void Unload()
    {
        // See https://msdn.microsoft.com/en-us/library/windows/desktop/bb762282(v=vs.85).aspx
        // This function closes the registry handle for the user profile too
        Win32Helper.Invoke(() => UnloadUserProfile(token.Handle, userProfile),
            $"Failed to unload user profile for user '{token.Username}'");
    }

    public void Dispose()
    {
        if (userProfile != null && !userProfile.IsClosed)
        {
            Unload();
            userProfile.Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROFILEINFO
    {
        public int dwSize;
        public readonly int dwFlags;
        public string lpUserName;
        public readonly string lpProfilePath;
        public readonly string lpDefaultPath;
        public readonly string lpServerName;
        public readonly string lpPolicyPath;
        public readonly IntPtr hProfile;
    }

#pragma warning disable PC003 // Native API not available in UWP
    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool LoadUserProfile(SafeAccessTokenHandle hToken, ref PROFILEINFO lpProfileInfo);

    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool UnloadUserProfile(SafeAccessTokenHandle hToken, SafeRegistryHandle hProfile);
#pragma warning restore PC003 // Native API not available in UWP
}