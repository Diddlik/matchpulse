using System.Diagnostics;
using System.Globalization;

namespace MatchPulse;

/// <summary>
/// Highlight video of a Match: the last <c>clipSeconds</c> of each replay saved in it, joined with ffmpeg
/// (stream copy, so cuts fall on keyframes). Replays closer together than a clip overlap; only the later one of
/// such a run is used, cut long enough to cover all of them.
/// </summary>
public static class HighlightReel
{
    public sealed record Replay(string Path, DateTimeOffset SavedAt);

    /// <summary>The cuts: which file, and how many seconds before its end to start.</summary>
    public static List<(string Path, double Seconds)> Cuts(IEnumerable<Replay> replays, double clipSeconds)
    {
        List<(string Path, double Seconds)> cuts = [];
        DateTimeOffset? runStart = null;
        Replay? last = null;
        foreach (var replay in replays.OrderBy(r => r.SavedAt))
        {
            if (last is not null && replay.SavedAt - last.SavedAt >= TimeSpan.FromSeconds(clipSeconds))
            {
                cuts.Add(Cut(last, runStart!.Value, clipSeconds));
                runStart = null;
            }
            runStart ??= replay.SavedAt;
            last = replay;
        }
        if (last is not null) cuts.Add(Cut(last, runStart!.Value, clipSeconds));
        return cuts;
    }

    static (string, double) Cut(Replay last, DateTimeOffset runStart, double clipSeconds) =>
        (last.Path, (last.SavedAt - runStart).TotalSeconds + clipSeconds);

    /// <summary>Writes the highlight video next to the first replay and returns its path; its name ends with the first
    /// 8 characters of the <paramref name="matchId"/>, like <c>{match}</c> in OBS rules.</summary>
    public static async Task<string> BuildAsync(IReadOnlyList<Replay> replays, double clipSeconds, DateTimeOffset matchEnded, string matchId)
    {
        var existing = replays.Where(r => File.Exists(r.Path)).ToList();
        if (existing.Count == 0) throw new ObsException("Highlight video: the saved replays are not on this PC.");
        var ext = System.IO.Path.GetExtension(existing[0].Path);
        var output = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(existing[0].Path)!,
            $"Highlights {matchEnded.ToLocalTime().ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture)} {matchId[..Math.Min(8, matchId.Length)]}{ext}");
        var temp = Directory.CreateTempSubdirectory("MatchPulse-reel-").FullName;
        try
        {
            var parts = new List<string>();
            foreach (var (path, seconds) in Cuts(existing, clipSeconds))
            {
                var part = System.IO.Path.Combine(temp, $"part{parts.Count}{ext}");
                await FfmpegAsync("-sseof", "-" + seconds.ToString("0.###", CultureInfo.InvariantCulture), "-i", path, "-c", "copy", part);
                parts.Add(part);
            }
            var list = System.IO.Path.Combine(temp, "list.txt");
            await File.WriteAllLinesAsync(list, parts.Select(p => $"file '{p.Replace("'", @"'\''")}'"));
            await FfmpegAsync("-f", "concat", "-safe", "0", "-i", list, "-c", "copy", output);
            return output;
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch (IOException) { }
        }
    }

    static async Task FfmpegAsync(params string[] args)
    {
        var start = new ProcessStartInfo("ffmpeg") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var a in (string[])["-y", "-hide_banner", "-loglevel", "error", .. args]) start.ArgumentList.Add(a);
        Process process;
        try { process = Process.Start(start)!; }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new ObsException("Highlight video: ffmpeg was not found. Install it (winget install Gyan.FFmpeg) and restart MatchPulse.");
        }
        using (process)
        {
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new ObsException($"Highlight video: ffmpeg failed: {error.Trim()}");
        }
    }
}
