using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Octopus.Shellfish.Windows;

static class EnvironmentBlock
{
    internal static Dictionary<string, string> GetEnvironmentVariablesForUser(AccessToken token, bool inheritFromCurrentProcess)
    {
        var env = IntPtr.Zero;

        // See https://msdn.microsoft.com/en-us/library/windows/desktop/bb762270(v=vs.85).aspx
        Win32Helper.Invoke(() => CreateEnvironmentBlock(out env, token.Handle, inheritFromCurrentProcess),
            $"Failed to load the environment variables for the user '{token.Username}'");

        var userEnvironment = new Dictionary<string, string>();
        try
        {
            var testData = new StringBuilder();
            unsafe
            {
                // The environment block is an array of null-terminated Unicode strings.
                // Key and Value are separated by =
                // The list ends with two nulls (\0\0).
                var start = (short*)env.ToPointer();
                var done = false;
                var current = start;
                while (!done)
                {
                    if (testData.Length > 0 && *current == 0 && current != start)
                    {
                        var data = testData.ToString();
                        var index = data.IndexOf('=');
                        if (index == -1)
                            userEnvironment.Add(data, "");
                        else if (index == data.Length - 1)
                            userEnvironment.Add(data.Substring(0, index), "");
                        else
                            userEnvironment.Add(data.Substring(0, index), data.Substring(index + 1));
                        testData.Length = 0;
                    }

                    if (*current == 0 && current != start && *(current - 1) == 0)
                        done = true;
                    if (*current != 0)
                        testData.Append((char)*current);
                    current++;
                }
            }
        }
        finally
        {
            // See https://msdn.microsoft.com/en-us/library/windows/desktop/bb762274(v=vs.85).aspx
            Win32Helper.Invoke(() => DestroyEnvironmentBlock(env),
                $"Failed to destroy the environment variables structure for user '{token.Username}'");
        }

        return userEnvironment;
    }

#pragma warning disable PC003 // Native API not available in UWP
    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, SafeAccessTokenHandle hToken, bool inheritFromCurrentProcess);

    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);
#pragma warning restore PC003 // Native API not available in UWP
}