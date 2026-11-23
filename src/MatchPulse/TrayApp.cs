using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace MatchPulse;

/// <summary>
/// The tray icon and the main window (<see cref="MainWindow"/>). Saving settings writes the config file and rebuilds
/// the Event Sinks, so changes apply without a restart; detection settings apply from the next game start.
/// An invalid config file is logged and reported, and defaults apply until settings are saved.
/// </summary>
sealed class TrayApp : ApplicationContext
{
    static readonly Dictionary<TrayState, Icon> Icons = new()
    {
        [TrayState.GameNotRunning] = Dot(Color.Gray),
        [TrayState.GameRunning] = Dot(Color.Orange),
        [TrayState.MatchRunning] = Dot(Color.LimeGreen),
    };
    static readonly Icon PausedIcon = Paused();

    readonly string _configPath;
    readonly ILogger _log;
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    readonly HttpClient _download = new() { Timeout = TimeSpan.FromMinutes(10) };
    System.Threading.Timer? _updateTimer;
    readonly NotifyIcon _tray = new() { Visible = true };
    static readonly Icon AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icons[TrayState.MatchRunning];
    readonly SynchronizationContext _ui;
    readonly UiHub _hub;
    readonly CancellationTokenSource _stop = new();
    readonly Thread _watcher;
    MainWindow? _window, _liveWindow;
    Hotkey? _hotkey;
    string _balloonPage = "status";

