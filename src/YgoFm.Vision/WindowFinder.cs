using System.Diagnostics;
using System.Drawing;

namespace YgoFm.Vision;

/// <summary>A top-level window the user could point the tool at.</summary>
public sealed record WindowInfo(IntPtr Handle, string Title, string ProcessName, Rectangle Bounds)
{
    public string Display => $"{Title}  —  {ProcessName}.exe  ({Bounds.Width}x{Bounds.Height})";
}

/// <summary>
/// Lists the visible windows on screen so the user can pick their emulator.
/// Deliberately knows nothing about specific emulators — any window will do.
/// </summary>
public static class WindowFinder
{
    /// <summary>Process names of known PlayStation 1 emulators, used only to sort likely matches first.</summary>
    private static readonly string[] LikelyEmulators =
        ["duckstation", "epsxe", "pcsx", "mednafen", "retroarch", "xebra", "psxfin", "beetle"];

    public static List<WindowInfo> Visible()
    {
        var found = new List<WindowInfo>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd) || NativeMethods.IsIconic(hWnd)) return true;

            var length = NativeMethods.GetWindowTextLength(hWnd);
            if (length == 0) return true;

            // Store apps keep hidden windows around; skip anything the compositor is not drawing.
            if (NativeMethods.DwmGetWindowAttribute(
                    hWnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            var buffer = new char[length + 1];
            var written = NativeMethods.GetWindowText(hWnd, buffer, buffer.Length);
            var title = new string(buffer, 0, written);
            if (string.IsNullOrWhiteSpace(title)) return true;

            var bounds = ScreenCapture.WindowBounds(hWnd);
            if (bounds.Width < 100 || bounds.Height < 100) return true;

            found.Add(new WindowInfo(hWnd, title, ProcessNameOf(hWnd), bounds));
            return true;
        }, IntPtr.Zero);

        return [.. found.OrderByDescending(LooksLikeEmulator).ThenBy(w => w.Title)];
    }

    private static bool LooksLikeEmulator(WindowInfo w) =>
        LikelyEmulators.Any(name => w.ProcessName.Contains(name, StringComparison.OrdinalIgnoreCase));

    private static string ProcessNameOf(IntPtr hWnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hWnd, out int pid);
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return "?";
        }
    }
}
