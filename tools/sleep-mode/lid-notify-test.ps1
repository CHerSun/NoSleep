<#
    Lid/power notification delivery probe (standalone, read-only, no settings are touched).

    Answers ONE question: do lid-switch / display / suspend notifications reach a normal
    user process on THIS machine, via (1) hidden window + RegisterPowerSettingNotification
    or (2) the powrprof callback APIs (PowerSettingRegisterNotification /
    PowerRegisterSuspendResumeNotification)?  Win11 build 26200 test hardware ignored
    path (1) completely - path (2) is the candidate mechanism for NoSleep's S0 features.

    USAGE: run it, close the lid when told, wait ~10 s, open it. ~2 minutes total.
        powershell -NoProfile -ExecutionPolicy Bypass -File .\lid-notify-test.ps1
#>
$ErrorActionPreference = 'Continue'

# self-verification banner - compare against the values published with this file
$self = Get-Item -LiteralPath $MyInvocation.MyCommand.Path
$hash = (Get-FileHash -LiteralPath $self.FullName -Algorithm SHA256).Hash
Write-Host ('lid-notify-test: {0} bytes, sha256 {1}' -f $self.Length, $hash.Substring(0, 16))

$src = @'
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace LidNotifyProbe
{
    public sealed class Probe : IDisposable
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

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private delegate uint PowerCallbackDelegate(IntPtr context, uint type, IntPtr setting);

        [StructLayout(LayoutKind.Sequential)]
        private struct SUBSCRIBE_PARAMETERS
        {
            public IntPtr Callback;
            public IntPtr Context;
        }

        private const uint WM_POWERBROADCAST = 0x218;
        private const uint WM_CLOSE = 0x10;

        private static readonly Guid LidGuid = new Guid("BA3E0F4D-B817-4094-A2D1-D56379E6A0F3");
        private static readonly Guid SessionDisplayGuid = new Guid("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5");

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

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerSettingRegisterNotification(IntPtr settingGuid, uint registrationType, IntPtr recipient, out IntPtr registrationHandle);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerRegisterSuspendResumeNotification(uint flags, IntPtr recipient, out IntPtr registrationHandle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private static readonly WndProcDelegate Proc = new WndProcDelegate(StaticWndProc);
        // STATIC rooting: the OS holds the callback pointer for the registration's lifetime;
        // an instance-only reference gets GC'd after the script scope ends and the next
        // notification crashes the process on a dead delegate (observed on test hardware)
        private static readonly PowerCallbackDelegate CbStatic = new PowerCallbackDelegate(CallbackRouter);
        private static Probe Active;
        private static bool ClassRegistered;
        private const string ClassName = "LidNotifyProbeWnd";

        private readonly System.Collections.Concurrent.ConcurrentQueue<string> queue = new System.Collections.Concurrent.ConcurrentQueue<string>();
        private readonly Thread thread;
        private IntPtr hWnd = IntPtr.Zero;
        private IntPtr wLid = IntPtr.Zero;
        private IntPtr wDisp = IntPtr.Zero;
        private IntPtr cbLid = IntPtr.Zero;
        private IntPtr cbDisp = IntPtr.Zero;
        private IntPtr cbSuspend = IntPtr.Zero;

        [ThreadStatic]
        private static bool cbMode;

        public Probe()
        {
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
                RegisterClass(ref wc);
                ClassRegistered = true;
            }
            hWnd = CreateWindowEx(0, ClassName, "lid-notify-probe", 0x80000000, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, inst, IntPtr.Zero);
            if (hWnd == IntPtr.Zero)
            {
                Push("window-create FAILED, err=" + Marshal.GetLastWin32Error());
                return;
            }
            Guid lid = LidGuid;
            Guid disp = SessionDisplayGuid;
            wLid = RegisterPowerSettingNotification(hWnd, ref lid, 0);
            wDisp = RegisterPowerSettingNotification(hWnd, ref disp, 0);
            Push("window path ready (lid=" + wLid + ", session-display=" + wDisp + ")");

            uint f1 = RegisterCb(LidGuid, out cbLid);
            uint f2 = RegisterCb(SessionDisplayGuid, out cbDisp);
            uint f3 = RegisterSuspendCb(out cbSuspend);
            Push("callback path ready (lid flags=" + f1 + ", session-display flags=" + f2 + ", suspend flags=" + f3 + "; 0 = failed)");
            Push("probe-ready");

            MSG m;
            while (GetMessage(out m, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref m);
                DispatchMessage(ref m);
            }
        }

        private static uint CallbackRouter(IntPtr context, uint type, IntPtr setting)
        {
            Probe self = Active;
            if (self != null)
                self.CallbackBody(type, setting);
            return 0;
        }

        private void CallbackBody(uint type, IntPtr setting)
        {
            cbMode = true;
            try { Handle((int)type, setting); }
            finally { cbMode = false; }
        }

        private static IntPtr StaticWndProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            Probe self = Active;
            if (self != null)
                return self.WndProc(h, msg, w, l);
            return DefWindowProc(h, msg, w, l);
        }

        private IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            if (msg == WM_POWERBROADCAST)
            {
                Handle(w.ToInt32(), l);
                return IntPtr.Zero;
            }
            if (msg == WM_CLOSE)
            {
                if (wLid != IntPtr.Zero) { UnregisterPowerSettingNotification(wLid); wLid = IntPtr.Zero; }
                if (wDisp != IntPtr.Zero) { UnregisterPowerSettingNotification(wDisp); wDisp = IntPtr.Zero; }
                // cb handles deliberately not unregistered (unregister-while-in-flight crashed in testing)
                DestroyWindow(h);
                PostQuitMessage(0);
                return IntPtr.Zero;
            }
            return DefWindowProc(h, msg, w, l);
        }

        private void Handle(int reason, IntPtr setting)
        {
            if (reason == 4) { Push("APMSUSPEND"); }
            else if (reason == 7 || reason == 18) { Push("APMRESUME(" + reason + ")"); }
            else if (reason == 0x8013)
            {
                try
                {
                    byte[] buf = new byte[24];
                    Marshal.Copy(setting, buf, 0, 24);
                    byte[] gb = new byte[16];
                    Array.Copy(buf, 0, gb, 0, 16);
                    Guid g = new Guid(gb);
                    byte data = buf[20];
                    if (g == LidGuid)
                    {
                        if (data == 0) Push("LID_CLOSED");
                        else Push("LID_OPEN");
                    }
                    else if (g == SessionDisplayGuid)
                    {
                        if (data == 0) Push("SESSION_DISPLAY_OFF");
                        else if (data == 1) Push("SESSION_DISPLAY_ON");
                        else Push("SESSION_DISPLAY_DIMMED(" + data + ")");
                    }
                    else
                    {
                        Push("SETTING " + g + " data=" + data);
                    }
                }
                catch (Exception ex) { Push("setting parse error: " + ex.Message); }
            }
            else
            {
                Push("PBT(" + reason + ")");
            }
        }

        private uint RegisterCb(Guid g, out IntPtr handle)
        {
            IntPtr guidPtr = Marshal.AllocHGlobal(16);
            Marshal.Copy(g.ToByteArray(), 0, guidPtr, 16);
            IntPtr pars = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(SUBSCRIBE_PARAMETERS)));
            try
            {
                SUBSCRIBE_PARAMETERS p = new SUBSCRIBE_PARAMETERS();
                p.Callback = Marshal.GetFunctionPointerForDelegate(CbStatic);
                p.Context = IntPtr.Zero;
                Marshal.StructureToPtr(p, pars, false);
                uint[] candidates = new uint[] { 2, 0 };
                foreach (uint f in candidates)
                {
                    IntPtr h;
                    if (PowerSettingRegisterNotification(guidPtr, f, pars, out h) == 0)
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

        private uint RegisterSuspendCb(out IntPtr handle)
        {
            IntPtr pars = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(SUBSCRIBE_PARAMETERS)));
            try
            {
                SUBSCRIBE_PARAMETERS p = new SUBSCRIBE_PARAMETERS();
                p.Callback = Marshal.GetFunctionPointerForDelegate(CbStatic);
                p.Context = IntPtr.Zero;
                Marshal.StructureToPtr(p, pars, false);
                uint[] candidates = new uint[] { 2, 0 };
                foreach (uint f in candidates)
                {
                    IntPtr h;
                    if (PowerRegisterSuspendResumeNotification(f, pars, out h) == 0)
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
                Marshal.FreeHGlobal(pars);
            }
        }

        public void Dispose()
        {
            if (hWnd != IntPtr.Zero)
            {
                PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                thread.Join(3000);
            }
            // NOTE: Active is deliberately NOT nulled and callback registrations are
            // deliberately NOT unregistered - the delegate/instance stay rooted for the
            // life of the process (unregister-while-in-flight and post-scope GC both
            // crashed processes during testing). The window is closed; the process is
            // expected to exit soon anyway.
        }

        private void Push(string s)
        {
            string prefix = cbMode ? "[cb] " : "";
            queue.Enqueue(string.Format("{0:HH:mm:ss.fff} {1}{2}", DateTime.Now, prefix, s));
        }
    }
}
'@
Add-Type -TypeDefinition $src -Language CSharp

