using System;
using System.Runtime.InteropServices;

namespace NoSleep
{
    /*
     * Value 	Meaning

ES_SYSTEM_REQUIRED
0x00000001

    Forces the system to be in the working state by resetting the system idle timer.

ES_USER_PRESENT
0x00000004

    This value is not supported. If ES_USER_PRESENT is combined with other esFlags values, the call will fail and none of the specified states will be set. 
    */
    [FlagsAttribute]
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

    internal static class WinU
    {
        // Import SetThreadExecutionState Win32 API and necessary flags
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static internal extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);
    }
}
