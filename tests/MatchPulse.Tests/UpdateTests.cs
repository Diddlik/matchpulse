using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace MatchPulse.Tests;

// Auto-update against an in-memory release site: latest.json and the installer.
public sealed class UpdateTests : IDisposable
{
    const string Base = "https://example.test/releases";
    static readonly byte[] Installer = Encoding.UTF8.GetBytes("installer bytes");
    readonly string _folder = Path.Combine(Path.GetTempPath(), "mp-update-" + Guid.NewGuid().ToString("N"));
    readonly Releases _site = new();
    Config _config = new([]);
    bool _gameRunning, _installed = true;
    readonly List<string> _installs = [];

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    Updater Updater(string current = "0.1.0") => new(new HttpClient(_site), Version.Parse(current), Base);

    UpdateService Service() => new(Updater(), () => _config, () => _gameRunning, () => _installed, _installs.Add, _folder, NullLogger.Instance);

    static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    [Fact]
    public async Task Newer_release_is_found_from_latest_json()
    {
        _site.Latest("0.2.0", Sha(Installer));

        var manifest = await Updater().CheckAsync(default);

        Assert.Equal(new UpdateManifest(new Version(0, 2, 0), "MatchPulse-Setup-0.2.0.exe", Sha(Installer)), manifest);
        Assert.Equal($"{Base}/latest/download/latest.json", _site.Requested[0]);
    }

    [Fact]
    public async Task Same_or_older_release_is_no_update()
    {
        _site.Latest("0.2.0", Sha(Installer));

        Assert.Null(await Updater("0.2.0").CheckAsync(default));
        Assert.Null(await Updater("0.3.1").CheckAsync(default));
    }

    [Fact]
    public async Task Installer_with_a_wrong_checksum_is_rejected_and_deleted()
    {
        _site.Latest("0.2.0", Sha(Encoding.UTF8.GetBytes("something else")));
        var updater = Updater();
        var manifest = (await updater.CheckAsync(default))!;

        await Assert.ThrowsAsync<InvalidDataException>(() => updater.DownloadAsync(manifest, _folder, default));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task Auto_install_waits_until_the_game_is_closed()
    {
        _site.Latest("0.2.0", Sha(Installer));
        _gameRunning = true;
        var updates = Service();

        await updates.CheckAsync();
        Assert.Equal("waitingForGame", (string?)updates.State()["status"]);
        Assert.Empty(_installs);

        _gameRunning = false;
        await updates.Tick();

        var installer = Assert.Single(_installs);
        Assert.Equal(Installer, File.ReadAllBytes(installer));
        Assert.Equal($"{Base}/download/v0.2.0/MatchPulse-Setup-0.2.0.exe", _site.Requested[^1]);
        Assert.Equal("installing", (string?)updates.State()["status"]);
    }

    [Fact]
    public async Task Without_auto_install_the_update_is_only_offered_until_the_Player_installs_it()
    {
        _site.Latest("0.2.0", Sha(Installer));
        _config = _config with { AutoInstallUpdates = false };
        var updates = Service();

        await updates.CheckAsync();
        Assert.Equal(("available", "0.2.0"), ((string?)updates.State()["status"], (string?)updates.State()["latest"]));
        Assert.Empty(_installs);

        await updates.InstallAsync();
        Assert.Single(_installs);
    }

    [Fact]
    public async Task Portable_copy_never_installs()
    {
        _site.Latest("0.2.0", Sha(Installer));
        _installed = false;
        var updates = Service();

        await updates.CheckAsync();
        await updates.InstallAsync();

        Assert.Empty(_installs);
        Assert.False((bool)updates.State()["installable"]!);
        Assert.Equal($"{Base}/tag/v0.2.0", (string?)updates.State()["releaseUrl"]);
    }

    [Fact]
    public async Task Failed_check_is_reported_and_the_daily_check_is_off_when_disabled()
    {
        var updates = Service(); // no latest.json: 404
        await updates.CheckAsync();
        Assert.Equal("error", (string?)updates.State()["status"]);

        _config = _config with { CheckForUpdates = false };
        _site.Requested.Clear();
        await Service().Tick();
        Assert.Empty(_site.Requested);
    }

    sealed class Releases : HttpMessageHandler
    {
        string? _latest;
        public List<string> Requested { get; } = [];

        public void Latest(string version, string sha) =>
            _latest = $$"""{ "version": "{{version}}", "installer": "MatchPulse-Setup-{{version}}.exe", "sha256": "{{sha.ToUpperInvariant()}}" }""";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            lock (Requested) Requested.Add(url);
            HttpResponseMessage response = url.EndsWith("/latest/download/latest.json") && _latest is not null
                ? new(HttpStatusCode.OK) { Content = new StringContent(_latest) }
                : url.Contains("/download/v") ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(Installer) }
                : new(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }
}
