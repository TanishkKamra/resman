using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ResMan
{
    public struct DisplayMode
    {
        public int Width, Height, Refresh;

        public DisplayMode(int w, int h, int hz) { Width = w; Height = h; Refresh = hz; }

        // hz <= 0 means "any refresh rate".
        public bool Matches(int w, int h, int hz)
        {
            return Width == w && Height == h && (hz <= 0 || Refresh == hz);
        }

        public override string ToString()
        {
            return Width + "\u00d7" + Height + " @ " + Refresh + " Hz";
        }
    }

    public class MonitorInfo
    {
        public string Device;   // \\.\DISPLAY1
        public string Adapter;  // GPU name
        public string Monitor;  // monitor name, often "Generic PnP Monitor"
        public bool Primary;
        public DisplayMode Native;

        public override string ToString()
        {
            string num = Device.Replace(@"\\.\DISPLAY", "");
            string s = "Display " + num + (Primary ? " (primary)" : "") + " \u2014 " + Native.Width + "\u00d7" + Native.Height;
            if (!string.IsNullOrEmpty(Monitor) && Monitor.IndexOf("Generic", StringComparison.OrdinalIgnoreCase) < 0)
                s += ", " + Monitor;
            return s;
        }
    }

    // Thin wrapper over EnumDisplaySettings / ChangeDisplaySettingsEx.
    // Changes are dynamic (not written to the registry), so a reboot always lands back on native.
    public static class Display
    {
        const int ENUM_CURRENT_SETTINGS = -1;
        const int ENUM_REGISTRY_SETTINGS = -2;
        const int DM_BITSPERPEL = 0x40000;
        const int DM_PELSWIDTH = 0x80000;
        const int DM_PELSHEIGHT = 0x100000;
        const int DM_DISPLAYFREQUENCY = 0x400000;
        const int DM_DISPLAYFIXEDOUTPUT = 0x20000000;
        const int DM_INTERLACED = 0x2;
        const uint CDS_TEST = 0x2;
        const int ATTACHED_TO_DESKTOP = 0x1;
        const int PRIMARY_DEVICE = 0x4;

        public const int Ok = 0;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplaySettingsW(string deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplayDevicesW(string device, uint devNum, ref DISPLAY_DEVICE dd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int ChangeDisplaySettingsExW(string deviceName, ref DEVMODE devMode, IntPtr hwnd, uint flags, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int ChangeDisplaySettingsExW(string deviceName, IntPtr devMode, IntPtr hwnd, uint flags, IntPtr lParam);

        static DEVMODE NewDevMode()
        {
            var dm = new DEVMODE();
            dm.dmDeviceName = "";
            dm.dmFormName = "";
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            return dm;
        }

        static DISPLAY_DEVICE NewDisplayDevice()
        {
            var dd = new DISPLAY_DEVICE();
            dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
            return dd;
        }

        public static List<MonitorInfo> GetMonitors()
        {
            var list = new List<MonitorInfo>();
            for (uint i = 0; ; i++)
            {
                var dd = NewDisplayDevice();
                if (!EnumDisplayDevicesW(null, i, ref dd, 0)) break;
                if ((dd.StateFlags & ATTACHED_TO_DESKTOP) == 0) continue;

                var mon = NewDisplayDevice();
                string monName = EnumDisplayDevicesW(dd.DeviceName, 0, ref mon, 0) ? mon.DeviceString : "";
                list.Add(new MonitorInfo
                {
                    Device = dd.DeviceName,
                    Adapter = dd.DeviceString,
                    Monitor = monName,
                    Primary = (dd.StateFlags & PRIMARY_DEVICE) != 0,
                    Native = GetNative(dd.DeviceName)
                });
            }
            return list;
        }

        // Saved device name if it still exists, else the primary monitor, else null.
        public static MonitorInfo Resolve(string device)
        {
            List<MonitorInfo> all = GetMonitors();
            MonitorInfo primary = null;
            foreach (MonitorInfo m in all)
            {
                if (!string.IsNullOrEmpty(device) && string.Equals(m.Device, device, StringComparison.OrdinalIgnoreCase)) return m;
                if (m.Primary) primary = m;
            }
            return primary ?? (all.Count > 0 ? all[0] : null);
        }

        public static List<DisplayMode> GetModes(string device)
        {
            var seen = new HashSet<long>();
            var list = new List<DisplayMode>();
            for (int i = 0; ; i++)
            {
                DEVMODE dm = NewDevMode();
                if (!EnumDisplaySettingsW(device, i, ref dm)) break;
                if (dm.dmBitsPerPel < 32 || (dm.dmDisplayFlags & DM_INTERLACED) != 0) continue;
                long key = dm.dmPelsWidth * 100000000L + dm.dmPelsHeight * 10000L + dm.dmDisplayFrequency;
                if (seen.Add(key)) list.Add(new DisplayMode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency));
            }
            list.Sort(delegate (DisplayMode a, DisplayMode b)
            {
                if (a.Width != b.Width) return b.Width.CompareTo(a.Width);
                if (a.Height != b.Height) return b.Height.CompareTo(a.Height);
                return b.Refresh.CompareTo(a.Refresh);
            });
            return list;
        }

        public static DisplayMode GetCurrent(string device) { return Read(device, ENUM_CURRENT_SETTINGS); }

        // The mode saved in Windows settings; what "native" means for this tool.
        public static DisplayMode GetNative(string device) { return Read(device, ENUM_REGISTRY_SETTINGS); }

        public static int GetCurrentFixedOutput(string device)
        {
            DEVMODE dm = NewDevMode();
            EnumDisplaySettingsW(device, ENUM_CURRENT_SETTINGS, ref dm);
            return dm.dmDisplayFixedOutput;
        }

        public static bool IsAtNative(string device)
        {
            DisplayMode n = GetNative(device);
            return GetCurrent(device).Matches(n.Width, n.Height, n.Refresh);
        }

        static DisplayMode Read(string device, int which)
        {
            DEVMODE dm = NewDevMode();
            if (!EnumDisplaySettingsW(device, which, ref dm)) return new DisplayMode();
            return new DisplayMode(dm.dmPelsWidth, dm.dmPelsHeight, dm.dmDisplayFrequency);
        }

        // testOnly = ask the driver whether the mode would work, without changing anything.
        public static int Apply(string device, int width, int height, int refresh, Scaling scaling, bool testOnly)
        {
            DEVMODE dm = NewDevMode();
            dm.dmPelsWidth = width;
            dm.dmPelsHeight = height;
            dm.dmBitsPerPel = 32;
            dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL;
            if (refresh > 0)
            {
                dm.dmDisplayFrequency = refresh;
                dm.dmFields |= DM_DISPLAYFREQUENCY;
            }
            if (scaling != Scaling.Keep)
            {
                dm.dmDisplayFixedOutput = (int)scaling; // DMDFO_STRETCH = 1, DMDFO_CENTER = 2
                dm.dmFields |= DM_DISPLAYFIXEDOUTPUT;
            }
            return ChangeDisplaySettingsExW(device, ref dm, IntPtr.Zero, testOnly ? CDS_TEST : 0, IntPtr.Zero);
        }

        // Passing no DEVMODE returns the monitor to the mode saved in Windows settings.
        public static int Restore(string device)
        {
            return ChangeDisplaySettingsExW(device, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        }

        public static string ErrorText(int code)
        {
            switch (code)
            {
                case 1: return "Windows needs a restart to apply this mode.";
                case -1: return "The display driver refused the change.";
                case -2: return "This monitor doesn't offer that resolution and refresh rate.";
                case -3: return "Windows couldn't save the setting.";
                case -4:
                case -5: return "The display settings were invalid.";
                case -6: return "That change isn't possible with the current multi-monitor layout.";
                default: return "Display change failed (code " + code + ").";
            }
        }

        public static string CustomResolutionHint(string adapter)
        {
            string a = (adapter ?? "").ToLowerInvariant();
            if (a.Contains("amd") || a.Contains("radeon"))
                return "To add a custom resolution, open AMD Software: Adrenalin Edition, go to the Display settings, then Custom Resolutions (or use CRU). Then come back and try again.";
            if (a.Contains("nvidia") || a.Contains("geforce") || a.Contains("rtx") || a.Contains("gtx"))
                return "To add a custom resolution, open NVIDIA Control Panel > Display > Change resolution > Customize (or use CRU). Then come back and try again.";
            if (a.Contains("intel"))
                return "To add a custom resolution, open Intel Graphics Command Center > Display > Custom Resolution. Then come back and try again.";
            return "Add it as a custom resolution in your graphics driver's control panel (or with CRU), then try again.";
        }
    }
}
