using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace NoSleep
{
    /// <summary>
    /// Power event notifications via the powrprof callback APIs (no window needed; the
    /// window-based RegisterPowerSettingNotification is avoided deliberately - see
    /// S0-FINDINGS.md T14 for the delivery measurements on real S0 hardware).
    ///
    /// Hard rules learned the hard way during testing (S0-FINDINGS.md, pitfalls):
    /// - the callback delegate MUST be statically rooted for the process lifetime
    ///   (a GC'd delegate crashes the process with 0x80131623 on the next event);
    /// - registrations are never unregistered (unregister-while-in-flight also crashed);
    ///   they die with the process.
    /// </summary>
    internal sealed class PowerNotifications
    {
        public const string LidSwitchStateChangeGuid = "BA3E0F4D-B817-4094-A2D1-D56379E6A0F3";

        private const uint DeviceNotifyCallback = 2; // DEVICE_NOTIFY_CALLBACK
        private const uint PbtApmSuspend = 4;
        private const uint PbtPowerSettingChange = 0x8013;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate uint PowerCallback(IntPtr context, uint type, IntPtr setting);

        [StructLayout(LayoutKind.Sequential)]
        private struct DeviceNotifySubscribeParameters
        {
            public IntPtr Callback;
            public IntPtr Context;
        }

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerSettingRegisterNotification(IntPtr settingGuid, uint registrationType, IntPtr recipient, out IntPtr registrationHandle);

        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern uint PowerRegisterSuspendResumeNotification(uint flags, IntPtr recipient, out IntPtr registrationHandle);

        // --- static rooting: these live as long as the process ---
        private static readonly PowerCallback StaticCallback = OnPowerCallback;
        private static readonly IntPtr SubscribeParams = BuildSubscribeParams();
        private static readonly IntPtr LidGuidPtr = GuidToPtr(LidSwitchStateChangeGuid);
        private static PowerNotifications _active; // routes the static callback to the instance

        private readonly SynchronizationContext _uiContext;

        /// <summary> Lid closed (data == 0 of GUID_LIDSWITCH_STATE_CHANGE). </summary>
        public event EventHandler LidClosed;
        /// <summary> System suspend initiated (PBT_APMSUSPEND). </summary>
        public event EventHandler SuspendInitiated;

        /// <summary> Same events, but executed immediately ON THE CALLBACK THREAD - use for
        /// time-critical reactions (the burst); everything else should use the UI-thread events. </summary>
        public event EventHandler LidClosedImmediate;
        public event EventHandler SuspendInitiatedImmediate;

        /// <summary> Must be constructed on the UI thread (captures its synchronization context). </summary>
        public PowerNotifications()
        {
            _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();
            _active = this;
            // Registration results are deliberately not checked: on lid-less machines the
            // lid registration simply yields no events; suspend callbacks are the fallback.
            IntPtr lidReg;
            PowerSettingRegisterNotification(LidGuidPtr, DeviceNotifyCallback, SubscribeParams, out lidReg);
            IntPtr suspendReg;
            PowerRegisterSuspendResumeNotification(DeviceNotifyCallback, SubscribeParams, out suspendReg);
        }

        private static IntPtr BuildSubscribeParams()
        {
            DeviceNotifySubscribeParameters p = new DeviceNotifySubscribeParameters();
            p.Callback = Marshal.GetFunctionPointerForDelegate(StaticCallback);
            p.Context = IntPtr.Zero;
            IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(DeviceNotifySubscribeParameters)));
            Marshal.StructureToPtr(p, ptr, false);
            return ptr; // never freed: the OS holds it for the registration lifetime
        }

        private static IntPtr GuidToPtr(string guidText)
        {
            IntPtr p = Marshal.AllocHGlobal(16);
            Marshal.Copy(new Guid(guidText).ToByteArray(), 0, p, 16);
            return p; // never freed (same reason)
        }

        private static uint OnPowerCallback(IntPtr context, uint type, IntPtr setting)
        {
            PowerNotifications self = _active;
            if (self == null)
                return 0;

            if (type == PbtApmSuspend)
            {
                self.SuspendInitiatedImmediate?.Invoke(self, EventArgs.Empty);
                self._uiContext.Post(s => self.SuspendInitiated?.Invoke(self, EventArgs.Empty), null);
            }
            else if (type == PbtPowerSettingChange && IsLidClosed(setting))
            {
                self.LidClosedImmediate?.Invoke(self, EventArgs.Empty);
                self._uiContext.Post(s => self.LidClosed?.Invoke(self, EventArgs.Empty), null);
            }
            return 0; // ERROR_SUCCESS
        }

        private static bool IsLidClosed(IntPtr setting)
        {
            try
            {
                // POWERBROADCAST_SETTING { GUID PowerSetting; DWORD DataLength; BYTE Data[1]; }
                byte[] buf = new byte[24];
                Marshal.Copy(setting, buf, 0, 24);
                byte[] guidBytes = new byte[16];
                Array.Copy(buf, 0, guidBytes, 0, 16);
                if (new Guid(guidBytes) != new Guid(LidSwitchStateChangeGuid))
                    return false;
                return buf[20] == 0; // 0 = lid closed
            }
            catch
            {
                return false;
            }
        }
    }
}
