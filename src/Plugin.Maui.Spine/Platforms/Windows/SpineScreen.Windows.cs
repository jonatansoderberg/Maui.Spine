using System.Runtime.InteropServices;
using Microsoft.Maui.Platform;

namespace Plugin.Maui.Spine.Core;

public static partial class SpineScreen
{
    private const uint WDA_NONE = 0;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    // Set when Spine excluded the window, so it never resets an affinity the app set itself.
    private static nint _excluded;

    static partial void ApplyCaptureExclusion()
    {
        if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window window)
            return;

        var hwnd = window.GetWindowHandle();

        if (ExcludeFromCapture)
        {
            if (GetWindowDisplayAffinity(hwnd, out var affinity) && affinity != WDA_NONE)
                return;

            if (SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE))
                _excluded = hwnd;
            else
                System.Diagnostics.Debug.WriteLine($"[Spine] SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) failed: {Marshal.GetLastWin32Error()}");
        }
        else if (_excluded == hwnd)
        {
            _excluded = 0;
            SetWindowDisplayAffinity(hwnd, WDA_NONE);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint hWnd, uint dwAffinity);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(nint hWnd, out uint pdwAffinity);
}
