using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Octopus.Shellfish.Windows;

static class WindowsEncodingHelper
{
    [DllImport("kernel32.dll", SetLastError = true)]
#pragma warning disable PC003 // Native API not available in UWP
    static extern bool GetCPInfoEx([MarshalAs(UnmanagedType.U4)] int codePage,
        [MarshalAs(UnmanagedType.U4)]
        int dwFlags,
        out CPINFOEX lpCPInfoEx);
#pragma warning restore PC003 // Native API not available in UWP

    public static Encoding GetOemEncoding()
    {
        try
        {
            // Get the OEM CodePage for the installation, otherwise fall back to code page 850 (DOS Western Europe)
            // https://en.wikipedia.org/wiki/Code_page_850
            const int CP_OEMCP = 1;
            const int dwFlags = 0;
            const int CodePage850 = 850;

            var codepage = GetCPInfoEx(CP_OEMCP, dwFlags, out var info) ? info.CodePage : CodePage850;

#if REQUIRES_CODE_PAGE_PROVIDER
                        var encoding = CodePagesEncodingProvider.Instance.GetEncoding(codepage);    // When it says that this can return null, it *really can* return null.
                        return encoding ?? defaultEncoding;
#else
            var encoding = Encoding.GetEncoding(codepage);
            return encoding ?? Encoding.UTF8;
#endif
        }
        catch
        {
            // Fall back to UTF8 if everything goes wrong
            return Encoding.UTF8;
        }
    }

    // ReSharper disable InconsistentNaming
    const int MAX_DEFAULTCHAR = 2;
    const int MAX_LEADBYTES = 12;
    const int MAX_PATH = 260;

    // ReSharper disable MemberCanBePrivate.Local
    // ReSharper disable once StructCanBeMadeReadOnly
    [StructLayout(LayoutKind.Sequential)]
    struct CPINFOEX
    {
        [MarshalAs(UnmanagedType.U4)]
        public readonly int MaxCharSize;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_DEFAULTCHAR)]
        public readonly byte[] DefaultChar;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LEADBYTES)]
        public readonly byte[] LeadBytes;

        public readonly char UnicodeDefaultChar;

        [MarshalAs(UnmanagedType.U4)]
        public readonly int CodePage;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
        public readonly string CodePageName;
    }
    // ReSharper restore MemberCanBePrivate.Local
    // ReSharper restore InconsistentNaming
}