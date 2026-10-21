using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NoSleep
{
    /// <summary>
    /// Windows Execution State ENUM with available and not deprecated flags.
    /// See <see href="https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate">SetThreadExecutionState</see>.
    /// See <see href="https://msdn.microsoft.com/en-us/library/aa373208.aspx?f=255&MSPPError=-2147217396">article</see> for details.
    /// </summary>
    [Flags]
    internal enum EXECUTION_STATE : uint
    {
        /// <summary> No flags. Should NEVER be used. Either use ES_CONTINUOUS with no other flags (if previously used) or nothing. </summary>
        None = 0x00000000,
        /// <summary> Forces the system to be in the working state by resetting the system idle timer. </summary>
        ES_SYSTEM_REQUIRED = 0x00000001,
        /// <summary> Forces the display to be on by resetting the display idle timer. </summary>
        ES_DISPLAY_REQUIRED = 0x00000002,
        /// <summary> Enables away mode. This value must be specified with ES_CONTINUOUS.
        /// <para/> Away mode should be used only by media-recording and media-distribution applications that must perform critical background processing on desktop computers while the computer appears to be sleeping. See Remarks.
        /// </summary>
        ES_AWAYMODE_REQUIRED = 0x00000040,
        /// <summary> Informs the system that the state being set should remain in effect until the next call that uses ES_CONTINUOUS and one of the other state flags is cleared. </summary>
        ES_CONTINUOUS = 0x80000000
    }

    /// <summary> Win32 API wrapper for SetThreadExecutionState. </summary>
    internal static class WinU
    {
        /// <summary>
        /// Import SetThreadExecutionState from Win32 API.
        /// See <see href="https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate">SetThreadExecutionState</see>.
        /// </summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static internal extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

        // --- Windows color mode detection (used only to pick the inactive tray icon color) ---
        private const string ThemeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        /// <summary> Cached read-only handle to the Personalize key; saves ~640 B/call of the naive open+read+dispose pattern. </summary>
        private static RegistryKey _themeKey;
        private static int _themeFailures;
        private static bool _themeFetchDisabled;

        /// <summary>
        /// Whether the taskbar follows a light Windows theme. Best-effort and cosmetic only:
        /// every failure path falls back to true (= today's black icon), exceptions are contained
        /// here (a throw out of a WinForms Timer.Tick would kill the app), and after 3 consecutive
        /// failures the fetcher disables itself forever. Measured cost on the cached handle:
        /// ~3-7 us and one boxed int (24 B) per call - polled at most once per 10 s.
        /// </summary>
        internal static bool IsTaskbarLightTheme()
        {
            if (_themeFetchDisabled)
                return true;
            try
            {
                if (_themeKey == null)
                {
                    _themeKey = Registry.CurrentUser.OpenSubKey(ThemeKeyPath);
                    if (_themeKey == null)
                    {
                        // No Personalize key (Windows older than 1809): a dark taskbar cannot exist - stop asking.
                        _themeFetchDisabled = true;
                        return true;
                    }
                }
                // The taskbar follows the "Windows" color mode (SystemUsesLightTheme) on Win10 1809+ and Win11; the apps value is a fallback.
                object value = _themeKey.GetValue("SystemUsesLightTheme") ?? _themeKey.GetValue("AppsUseLightTheme");
                _themeFailures = 0;
                return !(value is int light) || light != 0; // Missing or wrong-typed value: assume light (black icon).
            }
            catch
            {
                // Only realistic throw: ObjectDisposedException on a stale cached handle (a theoretical SecurityException is covered too).
                _themeKey = null; // Allow one reopen attempt on the next call.
                if (++_themeFailures >= 3)
                    _themeFetchDisabled = true; // Give up for good: keep the black icon, never throw again.
                return true;
            }
        }
    }

    /// <summary> A class with Process extensions to watch running apps via Win32 API </summary>
    public static class ProcessExtensions
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static unsafe extern bool QueryFullProcessImageName(
            IntPtr hProcess,
            int dwFlags,
            char* lpExeName,
            ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        public static unsafe string TryGetExecutablePath(this Process process)
        {
            IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, process.Id);
            if (hProcess == IntPtr.Zero)
                return null;

            try
            {
                // Max Windows path length is 32767 characters + ending \0 character
                const int maxPathChars = 32768;
                char* buffer = stackalloc char[maxPathChars];
                int size = maxPathChars; // Var for ref parameter

                if (QueryFullProcessImageName(hProcess, 0, buffer, ref size))
                {
                    // size now contains actual output string length - create a string from the buffer.
                    return new string(buffer, 0, size);
                }
                return null;
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }
    }
}
