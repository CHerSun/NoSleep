<#
    NoSleep S0 / Modern Standby diagnostic suite (issue #8)

    Answers, on the machine it runs on:
      1. Which sleep states are actually available (S0 Modern Standby vs S3) and what the
         power manager reports (GetPwrCapabilities: AoAc, SystemS3, LidPresent, ...).
      2. Whether a power request (SetThreadExecutionState ES_SYSTEM_REQUIRED) is visible
         in powercfg -requests and whether the system sleeps anyway:
           - on AC (expected: stays awake),
           - on battery (documented: requests are terminated 5 min after the sleep timeout).
         Phase 3b repeats this with the OTHER power-request mechanism (buho/PowerToys
         Awake style: PowerSetRequest(ExecutionRequired) only) to see whether that one
         prevents standby entry any better.
         Phase 3c is the CONTROL: same skeleton, NO request held. If the machine does
         not idle-sleep in the control window, the "stayed awake" verdicts of 3/3b
         prove nothing. NOTE: phases 3/3b/3c/3d shorten BOTH the sleep timeout AND the
         display-off timeout to 2 min (restored afterwards) - on Modern Standby a lit
         screen can gate standby entry, so without that the runs say nothing about the
         real "screen off + keep running" scenario.
         Phase 3d additionally RE-ARMS the request every 5 s (what the NoSleep app
         actually does) and runs a SECOND lock-free heartbeat process alongside -
         it answers (a) whether re-arming defeats the documented "requests
         terminated 5 min after the timeout on DC" rule, and (b) whether OTHER
         apps keep running during the held period or only the request holder
         survives (Modern Standby may suspend apps per-app via DAM).
      3. Whether writing the lid-close action (LIDACTION -> "Do nothing") via the
         PowerWriteAC/DCValueIndex APIs works WITHOUT elevation, and whether lid-close
         then stops triggering sleep (A/B test with automatic restore).
      4. Optional: whether simulated mouse input (SendInput) keeps an S0 system out of
         standby across a lid close.
      5. Event-driven jiggle (phase 6a): registers lid-switch + console-display power
         notifications in a hidden window; on lid-close it fires a 10-move burst
         instantly. Proves/disproves the DAM race and the micro-suspend+instant-wake
         behavior - the candidate default for the app's "prevent" jiggle mode.
      6. Display-off check (phase 6b): with LIDACTION=0, which display-off signals
         arrive while the lid is closed (console/session/monitor notifications) -
         the "screen off, laptop keeps running" goal. SC_MONITORPOWER is NOT used:
         on Modern Standby test hardware a manual screen-off broadcast itself
         triggered standby entry.

    USAGE (run TWICE on the target laptop - once in a normal window, once in an elevated
    one; the report is named after the role):
        powershell -NoProfile -ExecutionPolicy Bypass -File .\s0-diag.ps1            interactive
        powershell -NoProfile -ExecutionPolicy Bypass -File .\s0-diag.ps1 -Quick     static checks only

    Phases 1-2 always run (read-only + a write probe that restores immediately).
    Phases 3, 3b, 3c, 3d, 3e, 3f, 4, 5, 6a, 6b are opt-in via y/N prompts (they mutate
    power settings and restore them). Budget: phase 3 = 8 min, 3b = 8 min, 3c = 7 min,
    3d/3e/3f = 10 min each, 4 = 4 min, 5 = 2 min, 6a/6b = up to 4 min each (they wait
    up to 3 min for you to close the lid, then observe 30 s).
    Every report line is timestamped (HH:mm:ss); phase failures are logged into
    the report, not just the console.
    Results: S0-Report-<admin|user>.txt next to this script. Bring those back.
    If S0-Restore.txt still exists after a run, a phase was interrupted - the next run
    restores the saved values automatically before doing anything else.

    Self-contained: PowerShell 5.1, built-in tools only, no downloads, no NoSleep.exe.
#>
param([switch]$Quick)

$ErrorActionPreference = 'Continue'
$script:Dir         = Split-Path -Parent $MyInvocation.MyCommand.Path
$isAdmin            = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$script:Report      = Join-Path $script:Dir ('S0-Report-{0}.txt' -f $(if ($isAdmin) { 'admin' } else { 'user' }))
$script:RestoreFile = Join-Path $script:Dir 'S0-Restore.txt'

# --- power setting GUIDs (well-known, documented in Windows Driver Kit / powercfg) ---
$GUID_SUB_BUTTONS   = '4f971e89-eebd-4455-a8de-9e59040e7347'
$GUID_LIDACTION     = '5ca83367-6e45-459f-a27b-476b1d01c936'
$GUID_SBUTTONACTION = '961cc777-2547-4f9d-8174-7d86181b8a7a'
$GUID_PBUTTONACTION = '7648efa3-dd33-4e3e-b566-50f929386280'
$GUID_SUB_SLEEP     = '238c9fa8-0aad-41ed-83f4-97be242c8f20'
$GUID_STANDBYIDLE   = '29f6c1db-86da-48c5-9fdb-f2b67b1f44da'
$GUID_SUB_VIDEO     = '7516b95f-f776-4464-8c53-06167f40cc99'
$GUID_VIDEOIDLE     = '3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e'

# --- SetThreadExecutionState flags, as decimal strings on purpose:
#     PowerShell parses hex literals >= 0x80000000 as negative Int32, which cannot convert to UInt32.
$ES_RELEASE     = [uint32]'2147483648'   # 0x80000000 = lone ES_CONTINUOUS (release)
$ES_HOLD_SYSTEM = [uint32]'2147483649'   # 0x80000001 = ES_CONTINUOUS | ES_SYSTEM_REQUIRED
$ES_PING_SYSTEM = [uint32]'1'            # 0x00000001 = ES_SYSTEM_REQUIRED alone (ONE-SHOT idle-timer reset, no persistent claim)

$src = @'
using System;
using System.Runtime.InteropServices;

// Native probe helpers for the NoSleep S0 diagnostic suite.
// Kept C# 5 compatible: PowerShell 5.1 Add-Type compiles with the legacy CodeDom compiler.
public static class NoSleepProbe
{
    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr rootPowerKey, IntPtr policyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupGuid, IntPtr settingGuid, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupGuid, IntPtr settingGuid, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupGuid, IntPtr settingGuid, uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, IntPtr schemeGuid, IntPtr subGroupGuid, IntPtr settingGuid, uint value);

    [DllImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool GetPwrCapabilities(IntPtr systemPowerCapabilities);

    [DllImport("kernel32.dll")]
    private static extern uint SetThreadExecutionState(uint esFlags);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

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
        public uint type; // 0 = INPUT_MOUSE
        public MOUSEINPUT mi;
    }

    private static IntPtr GuidPtr(string guidText)
    {
        byte[] raw = new Guid(guidText).ToByteArray();
        IntPtr p = Marshal.AllocHGlobal(16);
        Marshal.Copy(raw, 0, p, 16);
        return p;
    }

    // Active power scheme GUID as a string, or null.
    public static string GetScheme()
    {
        IntPtr p;
        if (PowerGetActiveScheme(IntPtr.Zero, out p) != 0)
            return null;
        try
        {
            Guid g = (Guid)Marshal.PtrToStructure(p, typeof(Guid));
            return g.ToString();
        }
        finally
        {
            LocalFree(p);
        }
    }

    // Re-applies the given scheme, committing pending per-setting writes.
    public static uint ApplyScheme(string schemeGuid)
    {
        IntPtr p = GuidPtr(schemeGuid);
        try
        {
            return PowerSetActiveScheme(IntPtr.Zero, p);
        }
        finally
        {
            Marshal.FreeHGlobal(p);
        }
    }

    // Returns [win32 error code, value].
    public static uint[] ReadValue(string schemeGuid, string groupGuid, string settingGuid, bool ac)
    {
        IntPtr scheme = GuidPtr(schemeGuid);
        IntPtr group = GuidPtr(groupGuid);
        IntPtr setting = GuidPtr(settingGuid);
        try
        {
            uint value;
            uint status = ac
                ? PowerReadACValueIndex(IntPtr.Zero, scheme, group, setting, out value)
                : PowerReadDCValueIndex(IntPtr.Zero, scheme, group, setting, out value);
            return new uint[] { status, value };
        }
        finally
        {
            Marshal.FreeHGlobal(scheme);
            Marshal.FreeHGlobal(group);
            Marshal.FreeHGlobal(setting);
        }
    }

    // Returns win32 error code (0 = success).
    public static uint WriteValue(string schemeGuid, string groupGuid, string settingGuid, uint value, bool ac)
    {
        IntPtr scheme = GuidPtr(schemeGuid);
        IntPtr group = GuidPtr(groupGuid);
        IntPtr setting = GuidPtr(settingGuid);
        try
        {
            return ac
                ? PowerWriteACValueIndex(IntPtr.Zero, scheme, group, setting, value)
                : PowerWriteDCValueIndex(IntPtr.Zero, scheme, group, setting, value);
        }
        finally
        {
            Marshal.FreeHGlobal(scheme);
            Marshal.FreeHGlobal(group);
            Marshal.FreeHGlobal(setting);
        }
    }

    // SYSTEM_POWER_CAPABILITIES as a raw byte buffer (allocated large; only the head is decoded).
    // Null on failure.
    public static byte[] Capabilities()
    {
        byte[] buffer = new byte[512];
        System.Runtime.InteropServices.GCHandle handle = System.Runtime.InteropServices.GCHandle.Alloc(buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            if (!GetPwrCapabilities(handle.AddrOfPinnedObject()))
                return null;
            return buffer;
        }
        finally
        {
            handle.Free();
        }
    }

    public static uint ExecutionState(uint flags)
    {
        return SetThreadExecutionState(flags);
    }

    // Injects one relative mouse move. Returns the number of accepted inputs.
    public static uint Jiggle(int dx, int dy)
    {
        INPUT[] inputs = new INPUT[1];
        inputs[0].type = 0;
        inputs[0].mi.dx = dx;
        inputs[0].mi.dy = dy;
        inputs[0].mi.dwFlags = 0x0001; // MOUSEEVENTF_MOVE
        return SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct REASON_CONTEXT
    {
        public uint Version;  // POWER_REQUEST_CONTEXT_VERSION = 0
        public uint Flags;    // POWER_REQUEST_CONTEXT_SIMPLE_STRING = 1
        public IntPtr Reason; // union: LPWSTR SimpleReasonString (pointer-sized member)
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr PowerCreateRequest(ref REASON_CONTEXT context);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool PowerSetRequest(IntPtr powerRequest, int requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool PowerClearRequest(IntPtr powerRequest, int requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool PowerDestroyRequest(IntPtr powerRequest);

    // PowerCreateRequest handle, or IntPtr.Zero on failure. Request types:
    // 0=DisplayRequired 1=SystemRequired 2=AwayModeRequired 3=ExecutionRequired
    public static IntPtr CreateRequest(string reason)
    {
        REASON_CONTEXT ctx = new REASON_CONTEXT();
        ctx.Version = 0;
        ctx.Flags = 1;
        ctx.Reason = Marshal.StringToHGlobalUni(reason);
        try
        {
            return PowerCreateRequest(ref ctx);
        }
        finally
        {
            Marshal.FreeHGlobal(ctx.Reason);
        }
    }

    public static bool SetRequest(IntPtr request, int requestType)
    {
        return PowerSetRequest(request, requestType);
    }

    public static bool ClearRequest(IntPtr request, int requestType)
    {
        return PowerClearRequest(request, requestType);
    }

    public static bool DestroyRequest(IntPtr request)
    {
        return PowerDestroyRequest(request);
    }
}
'@
Add-Type -TypeDefinition $src -Language CSharp

# --- power notification window: hidden top-level window + raw user32 message loop on its own
#     thread (no WinForms reference needed, works on Windows PowerShell 5.1 and pwsh 7).
#     Receives WM_POWERBROADCAST, decodes lid-switch / console-display settings,
#     optionally fires an instant jiggle burst on lid-close / suspend, and enqueues
#     timestamped lines for the script to drain.
$src2 = @'
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace NoSleepKit
{
    public sealed class PowerNotifyWindow : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public POINT pt; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASS
        {
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        }

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
        private struct INPUT { public uint type; public MOUSEINPUT mi; }

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private const uint WM_POWERBROADCAST = 0x218;
        private const uint WM_CLOSE = 0x10;
        private const int PBT_APMSUSPEND = 4;
        private const int PBT_APMRESUME = 7;
        private const int PBT_APMRESUMEAUTOMATIC = 18;
        private const int PBT_POWERSETTINGCHANGE = 0x8013;

        private static readonly Guid LidSwitchGuid = new Guid("BA3E0F4D-B817-4094-A2D1-D56379E6A0F3");
        private static readonly Guid ConsoleDisplayGuid = new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47");
        private static readonly Guid SessionDisplayGuid = new Guid("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");
        private static readonly Guid MonitorPowerGuid = new Guid("02731015-4510-4526-99E6-E5A17EBD1AEA");
        private static readonly Guid AcDcGuid = new Guid("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern void PostQuitMessage(int nExitCode);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid PowerSettingGuid, uint Flags);

        [DllImport("user32.dll")]
        private static extern bool UnregisterPowerSettingNotification(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerSettingRegisterNotification(IntPtr settingGuid, uint registrationType, IntPtr recipient, out IntPtr registrationHandle);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerUnregisterNotification(IntPtr registrationHandle);

        private delegate uint PowerCallbackDelegate(IntPtr context, uint type, IntPtr setting);

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS
        {
            public IntPtr Callback;
            public IntPtr Context;
        }

        private static readonly WndProcDelegate Proc = new WndProcDelegate(StaticWndProc);
        private static PowerNotifyWindow Active;
        private static bool ClassRegistered;
        private const string ClassName = "NoSleepKitPowerNotifyWnd";

        // second notification path: powrprof callbacks (no window) - Win11 26200 test hardware
        // ignored the window-based RegisterPowerSettingNotification entirely.
        // STATIC rooting is mandatory: an instance-only delegate gets GC'd after the phase
        // ends while the OS still holds the callback pointer -> crash on the next
        // notification ("callback was made on a garbage collected delegate", 0x80131623).
        private static readonly PowerCallbackDelegate Cb = new PowerCallbackDelegate(CallbackRouter);
        private IntPtr cbLid = IntPtr.Zero;
        private IntPtr cbSess = IntPtr.Zero;
        private IntPtr cbAcdc = IntPtr.Zero;
        // thread-local: window-path and callback-path lines can arrive on different threads at once
        [ThreadStatic]
        private static bool cbMode;

        private readonly System.Collections.Concurrent.ConcurrentQueue<string> queue = new System.Collections.Concurrent.ConcurrentQueue<string>();
        private readonly bool autoBurst;
        private readonly Thread thread;
        private IntPtr hWnd = IntPtr.Zero;
        private IntPtr lidReg = IntPtr.Zero;
        private IntPtr dispReg = IntPtr.Zero;
        private IntPtr sessReg = IntPtr.Zero;
        private IntPtr monReg = IntPtr.Zero;
        private IntPtr acdcReg = IntPtr.Zero;

        public PowerNotifyWindow(bool autoBurstOnSleepEvents)
        {
            autoBurst = autoBurstOnSleepEvents;
            Active = this;
            thread = new Thread(new ThreadStart(Run));
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }

        public System.Collections.Concurrent.ConcurrentQueue<string> Queue
        {
            get { return queue; }
        }

        private void Run()
        {
            IntPtr inst = GetModuleHandle(null);
            if (!ClassRegistered)
            {
                WNDCLASS wc = new WNDCLASS();
                wc.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc);
                wc.hInstance = inst;
                wc.lpszClassName = ClassName;
                RegisterClass(ref wc); // atom 0 with "class already exists" is fine on a second window
                ClassRegistered = true;
            }
            // hidden top-level window (WS_POPUP, no WS_VISIBLE) - broadcasts skip message-only windows
            hWnd = CreateWindowEx(0, ClassName, "nosleep-s0-diag", 0x80000000, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
            if (hWnd == IntPtr.Zero)
            {
                Push("window-create FAILED, err=" + Marshal.GetLastWin32Error());
                return;
            }
            Guid lid = LidSwitchGuid;
            Guid disp = ConsoleDisplayGuid;
            Guid sess = SessionDisplayGuid;
            Guid mon = MonitorPowerGuid;
            Guid acdc = AcDcGuid;
            lidReg = RegisterPowerSettingNotification(hWnd, ref lid, 0);
            dispReg = RegisterPowerSettingNotification(hWnd, ref disp, 0);
            sessReg = RegisterPowerSettingNotification(hWnd, ref sess, 0);
            monReg = RegisterPowerSettingNotification(hWnd, ref mon, 0);
            acdcReg = RegisterPowerSettingNotification(hWnd, ref acdc, 0);
            Push("window-ready (lid=" + lidReg + ", console=" + dispReg + ", session=" + sessReg + ", monitor=" + monReg + ", acdc=" + acdcReg + ")");
            uint cf1 = RegisterCallback(LidSwitchGuid, out cbLid);
            uint cf2 = RegisterCallback(SessionDisplayGuid, out cbSess);
            uint cf3 = RegisterCallback(AcDcGuid, out cbAcdc);
            Push("callback-path registrations: lid(flags=" + cf1 + "), session(flags=" + cf2 + "), acdc(flags=" + cf3 + ") - 0 means failed");
            MSG m;
            while (GetMessage(out m, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref m);
                DispatchMessage(ref m);
            }
        }

        private static IntPtr StaticWndProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            PowerNotifyWindow self = Active;
            if (self != null)
                return self.WndProc(h, msg, w, l);
            return DefWindowProc(h, msg, w, l);
        }

        private IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            if (msg == WM_POWERBROADCAST)
            {
                int reason = w.ToInt32();
                if (reason == PBT_APMSUSPEND)
                {
                    Push("APMSUSPEND");
                    if (autoBurst)
                    {
                        Burst();
                        Push("auto-burst fired on APMSUSPEND");
                    }
                }
                else if (reason == PBT_APMRESUME || reason == PBT_APMRESUMEAUTOMATIC)
                {
                    Push("APMRESUME(" + reason + ")");
                }
                else if (reason == PBT_POWERSETTINGCHANGE)
                {
                    try { ParseSetting(l); }
                    catch (Exception ex) { Push("setting parse error: " + ex.Message); }
                }
                else
                {
                    Push("PBT(" + reason + ")"); // raw, so unknown broadcasts are still visible
                }
                return IntPtr.Zero;
            }
            if (msg == WM_CLOSE)
            {
                if (lidReg != IntPtr.Zero) { UnregisterPowerSettingNotification(lidReg); lidReg = IntPtr.Zero; }
                if (dispReg != IntPtr.Zero) { UnregisterPowerSettingNotification(dispReg); dispReg = IntPtr.Zero; }
                if (sessReg != IntPtr.Zero) { UnregisterPowerSettingNotification(sessReg); sessReg = IntPtr.Zero; }
                if (monReg != IntPtr.Zero) { UnregisterPowerSettingNotification(monReg); monReg = IntPtr.Zero; }
                if (acdcReg != IntPtr.Zero) { UnregisterPowerSettingNotification(acdcReg); acdcReg = IntPtr.Zero; }
                // cb handles are deliberately NOT unregistered: unregistering while a callback is
                // in flight crashed the process during local teardown testing; the registrations
                // die with the process anyway.
                DestroyWindow(h);
                PostQuitMessage(0);
                return IntPtr.Zero;
            }
            return DefWindowProc(h, msg, w, l);
        }

        private void ParseSetting(IntPtr lParam)
        {
            // POWERBROADCAST_SETTING { GUID PowerSetting; DWORD DataLength; BYTE Data[1]; }
            byte[] buf = new byte[24];
            Marshal.Copy(lParam, buf, 0, 24);
            byte[] gb = new byte[16];
            Array.Copy(buf, 0, gb, 0, 16);
            Guid g = new Guid(gb);
            byte data = buf[20];
            if (g == LidSwitchGuid)
            {
                if (data == 0)
                {
                    Push("LID_CLOSED");
                    if (autoBurst)
                    {
                        Burst();
                        Push("auto-burst fired on LID_CLOSED");
                    }
                }
                else
                {
                    Push("LID_OPEN");
                }
            }
            else if (g == ConsoleDisplayGuid)
            {
                if (data == 0) Push("DISPLAY_OFF");
                else if (data == 1) Push("DISPLAY_ON");
                else Push("DISPLAY_DIMMED(" + data + ")");
            }
            else if (g == SessionDisplayGuid)
            {
                if (data == 0) Push("SESSION_DISPLAY_OFF");
                else if (data == 1) Push("SESSION_DISPLAY_ON");
                else Push("SESSION_DISPLAY_DIMMED(" + data + ")");
            }
            else if (g == MonitorPowerGuid)
            {
                if (data == 0) Push("MONITOR_OFF");
                else Push("MONITOR_ON");
            }
            else if (g == AcDcGuid)
            {
                if (data == 0) Push("POWER_SOURCE_AC");
                else if (data == 1) Push("POWER_SOURCE_BATTERY");
                else Push("POWER_SOURCE_" + data);
            }
            else
            {
                Push("SETTING " + g + " data=" + data);
            }
        }

        private static uint CallbackRouter(IntPtr context, uint type, IntPtr setting)
        {
            PowerNotifyWindow self = Active;
            if (self != null)
                self.CallbackBody(type, setting);
            return 0;
        }

        private void CallbackBody(uint type, IntPtr setting)
        {
            cbMode = true;
            try
            {
                if (type == 0x8013)
                {
                    try { ParseSetting(setting); }
                    catch (Exception ex) { Push("setting parse error: " + ex.Message); }
                }
                else if (type == 4)
                {
                    Push("APMSUSPEND");
                    if (autoBurst) { Burst(); Push("auto-burst fired on APMSUSPEND"); }
                }
                else if (type == 7 || type == 18)
                {
                    Push("APMRESUME(" + type + ")");
                }
                else
                {
                    Push("PBT(" + type + ")");
                }
            }
            finally
            {
                cbMode = false;
            }
        }

        // Registers one setting via the powrprof callback API. Tries both plausible
        // DEVICE_NOTIFY_CALLBACK flag values; returns the flags that worked (0 = both failed).
        private uint RegisterCallback(Guid g, out IntPtr handle)
        {
            IntPtr guidPtr = Marshal.AllocHGlobal(16);
            Marshal.Copy(g.ToByteArray(), 0, guidPtr, 16);
            IntPtr pars = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS)));
            try
            {
                DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS p = new DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS();
                p.Callback = Marshal.GetFunctionPointerForDelegate(Cb);
                p.Context = IntPtr.Zero;
                Marshal.StructureToPtr(p, pars, false);
                uint[] candidates = new uint[] { 2, 0 };
                foreach (uint f in candidates)
                {
                    IntPtr h;
                    uint rc = PowerSettingRegisterNotification(guidPtr, f, pars, out h);
                    if (rc == 0)
                    {
                        handle = h;
                        return f;
                    }
                }
                handle = IntPtr.Zero;
                return 0;
            }
            finally
            {
                Marshal.FreeHGlobal(guidPtr);
                Marshal.FreeHGlobal(pars);
            }
        }

        private void Burst()
        {
            int dx = 1;
            for (int i = 0; i < 10; i++)
            {
                INPUT[] inp = new INPUT[1];
                inp[0].type = 0;
                inp[0].mi.dx = dx;
                inp[0].mi.dy = 0;
                inp[0].mi.dwFlags = 0x0001; // MOUSEEVENTF_MOVE
                SendInput(1, inp, Marshal.SizeOf(typeof(INPUT)));
                dx = -dx;
                Thread.Sleep(10);
            }
        }

        // NOTE: SC_MONITORPOWER deliberately NOT implemented: on Modern Standby test hardware
        // a manual screen-off broadcast itself triggered standby entry (506/507 storm).

        public void Dispose()
        {
            if (hWnd != IntPtr.Zero)
            {
                PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                thread.Join(3000);
            }
            if (Active == this)
                Active = null;
        }

        private void Push(string s)
        {
            string prefix = cbMode ? "[cb] " : "";
            queue.Enqueue(string.Format("{0:HH:mm:ss.fff} {1}{2}", DateTime.Now, prefix, s));
        }
    }
}
'@
Add-Type -TypeDefinition $src2 -Language CSharp

