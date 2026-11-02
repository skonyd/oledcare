namespace OledCare;

internal static class Program
{
    private const string MutexName = @"Local\OledCare.SingleInstance";
    private const string ShowEventName = @"Local\OledCare.ShowWindow";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        if (!isFirstInstance)
        {
            // Zaten çalışıyor: mevcut örneğin durum penceresini öne getir.
            showSignal.Set();
            return;
        }

        Application.ThreadException += (_, e) => Logger.Error("UI hatası", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Logger.Error("Beklenmeyen hata", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Logger.Error("Gözlenmeyen görev hatası", e.Exception);
            e.SetObserved();
        };

        ApplicationConfiguration.Initialize();

        bool startMinimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        Logger.Info($"OledCare başlatıldı (sürüm {Application.ProductVersion}, simge durumunda: {startMinimized}).");

        Application.Run(new TrayApp(startMinimized, showSignal));
    }
}
