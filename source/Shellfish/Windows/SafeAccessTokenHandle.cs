using System;
using System.Runtime.InteropServices;

namespace Octopus.Shellfish.Windows;

sealed class SafeAccessTokenHandle : SafeHandle
{
    // 0 is an Invalid Handle
    public SafeAccessTokenHandle(IntPtr handle) : base(handle, true)
    {
    }

    public static SafeAccessTokenHandle InvalidHandle => new(IntPtr.Zero);

    public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

    protected override bool ReleaseHandle()
        => CloseHandle(handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hHandle);
}