# ---------------------------------------------------------------- helpers

function Log([string]$text)
{
    # every non-empty report line gets a HH:mm:ss prefix
    $stamp = Get-Date -Format 'HH:mm:ss'
    foreach ($line in ($text -split '\r?\n'))
    {
        $entry = $line
        if ($line.Trim().Length -gt 0) { $entry = '{0} {1}' -f $stamp, $line }
        Add-Content -Path $script:Report -Value $entry -Encoding UTF8
        Write-Host $entry
    }
}

function Section([string]$title)
{
    Log ''
    Log ('======== {0} ========' -f $title)
}

function Run-Exe([string]$exe, [string[]]$exeArgs)
{
    Log ''
    Log ('> {0} {1}' -f $exe, ($exeArgs -join ' '))
    # merge stderr at cmd level: PowerShell would wrap it in error records and pollute the report
    try
    {
        $out = & cmd.exe /c ('{0} {1} 2>&1' -f $exe, ($exeArgs -join ' ')) | Out-String
        Log $out.TrimEnd()
    }
    catch
    {
        Log ('FAILED: {0}' -f $_.Exception.ToString())
    }
}

function Ask([string]$question)
{
    if ($Quick) { return $false }
    $answer = Read-Host ('{0} [y/N]' -f $question)
    return ($answer -match '^[Yy]')
}

