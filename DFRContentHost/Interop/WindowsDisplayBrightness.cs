using System;
using System.Management;
using System.Runtime.InteropServices;

namespace DFRContentHost.Interop
{
    public static class WindowsDisplayBrightness
    {
        private static readonly Guid DisplaySubgroup = new Guid("7516b95f-f776-4464-8c53-06167f40cc99");
        private static readonly Guid BrightnessSetting = new Guid("aded5e82-b909-4619-9949-f5d71dac0bcb");

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
            ref Guid subgroupGuid, ref Guid settingGuid, out uint value);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
            ref Guid subgroupGuid, ref Guid settingGuid, uint value);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid,
            ref Guid subgroupGuid, ref Guid settingGuid, uint value);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid activePolicyGuid);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        public static int GetBrightness()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    @"root\WMI", "SELECT * FROM WmiMonitorBrightness"))
                using (var monitors = searcher.Get())
                {
                    foreach (ManagementObject monitor in monitors)
                    {
                        using (monitor)
                            return Convert.ToInt32(monitor["CurrentBrightness"]);
                    }
                }
            }
            catch
            {
            }

            return TryReadPowerBrightness(out var brightness) ? brightness : -1;
        }

        public static bool SetBrightness(int brightness)
        {
            var clampedBrightness = (byte)Math.Max(0, Math.Min(100, brightness));
            var updated = false;
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    @"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                using (var monitors = searcher.Get())
                {
                    foreach (ManagementObject monitor in monitors)
                    {
                        using (monitor)
                        using (var parameters = monitor.GetMethodParameters("WmiSetBrightness"))
                        {
                            parameters["Timeout"] = (uint)0;
                            parameters["Brightness"] = clampedBrightness;
                            using (var result = monitor.InvokeMethod("WmiSetBrightness", parameters, null))
                                updated |= result != null && Convert.ToUInt32(result["ReturnValue"]) == 0;
                        }
                    }
                }
            }
            catch
            {
            }

            return TryWritePowerBrightness(clampedBrightness) || updated;
        }

        private static bool TryReadPowerBrightness(out int brightness)
        {
            brightness = -1;
            var schemeMemory = IntPtr.Zero;
            try
            {
                if (PowerGetActiveScheme(IntPtr.Zero, out schemeMemory) != 0 || schemeMemory == IntPtr.Zero)
                    return false;

                var schemeGuid = (Guid)Marshal.PtrToStructure(schemeMemory, typeof(Guid));
                var subgroupGuid = DisplaySubgroup;
                var settingGuid = BrightnessSetting;
                if (PowerReadACValueIndex(IntPtr.Zero, ref schemeGuid, ref subgroupGuid,
                        ref settingGuid, out var value) != 0)
                    return false;

                brightness = (int)Math.Min(100, value);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (schemeMemory != IntPtr.Zero)
                    LocalFree(schemeMemory);
            }
        }

        private static bool TryWritePowerBrightness(uint brightness)
        {
            var schemeMemory = IntPtr.Zero;
            try
            {
                if (PowerGetActiveScheme(IntPtr.Zero, out schemeMemory) != 0 || schemeMemory == IntPtr.Zero)
                    return false;

                var schemeGuid = (Guid)Marshal.PtrToStructure(schemeMemory, typeof(Guid));
                var subgroupGuid = DisplaySubgroup;
                var settingGuid = BrightnessSetting;
                var acResult = PowerWriteACValueIndex(IntPtr.Zero, ref schemeGuid,
                    ref subgroupGuid, ref settingGuid, brightness);
                var dcResult = PowerWriteDCValueIndex(IntPtr.Zero, ref schemeGuid,
                    ref subgroupGuid, ref settingGuid, brightness);
                var applyResult = PowerSetActiveScheme(IntPtr.Zero, ref schemeGuid);

                return acResult == 0 && dcResult == 0 && applyResult == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (schemeMemory != IntPtr.Zero)
                    LocalFree(schemeMemory);
            }
        }
    }
}