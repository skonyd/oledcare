namespace OledCare;

/// <summary>
/// Ekranları kapatır ve açar. Tüm pencere işlemleri UI iş parçacığında yürütülür.
/// </summary>
internal sealed class DisplayController : IDisposable
{
    private readonly Control _uiHost;
    private readonly List<BlackoutForm> _overlays = new();
    private System.Windows.Forms.Timer? _powerOffTimer;

    public DisplayController(Control uiHost)
    {
        _uiHost = uiHost;
    }

    /// <summary>Ekran şu an bu uygulama tarafından kapatılmış durumda mı.</summary>
    public bool IsOff { get; private set; }

    /// <summary>Monitör güç tasarrufuna alındı mı (perde dışında).</summary>
    public bool IsPoweredOff { get; private set; }

    public void TurnOff(OffMode mode, int powerOffDelaySeconds)
    {
        RunOnUi(() =>
        {
            if (IsOff)
                return;

            IsOff = true;
            Logger.Info($"Ekran kapatılıyor ({mode}).");

            switch (mode)
            {
                case OffMode.Overlay:
                    ShowOverlays();
                    break;

                case OffMode.OverlayThenPowerOff:
                    ShowOverlays();
                    _powerOffTimer = new System.Windows.Forms.Timer { Interval = Math.Max(1, powerOffDelaySeconds) * 1000 };
                    _powerOffTimer.Tick += (_, _) =>
                    {
                        StopPowerOffTimer();
                        if (IsOff)
                            PowerOffMonitors();
                    };
                    _powerOffTimer.Start();
                    break;

                case OffMode.PowerOff:
                    PowerOffMonitors();
                    break;
            }
        });
    }

    /// <summary>Ekranı geri açar.</summary>
    /// <param name="nudge">Monitör güç tasarrufundaysa sentetik fare hareketiyle uyandır.</param>
    public void TurnOn(bool nudge)
    {
        RunOnUi(() =>
        {
            if (!IsOff)
                return;

            StopPowerOffTimer();
            CloseOverlays();

            if (IsPoweredOff && nudge)
            {
                NativeMethods.SendMessage(_uiHost.Handle, NativeMethods.WM_SYSCOMMAND,
                    NativeMethods.SC_MONITORPOWER, NativeMethods.MONITOR_ON);
                NativeMethods.NudgeMouse();
            }

            IsOff = false;
            IsPoweredOff = false;
            Logger.Info("Ekran açıldı.");
        });
    }

    private void ShowOverlays()
    {
        foreach (var screen in Screen.AllScreens)
        {
            var overlay = new BlackoutForm(screen.Bounds);
            overlay.Show();
            _overlays.Add(overlay);
        }
    }

    private void CloseOverlays()
    {
        foreach (var overlay in _overlays)
        {
            overlay.Close();
            overlay.Dispose();
        }

        _overlays.Clear();
    }

    private void PowerOffMonitors()
    {
        IsPoweredOff = true;
        // PostMessage ile gönderilir; SendMessage bazı sürücülerde çağrıyı bekletebilir.
        NativeMethods.PostMessage(_uiHost.Handle, NativeMethods.WM_SYSCOMMAND,
            NativeMethods.SC_MONITORPOWER, NativeMethods.MONITOR_OFF);
        Logger.Info("Monitör güç tasarrufuna alındı.");
    }

    private void StopPowerOffTimer()
    {
        _powerOffTimer?.Stop();
        _powerOffTimer?.Dispose();
        _powerOffTimer = null;
    }

    private void RunOnUi(Action action)
    {
        if (_uiHost.IsDisposed)
            return;

        if (_uiHost.InvokeRequired)
            _uiHost.Invoke(action);
        else
            action();
    }

    public void Dispose()
    {
        RunOnUi(() =>
        {
            StopPowerOffTimer();
            CloseOverlays();
        });
    }

    /// <summary>Bir ekranı tamamen kaplayan siyah, imleçsiz pencere. OLED'de siyah piksel kapalıdır.</summary>
    private sealed class BlackoutForm : Form
    {
        private bool _cursorHidden;

        public BlackoutForm(Rectangle bounds)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            BackColor = Color.Black;
            ShowInTaskbar = false;
            TopMost = true;
            Text = "OledCare";
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TOOLWINDOW = 0x00000080;
                const int WS_EX_TOPMOST = 0x00000008;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            if (!_cursorHidden)
            {
                Cursor.Hide();
                _cursorHidden = true;
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_cursorHidden)
            {
                Cursor.Show();
                _cursorHidden = false;
            }

            base.OnFormClosed(e);
        }
    }
}