function Get-PowerEvents([datetime]$from, [datetime]$to)
{
    try
    {
        Get-WinEvent -FilterHashtable @{ LogName = 'System'; StartTime = $from; EndTime = $to } -ErrorAction Stop |
            Where-Object { ($_.ProviderName -like '*Kernel-Power*') -or ($_.Id -in 42, 107, 187, 506, 507) } |
            Sort-Object TimeCreated |
            ForEach-Object { Log ('    {0:yyyy-MM-dd HH:mm:ss}  {1}  Id={2}  Level={3}' -f $_.TimeCreated, $_.ProviderName, $_.Id, $_.LevelDisplayName) }
    }
    catch
    {
        Log ('    (no matching events or query failed: {0})' -f $_.Exception.Message)
    }
}

# Persists the original values of everything we are about to mutate, once per run.
function Save-Restore
{
    if (-not (Test-Path $script:RestoreFile))
    {
        # capture originals for all four slots on first mutation
        $scheme = [NoSleepProbe]::GetScheme()
        $slots = @(
            @{ k = 'lidAc';      ac = $true  },
            @{ k = 'lidDc';      ac = $false },
            @{ k = 'standbyAc';  ac = $true  },
            @{ k = 'standbyDc';  ac = $false },
            @{ k = 'videoAc';    ac = $true  },
            @{ k = 'videoDc';    ac = $false }
        )
        $lines = @('# values saved by s0-diag.ps1 before mutating power settings; delete after restore')
        foreach ($slot in $slots)
        {
            $group = $(if ($slot.k -like 'lid*') { $GUID_SUB_BUTTONS } elseif ($slot.k -like 'video*') { $GUID_SUB_VIDEO } else { $GUID_SUB_SLEEP })
            $setting = $(if ($slot.k -like 'lid*') { $GUID_LIDACTION } elseif ($slot.k -like 'video*') { $GUID_VIDEOIDLE } else { $GUID_STANDBYIDLE })
            $res = [NoSleepProbe]::ReadValue($scheme, $group, $setting, $slot.ac)
            if ($res[0] -eq 0) { $lines += ('{0}={1}' -f $slot.k, $res[1]) }
        }
        $lines | Set-Content -Path $script:RestoreFile -Encoding ASCII
        Log ('(restore snapshot saved to {0})' -f $script:RestoreFile)
    }
}

function Restore-And-Clean
{
    if (-not (Test-Path $script:RestoreFile)) { return }
    Section 'RESTORE (safety net: re-applying saved original values)'
    $scheme = [NoSleepProbe]::GetScheme()
    foreach ($line in (Get-Content $script:RestoreFile))
    {
        if ($line -like '#*') { continue }
        $parts = $line -split '=', 2
        if ($parts.Count -ne 2) { continue }
        $key = $parts[0]; $val = [uint32]$parts[1]
        $group = $(if ($key -like 'lid*') { $GUID_SUB_BUTTONS } elseif ($key -like 'video*') { $GUID_SUB_VIDEO } else { $GUID_SUB_SLEEP })
        $setting = $(if ($key -like 'lid*') { $GUID_LIDACTION } elseif ($key -like 'video*') { $GUID_VIDEOIDLE } else { $GUID_STANDBYIDLE })
        $ac = $key -like '*Ac'
        $status = [NoSleepProbe]::WriteValue($scheme, $group, $setting, $val, $ac)
        Log ('restored {0}={1} (write status 0x{2:X})' -f $key, $val, $status)
    }
    $applyStatus = [NoSleepProbe]::ApplyScheme($scheme)
    Log ('scheme re-applied (status 0x{0:X})' -f $applyStatus)
    Remove-Item $script:RestoreFile -Force
    Log 'restore snapshot deleted.'
}

# ---------------------------------------------------------------- phase 1: static facts

Section ('RUN START - role: {0}' -f $(if ($isAdmin) { 'ADMINISTRATOR' } else { 'normal user' }))
Log ('script dir : {0}' -f $script:Dir)
Log ('report     : {0}' -f $script:Report)
Log ('quick mode : {0}' -f $Quick)

try { $cs = Get-CimInstance Win32_ComputerSystem } catch { $cs = $null }
if ($cs) { Log ('machine    : {0} {1}' -f $cs.Manufacturer, $cs.Model) }
try { $os = Get-CimInstance Win32_OperatingSystem } catch { $os = $null }
if ($os) { Log ('os         : {0} (build {1})' -f $os.Caption, $os.BuildNumber) }

Add-Type -AssemblyName System.Windows.Forms
$powerStatus = [System.Windows.Forms.SystemInformation]::PowerStatus
Log ('power line: {0}   battery: {1}%' -f $powerStatus.PowerLineStatus, [int]($powerStatus.BatteryLifePercent * 100))

