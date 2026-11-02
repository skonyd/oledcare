using System.Text.Json;
using System.Text.Json.Serialization;

namespace OledCare;

public enum OffMode
{
    /// <summary>Tüm ekranları siyah bir perdeyle örter. Anında açılır.</summary>
    Overlay,

    /// <summary>Önce siyah perde, belirli süre sonra monitörü güç tasarrufuna alır.</summary>
    OverlayThenPowerOff,

    /// <summary>Monitörü doğrudan güç tasarrufuna alır.</summary>
    PowerOff,
}

public sealed class AppSettings
{
    /// <summary>Klavye/fare/kumanda boşta kaldıktan kaç saniye sonra kamera kontrolü başlasın.</summary>
    public int IdleBeforeCheckSeconds { get; set; } = 60;

    /// <summary>Kamera kontrolü sırasında kaç saniye yüz görülmezse ekran kapansın.</summary>
    public int AbsentBeforeOffSeconds { get; set; } = 20;

    /// <summary>İki kamera kontrolü arasındaki süre (milisaniye).</summary>
    public int CheckIntervalMs { get; set; } = 1000;

    /// <summary>Ekran kapalıyken geri dönüşü onaylamak için art arda kaç karede yüz görülmeli.</summary>
    public int ReturnConfirmFrames { get; set; } = 2;

    /// <summary>Yüz, görüntü yüksekliğinin en az yüzde kaçı kadar olmalı (uzaktaki kişileri yok saymak için).</summary>
    public int MinFaceSizePercent { get; set; } = 8;

    public OffMode OffMode { get; set; } = OffMode.OverlayThenPowerOff;

    /// <summary>OverlayThenPowerOff modunda perdeden sonra kaç saniye içinde güç kapansın.</summary>
    public int PowerOffDelaySeconds { get; set; } = 15;

    /// <summary>Ekran kapalıyken geri dönüş için kamera en fazla kaç dakika açık kalsın (0 = sınırsız).</summary>
    public int AwayCameraTimeoutMinutes { get; set; } = 120;

    /// <summary>Tam ekran oyun/video açıkken kamera kontrolünü atla.</summary>
    public bool PauseOnFullscreen { get; set; } = true;

    /// <summary>Kamera seni görürken Windows'un ekranı kendiliğinden kapatmasını engelle.</summary>
    public bool KeepDisplayOnWhilePresent { get; set; } = true;

    /// <summary>Kamera kullanılamazsa (başka uygulama kilitlediyse) ekranı yine de kapatma.</summary>
    public bool NeverTurnOffWithoutCamera { get; set; } = true;

    /// <summary>Kullanılacak kameranın cihaz kimliği. Boşsa varsayılan kamera.</summary>
    public string? CameraId { get; set; }

    /// <summary>Uygulama açıldığında izlemeye otomatik başlasın.</summary>
    public bool StartEnabled { get; set; } = true;

    /// <summary>Durum penceresinin X düğmesi uygulamayı kapatmak yerine tepsiye küçültsün.</summary>
    public bool CloseToTray { get; set; } = true;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OledCare");

    private static string FilePath => Path.Combine(DataDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (loaded is not null)
                {
                    loaded.Normalize();
                    return loaded;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Ayarlar okunamadı, varsayılanlar kullanılıyor: {ex.Message}");
        }

        return new AppSettings();
    }

    public void Save()
    {
        Normalize();
        Directory.CreateDirectory(DataDirectory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
    }

    public AppSettings Clone()
        => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    private void Normalize()
    {
        IdleBeforeCheckSeconds = Math.Clamp(IdleBeforeCheckSeconds, 5, 3600);
        AbsentBeforeOffSeconds = Math.Clamp(AbsentBeforeOffSeconds, 3, 3600);
        CheckIntervalMs = Math.Clamp(CheckIntervalMs, 250, 10000);
        ReturnConfirmFrames = Math.Clamp(ReturnConfirmFrames, 1, 10);
        MinFaceSizePercent = Math.Clamp(MinFaceSizePercent, 1, 80);
        PowerOffDelaySeconds = Math.Clamp(PowerOffDelaySeconds, 1, 3600);
        AwayCameraTimeoutMinutes = Math.Clamp(AwayCameraTimeoutMinutes, 0, 24 * 60);
    }
}
