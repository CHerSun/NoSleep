using System;
using System.Runtime.InteropServices;

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

    /// <summary>
    /// Extension methods for <see cref="EXECUTION_STATE"/> enum.
    /// </summary>
    internal static class ExecutionStateEnumExtensions
    {
        internal static EXECUTION_STATE EnableFlag(this EXECUTION_STATE value, EXECUTION_STATE flag) => value | flag;
        internal static EXECUTION_STATE DisableFlag(this EXECUTION_STATE value, EXECUTION_STATE flag) => value & ~flag;
        internal static EXECUTION_STATE ToggleFlag(this EXECUTION_STATE value, EXECUTION_STATE flag) => value ^ flag;
    }

    /// <summary>
    /// Win32 API wrapper.
    /// </summary>
    internal static class WinU
    {
        /// <summary>
        /// Import SetThreadExecutionState from Win32 API.
        /// See <see href="https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate">SetThreadExecutionState</see>.
        /// </summary>
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static internal extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);
    }
}