Section 'PHASE 1a: powercfg views'
Run-Exe 'powercfg.exe' @('-a')
Run-Exe 'powercfg.exe' @('-getactivescheme')
Run-Exe 'powercfg.exe' @('-requests')
Run-Exe 'powercfg.exe' @('-q', 'SCHEME_CURRENT', 'SUB_BUTTONS')

Section 'PHASE 1b: registry markers'
Run-Exe 'reg.exe' @('query', 'HKLM\SYSTEM\CurrentControlSet\Control\Power', '/v', 'PlatformAoAc')
Run-Exe 'reg.exe' @('query', 'HKLM\SYSTEM\CurrentControlSet\Control\Power', '/v', 'PlatformAoAcOverride')
Run-Exe 'reg.exe' @('query', 'HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerRequestOverride', '/s')

Section 'PHASE 1c: GetPwrCapabilities (power manager view)'
$cap = [NoSleepProbe]::Capabilities()
if ($cap -eq $null)
{
    Log 'GetPwrCapabilities FAILED'
}
else
{
    # SYSTEM_POWER_CAPABILITIES head layout (winnt.h, Win10+), 1 byte per BOOLEAN/UCHAR:
    # 0 PowerButtonPresent, 1 SleepButtonPresent, 2 LidPresent, 3 SystemS1, 4 SystemS2,
    # 5 SystemS3, 6 SystemS4, 7 SystemS5, 8 HiberFile, 9 FullWake, 10 VideoDimPresent,
    # 11 ApmPresent, 12 UpsPresent, 13 ThermalMode, 14 ProcessorThrottle,
    # 15 ProcessorMinThrottle, 16 ProcessorMaxThrottle, 17 FastSystemS4, 18 Hiberboot,
    # 19 WakeAlertPresent, 20 AoAc (Modern Standby!), 21 DiskSpinDown, 22 HiberFileType,
    # 23 AoAcConnectivitySupported, 24 SystemBatteriesPresent, 25 BatteriesAreShortTerm
    function B([int]$i) { if ($cap[$i] -ne 0) { 'yes' } else { 'no' } }
    Log ('LidPresent={0}  Batteries={1}' -f (B 2), (B 24))
    Log ('SystemS1={0} SystemS2={1} SystemS3={2} SystemS4={3} SystemS5={4}' -f (B 3), (B 4), (B 5), (B 6), (B 7))
    Log ('HiberFile={0} FastSystemS4={1} Hiberboot={2} WakeAlert={3}' -f (B 8), (B 17), (B 18), (B 19))
    Log ('AoAc={0} (Modern Standby)  AoAcConnectivity={1}  DiskSpinDown={2}  HiberFileType={3}' -f (B 20), (B 23), (B 21), $cap[22])
    if (($cap[5] -ne 0) -and ($cap[20] -ne 0)) { Log 'NOTE: both S3 and AoAc reported as available - unexpected, inspect raw output.' }
}

# ---------------------------------------------------------------- phase 2: LIDACTION write probe

Section 'PHASE 2: lid-action (LIDACTION) read/write probe - privilege test'
$scheme = [NoSleepProbe]::GetScheme()
if ($scheme -eq $null)
{
    Log 'cannot get active scheme - skipping phase 2'
}
else
{
    Log ('active scheme: {0}' -f $scheme)
    $readAc = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, $true)
    $readDc = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, $false)
    Log ('LIDACTION read AC: status 0x{0:X} value {1}   (0=DoNothing 1=Sleep 2=Hibernate 3=Shutdown)' -f $readAc[0], $readAc[1])
    Log ('LIDACTION read DC: status 0x{0:X} value {1}' -f $readDc[0], $readDc[1])

    if (($readAc[0] -ne 0) -and ($readDc[0] -ne 0))
    {
        Log 'LIDACTION not readable on this machine (no lid?) - write probe skipped.'
    }
    else
    {
        Save-Restore
        try
        {
            $writeAc = [NoSleepProbe]::WriteValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, 0, $true)
            $writeDc = [NoSleepProbe]::WriteValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, 0, $false)
            $applyStatus = [NoSleepProbe]::ApplyScheme($scheme)
            Log ('write 0 (Do nothing): AC status 0x{0:X}, DC status 0x{1:X}, apply status 0x{2:X}' -f $writeAc, $writeDc, $applyStatus)
            if (($writeAc -eq 0) -and ($writeDc -eq 0))
            {
                Log ('RESULT: LIDACTION write {0} WITHOUT elevation succeeded.' -f $(if ($isAdmin) { 'as admin' } else { 'as normal user' }))
            }
            else
            {
                Log ('RESULT: LIDACTION write FAILED (0=success). AC err=0x{0:X} DC err=0x{1:X}' -f $writeAc, $writeDc)
            }
            $checkAc = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, $true)
            Log ('verify after write, AC value = {0}' -f $checkAc[1])
        }
        catch
        {
            Log ('PHASE 2 write probe FAILED: {0}' -f $_.Exception.ToString())
        }
        finally
        {
            if ($readAc[0] -eq 0) { [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, $readAc[1], $true) }
            if ($readDc[0] -eq 0) { [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, $readDc[1], $false) }
            [void][NoSleepProbe]::ApplyScheme($scheme)
            Log 'original LIDACTION values restored.'
            Remove-Item $script:RestoreFile -Force -ErrorAction SilentlyContinue
            Log 'restore snapshot cleaned up.'
        }
    }

    $standbyAc = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, $true)
    $standbyDc = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, $false)
    Log ('standby idle read AC: status 0x{0:X} value {1}s   DC: status 0x{2:X} value {3}s (0 = never)' -f $standbyAc[0], $standbyAc[1], $standbyDc[0], $standbyDc[1])

    $videoAc = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, $true)
    $videoDc = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, $false)
    Log ('display-off (VIDEOIDLE) read AC: status 0x{0:X} value {1}s   DC: status 0x{2:X} value {3}s (0 = never)' -f $videoAc[0], $videoAc[1], $videoDc[0], $videoDc[1])

    $sbAc = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_BUTTONS, $GUID_SBUTTONACTION, $true)
    $sbDc = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_BUTTONS, $GUID_SBUTTONACTION, $false)
    $pbAc = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_BUTTONS, $GUID_PBUTTONACTION, $true)
    $pbDc = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_BUTTONS, $GUID_PBUTTONACTION, $false)
    Log ('SBUTTONACTION (sleep button): AC status 0x{0:X} value {1}, DC status 0x{2:X} value {3}' -f $sbAc[0], $sbAc[1], $sbDc[0], $sbDc[1])
    Log ('PBUTTONACTION (power button): AC status 0x{0:X} value {1}, DC status 0x{2:X} value {3}' -f $pbAc[0], $pbAc[1], $pbDc[0], $pbDc[1])
}

# ---------------------------------------------------------------- phase 3: request vs sleep timeout

if (Ask 'PHASE 3: hold ES_SYSTEM_REQUIRED ~8 min while sleep timeout is 2 min - does the system sleep anyway? (changes + restores the sleep timeout for the CURRENT power source)')
{
    Section 'PHASE 3: power request vs sleep timeout (does the OS honor the request?)'
    if ($scheme -eq $null) { Log 'no active scheme - skip' }
    else
    {
        $onAc = ($powerStatus.PowerLineStatus -eq 'Online')
        Log ('current power source: {0} (the DC case is the interesting one - docs say requests die 5 min after timeout)' -f $(if ($onAc) { 'AC' } else { 'BATTERY' }))
        Save-Restore
        $original = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, $onAc)
        $origVideo = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, $onAc)
        Log ('original standby timeout on {0}: {1}s (status 0x{2:X}); display-off timeout: {3}s (status 0x{4:X})' -f $(if ($onAc) { 'AC' } else { 'DC' }), $original[1], $original[0], $origVideo[1], $origVideo[0])
        $t0 = Get-Date
        try
        {
            $w = [NoSleepProbe]::WriteValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, 120, $onAc)
            $wv = [NoSleepProbe]::WriteValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, 120, $onAc)
            [void][NoSleepProbe]::ApplyScheme($scheme)
            Log ('standby AND display-off timeouts set to 120s on {0} (write status 0x{1:X}/0x{2:X})' -f $(if ($onAc) { 'AC' } else { 'DC' }), $w, $wv)

            Log 'holding SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED) for 8 minutes...'
            Log 'DO NOT touch the machine during this phase. The screen SHOULD turn off ~2 min in - the system must stay awake with the screen off.'
            [void][NoSleepProbe]::ExecutionState($ES_HOLD_SYSTEM)
            Start-Sleep -Seconds 10
            Run-Exe 'powercfg.exe' @('-requests')

            $ticks = New-Object System.Collections.Generic.List[datetime]
            $deadline = (Get-Date).AddSeconds(480)
            while ((Get-Date) -lt $deadline)
            {
                $now = Get-Date
                $ticks.Add($now)
                Add-Content -Path $script:Report -Value ('    {0:HH:mm:ss} heartbeat' -f $now) -Encoding UTF8
                Start-Sleep -Seconds 5
            }
        }
        catch
        {
            Log ('PHASE 3 FAILED: {0}' -f $_.Exception.ToString())
        }
        finally
        {
            [void][NoSleepProbe]::ExecutionState($ES_RELEASE)
            Log 'power request released.'
            if ($original[0] -eq 0)
            {
                [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, $original[1], $onAc)
                [void][NoSleepProbe]::ApplyScheme($scheme)
                Log ('standby timeout restored to {0}s.' -f $original[1])
            }
            if ($origVideo[0] -eq 0)
            {
                [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, $origVideo[1], $onAc)
                [void][NoSleepProbe]::ApplyScheme($scheme)
                Log ('display-off timeout restored to {0}s.' -f $origVideo[1])
            }
        }

        $maxGap = 0; $gapAt = $null
        for ($i = 1; $i -lt $ticks.Count; $i++)
        {
            $gap = ($ticks[$i] - ($ticks[$i - 1])).TotalSeconds
            if ($gap -gt $maxGap) { $maxGap = $gap; $gapAt = $ticks[$i] }
        }
        Log ('largest heartbeat gap: {0:N0}s at {1:HH:mm:ss} (>30s means the process was suspended = system slept)' -f $maxGap, $gapAt)
        Log ('VERDICT: {0}' -f $(if ($maxGap -gt 30) { 'SYSTEM SLEPT despite the held power request.' } else { 'system stayed awake for the whole 8 minutes.' }))
        Log 'Kernel-Power events during the window:'
        Get-PowerEvents $t0 (Get-Date)
    }
}

