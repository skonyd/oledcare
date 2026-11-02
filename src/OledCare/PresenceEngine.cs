using System.Diagnostics;
using Microsoft.Win32;

namespace OledCare;

public enum EngineState
{
    /// <summary>Kullanıcı izlemeyi durdurdu.</summary>
    Stopped,

    /// <summary>Kullanıcı klavye/fare/kumanda kullanıyor; kamera kapalı.</summary>
    Active,

    /// <summary>Kullanıcı bir süredir giriş yapmadı; kamera ile yüz aranıyor.</summary>
    Watching,

    /// <summary>Kullanıcı yok; ekran kapatıldı, geri dönüş bekleniyor.</summary>
    Away,

    /// <summary>Geçici olarak beklemede (tam ekran uygulama, kamera kullanılamıyor, oturum kilitli).</summary>
    Paused,
}

public sealed record EngineSnapshot(
    EngineState State,
    string Detail,
    bool CameraOpen,
    string? CameraName,
    TimeSpan IdleTime,
    DateTime? LastFaceSeen,
    DetectionResult LastDetection,
    bool FullscreenActive,
    bool DisplayOff,
    bool DisplayPoweredOff);

/// <summary>
/// Varlık algılama durum makinesi. Arka planda çalışır; kamerayı yalnızca kullanıcı boştayken açar.
/// </summary>
internal sealed class PresenceEngine : IAsyncDisposable
{
    private const int ActiveTickMs = 500;
    private static readonly TimeSpan CameraRetryDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan NoFrameTimeout = TimeSpan.FromSeconds(8);

    private readonly ActivityMonitor _activity = new();
    private readonly PresenceDetector _detector = new();
    private readonly DisplayController _display;
    private readonly object _gate = new();

    private AppSettings _settings;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile bool _enabled;
    private volatile bool _sessionLocked;
    private volatile bool _manualOffRequested;

    private EngineState _state = EngineState.Stopped;
    private string _detail = "Durduruldu";
    private TimeSpan _idle;
    private DateTime? _lastFaceSeen;
    private DetectionResult _lastDetection;
    private bool _fullscreen;

    private DateTime _watchStartedAt;
    private DateTime _lastFaceAtWatch;
    private DateTime _awayStartedAt;
    private DateTime _pausedUntil;
    private int _consecutiveFaceFrames;
    private bool _cameraUnavailable;
    private DateTime _ignoreInputUntil;
    private DateTime _lastFrameAt;

