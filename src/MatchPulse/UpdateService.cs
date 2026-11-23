using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace MatchPulse;

/// <summary>
/// When to check and install updates. With <see cref="Config.CheckForUpdates"/> it checks at start and every 24 h;
/// with <see cref="Config.AutoInstallUpdates"/> a found update is downloaded, verified and installed silently, but
/// never while the game runs. A portable copy only reports the update. <see cref="Tick"/> is called about once a minute.
/// </summary>
public sealed class UpdateService(Updater updater, Func<Config> config, Func<bool> gameRunning, Func<bool> installed,
    Action<string> install, string downloadFolder, ILogger log)
{
    static readonly TimeSpan Every = TimeSpan.FromHours(24);
    readonly Lock _lock = new();
    UpdateManifest? _latest;
    string _status = "idle"; // idle, checking, upToDate, available, waitingForGame, downloading, installing, error
    string? _error;
    DateTimeOffset? _checkedAt;
    bool _installWanted, _busy;

    /// <summary>The state changed; read it with <see cref="State"/>.</summary>
    public event Action? Changed;

    public JsonObject State()
    {
        lock (_lock)
            return new JsonObject
            {
                ["current"] = updater.Current.ToString(),
                ["latest"] = _latest?.Version.ToString(),
                ["status"] = _status,
                ["error"] = _error,
                ["checkedAt"] = _checkedAt,
                ["installable"] = installed(),
                ["releaseUrl"] = _latest is null ? null : updater.ReleasePage(_latest.Version),
            };
    }

    /// <summary>Checks now (at start, by the timer or the "Check now" button).</summary>
    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!Begin("checking")) return;
        try
        {
            var latest = await updater.CheckAsync(cancellationToken);
            lock (_lock)
            {
                _latest = latest;
                _checkedAt = DateTimeOffset.Now;
                _status = latest is null ? "upToDate" : "available";
                _error = null;
                _busy = false;
                if (latest is not null && config().AutoInstallUpdates && installed()) _installWanted = true;
            }
            if (latest is not null) log.LogInformation("Update {Version} available (running {Current})", latest.Version, updater.Current);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            Fail($"Update check failed: {e.Message}");
        }
        Changed?.Invoke();
        await InstallIfWantedAsync(cancellationToken);
    }

    /// <summary>The Player asked to install the available update.</summary>
    public async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_latest is null || !installed()) return;
            _installWanted = true;
        }
        await InstallIfWantedAsync(cancellationToken);
    }

    /// <summary>About once a minute: the daily check, and a wanted install once the game has closed.</summary>
    public async Task Tick(CancellationToken cancellationToken = default)
    {
        bool due;
        lock (_lock) due = config().CheckForUpdates && !_busy && (_checkedAt is null || DateTimeOffset.Now - _checkedAt >= Every);
        if (due) await CheckAsync(cancellationToken);
        else await InstallIfWantedAsync(cancellationToken);
    }

    async Task InstallIfWantedAsync(CancellationToken cancellationToken)
    {
        UpdateManifest latest;
        lock (_lock)
        {
            if (!_installWanted || _busy || _latest is null) return;
            if (gameRunning())
            {
                if (_status != "waitingForGame") { _status = "waitingForGame"; Changed?.Invoke(); }
                return;
            }
            latest = _latest;
            _busy = true;
            _status = "downloading";
        }
        Changed?.Invoke();
        try
        {
            var installer = await updater.DownloadAsync(latest, downloadFolder, cancellationToken);
            lock (_lock) _status = "installing";
            Changed?.Invoke();
            log.LogInformation("Installing update {Version}", latest.Version);
            install(installer);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            lock (_lock) _installWanted = false; // no retry loop on a bad download; the next check tries again
            Fail($"Update {latest.Version} not installed: {e.Message}");
            Changed?.Invoke();
        }
    }

    bool Begin(string status)
    {
        lock (_lock)
        {
            if (_busy) return false;
            _busy = true;
            _status = status;
        }
        Changed?.Invoke();
        return true;
    }

    void Fail(string message)
    {
        log.LogWarning("{Message}", message);
        lock (_lock)
        {
            _status = "error";
            _error = message;
            _busy = false;
        }
    }
}