$probe = New-Object -TypeName 'LidNotifyProbe.Probe'
$ready = $false
for ($i = 0; $i -lt 50 -and -not $ready; $i++)
{
    Start-Sleep -Milliseconds 100
    [string]$p = ''
    while ($probe.Queue.TryDequeue([ref]$p)) { Write-Host $p; if ($p -like '*probe-ready*') { $ready = $true }; $p = '' }
}
if (-not $ready) { Write-Host 'probe did NOT become ready.' }

Write-Host ''
Write-Host '>>> CLOSE the lid now, wait ~10 s, then OPEN it. Watching for 3 minutes (Ctrl+C to stop early)...'
Write-Host ''

$sawClosed = $false
$sawOpen = $false
$deadline = (Get-Date).AddSeconds(180)
while ((Get-Date) -lt $deadline -and -not ($sawClosed -and $sawOpen))
{
    [string]$p = ''
    while ($probe.Queue.TryDequeue([ref]$p))
    {
        if ($p -like '*LID_CLOSED*') { $sawClosed = $true }
        if ($p -like '*LID_OPEN*') { $sawOpen = $true }
        Write-Host $p
        $p = ''
    }
    Start-Sleep -Milliseconds 200
}

Start-Sleep -Seconds 2
[string]$p = ''
while ($probe.Queue.TryDequeue([ref]$p)) { Write-Host $p; $p = '' }
$probe.Dispose()

Write-Host ''
Write-Host '--- VERDICT ---'
if ($sawClosed -or $sawOpen)
{
    Write-Host 'Lid notifications RECEIVED - check which lines have the [cb] prefix:'
    Write-Host '  [cb] lines present  -> the powrprof callback API works: this is the mechanism NoSleep will use.'
    Write-Host '  only unprefixed lines -> the window path works here.'
}
else
{
    Write-Host 'No lid notifications at all. If APMSUSPEND/[cb] APMSUSPEND appeared during the sleep, suspend notifications work (usable fallback trigger). If the log is completely empty besides startup, power notifications are blocked on this machine and NoSleep''s S0 event-driven features cannot be built here.'
}
