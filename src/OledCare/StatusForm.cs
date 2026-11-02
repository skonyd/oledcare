using System.Diagnostics;

namespace OledCare;

/// <summary>Durum penceresi: canlı bilgiler, kontrol düğmeleri ve ayarlar.</summary>
internal sealed class StatusForm : Form
{
    private readonly Func<EngineSnapshot> _snapshot;
    private readonly Func<bool> _isEnabled;
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 500 };
    private readonly Process _process = Process.GetCurrentProcess();
    private TimeSpan _lastCpuTime;
    private DateTime _lastCpuSample = DateTime.UtcNow;
    private double _cpuPercent;
    private bool _exiting;
    private bool _trayHintShown;

    // Durum
    private readonly Panel _stateDot = new() { Size = new Size(18, 18), Margin = new Padding(0, 6, 8, 0) };
    private readonly Label _stateLabel = new() { AutoSize = true, Font = new Font("Segoe UI", 14f, FontStyle.Bold) };
    private readonly Label _detailLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(26, 0, 0, 8) };
    private readonly Dictionary<string, Label> _values = new();

    // Düğmeler
    private readonly Button _toggleButton = new() { AutoSize = true, MinimumSize = new Size(110, 32) };
    private readonly Button _testOffButton = new() { Text = "Ekranı şimdi kapat", AutoSize = true, MinimumSize = new Size(110, 32) };
    private readonly Button _hideButton = new() { Text = "Simge durumuna küçült", AutoSize = true, MinimumSize = new Size(110, 32) };
    private readonly Button _exitButton = new() { Text = "Çıkış", AutoSize = true, MinimumSize = new Size(80, 32) };

    // Ayarlar
    private readonly NumericUpDown _idleSeconds = NewNumber(5, 3600);
    private readonly NumericUpDown _absentSeconds = NewNumber(3, 3600);
    private readonly NumericUpDown _absentFullscreenSeconds = NewNumber(3, 3600);
    private readonly NumericUpDown _checkIntervalMs = NewNumber(250, 10000, 250);
    private readonly NumericUpDown _returnFrames = NewNumber(1, 10);
    private readonly NumericUpDown _minFacePercent = NewNumber(1, 80);
    private readonly NumericUpDown _powerOffDelay = NewNumber(1, 3600);
    private readonly NumericUpDown _awayCameraMinutes = NewNumber(0, 1440);
    private readonly ComboBox _offMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly ComboBox _camera = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly CheckBox _neverOffInFullscreen = new() { Text = "Tam ekran oyun/video açıkken ekranı hiç kapatma (kamera kullanılmaz)", AutoSize = true };
    private readonly CheckBox _keepDisplayOn = new() { Text = "Kamera beni görürken Windows ekranı kapatmasın", AutoSize = true };
    private readonly CheckBox _neverWithoutCamera = new() { Text = "Kamera kullanılamıyorsa ekranı hiç kapatma", AutoSize = true };
    private readonly CheckBox _startEnabled = new() { Text = "Açılışta izlemeye otomatik başla", AutoSize = true };
    private readonly CheckBox _startWithWindows = new() { Text = "Windows ile birlikte başlat (simge durumunda)", AutoSize = true };
    private readonly CheckBox _closeToTray = new() { Text = "X düğmesi uygulamayı kapatmasın, tepsiye küçültsün", AutoSize = true };
    private readonly Button _saveButton = new() { Text = "Ayarları kaydet", AutoSize = true, MinimumSize = new Size(130, 32) };
    private readonly Label _saveStatus = new() { AutoSize = true, ForeColor = Color.SeaGreen, Margin = new Padding(8, 9, 0, 0) };

    private AppSettings _settings;

    public StatusForm(AppSettings settings, Func<EngineSnapshot> snapshot, Func<bool> isEnabled)
    {
        _settings = settings.Clone();
        _snapshot = snapshot;
        _isEnabled = isEnabled;
        _lastCpuTime = _process.TotalProcessorTime;

        Text = "OledCare";
        Icon = IconFactory.For(EngineState.Active);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        MinimumSize = new Size(620, 600);
        Size = new Size(680, 820);

        BuildLayout();
        LoadSettingsIntoControls();

        _toggleButton.Click += (_, _) => ToggleRequested?.Invoke();
        _testOffButton.Click += (_, _) => TestOffRequested?.Invoke();
        _hideButton.Click += (_, _) => HideToTray();
        _exitButton.Click += (_, _) => ExitRequested?.Invoke();
        _saveButton.Click += (_, _) => SaveSettings();
        _offMode.SelectedIndexChanged += (_, _) => UpdateOffModeControls();

        _refreshTimer.Tick += (_, _) => RefreshStatus();
        RefreshStatus();
    }

    public event Action? ToggleRequested;

    public event Action? TestOffRequested;

    public event Action? ExitRequested;

    public event Action<AppSettings>? SettingsSaved;

    /// <summary>Tepsiye küçültme ilk kez yapıldığında bilgi balonu göstermek için.</summary>
    public event Action? FirstHiddenToTray;

    public void PrepareForExit() => _exiting = true;

    public void ShowFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;

        Activate();
        BringToFront();
    }

    private void HideToTray()
    {
        Hide();
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            FirstHiddenToTray?.Invoke();
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        // Pencere gizliyken bilgileri güncellemeye gerek yok.
        if (Visible)
        {
            RefreshStatus();
            _refreshTimer.Start();
        }
        else
        {
            _refreshTimer.Stop();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && Visible)
        {
            HideToTray();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            if (_settings.CloseToTray)
                HideToTray();
            else
                ExitRequested?.Invoke();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await LoadCamerasAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer.Dispose();
            _process.Dispose();
        }

        base.Dispose(disposing);
    }

    // ---------------- Yerleşim ----------------

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(16),
            AutoScroll = true,
        };

        // Başlık
        var header = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        _stateDot.Paint += (_, pe) =>
        {
            pe.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(_stateDot.Tag is Color c ? c : Color.Gray);
            pe.Graphics.FillEllipse(brush, 0, 0, _stateDot.Width - 1, _stateDot.Height - 1);
        };
        header.Controls.Add(_stateDot);
        header.Controls.Add(_stateLabel);
        root.Controls.Add(header);
        root.Controls.Add(_detailLabel);

        // Bilgi tablosu
        var info = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 12) };
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        info.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (var key in new[]
                 {
                     "Kamera", "Son yüz algılama", "Son kontrol", "Boşta geçen süre",
                     "Tam ekran uygulama", "Ekran", "Kapatma yöntemi", "Uygulama CPU / bellek",
                 })
        {
            var name = new Label { Text = key, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 4, 0, 4) };
            var value = new Label { Text = "-", AutoSize = true, Margin = new Padding(0, 4, 0, 4) };
            info.Controls.Add(name);
            info.Controls.Add(value);
            _values[key] = value;
        }

        root.Controls.Add(info);

        // Düğmeler
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 0, 0, 12) };
        buttons.Controls.AddRange(new Control[] { _toggleButton, _testOffButton, _hideButton, _exitButton });
        root.Controls.Add(buttons);

        // Ayarlar
        var group = new GroupBox { Text = "Ayarlar", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(12) };
        var grid = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Top };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddRow(grid, "Boşta kalınca kontrole başla (sn)", _idleSeconds);
        AddRow(grid, "Yüz görülmezse kapat (sn)", _absentSeconds);
        AddRow(grid, "Tam ekranda yüz görülmezse kapat (sn)", _absentFullscreenSeconds);
        AddRow(grid, "Kontrol aralığı (ms)", _checkIntervalMs);
        AddRow(grid, "Geri dönüş için gereken kare", _returnFrames);
        AddRow(grid, "En küçük yüz boyutu (% görüntü)", _minFacePercent);
        AddRow(grid, "Kapatma yöntemi", _offMode);
        AddRow(grid, "Perdeden sonra güç kapatma (sn)", _powerOffDelay);
        AddRow(grid, "Ekran kapalıyken kamera süresi (dk, 0 = sınırsız)", _awayCameraMinutes);

        var cameraRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var refreshCameras = new Button { Text = "Yenile", AutoSize = true };
        refreshCameras.Click += async (_, _) => await LoadCamerasAsync();
        cameraRow.Controls.Add(_camera);
        cameraRow.Controls.Add(refreshCameras);
        AddRow(grid, "Kamera", cameraRow);

        foreach (var cb in new[] { _neverOffInFullscreen, _keepDisplayOn, _neverWithoutCamera, _startEnabled, _startWithWindows, _closeToTray })
        {
            cb.Margin = new Padding(0, 4, 0, 4);
            grid.Controls.Add(cb);
            grid.SetColumnSpan(cb, 2);
        }

        var saveRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 10, 0, 0) };
        var openLog = new LinkLabel { Text = "Günlük dosyasını aç", AutoSize = true, Margin = new Padding(16, 9, 0, 0) };
        openLog.LinkClicked += (_, _) => OpenLog();
        saveRow.Controls.Add(_saveButton);
        saveRow.Controls.Add(_saveStatus);
        saveRow.Controls.Add(openLog);
        grid.Controls.Add(saveRow);
        grid.SetColumnSpan(saveRow, 2);

        group.Controls.Add(grid);
        root.Controls.Add(group);

        _offMode.Items.AddRange(new object[]
        {
            new OffModeItem(OffMode.Overlay, "Siyah perde (anında açılır)"),
            new OffModeItem(OffMode.OverlayThenPowerOff, "Siyah perde + monitörü güç tasarrufuna al"),
            new OffModeItem(OffMode.PowerOff, "Sadece monitörü güç tasarrufuna al"),
        });

        Controls.Add(root);
    }

    private static void AddRow(TableLayoutPanel grid, string label, Control control)
    {
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 7, 8, 4) });
        control.Margin = new Padding(0, 3, 0, 3);
        grid.Controls.Add(control);
    }

    private static NumericUpDown NewNumber(int min, int max, int increment = 1)
        => new() { Minimum = min, Maximum = max, Increment = increment, Width = 100 };

    // ---------------- Ayarlar ----------------

    private void LoadSettingsIntoControls()
    {
        _idleSeconds.Value = _settings.IdleBeforeCheckSeconds;
        _absentSeconds.Value = _settings.AbsentBeforeOffSeconds;
        _absentFullscreenSeconds.Value = _settings.AbsentBeforeOffFullscreenSeconds;
        _checkIntervalMs.Value = _settings.CheckIntervalMs;
        _returnFrames.Value = _settings.ReturnConfirmFrames;
        _minFacePercent.Value = _settings.MinFaceSizePercent;
        _powerOffDelay.Value = _settings.PowerOffDelaySeconds;
        _awayCameraMinutes.Value = _settings.AwayCameraTimeoutMinutes;
        _offMode.SelectedItem = _offMode.Items.Cast<OffModeItem>().First(i => i.Mode == _settings.OffMode);
        _neverOffInFullscreen.Checked = _settings.NeverTurnOffInFullscreen;
        _keepDisplayOn.Checked = _settings.KeepDisplayOnWhilePresent;
        _neverWithoutCamera.Checked = _settings.NeverTurnOffWithoutCamera;
        _startEnabled.Checked = _settings.StartEnabled;
        _closeToTray.Checked = _settings.CloseToTray;

        try
        {
            _startWithWindows.Checked = StartupManager.IsEnabled();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Başlangıç ayarı okunamadı: {ex.Message}");
        }

        UpdateOffModeControls();
    }

    private void UpdateOffModeControls()
        => _powerOffDelay.Enabled = (_offMode.SelectedItem as OffModeItem)?.Mode == OffMode.OverlayThenPowerOff;

    private async Task LoadCamerasAsync()
    {
        try
        {
            var cameras = await PresenceDetector.ListCamerasAsync();
            _camera.Items.Clear();
            _camera.Items.Add(new CameraInfo(string.Empty, "Varsayılan kamera"));
            foreach (var cam in cameras)
                _camera.Items.Add(cam);

            _camera.SelectedItem = _camera.Items.Cast<CameraInfo>()
                .FirstOrDefault(c => !string.IsNullOrEmpty(_settings.CameraId) && c.Id == _settings.CameraId)
                ?? _camera.Items[0];
        }
        catch (Exception ex)
        {
            Logger.Warn($"Kameralar listelenemedi: {ex.Message}");
        }
    }

    private void SaveSettings()
    {
        var s = _settings.Clone();
        s.IdleBeforeCheckSeconds = (int)_idleSeconds.Value;
        s.AbsentBeforeOffSeconds = (int)_absentSeconds.Value;
        s.AbsentBeforeOffFullscreenSeconds = (int)_absentFullscreenSeconds.Value;
        s.CheckIntervalMs = (int)_checkIntervalMs.Value;
        s.ReturnConfirmFrames = (int)_returnFrames.Value;
        s.MinFaceSizePercent = (int)_minFacePercent.Value;
        s.PowerOffDelaySeconds = (int)_powerOffDelay.Value;
        s.AwayCameraTimeoutMinutes = (int)_awayCameraMinutes.Value;
        s.OffMode = (_offMode.SelectedItem as OffModeItem)?.Mode ?? OffMode.OverlayThenPowerOff;
        s.NeverTurnOffInFullscreen = _neverOffInFullscreen.Checked;
        s.KeepDisplayOnWhilePresent = _keepDisplayOn.Checked;
        s.NeverTurnOffWithoutCamera = _neverWithoutCamera.Checked;
        s.StartEnabled = _startEnabled.Checked;
        s.CloseToTray = _closeToTray.Checked;
        if (_camera.SelectedItem is CameraInfo cam)
            s.CameraId = string.IsNullOrEmpty(cam.Id) ? null : cam.Id;

        try
        {
            s.Save();
            StartupManager.SetEnabled(_startWithWindows.Checked);
            _settings = s;
            SettingsSaved?.Invoke(s.Clone());
            _saveStatus.ForeColor = Color.SeaGreen;
            _saveStatus.Text = $"Kaydedildi ({DateTime.Now:HH:mm:ss})";
        }
        catch (Exception ex)
        {
            Logger.Error("Ayarlar kaydedilemedi", ex);
            _saveStatus.ForeColor = Color.Firebrick;
            _saveStatus.Text = "Kaydedilemedi: " + ex.Message;
        }
    }

    private static void OpenLog()
    {
        try
        {
            if (!File.Exists(Logger.FilePath))
                Logger.Info("Günlük dosyası oluşturuldu.");

            Process.Start(new ProcessStartInfo(Logger.FilePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Günlük dosyası açılamadı: " + ex.Message, "OledCare", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ---------------- Canlı durum ----------------

    private void RefreshStatus()
    {
        var snap = _snapshot();
        bool enabled = _isEnabled();

        _stateLabel.Text = StateText(snap.State);
        _detailLabel.Text = snap.Detail;
        _stateDot.Tag = IconFactory.ColorFor(snap.State);
        _stateDot.Invalidate();
        Icon = IconFactory.For(snap.State);

        _toggleButton.Text = enabled ? "Durdur" : "Başlat";

        _values["Kamera"].Text = snap.CameraOpen ? $"Açık ({snap.CameraName})" : "Kapalı";
        _values["Son yüz algılama"].Text = snap.LastFaceSeen is { } seen ? $"{seen:HH:mm:ss} ({Ago(seen)} önce)" : "Henüz yok";
        _values["Son kontrol"].Text = snap.LastDetection.HasFrame
            ? $"{snap.LastDetection.FaceCount} yüz, en büyük %{snap.LastDetection.LargestFacePercent:0}, {snap.LastDetection.Duration.TotalMilliseconds:0} ms"
            : "-";
        _values["Boşta geçen süre"].Text = FormatSpan(snap.IdleTime);
        _values["Tam ekran uygulama"].Text = !snap.FullscreenActive ? "Hayır"
            : _settings.NeverTurnOffInFullscreen ? "Evet (ekran kapatılmıyor)"
            : $"Evet (kamera ile kontrol, {_settings.AbsentBeforeOffFullscreenSeconds} sn)";
        _values["Ekran"].Text = !snap.DisplayOff ? "Açık" : snap.DisplayPoweredOff ? "Kapalı (güç tasarrufu)" : "Kapalı (siyah perde)";
        _values["Kapatma yöntemi"].Text = (_offMode.Items.Cast<OffModeItem>().FirstOrDefault(i => i.Mode == _settings.OffMode))?.Text ?? "-";

        SampleCpu();
        _values["Uygulama CPU / bellek"].Text = $"%{_cpuPercent:0.0} / {_process.WorkingSet64 / (1024 * 1024)} MB";
    }

    private void SampleCpu()
    {
        _process.Refresh();
        var now = DateTime.UtcNow;
        var cpu = _process.TotalProcessorTime;
        double elapsedMs = (now - _lastCpuSample).TotalMilliseconds;
        if (elapsedMs >= 1000)
        {
            _cpuPercent = (cpu - _lastCpuTime).TotalMilliseconds / (elapsedMs * Environment.ProcessorCount) * 100.0;
            _lastCpuTime = cpu;
            _lastCpuSample = now;
        }
    }

    public static string StateText(EngineState state) => state switch
    {
        EngineState.Active => "Kullanılıyor",
        EngineState.Watching => "Kamera ile kontrol ediliyor",
        EngineState.Away => "Ekran kapalı",
        EngineState.Paused => "Beklemede",
        _ => "Durduruldu",
    };

    private static string Ago(DateTime time) => FormatSpan(DateTime.Now - time);

    private static string FormatSpan(TimeSpan span)
        => span.TotalHours >= 1 ? $"{(int)span.TotalHours} sa {span.Minutes} dk"
            : span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes} dk {span.Seconds} sn"
            : $"{span.Seconds} sn";

    private sealed record OffModeItem(OffMode Mode, string Text)
    {
        public override string ToString() => Text;
    }
}
