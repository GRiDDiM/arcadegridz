using System.Runtime.InteropServices;

namespace ArcadeGridz.App.Services;

/// <summary>A connected monitor / TV. Bounds and WorkArea are physical screen pixels (same space as AppWindow).</summary>
public sealed record ScreenInfo(int Number, string Device, string Name, bool Primary,
    int X, int Y, int Width, int Height, int WorkX, int WorkY, int WorkWidth, int WorkHeight)
{
    /// <summary>Stable-enough key for settings: GDI device name + position + size.</summary>
    public string Key => $"{Device}|{X},{Y},{Width},{Height}";
    public string Label => $"{Number}  ·  {Name}  ·  {Width}×{Height}{(Primary ? "  ·  MAIN" : "")}";
    public bool Contains(int x, int y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
    public override string ToString() => Label;
}

/// <summary>Finds every connected display with its real name (e.g. "LG TV SSCR2") via QueryDisplayConfig.</summary>
public static class DisplayService
{
    public static List<ScreenInfo> Detect()
    {
        var names = FriendlyNames();
        var raw = new List<(string device, RECT bounds, RECT work, bool primary)>();
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
            {
                var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (GetMonitorInfo(hMonitor, ref info))
                    raw.Add((info.szDevice, info.rcMonitor, info.rcWork, (info.dwFlags & 1) != 0));
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) { StartupLog.Exception("Enumerate monitors", ex); }

        // number them left to right like Windows' display settings roughly does
        var ordered = raw.OrderBy(r => r.bounds.Left).ThenBy(r => r.bounds.Top).ToList();
        var screens = new List<ScreenInfo>();
        for (int i = 0; i < ordered.Count; i++)
        {
            var (device, b, w, primary) = ordered[i];
            var name = names.TryGetValue(device, out var n) && !string.IsNullOrWhiteSpace(n) ? n.Trim() : $"DISPLAY {i + 1}";
            screens.Add(new ScreenInfo(i + 1, device, name.ToUpperInvariant(), primary,
                b.Left, b.Top, b.Right - b.Left, b.Bottom - b.Top,
                w.Left, w.Top, w.Right - w.Left, w.Bottom - w.Top));
        }
        StartupLog.Write("display", "Detected: " + string.Join(" | ", screens.Select(s => s.Label)));
        return screens;
    }

    public static ScreenInfo? Find(List<ScreenInfo> screens, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        return screens.FirstOrDefault(s => s.Key == key)
            // monitors can be renumbered (DISPLAY1 -> DISPLAY2) after a driver update; fall back to the device or size
            ?? screens.FirstOrDefault(s => key.StartsWith(s.Device + "|", StringComparison.Ordinal))
            ?? screens.FirstOrDefault(s => key.EndsWith($"|{s.X},{s.Y},{s.Width},{s.Height}", StringComparison.Ordinal));
    }

    /// <summary>GDI device name (\\.\DISPLAY1) → monitor friendly name.</summary>
    static Dictionary<string, string> FriendlyNames()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != 0) return map;
            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return map;
            for (int i = 0; i < pathCount; i++)
            {
                var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                source.header.type = 1; // DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME
                source.header.size = Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
                source.header.adapterId = paths[i].sourceInfo.adapterId;
                source.header.id = paths[i].sourceInfo.id;
                if (DisplayConfigGetDeviceInfo(ref source) != 0) continue;

                var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                target.header.type = 2; // DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME
                target.header.size = Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>();
                target.header.adapterId = paths[i].targetInfo.adapterId;
                target.header.id = paths[i].targetInfo.id;
                if (DisplayConfigGetDeviceInfo(ref target) != 0) continue;

                var name = target.monitorFriendlyDeviceName;
                if (string.IsNullOrWhiteSpace(name))
                    name = target.outputTechnology switch { 0x80000000u or 11u or 13u => "BUILT-IN SCREEN", 5u => "HDMI DISPLAY", 10u => "DISPLAYPORT DISPLAY", _ => "" };
                if (!string.IsNullOrWhiteSpace(source.viewGdiDeviceName) && !map.ContainsKey(source.viewGdiDeviceName))
                    map[source.viewGdiDeviceName] = name;
            }
        }
        catch (Exception ex) { StartupLog.Exception("Monitor names", ex); }
        return map;
    }

    // ---- Win32 ----
    const uint QDC_ONLY_ACTIVE_PATHS = 2;

    delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprcMonitor, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_RATIONAL { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology;
        public uint rotation; public uint scaling; public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering; public int targetAvailable; public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_PATH_INFO { public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo; public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo; public uint flags; }

    // only the header fields are read; the 48-byte mode union is kept as padding
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    struct DISPLAYCONFIG_MODE_INFO { public uint infoType; public uint id; public LUID adapterId; }

    [StructLayout(LayoutKind.Sequential)]
    struct DISPLAYCONFIG_DEVICE_INFO_HEADER { public uint type; public int size; public LUID adapterId; public uint id; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll")]
    static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    [DllImport("user32.dll")]
    static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);
}
