using System;
using System.Runtime.InteropServices;

namespace NoSleep
{
    [FlagsAttribute]
    internal enum EXECUTION_STATE : uint
    {
        ES_AWAYMODE_REQUIRED = 0x00000040,
        ES_CONTINUOUS = 0x80000000,
        ES_DISPLAY_REQUIRED = 0x00000002,
        ES_SYSTEM_REQUIRED = 0x00000001
    }

    internal static class WinU
    {
        // Import SetThreadExecutionState Win32 API and necessary flags
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static internal extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);
    }
}
