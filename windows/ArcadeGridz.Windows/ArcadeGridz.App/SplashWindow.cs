using System.Runtime.InteropServices;
using ArcadeGridz.App.Services;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using DrawingImaging = System.Drawing.Imaging;

namespace ArcadeGridz.App;

/// <summary>
/// Startup splash like Photoshop / Steam: the ArcadeGridz logo floats in the middle of whatever is already on
/// screen (no window frame, no fullscreen loading screen). It is a per-pixel-alpha layered Win32 window, so the
/// desktop shows through everywhere except the logo and its soft neon glow. It is click-through, never takes
/// focus and has no taskbar button. The fullscreen launcher loads cloaked behind it and the splash fades out
/// over it once the launcher is ready.
/// All calls must come from the UI thread (Program.Main runs WinUI on the same thread).
/// </summary>
static class SplashWindow
{
    const string ClassName = "ArcadeGridzSplash";
    static IntPtr hwnd, memDc, dib, oldBitmap;
    static int width, height, left, top;
    static Microsoft.UI.Dispatching.DispatcherQueueTimer? fadeTimer;

    public static bool IsOpen => hwnd != IntPtr.Zero;

    public static void Show()
    {
        try
        {
            var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "arcadegridz-logo.png");
            if (!File.Exists(logoPath)) { StartupLog.Write("splash", "Logo missing; splash skipped"); return; }

            var monitor = MonitorFromPoint(default, MONITOR_DEFAULTTOPRIMARY);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref info)) { StartupLog.Write("splash", "No monitor info; splash skipped"); return; }
            int monitorW = info.rcMonitor.right - info.rcMonitor.left, monitorH = info.rcMonitor.bottom - info.rcMonitor.top;

            using var logo = new Drawing.Bitmap(logoPath);
            // ~38% of the screen width, never blown up past 2x the source art
            int logoW = Math.Min((int)(monitorW * 0.38), logo.Width * 2);
            int logoH = Math.Max(1, logoW * logo.Height / Math.Max(1, logo.Width));
            int padX = logoW / 5, padY = Math.Max(logoH / 2, logoW / 8);
            width = logoW + padX * 2;
            height = logoH + padY * 2;
            left = info.rcMonitor.left + (monitorW - width) / 2;
            top = info.rcMonitor.top + (monitorH - height) / 2;

            using var canvas = new Drawing.Bitmap(width, height, DrawingImaging.PixelFormat.Format32bppArgb);
            using (var g = Drawing.Graphics.FromImage(canvas))
            {
                g.Clear(Drawing.Color.Transparent);
                g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias;
                g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic;
                g.CompositingQuality = Drawing2D.CompositingQuality.HighQuality;
                g.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality;
                // soft cyan -> magenta neon haze behind the logo (fades to fully transparent at the edges)
                FillGlow(g, new Drawing.RectangleF(0, padY * 0.35f, width, height - padY * 0.7f),
                    Drawing.Color.FromArgb(120, 94, 231, 255), Drawing.Color.FromArgb(0, 255, 58, 167));
                FillGlow(g, new Drawing.RectangleF(padX * 0.6f, padY * 0.7f, width - padX * 1.2f, height - padY * 1.4f),
                    Drawing.Color.FromArgb(70, 255, 111, 181), Drawing.Color.FromArgb(0, 159, 124, 255));
                g.DrawImage(logo, new Drawing.Rectangle(padX, padY, logoW, logoH));
            }

            var screenDc = GetDC(IntPtr.Zero);
            try
            {
                memDc = CreateCompatibleDC(screenDc);
                var header = new BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = width,
                    biHeight = -height, // top-down rows, same order as GDI+ LockBits
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0
                };
                dib = CreateDIBSection(screenDc, ref header, 0, out var bits, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero) throw new InvalidOperationException("CreateDIBSection failed");
                // UpdateLayeredWindow wants premultiplied BGRA; GDI+ converts for us via Format32bppPArgb
                var data = canvas.LockBits(new Drawing.Rectangle(0, 0, width, height),
                    DrawingImaging.ImageLockMode.ReadOnly, DrawingImaging.PixelFormat.Format32bppPArgb);
                try
                {
                    var row = new byte[width * 4];
                    for (int y = 0; y < height; y++)
                    {
                        Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                        Marshal.Copy(row, 0, bits + y * width * 4, row.Length);
                    }
                }
                finally { canvas.UnlockBits(data); }
                oldBitmap = SelectObject(memDc, dib);
            }
            finally { ReleaseDC(IntPtr.Zero, screenDc); }

            var instance = GetModuleHandle(null);
            var wc = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = GetProcAddress(GetModuleHandle("user32.dll"), "DefWindowProcW"),
                hInstance = instance,
                lpszClassName = ClassName
            };
            RegisterClassEx(ref wc); // harmless if already registered
            hwnd = CreateWindowEx(
                WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE,
                ClassName, "ArcadeGridz", WS_POPUP, left, top, width, height,
                IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastWin32Error()})");
            Present(255);
            ShowWindow(hwnd, SW_SHOWNOACTIVATE);
            StartupLog.Write("splash", $"Overlay splash shown {width}x{height} at {left},{top}");
        }
        catch (Exception ex)
        {
            StartupLog.Exception("Splash overlay", ex);
            Close();
        }
    }

    /// <summary>Fades the overlay out (about a quarter second) and destroys it.</summary>
    public static void FadeOutAndClose(Microsoft.UI.Dispatching.DispatcherQueue queue)
    {
        if (!IsOpen || fadeTimer is not null) return;
        int alpha = 255;
        fadeTimer = queue.CreateTimer();
        fadeTimer.Interval = TimeSpan.FromMilliseconds(16);
        fadeTimer.Tick += (t, _) =>
        {
            alpha -= 18;
            if (alpha <= 0) { t.Stop(); fadeTimer = null; Close(); return; }
            Present((byte)alpha);
        };
        fadeTimer.Start();
    }

    public static void Close()
    {
        if (hwnd != IntPtr.Zero) { DestroyWindow(hwnd); hwnd = IntPtr.Zero; StartupLog.Write("splash", "Overlay splash closed"); }
        if (memDc != IntPtr.Zero)
        {
            if (oldBitmap != IntPtr.Zero) SelectObject(memDc, oldBitmap);
            DeleteDC(memDc);
            memDc = oldBitmap = IntPtr.Zero;
        }
        if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
    }

    static void FillGlow(Drawing.Graphics g, Drawing.RectangleF bounds, Drawing.Color center, Drawing.Color edge)
    {
        using var path = new Drawing2D.GraphicsPath();
        path.AddEllipse(bounds);
        using var brush = new Drawing2D.PathGradientBrush(path)
        {
            CenterColor = center,
            SurroundColors = [edge],
            FocusScales = new Drawing.PointF(0.35f, 0.2f)
        };
        g.FillPath(brush, path);
    }

    static void Present(byte alpha)
    {
        if (hwnd == IntPtr.Zero || memDc == IntPtr.Zero) return;
        var screenDc = GetDC(IntPtr.Zero);
        try
        {
            var dst = new POINT { x = left, y = top };
            var size = new SIZE { cx = width, cy = height };
            var src = new POINT();
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = AC_SRC_ALPHA };
            UpdateLayeredWindow(hwnd, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
        }
        finally { ReleaseDC(IntPtr.Zero, screenDc); }
    }

    // ---- Win32 ----
    const int WS_POPUP = unchecked((int)0x80000000);
    const int WS_EX_LAYERED = 0x00080000, WS_EX_TOPMOST = 0x00000008, WS_EX_TOOLWINDOW = 0x00000080,
        WS_EX_TRANSPARENT = 0x00000020, WS_EX_NOACTIVATE = 0x08000000;
    const int SW_SHOWNOACTIVATE = 4, ULW_ALPHA = 2, MONITOR_DEFAULTTOPRIMARY = 1;
    const byte AC_SRC_ALPHA = 1;

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }
    [StructLayout(LayoutKind.Sequential)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEX
    {
        public int cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName, lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dstDc, ref POINT dst, ref SIZE size,
        IntPtr srcDc, ref POINT src, int key, ref BLENDFUNCTION blend, int flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, int flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER info, int usage, out IntPtr bits, IntPtr section, int offset);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
}
