using System;
using System.ComponentModel;

namespace Octopus.Shellfish.Windows;

static class Win32Helper
{
    public static bool Invoke(Func<bool> nativeMethod, string failureDescription)
    {
        try
        {
            return nativeMethod() ? true : throw new Win32Exception();
        }
        catch (Win32Exception ex)
        {
            throw new Exception($"{failureDescription}: {ex.Message}", ex);
        }
    }

    public static T Invoke<T>(Func<T> nativeMethod, Func<T, bool> successful, string failureDescription)
    {
        try
        {
            var result = nativeMethod();
            return successful(result) ? result : throw new Win32Exception();
        }
        catch (Win32Exception ex)
        {
            throw new Exception($"{failureDescription}: {ex.Message}", ex);
        }
    }

    public static IntPtr Invoke(Func<IntPtr> nativeMethod, string failureDescription)
    {
        try
        {
            var result = nativeMethod();
            return result != IntPtr.Zero ? result : throw new Win32Exception();
        }
        catch (Win32Exception ex)
        {
            throw new Exception($"{failureDescription}: {ex.Message}", ex);
        }
    }
}