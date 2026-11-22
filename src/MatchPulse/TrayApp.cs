using System.Drawing;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;

namespace MatchPulse;

/// <summary>
/// The tray icon and settings window. Saving settings writes the config file and rebuilds the Event Sinks,
/// so changes apply without a restart. Detection dispatches through <see cref="Dispatcher"/>.
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
    readonly string _logPath;
    readonly ILogger _log;
    readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    readonly NotifyIcon _tray = new() { Visible = true };
    readonly SynchronizationContext _ui;
    SettingsForm? _settings;

    public TrayApp(string configPath, string logPath, ILogger log)
    {
        _configPath = configPath;
        _logPath = logPath;
        _log = log;
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        Config = Config.LoadOrDefault(configPath);
        Dispatcher = new EventDispatcher(Sinks(), log);
        _tray.DoubleClick += (_, _) => ShowSettings();
        Status.Changed += _ => _ui.Post(_ => ShowState(), null);
        RefreshTray();
        log.LogInformation("Tray app started, config {Path}", configPath);
    }

    public Config Config { get; private set; }

    /// <summary>Tray state. The Game Watcher (ticket 05) calls <see cref="AppStatus.SetGameRunning"/> on it.</summary>
    public AppStatus Status { get; } = new();

    /// <summary>Current Event Dispatcher; replaced when settings are saved, so read it per Game Event.</summary>
    public EventDispatcher Dispatcher { get; private set; }

    // Ticket 06: add one Script sink per Config.Scripts entry here.
    IEnumerable<IEventSink> Sinks() => [Status, .. Config.Webhooks.Select(url => new WebhookSink(_http, url))];

    void Apply(Config config)
    {
        config.Save(_configPath);
        Config = config;
        var old = Dispatcher;
        Dispatcher = new EventDispatcher(Sinks(), _log);
        _ = old.CompleteAsync();
        _log.LogInformation("Settings applied: game language {Language}, {Webhooks} webhook(s), {Scripts} script(s)",
            config.GameLanguage, config.Webhooks.Count, config.Scripts.Count);
        RefreshTray();
    }

    void ShowSettings()
    {
        if (_settings is { IsDisposed: false }) { _settings.Activate(); return; }
        _settings = new SettingsForm(Config, _logPath, Apply);
        _settings.Show();
    }

    void RefreshTray()
    {
        string T(string key) => Ui.T(Config.UiLanguage, key);
        _tray.ContextMenuStrip?.Dispose();
        _tray.ContextMenuStrip = new ContextMenuStrip();
        _tray.ContextMenuStrip.Items.Add(T("Settings"), null, (_, _) => ShowSettings());
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
        _tray.Visible = false;
        _tray.Dispose();
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
