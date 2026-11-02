namespace OledCare;

/// <summary>Uygulamanın yaşam döngüsü: tepsi simgesi, menü, durum penceresi ve motor.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly Control _uiHost = new();
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _toggleItem = new();
    private readonly DisplayController _display;
    private readonly PresenceEngine _engine;
    private readonly StatusForm _statusForm;
    private bool _exiting;

    public TrayApp(bool startMinimized, EventWaitHandle showSignal)
    {
        // UI iş parçacığına çağrı yapabilmek ve SC_MONITORPOWER mesajı göndermek için gizli bir pencere tanıtıcısı.
        _ = _uiHost.Handle;

        var settings = AppSettings.Load();
        _display = new DisplayController(_uiHost);
        _engine = new PresenceEngine(_display, settings);
        _engine.StateChanged += state => PostToUi(() => UpdateTray(state));

        _statusForm = new StatusForm(settings, _engine.GetSnapshot, () => _engine.Enabled);
        _statusForm.ToggleRequested += ToggleEngine;
        _statusForm.TestOffRequested += _engine.TurnOffNow;
        _statusForm.ExitRequested += ExitApp;
        _statusForm.SettingsSaved += _engine.UpdateSettings;
        _statusForm.FirstHiddenToTray += () =>
            _tray?.ShowBalloonTip(3000, "OledCare arka planda çalışıyor",
                "Durum penceresini açmak için tepsideki simgeye çift tıkla.", ToolTipIcon.Info);

        _tray = new NotifyIcon
        {
            Icon = IconFactory.For(EngineState.Stopped),
            Text = "OledCare",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _tray.DoubleClick += (_, _) => _statusForm.ShowFromTray();

        if (settings.StartEnabled)
            _engine.Start();
        UpdateTray(settings.StartEnabled ? EngineState.Active : EngineState.Stopped);

        if (!startMinimized)
            _statusForm.ShowFromTray();

        // İkinci kez çalıştırılırsa mevcut pencereyi öne getir.
        ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => PostToUi(_statusForm.ShowFromTray),
            null, Timeout.Infinite, executeOnlyOnce: false);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("Durum penceresini aç", null, (_, _) => _statusForm.ShowFromTray())
        {
            Font = new Font(menu.Font, FontStyle.Bold),
        };
        _toggleItem.Click += (_, _) => ToggleEngine();

        menu.Items.Add(open);
        menu.Items.Add(_toggleItem);
        menu.Items.Add("Ekranı şimdi kapat", null, (_, _) => _engine.TurnOffNow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => ExitApp());
        return menu;
    }

    private void ToggleEngine()
    {
        if (_engine.Enabled)
            _engine.Stop();
        else
            _engine.Start();

        UpdateTray(_engine.Enabled ? EngineState.Active : EngineState.Stopped);
    }

    private void UpdateTray(EngineState state)
    {
        if (_exiting)
            return;

        _tray.Icon = IconFactory.For(state);
        var text = $"OledCare - {StatusForm.StateText(state)}";
        _tray.Text = text.Length > 63 ? text[..63] : text;
        _toggleItem.Text = _engine.Enabled ? "İzlemeyi durdur" : "İzlemeyi başlat";
    }

    private void PostToUi(Action action)
    {
        if (_exiting || _uiHost.IsDisposed)
            return;

        try
        {
            _uiHost.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // Kapanış sırasında tanıtıcı yok edilmiş olabilir.
        }
    }

    private async void ExitApp()
    {
        if (_exiting)
            return;

        _exiting = true;
        Logger.Info("Uygulama kapatılıyor.");
        _tray.Visible = false;

        try
        {
            // Motor arka planda UI'ye çağrı yapabildiği için await ile bekliyoruz (UI kilitlenmesin).
            await _engine.DisposeAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Motor kapatılırken hata", ex);
        }

        _display.TurnOn(nudge: false);
        _display.Dispose();
        _statusForm.PrepareForExit();
        _statusForm.Close();
        _statusForm.Dispose();
        _tray.Dispose();
        _uiHost.Dispose();
        ExitThread();
    }
}
