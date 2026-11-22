using Microsoft.Extensions.Logging;

namespace MatchPulse.Tests;

public class FileLogTests
{
    readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"matchpulse-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public void Log_lines_are_written_to_the_file()
    {
        var path = Path.Combine(_dir, "matchpulse.log");
        using (var log = new FileLog(path, maxBytes: 1_000_000))
            log.CreateLogger("MatchPulse").LogWarning("Event Sink {Sink} failed", "webhook x");

        Assert.Contains("Event Sink webhook x failed", File.ReadAllText(path));
    }

    [Fact]
    public void A_full_log_file_is_rotated_to_one_backup()
    {
        var path = Path.Combine(_dir, "matchpulse.log");
        using (var log = new FileLog(path, maxBytes: 100))
        {
            var logger = log.CreateLogger("MatchPulse");
            logger.LogInformation("first {Pad}", new string('a', 100));
            logger.LogInformation("second");
        }

        Assert.Contains("first", File.ReadAllText(Path.Combine(_dir, "matchpulse.1.log")));
        Assert.DoesNotContain("first", File.ReadAllText(path));
        Assert.Contains("second", File.ReadAllText(path));
    }
}