# ---------------------------------------------------------------- phase 3b: buho-style ExecutionRequired

if (Ask 'PHASE 3b: hold ONLY PowerRequestExecutionRequired (the "buho"/PowerToys-Awake approach) for ~8 min - does it prevent standby entry. Run')
{
    Section 'PHASE 3b: ExecutionRequired-only power request vs sleep timeout'
    if ($scheme -eq $null) { Log 'no active scheme - skip' }
    else
    {
        $onAc2 = ($powerStatus.PowerLineStatus -eq 'Online')
        Log ('power source: {0} (DC is the interesting case).' -f $(if ($onAc2) { 'AC' } else { 'BATTERY' }))
        Save-Restore
        $original2 = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, $onAc2)
        $origVideo2 = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, $onAc2)
        Log ('original standby timeout: {0}s (status 0x{1:X}); display-off timeout: {2}s (status 0x{3:X})' -f $original2[1], $original2[0], $origVideo2[1], $origVideo2[0])
        $t0b = Get-Date
        $request = [IntPtr]::Zero
        try
        {
            [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, 120, $onAc2)
            [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, 120, $onAc2)
            [void][NoSleepProbe]::ApplyScheme($scheme)
            Log 'standby AND display-off timeouts set to 120s.'
            $request = [NoSleepProbe]::CreateRequest('NoSleep S0 diagnostic suite')
            if ($request -eq [IntPtr]::Zero) { throw 'PowerCreateRequest failed' }
            $okExec = [NoSleepProbe]::SetRequest($request, 3)
            Log ('holding PowerSetRequest(ExecutionRequired) for 8 minutes (set result: {0}).' -f $okExec)
            Log 'NOTE: heartbeats may CONTINUE during standby (this request keeps the process alive, per docs) - the verdict relies on Kernel-Power events, not on heartbeat gaps.'
            Log 'DO NOT touch the machine during this phase.'
            Start-Sleep -Seconds 10
            Run-Exe 'powercfg.exe' @('-requests')
            $deadline = (Get-Date).AddSeconds(480)
            while ((Get-Date) -lt $deadline)
            {
                Add-Content -Path $script:Report -Value ('    {0:HH:mm:ss} heartbeat' -f (Get-Date)) -Encoding UTF8
                Start-Sleep -Seconds 5
            }
        }
        catch
        {
            Log ('PHASE 3b FAILED: {0}' -f $_.Exception.ToString())
        }
        finally
        {
            if ($request -ne [IntPtr]::Zero)
            {
                [void][NoSleepProbe]::ClearRequest($request, 3)
                [void][NoSleepProbe]::DestroyRequest($request)
            }
            Log 'request cleared/destroyed.'
            if ($original2[0] -eq 0)
            {
                [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, $original2[1], $onAc2)
                [void][NoSleepProbe]::ApplyScheme($scheme)
                Log ('standby timeout restored to {0}s.' -f $original2[1])
            }
            if ($origVideo2[0] -eq 0)
            {
                [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, $origVideo2[1], $onAc2)
                [void][NoSleepProbe]::ApplyScheme($scheme)
                Log ('display-off timeout restored to {0}s.' -f $origVideo2[1])
            }
        }
        Log 'Kernel-Power events during the window - any sleep entries (Id 42/507) mean ExecutionRequired did NOT prevent standby:'
        Get-PowerEvents $t0b (Get-Date)
    }
}

# ---------------------------------------------------------------- phase 3c: control (no request)

if (Ask 'PHASE 3c: CONTROL - phase 3 skeleton but NO request held. Proves the machine idle-sleeps on its own; without it, phases 3/3b prove nothing. ~7 min. Run')
{
    Section 'PHASE 3c: control - no request, 2-min timeout'
    if ($scheme -eq $null) { Log 'no active scheme - skip' }
    else
    {
        $onAc3 = ($powerStatus.PowerLineStatus -eq 'Online')
        Log ('current power source: {0}' -f $(if ($onAc3) { 'AC' } else { 'BATTERY' }))
        Save-Restore
        $original3 = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, $onAc3)
        $origVideo3 = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, $onAc3)
        Log ('original standby timeout: {0}s; display-off timeout: {1}s' -f $original3[1], $origVideo3[1])
        $t0c = Get-Date
        try
        {
            [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, 120, $onAc3)
            [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, 120, $onAc3)
            [void][NoSleepProbe]::ApplyScheme($scheme)
            Log 'standby AND display-off timeouts set to 120s. NO power request will be held this time.'
            Log 'DO NOT touch the machine. Expected: the screen turns off ~2 min in, the machine sleeps ~2-4 min in and the log below pauses - wake it with a keypress only after the loop finishes (~7 min).'

            $ticksC = New-Object System.Collections.Generic.List[datetime]
            $deadline = (Get-Date).AddSeconds(420)
            while ((Get-Date) -lt $deadline)
            {
                $now = Get-Date
                $ticksC.Add($now)
                Add-Content -Path $script:Report -Value ('    {0:HH:mm:ss} heartbeat' -f $now) -Encoding UTF8
                Start-Sleep -Seconds 5
            }
        }
        catch
        {
            Log ('PHASE 3c FAILED: {0}' -f $_.Exception.ToString())
        }
        finally
        {
            if ($original3[0] -eq 0)
            {
                [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, $original3[1], $onAc3)
                [void][NoSleepProbe]::ApplyScheme($scheme)
                Log ('standby timeout restored to {0}s.' -f $original3[1])
            }
            if ($origVideo3[0] -eq 0)
            {
                [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, $origVideo3[1], $onAc3)
                [void][NoSleepProbe]::ApplyScheme($scheme)
                Log ('display-off timeout restored to {0}s.' -f $origVideo3[1])
            }
        }

        $maxGapC = 0; $gapAtC = $null
        for ($i = 1; $i -lt $ticksC.Count; $i++)
        {
            $gap = ($ticksC[$i] - ($ticksC[$i - 1])).TotalSeconds
            if ($gap -gt $maxGapC) { $maxGapC = $gap; $gapAtC = $ticksC[$i] }
        }
        Log ('largest heartbeat gap: {0:N0}s at {1:HH:mm:ss}' -f $maxGapC, $gapAtC)
        Log ('VERDICT: {0}' -f $(if ($maxGapC -gt 30) { 'CONTROL OK - the machine idle-sleeps on its own, so phases 3/3b "stayed awake" results are meaningful.' } else { 'CONTROL NEGATIVE - the machine did NOT sleep even without any request (idle entry did not trigger). Phase 3/3b "stayed awake" verdicts are INCONCLUSIVE.' }))
        Log 'Kernel-Power events during the window:'
        Get-PowerEvents $t0c (Get-Date)
    }
}

# ---------------------------------------------------------------- phase 3d: re-armed request (app behavior) + lock-free child

if (Ask 'PHASE 3d: RE-ARM the request every 5 s (what the app does) while a SECOND lock-free heartbeat process runs - answers (a) does re-arming defeat the 5-min DC termination, (b) do OTHER apps keep running or does only the request holder survive. 10 min. Run')
{
    Section 'PHASE 3d: re-armed ES request + lock-free child vs sleep timeout'
    if ($scheme -eq $null) { Log 'no active scheme - skip' }
    else
    {
        $onAc4 = ($powerStatus.PowerLineStatus -eq 'Online')
        Log ('current power source: {0}' -f $(if ($onAc4) { 'AC' } else { 'BATTERY' }))
        Save-Restore
        $original4 = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, $onAc4)
        $origVideo4 = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, $onAc4)
        Log ('original standby timeout: {0}s; display-off timeout: {1}s' -f $original4[1], $origVideo4[1])

        # lock-free child process: a second PowerShell that only logs timestamps (no power request),
        # so we can see whether OTHER apps get suspended while the request holder keeps running
        $childPs1 = Join-Path $script:Dir 's0-child-heartbeat.ps1'
        $childLog = Join-Path $script:Dir 'S0-Child-Heartbeat.txt'
        Set-Content -Path $childPs1 -Encoding ASCII -Value @'