    public TrayApp(string configPath, FileLog file, ILogger log, bool showWindow = false, WaitHandle? showRequested = null)
    {
        _configPath = configPath;
        _log = log;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _hub = new UiHub(() => Config, Apply, c => Program.Sinks(c, _http), Path.GetDirectoryName(configPath)!);
        _hub.WindowRequested += page => _ui.Post(_ => ShowLiveWindow(), null);
        file.Written += _hub.Logged;
        try
        {
            Config = Config.LoadOrDefault(configPath);
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
        {
            log.LogError(e, "Config {Path} is invalid; using defaults", configPath);
            Config = new Config([]);
            _hub.ConfigInvalid();
            MessageBox.Show($"{Ui.T(Config.UiLanguage, "ConfigInvalid")}\n\n{configPath}\n{e.Message}", "MatchPulse", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        Dispatcher = new EventDispatcher(Sinks(), log);
        Dispatcher.Delivered += _hub.Delivered;
        _hub.Problem += (key, page) => _ui.Post(_ => Notify(key, page), null);
        _tray.DoubleClick += (_, _) => ShowWindow();
        _tray.BalloonTipClicked += (_, _) => ShowWindow(_balloonPage);
        Status.Changed += _ => _ui.Post(_ => ShowState(), null);
        RefreshTray();
        log.LogInformation("Tray app started, config {Path}", configPath);
        _watcher = new Thread(() => GameWatcher.Run(() => Config, Dispatcher.Dispatch, log, _stop.Token, running =>
            {
                Status.SetGameRunning(running);
                _hub.SetGameRunning(running);
            }, _hub.KillPicture))
            { IsBackground = true, Name = "Game Watcher" };
        _watcher.Start();
        _hotkey = new Hotkey(() => SetActive(!Config.Active));
        RegisterHotkey();
        StartUpdates();
        if (showWindow) ShowWindow();
        if (showRequested is not null)
            ThreadPool.RegisterWaitForSingleObject(showRequested, (_, _) => _ui.Post(_ => ShowWindow(), null), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public Config Config { get; private set; }

    /// <summary>Tray state, from the Game Watcher and the Match Game Events.</summary>
    public AppStatus Status { get; } = new();

    /// <summary>The Event Dispatcher; its Event Sinks are replaced when settings are saved.</summary>
    public EventDispatcher Dispatcher { get; }

    IEnumerable<IEventSink> Sinks() => [Status, _hub, .. Program.Sinks(Config, _http, _hub.Delivered)];

    // Called by the UiHub on a worker thread; throws InvalidDataException for invalid settings.
    void Apply(Config config)
    {
        config.Save(_configPath);
        var before = Config;
        Config = config;
        _ui.Post(_ =>
        {
            if (before.Active != config.Active) ShowActive();
            if (before.ToggleHotkey != config.ToggleHotkey) RegisterHotkey();
        }, null);
        Dispatcher.ReplaceSinks(Sinks());
        _log.LogInformation("Settings applied: game language {Language}, Kill Marker colour {Color}, {Webhooks} webhook(s), {Scripts} script(s)",
            config.GameLanguage, config.KillMarkerColor, config.Webhooks.Count, config.Scripts.Count);
        _ui.Post(_ => RefreshTray(), null);
    }

    void StartUpdates()
    {
        // MATCHPULSE_UPDATE_URL points the updater at a test release site (see UpdateTests and the ticket 17 notes).
        var updater = new Updater(_download, Updater.AppVersion, Environment.GetEnvironmentVariable("MATCHPULSE_UPDATE_URL") ?? Updater.DefaultBaseUrl);
        var updates = new UpdateService(updater, () => Config, () => Status.State != TrayState.GameNotRunning, Updater.IsInstalled,
            installer =>
            {
                Updater.StartInstaller(installer);
                _ui.Post(_ => ExitThread(), null); // the installer replaces the files and starts MatchPulse again
            },
            Path.Combine(Path.GetTempPath(), "MatchPulse-update"), _log);
        _hub.Updates = updates;
        updates.Changed += () =>
        {
            if ((string?)updates.State()["status"] == "available" && !Config.AutoInstallUpdates)
                _ui.Post(_ => Notify("UpdateAvailable", "setup"), null);
        };
        // First check shortly after start (if enabled), then once a minute for the daily check and a waiting install.
        _updateTimer = new System.Threading.Timer(_ => _ = updates.Tick(), null, TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(1));

        // "Updated to version X" once after an update.
        var marker = Path.Combine(_hub.DataFolder, "version.txt");
        try
        {
            var before = File.Exists(marker) && Version.TryParse(File.ReadAllText(marker).Trim(), out var v) ? v : null;
            if (before is not null && before < updater.Current)
            {
                _balloonPage = "status";
                _tray.ShowBalloonTip(5000, "MatchPulse", string.Format(Ui.T(Config.UiLanguage, "Updated"), updater.Current), ToolTipIcon.Info);
            }
            if (before != updater.Current) File.WriteAllText(marker, updater.Current.ToString());
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Turns MatchPulse on or off (hotkey, tray menu); the window follows.</summary>
    void SetActive(bool active)
    {
        if (active == Config.Active) return;
        try
        {
            Apply(Config with { Active = active });
            _hub.SettingsChanged();
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _log.LogError(e, "Could not switch MatchPulse {State}", active ? "on" : "off");
        }
    }

    void ShowActive()
    {
        _log.LogInformation("MatchPulse {State}", Config.Active ? "activated" : "deactivated");
        Overlay.Show(Ui.T(Config.UiLanguage, Config.Active ? "Activated" : "Deactivated"), Config.Active ? Color.LimeGreen : Color.Gray);
        RefreshTray();
    }

    void RegisterHotkey()
    {
        if (_hotkey is null) return;
        if (!_hotkey.Set(Config.ToggleHotkey))
            _log.LogWarning("Hotkey {Hotkey} could not be registered: another program probably uses it", Config.ToggleHotkey);
        else if (Config.ToggleHotkey != "")
            _log.LogInformation("Hotkey {Hotkey} turns MatchPulse on and off", Config.ToggleHotkey);
    }

    void ShowWindow(string? page = null)
    {
        if (_window is { IsDisposed: false })
        {
            if (page is not null) _window.Navigate(page);
            if (_window.WindowState == FormWindowState.Minimized) _window.WindowState = FormWindowState.Normal;
            _window.Activate();
            return;
        }
        _window = new MainWindow(_hub, AppIcon, page);
        _window.Show();
    }

    // The live monitor in a window of its own, e.g. on a second screen; it never takes the focus from the game.
    void ShowLiveWindow()
    {
        if (_liveWindow is { IsDisposed: false }) return;
        _liveWindow = new MainWindow(_hub, AppIcon, "live", passive: true);
        _liveWindow.Show();
    }

    void Notify(string key, string page)
    {
        if (!Config.NotifyOnProblems) return;
        _balloonPage = page;
        _tray.ShowBalloonTip(5000, "MatchPulse", Ui.T(Config.UiLanguage, key), ToolTipIcon.Warning);
    }

    void RefreshTray()
    {
        string T(string key) => Ui.T(Config.UiLanguage, key);
        _tray.ContextMenuStrip?.Dispose();
        _tray.ContextMenuStrip = new ContextMenuStrip();
        _tray.ContextMenuStrip.Items.Add(T("Open"), null, (_, _) => ShowWindow());
        _tray.ContextMenuStrip.Items.Add(new ToolStripMenuItem(T("Active"), null, (_, _) => SetActive(!Config.Active))
        {
            Checked = Config.Active,
            ShortcutKeyDisplayString = Config.ToggleHotkey,
        });
        _tray.ContextMenuStrip.Items.Add(T("Exit"), null, (_, _) => ExitThread());
        ShowState();
    }

    void ShowState()
    {
        var state = Status.State;
        _tray.Icon = Config.Active ? Icons[state] : PausedIcon;
        _tray.Text = $"MatchPulse - {Ui.T(Config.UiLanguage, Config.Active ? state.ToString() : "Paused")}";
    }

    protected override void ExitThreadCore()
    {
        _updateTimer?.Dispose();
        _hotkey?.Dispose();
        _window?.Close();
        _liveWindow?.Close();
        _tray.Visible = false;
        _tray.Dispose();
        _stop.Cancel();
        _watcher.Join(TimeSpan.FromSeconds(5));
        Dispatcher.CompleteAsync().Wait(TimeSpan.FromSeconds(5));
        _http.Dispose();
        _download.Dispose();
        base.ExitThreadCore();
    }

    // Paused: a grey ring with two pause bars.
    static Icon Paused()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        using (var pen = new Pen(Color.Gray, 2))
        using (var bars = new SolidBrush(Color.Gray))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.DrawEllipse(pen, 1, 1, 13, 13);
            g.FillRectangle(bars, 5, 4, 2, 8);
            g.FillRectangle(bars, 9, 4, 2, 8);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    static Icon Dot(Color color)
    {
        using var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(color))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.FillEllipse(brush, 1, 1, 14, 14);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }
}
