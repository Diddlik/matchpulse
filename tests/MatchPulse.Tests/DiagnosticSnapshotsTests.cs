using Microsoft.Extensions.Logging.Abstractions;
using OpenCvSharp;

namespace MatchPulse.Tests;

public sealed class DiagnosticSnapshotsTests : IDisposable
{
    static readonly DateTimeOffset Start = new(2026, 11, 24, 20, 0, 0, TimeSpan.FromHours(1));
    readonly string _folder = Path.Combine(Path.GetTempPath(), "mp-diag-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    [Fact]
    public void Only_the_newest_snapshots_are_kept()
    {
        Directory.CreateDirectory(_folder);
        for (var i = 0; i < DiagnosticSnapshots.KeepFiles; i++)
            File.WriteAllBytes(Path.Combine(_folder, $"2026-11-23_20-00-{i / 10:00}.{i % 10:000}_old.jpg"), []);
        using var snapshots = new DiagnosticSnapshots(_folder, NullLogger.Instance);
        using var image = new Mat(1440, 2560, MatType.CV_8UC3, Scalar.Gray);
        snapshots.Add(new Frame(Start, image));

        snapshots.Write("kill_marker_without_kill_badge", Start);

        var files = Directory.GetFiles(_folder).Select(Path.GetFileName).ToList();
        Assert.Equal(DiagnosticSnapshots.KeepFiles, files.Count);
        Assert.DoesNotContain("2026-11-23_20-00-00.000_old.jpg", files);
        Assert.Contains(files, f => f!.EndsWith("_kill_marker_without_kill_badge.jpg"));
    }
}
