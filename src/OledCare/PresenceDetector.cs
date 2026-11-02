using System.Diagnostics;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.FaceAnalysis;
using Windows.Media.MediaProperties;

namespace OledCare;

public sealed record CameraInfo(string Id, string Name)
{
    public override string ToString() => Name;
}

public readonly record struct DetectionResult(bool HasFrame, int FaceCount, double LargestFacePercent, TimeSpan Duration);

/// <summary>
/// Windows'un yerleşik yüz algılayıcısı (Windows.Media.FaceAnalysis) ile kamerada yüz arar.
/// Kamera yalnızca gerektiğinde açılır; görüntüler hiçbir yere kaydedilmez veya gönderilmez.
/// </summary>
internal sealed class PresenceDetector : IAsyncDisposable
{
    private MediaCapture? _capture;
    private MediaFrameReader? _reader;
    private FaceDetector? _faceDetector;
    private BitmapPixelFormat _detectorFormat = BitmapPixelFormat.Gray8;

    public bool IsOpen => _reader is not null;

    public string? OpenCameraName { get; private set; }

    public static async Task<IReadOnlyList<CameraInfo>> ListCamerasAsync()
    {
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        return devices.Select(d => new CameraInfo(d.Id, d.Name)).ToList();
    }

    /// <summary>Kamerayı açar. Kamera başka bir uygulamada kullanılıyorsa paylaşımlı okuma modunu dener.</summary>
    public async Task OpenAsync(string? cameraId)
    {
        if (IsOpen)
            return;

        _faceDetector ??= await CreateFaceDetectorAsync();

        var cameras = await ListCamerasAsync();
        if (cameras.Count == 0)
            throw new InvalidOperationException("Bilgisayarda kamera bulunamadı.");

        var camera = cameras.FirstOrDefault(c => c.Id == cameraId) ?? cameras[0];

        // Önce paylaşımlı okuma: Teams/Zoom kamerayı kullanırken bile çalışabilir ve onları engellemez.
        Exception? lastError = null;
        foreach (var sharingMode in new[] { MediaCaptureSharingMode.SharedReadOnly, MediaCaptureSharingMode.ExclusiveControl })
        {
            try
            {
                await OpenWithModeAsync(camera, sharingMode);
                OpenCameraName = camera.Name;
                Logger.Info($"Kamera açıldı: {camera.Name} ({sharingMode})");
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                await CloseAsync();
            }
        }

        throw new InvalidOperationException($"Kamera açılamadı: {lastError?.Message}", lastError);
    }

    private async Task OpenWithModeAsync(CameraInfo camera, MediaCaptureSharingMode sharingMode)
    {
        var capture = new MediaCapture();
        _capture = capture;

        await capture.InitializeAsync(new MediaCaptureInitializationSettings
        {
            VideoDeviceId = camera.Id,
            StreamingCaptureMode = StreamingCaptureMode.Video,
            MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            SharingMode = sharingMode,
        });

        var source = capture.FrameSources.Values
            .Where(s => s.Info.SourceKind == MediaFrameSourceKind.Color)
            .OrderBy(s => s.Info.MediaStreamType == Windows.Media.Capture.MediaStreamType.VideoPreview ? 0 : 1)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("Kamerada renkli görüntü kaynağı bulunamadı.");

        if (sharingMode == MediaCaptureSharingMode.ExclusiveControl)
        {
            await TrySelectSmallFormatAsync(source);
        }

        // NV12 istenir: MJPG gönderen kameraların görüntüsü Windows tarafından çözülür
        // ve yüz algılayıcı NV12'yi dönüştürmeden işleyebilir.
        var reader = await capture.CreateFrameReaderAsync(source, MediaEncodingSubtypes.Nv12);
        reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;

        var status = await reader.StartAsync();
        if (status != MediaFrameReaderStartStatus.Success)
        {
            reader.Dispose();
            throw new InvalidOperationException($"Görüntü okuyucu başlatılamadı: {status}");
        }

        _reader = reader;
    }

    /// <summary>İşlemci yükünü azaltmak için ~640 piksel genişliğe yakın en küçük formatı seçer.</summary>
    private static async Task TrySelectSmallFormatAsync(MediaFrameSource source)
    {
        try
        {
            var format = source.SupportedFormats
                .Where(f => f.VideoFormat.Width >= 320)
                .OrderBy(f => Math.Abs((int)f.VideoFormat.Width - 640))
                .ThenByDescending(f => f.FrameRate.Denominator == 0 ? 0 : (double)f.FrameRate.Numerator / f.FrameRate.Denominator)
                .FirstOrDefault();

            if (format is not null)
            {
                await source.SetFormatAsync(format);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Kamera formatı ayarlanamadı, varsayılan kullanılacak: {ex.Message}");
        }
    }

    private async Task<FaceDetector> CreateFaceDetectorAsync()
    {
        if (!FaceDetector.IsSupported)
            throw new PlatformNotSupportedException("Bu Windows sürümü yüz algılamayı desteklemiyor.");

        var detector = await FaceDetector.CreateAsync();
        var supported = FaceDetector.GetSupportedBitmapPixelFormats();
        _detectorFormat = supported.Contains(BitmapPixelFormat.Nv12) ? BitmapPixelFormat.Nv12
            : supported.Contains(BitmapPixelFormat.Gray8) ? BitmapPixelFormat.Gray8
            : supported[0];
        return detector;
    }

    /// <summary>Kameradaki en güncel karede yüz arar.</summary>
    public async Task<DetectionResult> DetectAsync(int minFaceSizePercent)
    {
        if (_reader is null || _faceDetector is null)
            return default;

        var sw = Stopwatch.StartNew();
        using var frame = _reader.TryAcquireLatestFrame();
        var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
        if (bitmap is null)
            return new DetectionResult(false, 0, 0, sw.Elapsed);

        SoftwareBitmap? converted = null;
        try
        {
            var input = bitmap;
            if (bitmap.BitmapPixelFormat != _detectorFormat)
            {
                converted = SoftwareBitmap.Convert(bitmap, _detectorFormat);
                input = converted;
            }

            var faces = await _faceDetector.DetectFacesAsync(input);
            double frameHeight = Math.Max(1, input.PixelHeight);
            var sizes = faces.Select(f => f.FaceBox.Height / frameHeight * 100.0).ToList();
            var qualifying = sizes.Where(s => s >= minFaceSizePercent).ToList();

            return new DetectionResult(
                HasFrame: true,
                FaceCount: qualifying.Count,
                LargestFacePercent: sizes.Count == 0 ? 0 : sizes.Max(),
                Duration: sw.Elapsed);
        }
        finally
        {
            // Karenin kendi bitmap'i frame ile birlikte serbest bırakılır.
            converted?.Dispose();
        }
    }

    /// <summary>Kamerayı kapatır; kamera ışığı söner.</summary>
    public async Task CloseAsync()
    {
        var reader = _reader;
        var capture = _capture;
        _reader = null;
        _capture = null;
        OpenCameraName = null;

        if (reader is not null)
        {
            try
            {
                await reader.StopAsync();
            }
            catch
            {
                // Kapatma sırasında oluşan hatalar önemsiz.
            }

            reader.Dispose();
        }

        capture?.Dispose();

        if (reader is not null)
        {
            Logger.Info("Kamera kapatıldı.");
        }
    }

    public async ValueTask DisposeAsync() => await CloseAsync();
}
