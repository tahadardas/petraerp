using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PetraERP.Desktop.Platform;

/// <summary>Matches the native title bar to the application theme (Windows 10 20H1+ / Windows 11).</summary>
internal static class WindowAppearance
{
    private const int UseImmersiveDarkMode = 20;

    internal static void ApplyTitleBar(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var value = dark ? 1 : 0;
        _ = NativeMethods.DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref value, sizeof(int));
    }

    private static class NativeMethods
    {
        [DllImport("dwmapi.dll", PreserveSig = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