    public PresenceEngine(DisplayController display, AppSettings settings)
    {
        _display = display;
        _settings = settings.Clone();
        SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public event Action<EngineState>? StateChanged;

    public bool Enabled => _enabled;

    public void UpdateSettings(AppSettings settings)
    {
        lock (_gate)
        {
            _settings = settings.Clone();
        }
    }

    public EngineSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return new EngineSnapshot(_state, _detail, _detector.IsOpen, _detector.OpenCameraName, _idle,
                _lastFaceSeen, _lastDetection, _fullscreen, _display.IsOff, _display.IsPoweredOff);
        }
    }

    public void Start()
    {
        _enabled = true;
        if (_loop is null || _loop.IsCompleted)
        {
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => RunAsync(_cts.Token));
        }

        Logger.Info("İzleme başlatıldı.");
    }

    public void Stop()
    {
        _enabled = false;
        Logger.Info("İzleme durduruldu.");
    }

    /// <summary>Test için ekranı hemen kapatır; geri dönüş normal şekilde algılanır.</summary>
    public void TurnOffNow() => _manualOffRequested = true;

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            int delayMs = ActiveTickMs;
            try
            {
                delayMs = await TickAsync();
            }
            catch (Exception ex)
            {
                Logger.Error("Döngü hatası", ex);
                await SafeCloseCameraAsync();
                SetState(EngineState.Paused, "Beklenmeyen hata, tekrar denenecek");
                _pausedUntil = DateTime.Now + CameraRetryDelay;
                delayMs = 2000;
            }

            try
            {
                await Task.Delay(delayMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await SafeCloseCameraAsync();
    }

    /// <summary>Bir adım çalışır ve bir sonraki adıma kadar beklenecek süreyi döndürür.</summary>
    private async Task<int> TickAsync()
    {
        AppSettings s;
        lock (_gate)
        {
            s = _settings;
        }

        var now = DateTime.Now;
        var previousIdle = _idle;
        var idle = _activity.GetIdleTime();
        lock (_gate)
        {
            _idle = idle;
        }

        // Elle kapatmadan hemen sonra düğmeye tıklayan fare hareketi ve kameradaki yüz ekranı geri açmasın.
        bool userInput = now >= _ignoreInputUntil
            && (idle < previousIdle || idle < TimeSpan.FromMilliseconds(ActiveTickMs));

        if (!_enabled)
        {
            await WakeIfOffAsync(nudge: false);
            await SafeCloseCameraAsync();
            SetState(EngineState.Stopped, "Durduruldu");
            return ActiveTickMs;
        }

        if (_sessionLocked)
        {
            await WakeIfOffAsync(nudge: false);
            await SafeCloseCameraAsync();
            SetState(EngineState.Paused, "Windows oturumu kilitli");
            return ActiveTickMs;
        }

        if (_manualOffRequested)
        {
            _manualOffRequested = false;
            if (_state != EngineState.Away)
            {
                _ignoreInputUntil = now.AddSeconds(5);
                await GoAwayAsync(s, "Elle kapatıldı (test)");
                return s.CheckIntervalMs;
            }
        }

        switch (_state)
        {
            case EngineState.Stopped:
            case EngineState.Active:
                if (idle.TotalSeconds < s.IdleBeforeCheckSeconds)
                {
                    SetState(EngineState.Active, "Bilgisayar kullanılıyor");
                    return ActiveTickMs;
                }

                return await BeginWatchingAsync(s);

            case EngineState.Watching:
                if (userInput)
                {
                    await SafeCloseCameraAsync();
                    SetState(EngineState.Active, "Bilgisayar kullanılıyor");
                    return ActiveTickMs;
                }

                return await WatchStepAsync(s, now);

            case EngineState.Away:
                if (userInput)
                {
                    await WakeIfOffAsync(nudge: false);
                    await SafeCloseCameraAsync();
                    SetState(EngineState.Active, "Klavye/fare ile geri dönüldü");
                    return ActiveTickMs;
                }

                return await AwayStepAsync(s, now);

            case EngineState.Paused:
                if (userInput || idle.TotalSeconds < s.IdleBeforeCheckSeconds)
                {
                    _cameraUnavailable = false;
                    SetState(EngineState.Active, "Bilgisayar kullanılıyor");
                    return ActiveTickMs;
                }

                if (now < _pausedUntil)
                    return ActiveTickMs;

                return await BeginWatchingAsync(s);
        }

        return ActiveTickMs;
    }

    private async Task<int> BeginWatchingAsync(AppSettings s)
    {
        bool fullscreen = UpdateFullscreen();

        if (fullscreen && s.NeverTurnOffInFullscreen)
        {
            SetState(EngineState.Paused, "Tam ekran uygulama açık; ekran kapatılmayacak");
            _pausedUntil = DateTime.Now.AddSeconds(5);
            return ActiveTickMs;
        }

        if (!await TryOpenCameraAsync(s))
        {
            if (s.NeverTurnOffWithoutCamera)
            {
                SetState(EngineState.Paused, "Kamera kullanılamıyor; ekran kapatılmayacak");
                _pausedUntil = DateTime.Now + CameraRetryDelay;
                return ActiveTickMs;
            }

            await GoAwayAsync(s, "Kamera yok; boşta kalma süresine göre kapatıldı");
            return ActiveTickMs;
        }

        _watchStartedAt = DateTime.Now;
        _lastFaceAtWatch = _watchStartedAt;
        _lastFrameAt = _watchStartedAt;
        SetState(EngineState.Watching, "Kamera ile kontrol ediliyor");
        return s.CheckIntervalMs;
    }

    private async Task<int> WatchStepAsync(AppSettings s, DateTime now)
    {
        var result = await DetectAsync(s);

        if (!result.HasFrame)
        {
            // Görüntü gelmiyorsa "yüz yok" sayıp ekranı kapatmak yanlış olur; kamerayı arızalı say.
            if (now - _lastFrameAt > NoFrameTimeout)
            {
                Logger.Warn("Kameradan görüntü alınamıyor.");
                await SafeCloseCameraAsync();
                _cameraUnavailable = true;
                if (s.NeverTurnOffWithoutCamera)
                {
                    SetState(EngineState.Paused, "Kameradan görüntü alınamıyor; ekran kapatılmayacak");
                    _pausedUntil = now + CameraRetryDelay;
                    return ActiveTickMs;
                }

                await GoAwayAsync(s, "Kamera görüntüsü yok; boşta kalma süresine göre kapatıldı");
                return ActiveTickMs;
            }

            // Görüntü gelmediği süre, yüz görülmeyen süreye sayılmaz.
            _lastFaceAtWatch += TimeSpan.FromMilliseconds(Math.Min(s.CheckIntervalMs, (now - _lastFrameAt).TotalMilliseconds));
            SetState(EngineState.Watching, "Kamera görüntüsü bekleniyor");
            return s.CheckIntervalMs;
        }

        _lastFrameAt = now;

        if (result.FaceCount > 0)
        {
            _lastFaceAtWatch = now;
            if (s.KeepDisplayOnWhilePresent)
            {
                NativeMethods.ResetDisplayIdleTimer();
            }

            SetState(EngineState.Watching, "Önünde biri var; ekran açık kalıyor");
            // Kullanıcı ekrandayken daha seyrek kontrol ederek işlemciyi rahatlat.
            return Math.Min(s.CheckIntervalMs * 2, 5000);
        }

        // Tam ekran oyun/videoda (ör. uzun ara sahne) daha uzun bekle.
        bool fullscreen = UpdateFullscreen();
        if (fullscreen && s.NeverTurnOffInFullscreen)
        {
            await SafeCloseCameraAsync();
            SetState(EngineState.Paused, "Tam ekran uygulama açık; ekran kapatılmayacak");
            _pausedUntil = now.AddSeconds(5);
            return ActiveTickMs;
        }

        int limit = fullscreen ? s.AbsentBeforeOffFullscreenSeconds : s.AbsentBeforeOffSeconds;
        var absent = now - _lastFaceAtWatch;
        if (absent.TotalSeconds >= limit)
        {
            await GoAwayAsync(s, "Yüz görülmedi; ekran kapatıldı");
            return s.CheckIntervalMs;
        }

        int remaining = (int)Math.Ceiling(limit - absent.TotalSeconds);
        SetState(EngineState.Watching, $"Yüz görülmüyor; {remaining} sn içinde kapanacak{(fullscreen ? " (tam ekran)" : string.Empty)}");
        return s.CheckIntervalMs;
    }

    private bool UpdateFullscreen()
    {
        bool fullscreen = FullscreenDetector.IsFullscreenAppActive();
        lock (_gate)
        {
            _fullscreen = fullscreen;
        }

        return fullscreen;
    }

    private async Task GoAwayAsync(AppSettings s, string detail)
    {
        _display.TurnOff(s.OffMode, s.PowerOffDelaySeconds);
        _awayStartedAt = DateTime.Now;
        _consecutiveFaceFrames = 0;

        // Geri dönüşü görebilmek için kamera açık kalır (manuel testte de açılır).
        if (!_detector.IsOpen)
        {
            await TryOpenCameraAsync(s);
        }

        SetState(EngineState.Away, detail);
    }

    private async Task<int> AwayStepAsync(AppSettings s, DateTime now)
    {
        if (!_detector.IsOpen)
        {
            SetState(EngineState.Away, "Ekran kapalı; açmak için klavye/fareye dokun");
            return ActiveTickMs;
        }

        if (s.AwayCameraTimeoutMinutes > 0 && (now - _awayStartedAt).TotalMinutes >= s.AwayCameraTimeoutMinutes)
        {
            await SafeCloseCameraAsync();
            Logger.Info("Uzun süre dönülmedi; gizlilik için kamera kapatıldı.");
            SetState(EngineState.Away, "Ekran kapalı; açmak için klavye/fareye dokun");
            return ActiveTickMs;
        }

        if (now < _ignoreInputUntil)
        {
            SetState(EngineState.Away, "Elle kapatıldı; birkaç saniye sonra seni görünce açılacak");
            return s.CheckIntervalMs;
        }

        var result = await DetectAsync(s);
        if (result.FaceCount > 0)
        {
            _consecutiveFaceFrames++;
            if (_consecutiveFaceFrames >= s.ReturnConfirmFrames)
            {
                await WakeIfOffAsync(nudge: true);
                await SafeCloseCameraAsync();
                SetState(EngineState.Active, "Geri dönüş algılandı; ekran açıldı");
                return ActiveTickMs;
            }
        }
        else
        {
            _consecutiveFaceFrames = 0;
        }

        SetState(EngineState.Away, "Ekran kapalı; geri dönüş bekleniyor");
        return s.CheckIntervalMs;
    }

    private async Task<DetectionResult> DetectAsync(AppSettings s)
    {
        var result = await _detector.DetectAsync(s.MinFaceSizePercent);
        lock (_gate)
        {
            _lastDetection = result;
            if (result.FaceCount > 0)
            {
                _lastFaceSeen = DateTime.Now;
            }
        }

        return result;
    }

    private async Task<bool> TryOpenCameraAsync(AppSettings s)
    {
        if (_detector.IsOpen)
            return true;

        try
        {
            await _detector.OpenAsync(s.CameraId);
            _cameraUnavailable = false;
            return true;
        }
        catch (Exception ex)
        {
            if (!_cameraUnavailable)
            {
                Logger.Warn(ex.Message);
            }

            _cameraUnavailable = true;
            return false;
        }
    }

    private async Task WakeIfOffAsync(bool nudge)
    {
        if (_display.IsOff)
        {
            _display.TurnOn(nudge);
        }

        await Task.CompletedTask;
    }

    private async Task SafeCloseCameraAsync()
    {
        try
        {
            await _detector.CloseAsync();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Kamera kapatılamadı: {ex.Message}");
        }
    }

    private void SetState(EngineState state, string detail)
    {
        bool changed;
        lock (_gate)
        {
            changed = _state != state;
            _state = state;
            _detail = detail;
        }

        if (changed)
        {
            Logger.Info($"Durum: {state} - {detail}");
            StateChanged?.Invoke(state);
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            _sessionLocked = true;
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            _sessionLocked = false;
    }

    public async ValueTask DisposeAsync()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _enabled = false;
        _cts?.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // Kapanış sırasında beklenen.
            }
        }

        await _detector.DisposeAsync();
        _cts?.Dispose();
    }
}
