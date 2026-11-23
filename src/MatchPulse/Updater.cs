using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace MatchPulse;

/// <summary>What the release workflow publishes as <c>latest.json</c> next to each release's files.</summary>
public sealed record UpdateManifest(Version Version, string Installer, string Sha256);

/// <summary>
/// Updates from the GitHub releases without the GitHub API: <c>releases/latest/download/latest.json</c> is a plain
/// file download (pre-releases are never "latest"). The installer is checked against the SHA-256 in the manifest.
/// </summary>
public sealed class Updater(HttpClient http, Version current, string baseUrl = Updater.DefaultBaseUrl)
{
    public const string DefaultBaseUrl = "https://github.com/Diddlik/matchpulse/releases";
    public const string AppId = "{6D3C1E52-8B0A-4C5E-9E3F-4B1F0C7A2D91}_is1"; // AppId in installer/MatchPulse.iss

    public Version Current => current;

    public static Version AppVersion => typeof(Updater).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(0, 0, 0);

    public string ReleasePage(Version version) => $"{baseUrl}/tag/v{version}";

    /// <summary>The newest release's manifest if it is newer than the running version, else null.</summary>
    public async Task<UpdateManifest?> CheckAsync(CancellationToken cancellationToken)
    {
        var json = JsonNode.Parse(await http.GetStringAsync($"{baseUrl}/latest/download/latest.json", cancellationToken))!;
        var manifest = new UpdateManifest(
            Version.Parse((string)json["version"]!),
            Path.GetFileName((string)json["installer"]!),
            ((string)json["sha256"]!).ToLowerInvariant());
        return manifest.Version > current ? manifest : null;
    }

    /// <summary>Downloads the installer to <paramref name="folder"/> and checks its SHA-256; throws on a mismatch.</summary>
    public async Task<string> DownloadAsync(UpdateManifest manifest, string folder, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, manifest.Installer);
        await using (var source = await http.GetStreamAsync($"{baseUrl}/download/v{manifest.Version}/{manifest.Installer}", cancellationToken))
        await using (var file = File.Create(path))
            await source.CopyToAsync(file, cancellationToken);
        string hash;
        await using (var file = File.OpenRead(path))
            hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
        if (hash != manifest.Sha256)
        {
            File.Delete(path);
            throw new InvalidDataException($"Update {manifest.Installer} has SHA-256 {hash}, expected {manifest.Sha256}; not installed.");
        }
        return path;
    }

    /// <summary>True when this copy was installed by the installer (not a portable copy), so it can update itself.</summary>
    public static bool IsInstalled() => InstalledFor() is not null;

    /// <summary>"user" or "all" for the installation this copy runs from, null for a portable copy.</summary>
    static string? InstalledFor()
    {
        foreach (var (root, scope) in new[] { (Registry.CurrentUser, "user"), (Registry.LocalMachine, "all") })
        {
            using var key = root.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{AppId}");
            if (key?.GetValue("InstallLocation") is string location
                && Path.GetFullPath(location).TrimEnd('\\').Equals(Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return scope;
        }
        return null;
    }

    /// <summary>Starts the installer silently; it closes MatchPulse, installs over it and starts it again in the tray.
    /// An installation for all users (Program Files) is updated for all users again, which asks once for admin rights (UAC).</summary>
    public static void StartInstaller(string installer)
    {
        var allUsers = InstalledFor() == "all";
        Process.Start(new ProcessStartInfo(installer, $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /relaunch=1 {(allUsers ? "/ALLUSERS" : "/CURRENTUSER")}")
        {
            UseShellExecute = allUsers, // lets Windows show the UAC prompt
            Verb = allUsers ? "runas" : "",
        });
    }
}
