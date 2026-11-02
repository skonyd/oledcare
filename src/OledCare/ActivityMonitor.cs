using System.Diagnostics;

namespace OledCare;

/// <summary>
/// Kullanıcının bilgisayarı aktif kullanıp kullanmadığını izler:
/// klavye/fare (GetLastInputInfo) ve Xbox uyumlu kumandalar (XInput).
/// </summary>
internal sealed class ActivityMonitor
{
    private readonly uint[] _lastPacket = new uint[4];
    private readonly bool[] _connected = new bool[4];
    private bool _xinputAvailable = true;
    private long _lastGamepadActivityTicks = Stopwatch.GetTimestamp();

    /// <summary>En son klavye, fare veya kumanda hareketinden bu yana geçen süre.</summary>
    public TimeSpan GetIdleTime()
    {
        PollGamepads();

        var inputIdle = NativeMethods.GetInputIdleTime();
        var gamepadIdle = Stopwatch.GetElapsedTime(_lastGamepadActivityTicks);
        return inputIdle < gamepadIdle ? inputIdle : gamepadIdle;
    }

    private void PollGamepads()
    {
        if (!_xinputAvailable)
            return;

        try
        {
            for (int i = 0; i < 4; i++)
            {
                const int ErrorSuccess = 0;
                if (NativeMethods.XInputGetState(i, out var state) != ErrorSuccess)
                {
                    _connected[i] = false;
                    continue;
                }

                // Paket numarası her durum değişikliğinde artar; yeni bağlanan kumanda etkinlik sayılmaz.
                if (_connected[i] && state.dwPacketNumber != _lastPacket[i])
                {
                    _lastGamepadActivityTicks = Stopwatch.GetTimestamp();
                }

                _connected[i] = true;
                _lastPacket[i] = state.dwPacketNumber;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _xinputAvailable = false;
            Logger.Warn("XInput bulunamadı, kumanda etkinliği izlenmeyecek.");
        }
    }
}