param([string]$logPath)
for ($i = 0; $i -lt 140; $i++)
{
    Add-Content -LiteralPath $logPath -Value ('{0:HH:mm:ss.fff} heartbeat' -f (Get-Date))
    Start-Sleep -Seconds 5
}
'@
        Remove-Item $childLog -Force -ErrorAction SilentlyContinue
        $child = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $childPs1, $childLog) -WindowStyle Hidden -PassThru
        Log ('lock-free child process started (pid {0}), logging to S0-Child-Heartbeat.txt' -f $child.Id)

        $t0d = Get-Date
        $tailGapMark = $null
        try
        {
            [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, 120, $onAc4)
            [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, 120, $onAc4)
            [void][NoSleepProbe]::ApplyScheme($scheme)
            Log 'standby AND display-off timeouts set to 120s.'
            Log 'parent holds AND RE-ARMS SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED) every 5 s for 10 minutes; child holds nothing.'
            Log 'DO NOT touch the machine. Screen turns off ~2 min in. Two separate verdicts below (parent = re-arm vs termination; child = do other apps survive).'

            $ticksD = New-Object System.Collections.Generic.List[datetime]
            $deadline = (Get-Date).AddSeconds(600)
            while ((Get-Date) -lt $deadline)
            {
                $now = Get-Date
                [void][NoSleepProbe]::ExecutionState($ES_HOLD_SYSTEM) # re-arm, like the app
                $ticksD.Add($now)
                Add-Content -Path $script:Report -Value ('    {0:HH:mm:ss} heartbeat (parent, request holder)' -f $now) -Encoding UTF8
                Start-Sleep -Seconds 5
            }
        }
        catch
        {
            Log ('PHASE 3d FAILED: {0}' -f $_.Exception.ToString())
        }
        finally
        {
            $tailGapMark = Get-Date
            [void][NoSleepProbe]::ExecutionState($ES_RELEASE)
            Log 'power request released.'
            if ($original4[0] -eq 0)
            {
                [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, $original4[1], $onAc4)
                [void][NoSleepProbe]::ApplyScheme($scheme)
                Log ('standby timeout restored to {0}s.' -f $original4[1])
            }
            if ($origVideo4[0] -eq 0)
            {
                [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, $origVideo4[1], $onAc4)
                [void][NoSleepProbe]::ApplyScheme($scheme)
                Log ('display-off timeout restored to {0}s.' -f $origVideo4[1])
            }
            if (-not $child.HasExited) { Stop-Process -Id $child.Id -Force -ErrorAction SilentlyContinue }
            $child.WaitForExit()
        }

        $maxGapD = 0; $gapAtD = $null
        for ($i = 1; $i -lt $ticksD.Count; $i++)
        {
            $gap = ($ticksD[$i] - ($ticksD[$i - 1])).TotalSeconds
            if ($gap -gt $maxGapD) { $maxGapD = $gap; $gapAtD = $ticksD[$i] }
        }
        # the loop can exit while suspended mid-iteration (the pass-3 battery run hid a
        # 3-minute parent suspension this way) - count the tail gap from the last tick
        if (($ticksD.Count -gt 0) -and ($null -ne $tailGapMark))
        {
            $tailGap = ($tailGapMark - $ticksD[$ticksD.Count - 1]).TotalSeconds
            if ($tailGap -gt 30) { Log ('NOTE: tail gap of {0:N0}s after the last tick - the parent was suspended at phase end (counted into the verdict).' -f $tailGap) }
            if ($tailGap -gt $maxGapD) { $maxGapD = $tailGap; $gapAtD = $ticksD[$ticksD.Count - 1] }
        }
        Log ('PARENT (request holder): largest heartbeat gap {0:N0}s at {1:HH:mm:ss}' -f $maxGapD, $gapAtD)

        $childTicks = New-Object System.Collections.Generic.List[datetime]
        if (Test-Path $childLog)
        {
            foreach ($line in (Get-Content $childLog))
            {
                $stamp = ($line -split ' ')[0]
                $parsed = [datetime]::MinValue
                if ([datetime]::TryParseExact($stamp, 'HH:mm:ss.fff', $null, [System.Globalization.DateTimeStyles]::None, [ref]$parsed))
                { $childTicks.Add($parsed) }
            }
        }
        if ($childTicks.Count -lt 2)
        {
            Log 'CHILD (lock-free): no usable heartbeat lines - inconclusive.'
        }
        else
        {
            $maxGapE = 0; $gapAtE = $null
            for ($i = 1; $i -lt $childTicks.Count; $i++)
            {
                $gap = ($childTicks[$i] - ($childTicks[$i - 1])).TotalSeconds
                if ($gap -gt $maxGapE) { $maxGapE = $gap; $gapAtE = $childTicks[$i] }
            }
            Log ('CHILD (lock-free, no request): {0} lines, largest heartbeat gap {1:N0}s at {2:HH:mm:ss}' -f $childTicks.Count, $maxGapE, $gapAtE)
            Log 'child heartbeat lines (gaps > 30 s mean the child was suspended by the system):'
            foreach ($t in $childTicks) { Log ('    {0:HH:mm:ss.fff}' -f $t) }

            Log 'VERDICT matrix:'
            if (($maxGapD -le 30) -and ($maxGapE -le 30)) { Log '  parent alive AND child alive: re-arm defeats the termination AND the whole system keeps running (system-wide prevention - best case).' }
            elseif (($maxGapD -le 30) -and ($maxGapE -gt 30)) { Log '  parent alive BUT child suspended: per-app model confirmed - the request exempts only its holder; OTHER apps (music players!) still get suspended. ES requests cannot serve NoSleep''s purpose on battery S0; lid-override/jiggle become the primary mechanisms.' }
            elseif (($maxGapD -gt 30) -and ($maxGapE -le 30)) { Log '  parent slept but child alive - unexpected, inspect events.' }
            else { Log '  both slept: re-arm does NOT defeat the 5-min termination (phase-3 cliff stands even with re-arming).' }
        }

        Log 'Kernel-Power events during the window:'
        Get-PowerEvents $t0d (Get-Date)
        Remove-Item $childPs1, $childLog -Force -ErrorAction SilentlyContinue
    }
}

# ---------------------------------------------------------------- phase 3e: one-shot pings (no persistent claim)

if (Ask 'PHASE 3e: ONE-SHOT pings - SetThreadExecutionState(ES_SYSTEM_REQUIRED) WITHOUT ES_CONTINUOUS every 5 s (each = a single idle-timer reset, not a persistent power request) + lock-free child. Battery question: do idle-timer resets defer the 5-min cliff? 10 min. Run')
{
    Section 'PHASE 3e: one-shot idle-timer pings vs sleep timeout'
    if ($scheme -eq $null) { Log 'no active scheme - skip' }
    else
    {
        $onAc5 = ($powerStatus.PowerLineStatus -eq 'Online')
        Log ('current power source: {0}' -f $(if ($onAc5) { 'AC' } else { 'BATTERY' }))
        Save-Restore
        $original5 = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, $onAc5)
        $origVideo5 = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, $onAc5)
        Log ('original standby timeout: {0}s; display-off timeout: {1}s' -f $original5[1], $origVideo5[1])

        $childPs1 = Join-Path $script:Dir 's0-child-heartbeat.ps1'
        $childLog = Join-Path $script:Dir 'S0-Child-Heartbeat.txt'
        Set-Content -Path $childPs1 -Encoding ASCII -Value @'
param([string]$logPath)
for ($i = 0; $i -lt 140; $i++)
{
    Add-Content -LiteralPath $logPath -Value ('{0:HH:mm:ss.fff} heartbeat' -f (Get-Date))
    Start-Sleep -Seconds 5
}
'@
        Remove-Item $childLog -Force -ErrorAction SilentlyContinue
        $child5 = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $childPs1, $childLog) -WindowStyle Hidden -PassThru
        Log ('lock-free child process started (pid {0})' -f $child5.Id)

        $t0e = Get-Date
        $tailGapMark5 = $null
        try
        {
            [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, 120, $onAc5)
            [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, 120, $onAc5)
            [void][NoSleepProbe]::ApplyScheme($scheme)
            Log 'standby AND display-off timeouts set to 120s.'
            Log 'parent sends ONE-SHOT ES_SYSTEM_REQUIRED pings every 5 s (NO persistent claim); child holds nothing.'
            Log 'DO NOT touch the machine. Screen turns off ~2 min in. PASS = everything keeps running for the full 10 min, past the ~7 min mark.'

            $ticksE = New-Object System.Collections.Generic.List[datetime]
            $deadline = (Get-Date).AddSeconds(600)
            while ((Get-Date) -lt $deadline)
            {
                $now = Get-Date
                [void][NoSleepProbe]::ExecutionState($ES_PING_SYSTEM) # one-shot idle-timer reset
                $ticksE.Add($now)
                Add-Content -Path $script:Report -Value ('    {0:HH:mm:ss} heartbeat (parent, one-shot pings)' -f $now) -Encoding UTF8
                Start-Sleep -Seconds 5
            }
        }
        catch
        {
            Log ('PHASE 3e FAILED: {0}' -f $_.Exception.ToString())
        }
        finally
        {
            $tailGapMark5 = Get-Date
            [void][NoSleepProbe]::ExecutionState($ES_RELEASE) # hygiene; nothing persistent was claimed
            Log 'done pinging.'
            if ($original5[0] -eq 0)
            {
                [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, $original5[1], $onAc5)
                [void][NoSleepProbe]::ApplyScheme($scheme)
                Log ('standby timeout restored to {0}s.' -f $original5[1])
            }
            if ($origVideo5[0] -eq 0)
            {
                [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, $origVideo5[1], $onAc5)
                [void][NoSleepProbe]::ApplyScheme($scheme)
                Log ('display-off timeout restored to {0}s.' -f $origVideo5[1])
            }
            if (-not $child5.HasExited) { Stop-Process -Id $child5.Id -Force -ErrorAction SilentlyContinue }
            $child5.WaitForExit()
        }

        $maxGapE = 0; $gapAtE = $null
        for ($i = 1; $i -lt $ticksE.Count; $i++)
        {
            $gap = ($ticksE[$i] - ($ticksE[$i - 1])).TotalSeconds
            if ($gap -gt $maxGapE) { $maxGapE = $gap; $gapAtE = $ticksE[$i] }
        }
        if (($ticksE.Count -gt 0) -and ($null -ne $tailGapMark5))
        {
            $tailGap5 = ($tailGapMark5 - $ticksE[$ticksE.Count - 1]).TotalSeconds
            if ($tailGap5 -gt 30) { Log ('NOTE: tail gap of {0:N0}s after the last tick - the parent was suspended at phase end.' -f $tailGap5) }
            if ($tailGap5 -gt $maxGapE) { $maxGapE = $tailGap5; $gapAtE = $ticksE[$ticksE.Count - 1] }
        }
        Log ('PARENT (one-shot pings): largest heartbeat gap {0:N0}s at {1:HH:mm:ss}' -f $maxGapE, $gapAtE)

        $childTicks5 = New-Object System.Collections.Generic.List[datetime]
        if (Test-Path $childLog)
        {
            foreach ($line in (Get-Content $childLog))
            {
                $stamp = ($line -split ' ')[0]
                $parsed = [datetime]::MinValue
                if ([datetime]::TryParseExact($stamp, 'HH:mm:ss.fff', $null, [System.Globalization.DateTimeStyles]::None, [ref]$parsed))
                { $childTicks5.Add($parsed) }
            }
        }
        if ($childTicks5.Count -lt 2)
        {
            Log 'CHILD (lock-free): no usable heartbeat lines - inconclusive.'
        }
        else
        {
            $maxGapC5 = 0; $gapAtC5 = $null
            for ($i = 1; $i -lt $childTicks5.Count; $i++)
            {
                $gap = ($childTicks5[$i] - ($childTicks5[$i - 1])).TotalSeconds
                if ($gap -gt $maxGapC5) { $maxGapC5 = $gap; $gapAtC5 = $childTicks5[$i] }
            }
            Log ('CHILD (lock-free, no request): {0} lines, largest heartbeat gap {1:N0}s at {2:HH:mm:ss}' -f $childTicks5.Count, $maxGapC5, $gapAtC5)
            Log 'VERDICT matrix:'
            if (($maxGapE -le 30) -and ($maxGapC5 -le 30)) { Log '  one-shot idle-timer pings HOLD the system awake on battery past the termination cliff (past the ~7 min mark) - zero-side-effect battery solution, better than jiggle.' }
            elseif (($maxGapE -le 30) -and ($maxGapC5 -gt 30)) { Log '  parent alive but child suspended - unexpected for one-shot pings, inspect events.' }
            else { Log '  one-shot pings DO NOT defer Modern Standby entry on battery (system slept) - the cliff stands; jiggle remains the only battery mechanism.' }
        }

        Log 'Kernel-Power events during the window:'
        Get-PowerEvents $t0e (Get-Date)
        Remove-Item $childPs1, $childLog -Force -ErrorAction SilentlyContinue
    }
}

