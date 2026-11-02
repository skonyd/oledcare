namespace OledCare;

/// <summary>Basit dosya günlüğü: %APPDATA%\OledCare\oledcare.log (1 MB'ı aşınca yenilenir).</summary>
internal static class Logger
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(AppSettings.DataDirectory, "oledcare.log");

    public static string FilePath => LogPath;

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppSettings.DataDirectory);
                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(LogPath, LogPath + ".old", overwrite: true);
                }

                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Günlük yazılamazsa uygulama çalışmaya devam etmeli.
        }
    }
}
