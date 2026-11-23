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

    readonly string _configPath;
    readonly ILogger _log;
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    readonly NotifyIcon _tray = new() { Visible = true };
    readonly SynchronizationContext _ui;
    readonly UiHub _hub;
    readonly CancellationTokenSource _stop = new();
    readonly Thread _watcher;
    MainWindow? _window;
    string _balloonPage = "status";

    public TrayApp(string configPath, FileLog file, ILogger log, bool showWindow = false)
    {
        _configPath = configPath;
        _log = log;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _hub = new UiHub(() => Config, Apply, c => Program.Sinks(c, _http), DiagnosticSnapshots.DefaultFolder);
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
            }))
            { IsBackground = true, Name = "Game Watcher" };
        _watcher.Start();
        if (showWindow) ShowWindow();
    }

    public Config Config { get; private set; }

    /// <summary>Tray state, from the Game Watcher and the Match Game Events.</summary>
    public AppStatus Status { get; } = new();

    /// <summary>The Event Dispatcher; its Event Sinks are replaced when settings are saved.</summary>
    public EventDispatcher Dispatcher { get; }

    IEnumerable<IEventSink> Sinks() => [Status, _hub, .. Program.Sinks(Config, _http)];

    // Called by the UiHub on a worker thread; throws InvalidDataException for invalid settings.
    void Apply(Config config)
    {
        config.Save(_configPath);
        Config = config;
        Dispatcher.ReplaceSinks(Sinks());
        _log.LogInformation("Settings applied: game language {Language}, Kill Marker colour {Color}, {Webhooks} webhook(s), {Scripts} script(s)",
            config.GameLanguage, config.KillMarkerColor, config.Webhooks.Count, config.Scripts.Count);
        _ui.Post(_ => RefreshTray(), null);
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
        _window = new MainWindow(_hub, DiagnosticSnapshots.DefaultFolder, Icons[TrayState.MatchRunning], page);
        _window.Show();
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
        _tray.ContextMenuStrip.Items.Add(T("Exit"), null, (_, _) => ExitThread());
        ShowState();
    }

    void ShowState()
    {
        var state = Status.State;
        _tray.Icon = Icons[state];
        _tray.Text = $"MatchPulse - {Ui.T(Config.UiLanguage, state.ToString())}";
    }

    protected override void ExitThreadCore()
    {
        _window?.Close();
        _tray.Visible = false;
        _tray.Dispose();
        _stop.Cancel();
        _watcher.Join(TimeSpan.FromSeconds(5));
        Dispatcher.CompleteAsync().Wait(TimeSpan.FromSeconds(5));
        _http.Dispose();
        base.ExitThreadCore();
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
