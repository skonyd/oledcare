using System.Text;

namespace OledCare;

/// <summary>Ön plandaki uygulamanın tam ekran (oyun, video, sunum) olup olmadığını tespit eder.</summary>
internal static class FullscreenDetector
{
    private static readonly int OwnProcessId = Environment.ProcessId;

    public static bool IsFullscreenAppActive()
    {
        try
        {
            if (NativeMethods.ShellReportsFullscreen())
                return true;

            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero || hwnd == NativeMethods.GetShellWindow() || hwnd == NativeMethods.GetDesktopWindow())
                return false;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == OwnProcessId)
                return false;

            // Masaüstü (Progman/WorkerW) tüm ekranı kaplar ama tam ekran uygulama değildir.
            var className = new StringBuilder(64);
            NativeMethods.GetClassName(hwnd, className, className.Capacity);
            if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd")
                return false;

            if (!NativeMethods.GetWindowRect(hwnd, out var rect))
                return false;

            var windowBounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            var screenBounds = Screen.FromHandle(hwnd).Bounds;

            // Kenarlıksız tam ekran oyunlar ve F11 videolar pencereyi monitörle birebir kaplar.
            return windowBounds.Contains(screenBounds);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Tam ekran kontrolü başarısız: {ex.Message}");
            return false;
        }
    }
}