# ---------------------------------------------------------------- phase 3f: cycled persistent claim (release + re-claim)

if (Ask 'PHASE 3f: CYCLED persistent claim - release (lone ES_CONTINUOUS) then immediately re-claim (ES_CONTINUOUS|ES_SYSTEM_REQUIRED) every 5 s, forcing a genuinely NEW power request each time (unlike repeated identical claims, which may be no-ops). + lock-free child. 10 min. Run')
{
    Section 'PHASE 3f: cycled persistent claim vs sleep timeout'
    if ($scheme -eq $null) { Log 'no active scheme - skip' }
    else
    {
        $onAc6 = ($powerStatus.PowerLineStatus -eq 'Online')
        Log ('current power source: {0}' -f $(if ($onAc6) { 'AC' } else { 'BATTERY' }))
        Save-Restore
        $original6 = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, $onAc6)
        $origVideo6 = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, $onAc6)
        Log ('original standby timeout: {0}s; display-off timeout: {1}s' -f $original6[1], $origVideo6[1])

        $childPs1 = Join-Path $script:Dir 's0-child-heartbeat.ps1'
        $childLog = Join-Path $script:Dir 'S0-Child-Heartbeat.txt'
        Set-Content -Path $childPs1 -Encoding ASCII -Value @'
param([string]$logPath)
for ($i = 0; $i -lt 140; $i++)
{
    Add-Content -LiteralPath $logPath -Value ('{0:HH:mm:ss.fff} heartbeat' -f (Get-Date))
    Start-Sleep -Seconds 5
}
'@
        Remove-Item $childLog -Force -ErrorAction SilentlyContinue
        $child6 = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $childPs1, $childLog) -WindowStyle Hidden -PassThru
        Log ('lock-free child process started (pid {0})' -f $child6.Id)

        $t0f = Get-Date
        $tailGapMark6 = $null
        try
        {
            [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, 120, $onAc6)
            [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, 120, $onAc6)
            [void][NoSleepProbe]::ApplyScheme($scheme)
            Log 'standby AND display-off timeouts set to 120s.'
            Log 'parent CYCLES the persistent claim every 5 s: release, then re-claim (a fresh power request each cycle); child holds nothing.'
            Log 'DO NOT touch the machine. Screen turns off ~2 min in.'

            $ticksF = New-Object System.Collections.Generic.List[datetime]
            $deadline = (Get-Date).AddSeconds(600)
            while ((Get-Date) -lt $deadline)
            {
                $now = Get-Date
                [void][NoSleepProbe]::ExecutionState($ES_RELEASE)   # clear the persistent claim...
                [void][NoSleepProbe]::ExecutionState($ES_HOLD_SYSTEM) # ...and immediately make a fresh one
                $ticksF.Add($now)
                Add-Content -Path $script:Report -Value ('    {0:HH:mm:ss} heartbeat (parent, cycled claim)' -f $now) -Encoding UTF8
                Start-Sleep -Seconds 5
            }
        }
        catch
        {
            Log ('PHASE 3f FAILED: {0}' -f $_.Exception.ToString())
        }
        finally
        {
            $tailGapMark6 = Get-Date
            [void][NoSleepProbe]::ExecutionState($ES_RELEASE)
            Log 'persistent claim released.'
            if ($original6[0] -eq 0)
            {
                [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_SLEEP, $GUID_STANDBYIDLE, $original6[1], $onAc6)
                [void][NoSleepProbe]::ApplyScheme($scheme)
                Log ('standby timeout restored to {0}s.' -f $original6[1])
            }
            if ($origVideo6[0] -eq 0)
            {
                [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_VIDEO, $GUID_VIDEOIDLE, $origVideo6[1], $onAc6)
                [void][NoSleepProbe]::ApplyScheme($scheme)
                Log ('display-off timeout restored to {0}s.' -f $origVideo6[1])
            }
            if (-not $child6.HasExited) { Stop-Process -Id $child6.Id -Force -ErrorAction SilentlyContinue }
            $child6.WaitForExit()
        }

        $maxGapF = 0; $gapAtF = $null
        for ($i = 1; $i -lt $ticksF.Count; $i++)
        {
            $gap = ($ticksF[$i] - ($ticksF[$i - 1])).TotalSeconds
            if ($gap -gt $maxGapF) { $maxGapF = $gap; $gapAtF = $ticksF[$i] }
        }
        if (($ticksF.Count -gt 0) -and ($null -ne $tailGapMark6))
        {
            $tailGap6 = ($tailGapMark6 - $ticksF[$ticksF.Count - 1]).TotalSeconds
            if ($tailGap6 -gt 30) { Log ('NOTE: tail gap of {0:N0}s after the last tick - the parent was suspended at phase end.' -f $tailGap6) }
            if ($tailGap6 -gt $maxGapF) { $maxGapF = $tailGap6; $gapAtF = $ticksF[$ticksF.Count - 1] }
        }
        Log ('PARENT (cycled claim): largest heartbeat gap {0:N0}s at {1:HH:mm:ss}' -f $maxGapF, $gapAtF)

        $childTicks6 = New-Object System.Collections.Generic.List[datetime]
        if (Test-Path $childLog)
        {
            foreach ($line in (Get-Content $childLog))
            {
                $stamp = ($line -split ' ')[0]
                $parsed = [datetime]::MinValue
                if ([datetime]::TryParseExact($stamp, 'HH:mm:ss.fff', $null, [System.Globalization.DateTimeStyles]::None, [ref]$parsed))
                { $childTicks6.Add($parsed) }
            }
        }
        if ($childTicks6.Count -lt 2)
        {
            Log 'CHILD (lock-free): no usable heartbeat lines - inconclusive.'
        }
        else
        {
            $maxGapC6 = 0; $gapAtC6 = $null
            for ($i = 1; $i -lt $childTicks6.Count; $i++)
            {
                $gap = ($childTicks6[$i] - ($childTicks6[$i - 1])).TotalSeconds
                if ($gap -gt $maxGapC6) { $maxGapC6 = $gap; $gapAtC6 = $childTicks6[$i] }
            }
            Log ('CHILD (lock-free, no request): {0} lines, largest heartbeat gap {1:N0}s at {2:HH:mm:ss}' -f $childTicks6.Count, $maxGapC6, $gapAtC6)
            Log 'VERDICT matrix:'
            if (($maxGapF -le 30) -and ($maxGapC6 -le 30)) { Log '  CYCLED claims HOLD the system awake past the cliff - the termination is a one-time sweep that a fresh request survives; NoSleep should cycle its claim.' }
            elseif (($maxGapF -le 30) -and ($maxGapC6 -gt 30)) { Log '  parent alive but child suspended - unexpected, inspect events.' }
            else { Log '  CYCLED claims also die at the cliff - the termination sweeps ALL requests at timeout+5min regardless of age; persistent claims are dead on battery S0.' }
        }

        Log 'Kernel-Power events during the window:'
        Get-PowerEvents $t0f (Get-Date)
        Remove-Item $childPs1, $childLog -Force -ErrorAction SilentlyContinue
    }
}

# ---------------------------------------------------------------- phase 4: lid close A/B

$lidPresent = ($cap -ne $null) -and ($cap[2] -ne 0)
if (-not $lidPresent)
{
    Section 'PHASE 4+5+6: skipped - no lid reported by GetPwrCapabilities'
}
elseif (Ask 'PHASE 4: lid-close A/B test (baseline sleep vs LIDACTION=Do nothing). Needs you to close/open the lid twice, ~2 min. Run')
{
    Section 'PHASE 4: lid-close A/B'
    if ($scheme -eq $null) { Log 'no active scheme - skip' }
    else
    {
        $origAc = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, $true)
        $origDc = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, $false)
        $startAction = [Math]::Max($origAc[1], $origDc[1])
        if ($startAction -eq 0)
        {
            Log ('current lid action is already "Do nothing" (AC={0}, DC={1}) - baseline would not sleep, test is meaningless. Skipping.' -f $origAc[1], $origDc[1])
        }
        else
        {
            Log ('current lid action: AC={0}, DC={1} (1=sleep). Baseline first, then with override.' -f $origAc[1], $origDc[1])
            Save-Restore
            try
            {
                Log ''
                Log '--- A: BASELINE (lid action = current, expect a sleep/wake pair) ---'
                $t0 = Get-Date
                [void](Read-Host 'Press ENTER, then CLOSE the lid, wait ~45 s, OPEN the lid, then come back and wait for the prompt')
                Start-Sleep -Seconds 90
                [void](Read-Host 'Lid reopened? Press ENTER to record events')
                Log 'events in window A:'
                Get-PowerEvents $t0.AddSeconds(-5) (Get-Date)

                Log ''
                Log '--- B: LIDACTION=0 override (expect NO sleep) ---'
                [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, 0, $true)
                [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, 0, $false)
                [void][NoSleepProbe]::ApplyScheme($scheme)
                Log 'lid action set to "Do nothing" (AC+DC) and applied.'
                $t1 = Get-Date
                [void](Read-Host 'Press ENTER, then CLOSE the lid, wait ~45 s, OPEN the lid, then come back and wait for the prompt')
                Start-Sleep -Seconds 90
                [void](Read-Host 'Lid reopened? Press ENTER to record events')
                Log 'events in window B:'
                Get-PowerEvents $t1.AddSeconds(-5) (Get-Date)
                Log 'INTERPRETATION: window B has no Kernel-Power 42/507 (sleep) entries while window A does => the lid-override approach works.'
            }
            catch
            {
                Log ('PHASE 4 FAILED: {0}' -f $_.Exception.ToString())
            }
            finally
            {
                if ($origAc[0] -eq 0) { [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, $origAc[1], $true) }
                if ($origDc[0] -eq 0) { [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, $origDc[1], $false) }
                [void][NoSleepProbe]::ApplyScheme($scheme)
                Log 'original lid action restored.'
            }
        }
    }
}

