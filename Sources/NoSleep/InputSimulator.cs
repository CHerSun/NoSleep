using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace NoSleep
{
    /// <summary>
    /// Minimal input simulation via SendInput: relative 1-pixel mouse moves only - enough
    /// to reset every idle timer and to wake the system out of an entry transition
    /// (S0-FINDINGS.md, T06), invisible in practice on any modern pointer acceleration.
    /// </summary>
    internal static class InputSimulator
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type; // 0 = INPUT_MOUSE; MOUSEINPUT is the largest union member,
            // so this layout matches the native INPUT size (validated during testing).
            public MOUSEINPUT mi;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private const uint InputMouse = 0;
        private const uint MouseEventfMove = 0x0001;

        /// <summary> One relative mouse move. Safe from any thread. </summary>
        public static void Move(int dx, int dy)
        {
            INPUT[] input = new INPUT[1];
            input[0].type = InputMouse;
            input[0].mi.dx = dx;
            input[0].mi.dy = dy;
            input[0].mi.dwFlags = MouseEventfMove;
            SendInput(1, input, Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>
        /// Ten alternating 1 px moves, 10 ms apart (~100 ms total). Intended to run on the
        /// power-callback thread the instant a sleep attempt is reported, so at least some
        /// of the moves land inside the pre-suspend window (~107 ms lid-close to suspend,
        /// S0-FINDINGS.md T14) and the rest trip the wake-on-input path.
        /// </summary>
        public static void Burst()
        {
            int dx = 1;
            for (int i = 0; i < 10; i++)
            {
                Move(dx, 0);
                dx = -dx;
                Thread.Sleep(10);
            }
        }
    }
}
