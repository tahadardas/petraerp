using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using PetraERP.Desktop.Shell;

namespace PetraERP.Desktop.Platform;

/// <summary>Native geometry stays in screen pixels; persisted offsets/size are monitor-relative DIPs.</summary>
internal static class WindowsDocumentPlacement
{
    private const int MonitorNearest = 2;
    private const uint NoSize = 0x0001, NoZOrder = 0x0004, NoActivate = 0x0010;
    internal static void Apply(Window window, Window owner, DetachedWindowPlacement? saved, int cascade)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var fallback = Native.MonitorFromWindow(new WindowInteropHelper(owner).Handle, MonitorNearest);
        var monitor = Info(fallback);
        if (saved is { IsValid: true })
        {
            Native.MonitorCallback find = (nint candidate, nint _, ref NativeRect bounds, nint data) =>
            {
                var info = Info(candidate);
                if (string.Equals(info.Device, saved.Monitor, StringComparison.OrdinalIgnoreCase)) monitor = info;
                return true;
            };
            _ = Native.EnumDisplayMonitors(0, 0, find, 0);
        }
        // First move the HWND to the destination so GetDpiForWindow supplies its current per-monitor DPI.
        // Then fit logical size to that work area. Never pass WINDOWPLACEMENT workspace coordinates to SetWindowPos.
        _ = Native.SetWindowPos(handle, 0, monitor.Work.Left, monitor.Work.Top, 0, 0, NoSize | NoZOrder | NoActivate);
        var scale = Math.Max(96, Native.GetDpiForWindow(handle)) / 96d;
        var area = new MonitorWorkArea(monitor.Device, monitor.Work.Left, monitor.Work.Top,
            monitor.Work.Right - monitor.Work.Left, monitor.Work.Bottom - monitor.Work.Top);
        if (saved is not { IsValid: true })
        {
            var targetWidth = window.Width > 0 ? window.Width : 960;
            var targetHeight = window.Height > 0 ? window.Height : 680;
            var centerX = Math.Max(0, (area.Width / scale - targetWidth) / 2.0);
            var centerY = Math.Max(0, (area.Height / scale - targetHeight) / 2.0);
            saved = new DetachedWindowPlacement(monitor.Device, centerX, centerY, targetWidth, targetHeight, false);
        }
        var bounds = WindowPlacementGeometry.Fit(saved, area, scale, window.Width, window.Height, window.MinWidth, window.MinHeight);
        window.MinWidth = Math.Min(window.MinWidth, area.Width / scale);
        window.MinHeight = Math.Min(window.MinHeight, area.Height / scale);
        _ = Native.SetWindowPos(handle, 0, bounds.Left, bounds.Top, bounds.Width, bounds.Height, NoZOrder | NoActivate);
    }

    internal static DetachedWindowPlacement? Capture(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return null;
        var placement = new NativePlacement { Length = Marshal.SizeOf<NativePlacement>() };
        if (!Native.GetWindowPlacement(handle, ref placement)) return null;
        var info = Info(Native.MonitorFromWindow(handle, MonitorNearest));
        var normal = placement.Normal;
        if (window.WindowState == WindowState.Normal)
        {
            if (!Native.GetWindowRect(handle, out normal)) return null;
        }
        else
        {
            // DocumentWindow is a regular top-level window, not WS_EX_TOOLWINDOW. Its restore rectangle is in
            // workspace coordinates; translate it exactly once to screen coordinates using the monitor work offset.
            var dx = info.Work.Left - info.Monitor.Left; var dy = info.Work.Top - info.Monitor.Top;
            normal.Left += dx; normal.Right += dx; normal.Top += dy; normal.Bottom += dy;
        }
        var scale = Math.Max(96, Native.GetDpiForWindow(handle)) / 96d;
        var result = new DetachedWindowPlacement(info.Device, (normal.Left - info.Work.Left) / scale,
            (normal.Top - info.Work.Top) / scale, (normal.Right - normal.Left) / scale, (normal.Bottom - normal.Top) / scale,
            placement.Show == 3 || placement.Show == 2 && (placement.Flags & 2) != 0);
        return result.IsValid ? result : null;
    }

    private static MonitorInfo Info(nint monitor)
    {
        var result = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>(), Device = string.Empty };
        if (!Native.GetMonitorInfo(monitor, ref result)) throw new InvalidOperationException("Windows did not provide a monitor work area.");
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePlacement
    {
        internal int Length, Flags, Show;
        internal NativePoint Minimized, Maximized;
        internal NativeRect Normal;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        internal int Size;
        internal NativeRect Monitor, Work;
        internal int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Device;
    }

    private static class Native
    {
        internal delegate bool MonitorCallback(nint monitor, nint dc, ref NativeRect bounds, nint data);
        [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern nint MonitorFromWindow(nint window, int flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
        [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
        [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint GetDpiForWindow(nint window);
        [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowRect(nint window, out NativeRect rect);
        [DllImport("user32.dll")][DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetWindowPlacement(nint window, ref NativePlacement placement);
    }
}