# ---------------------------------------------------------------- phase 5: jiggle vs standby

if ($lidPresent -and (Ask 'PHASE 5: simulated mouse input (1 px jiggle every second) across a lid close - does it keep the system out of standby'))
{
    Section 'PHASE 5: SendInput jiggle vs lid close'
    if ($scheme -eq $null) { Log 'no active scheme - skip' }
    else
    {
        $origAc = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, $true)
        $origDc = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, $false)
        if (([Math]::Max($origAc[1], $origDc[1])) -eq 0)
        {
            Log 'lid action is already "Do nothing" - the test needs lid=sleep. Skipping.'
        }
        else
        {
            Save-Restore
            Log 'jiggling 1 px every second for 2 minutes. The on-screen cursor will wobble.'
            $t0 = Get-Date
            $dirRight = $true
            try
            {
                [void](Read-Host 'Press ENTER, then CLOSE the lid, wait ~60 s, OPEN the lid, then wait for the prompt')
                $deadline = (Get-Date).AddSeconds(120)
                while ((Get-Date) -lt $deadline)
                {
                    $dx = $(if ($dirRight) { 1 } else { -1 })
                    [void][NoSleepProbe]::Jiggle($dx, 0)
                    Add-Content -Path $script:Report -Value ('    {0:HH:mm:ss} jiggle' -f (Get-Date)) -Encoding UTF8
                    $dirRight = -not $dirRight
                    Start-Sleep -Seconds 1
                }
            }
            catch
            {
                Log ('PHASE 5 FAILED: {0}' -f $_.Exception.ToString())
            }
            finally
            {
                if ($origAc[0] -eq 0) { [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, $origAc[1], $true) }
                if ($origDc[0] -eq 0) { [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, $origDc[1], $false) }
                [void][NoSleepProbe]::ApplyScheme($scheme)
                Log 'original lid action restored.'
            }
            Log 'events in window:'
            Get-PowerEvents $t0.AddSeconds(-5) (Get-Date)
            Log 'INTERPRETATION: jiggle lines pause (system suspended us) or sleep events appear => jiggle loses; continuous jiggle lines and no sleep => jiggle works.'
        }
    }
}

# ---------------------------------------------------------------- phase 6a: event-driven jiggle

if ($lidPresent -and (Ask 'PHASE 6a: event-driven jiggle - close the lid when told to; a 10-move burst fires the moment the notification arrives. ~4 min. Run'))
{
    Section 'PHASE 6a: event-driven jiggle via lid-switch notification (auto-burst on close)'
    $kit = $null
    try
    {
        $kit = New-Object -TypeName 'NoSleepKit.PowerNotifyWindow' -ArgumentList $true
        $ready = $false
        for ($i = 0; $i -lt 50 -and -not $ready; $i++)
        {
            Start-Sleep -Milliseconds 100
            [string]$peek = ''
            while ($kit.Queue.TryDequeue([ref]$peek)) { if ($peek -like '*window-ready*') { $ready = $true }; Log $peek; $peek = '' }
        }
        if (-not $ready)
        {
            Log 'notification window did not become ready - phase 6a inconclusive.'
        }
        else
        {
            Log 'CLOSE THE LID NOW - within the next 3 minutes (the burst fires the moment the notification arrives)...'
            $t6 = Get-Date
            $sawLidClosed = $false
            $deadline6 = (Get-Date).AddSeconds(180)
            while ((Get-Date) -lt $deadline6 -and -not $sawLidClosed)
            {
                [string]$peek = ''
                while ($kit.Queue.TryDequeue([ref]$peek))
                {
                    if ($peek -like '*LID_CLOSED*') { $sawLidClosed = $true }
                    Log $peek
                    $peek = ''
                }
                Start-Sleep -Milliseconds 200
            }
            if (-not $sawLidClosed)
            {
                Log 'RESULT: no LID_CLOSED notification within 3 min - either the lid was never closed, or notifications do not reach us (raw PBT(...)/SETTING lines above; if completely empty, it is a delivery problem).'
            }
            else
            {
                Log 'LID_CLOSED received (burst fired automatically). Keep the lid CLOSED - observing for 30 s...'
                $deadline6b = (Get-Date).AddSeconds(30)
                while ((Get-Date) -lt $deadline6b)
                {
                    [string]$peek = ''
                    while ($kit.Queue.TryDequeue([ref]$peek)) { Log $peek; $peek = '' }
                    Start-Sleep -Milliseconds 200
                }
                [void](Read-Host 'Now OPEN the lid, then press ENTER')
                Start-Sleep -Seconds 2
                [string]$peek = ''
                while ($kit.Queue.TryDequeue([ref]$peek)) { Log $peek; $peek = '' }
            }
            Log 'Kernel-Power events in the window:'
            Get-PowerEvents $t6.AddSeconds(-5) (Get-Date)
            Log 'INTERPRETATION: LID_CLOSED before any 506 = notification arrives pre-suspend (no DAM race); 507 within ~1 s of 506 = the burst woke the system.'
        }
    }
    catch
    {
        Log ('PHASE 6a FAILED: {0}' -f $_.Exception.ToString())
    }
    finally
    {
        if ($kit -ne $null) { $kit.Dispose() }
    }
}

# ---------------------------------------------------------------- phase 6b: override + display-off

if ($lidPresent -and (Ask 'PHASE 6b: with LIDACTION=0 - which display-off signals arrive on lid close (the "screen off, laptop running" goal). ~4 min. Run'))
{
    Section 'PHASE 6b: lid override + display-off observation'
    if ($scheme -eq $null) { Log 'no active scheme - skip' }
    else
    {
        $origAc6 = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, $true)
        $origDc6 = [NoSleepProbe]::ReadValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, $false)
        Save-Restore
        $kit6 = $null
        try
        {
            [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, 0, $true)
            [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, 0, $false)
            [void][NoSleepProbe]::ApplyScheme($scheme)
            Log 'lid action = Do nothing (AC+DC). No auto-jiggle this time (observation only).'

            $kit6 = New-Object -TypeName 'NoSleepKit.PowerNotifyWindow' -ArgumentList $false
            $ready6 = $false
            for ($i = 0; $i -lt 50 -and -not $ready6; $i++)
            {
                Start-Sleep -Milliseconds 100
                [string]$peek = ''
                while ($kit6.Queue.TryDequeue([ref]$peek)) { if ($peek -like '*window-ready*') { $ready6 = $true }; Log $peek; $peek = '' }
            }
            if (-not $ready6) { throw 'notification window did not become ready' }

            Log 'CLOSE THE LID NOW - within the next 3 minutes...'
            $t6b = Get-Date
            $sawLid6 = $false
            $deadline6c = (Get-Date).AddSeconds(180)
            while ((Get-Date) -lt $deadline6c -and -not $sawLid6)
            {
                [string]$peek = ''
                while ($kit6.Queue.TryDequeue([ref]$peek))
                {
                    if ($peek -like '*LID_CLOSED*') { $sawLid6 = $true }
                    Log $peek
                    $peek = ''
                }
                Start-Sleep -Milliseconds 200
            }

            if (-not $sawLid6)
            {
                Log 'no lid notification within 3 min - phase 6b display observation inconclusive (see raw lines above).'
            }
            else
            {
                Log 'LID_CLOSED received - keeping it closed for 30 s, watching for display-off signals...'
                $sawDisplayOff6 = $false
                $deadline6d = (Get-Date).AddSeconds(30)
                while ((Get-Date) -lt $deadline6d)
                {
                    [string]$peek = ''
                    while ($kit6.Queue.TryDequeue([ref]$peek))
                    {
                        if ($peek -like '*DISPLAY_OFF*' -or $peek -like '*SESSION_DISPLAY_OFF*' -or $peek -like '*MONITOR_OFF*') { $sawDisplayOff6 = $true }
                        Log $peek
                        $peek = ''
                    }
                    Start-Sleep -Milliseconds 200
                }
                [void](Read-Host 'Now OPEN the lid, then press ENTER')
                Start-Sleep -Seconds 2
                [string]$peek = ''
                while ($kit6.Queue.TryDequeue([ref]$peek)) { Log $peek; $peek = '' }

                if ($sawDisplayOff6)
                {
                    Log 'RESULT: a display-off signal arrived while the lid was closed - the screen turns off by itself with the override active.'
                }
                else
                {
                    Log 'RESULT: NO display-off signal while the lid was closed - the panel either stays on until the display timeout, or the signals do not reach us (check raw lines above).'
                }
            }

            Log 'Kernel-Power events in the window (expect NO 42/506 = system kept running):'
            Get-PowerEvents $t6b.AddSeconds(-5) (Get-Date)
        }
        catch
        {
            Log ('PHASE 6b FAILED: {0}' -f $_.Exception.ToString())
        }
        finally
        {
            if ($origAc6[0] -eq 0) { [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, $origAc6[1], $true) }
            if ($origDc6[0] -eq 0) { [void][NoSleepProbe]::WriteValue($scheme, $GUID_SUB_BUTTONS, $GUID_LIDACTION, $origDc6[1], $false) }
            [void][NoSleepProbe]::ApplyScheme($scheme)
            Log 'original lid action restored.'
            if ($kit6 -ne $null) { $kit6.Dispose() }
        }
    }
}

# ---------------------------------------------------------------- wrap up

Restore-And-Clean
Section 'RUN END'
if (Test-Path $script:RestoreFile)
{
    Log 'WARNING: S0-Restore.txt still exists - run this script once more to restore settings.'
}
else
{
    Log 'All mutated settings restored (or nothing was mutated). Safe to copy the reports out.'
}
Log ('report written to {0}' -f $script:Report)
Write-Host ''
Write-Host ('Done. Report: {0}' -f $script:Report)
if (-not $isAdmin)
{
    Write-Host 'Now run the SAME command in an ELEVATED PowerShell for the admin-role report, then take both files.'
}
