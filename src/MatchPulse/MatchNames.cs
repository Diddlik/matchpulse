using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using OpenCvSharp;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace MatchPulse;

/// <summary>
/// Match Name: the mode ("RAPID ROYALE – TRIOS") and the map ("MIRAK-TAL: FLUSSUFER") the lobby shows top left
/// before a Match, read with Windows OCR. A name read is replaced by the closest known name (<see cref="Config.MatchNames"/>)
/// when they are nearly the same, so names the Player corrected once stay correct.
/// </summary>
public sealed class MatchNames(OcrEngine engine)
{
    /// <summary>The lobby header, relative to the frame. Its position differs a little between lobby screens.</summary>
    public static readonly Rect2d Region = new(0.0, 0.0, 0.40, 0.13);

    /// <summary>Normalised similarity a read name needs to count as a known one. TRIOS vs DUOS scores 0.81.</summary>
    public const double MinSimilarity = 0.9;

    /// <summary>An OCR engine for the game language, or null when Windows has none installed.</summary>
    public static MatchNames? Create(string gameLanguage)
    {
        var language = new Language(gameLanguage == "de" ? "de-DE" : "en-US");
        var engine = OcrEngine.IsLanguageSupported(language) ? OcrEngine.TryCreateFromLanguage(language) : OcrEngine.TryCreateFromUserProfileLanguages();
        return engine is null ? null : new MatchNames(engine);
    }

    /// <summary>The mode and map in the lobby header of <paramref name="frame"/>, or null when none is shown.</summary>
    public (string Mode, string Map)? Read(Mat frame)
    {
        using var header = new Mat(frame, Cue.Pixels(Region, frame.Size()));
        // Grey reads the light text on a busy background better than colour.
        using var gray = header.CvtColor(ColorConversionCodes.BGR2GRAY);
        using var bgra = gray.CvtColor(ColorConversionCodes.GRAY2BGRA);
        var bytes = new byte[bgra.Total() * 4];
        Marshal.Copy(bgra.Data, bytes, 0, bytes.Length);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(bytes.AsBuffer(), BitmapPixelFormat.Bgra8, bgra.Width, bgra.Height);
        var result = engine.RecognizeAsync(bitmap).AsTask().GetAwaiter().GetResult();
        return Parse([.. result.Lines.Select(l => l.Text)]);
    }

    /// <summary>The map is the line "map: area"; the mode is the line above it.</summary>
    public static (string Mode, string Map)? Parse(IReadOnlyList<string> lines)
    {
        for (var i = 1; i < lines.Count; i++)
            if (lines[i].Contains(':') && lines[i - 1].Count(char.IsLetter) >= 3)
                return (Clean(lines[i - 1]), Clean(lines[i]));
        return null;
    }

    /// <summary>Of several reads of one name, the one read most often, then the one most like all the others:
    /// single reads over a busy background miss letters, each time others.</summary>
    public static string Consensus(IReadOnlyList<string> reads) =>
        reads.OrderByDescending(r => reads.Count(o => o == r)).ThenByDescending(r => reads.Sum(o => Similarity(r, o))).First();

    /// <summary>The known name nearly the same as <paramref name="read"/>, else <paramref name="read"/> itself (a new name).</summary>
    public static (string Name, bool Known) Resolve(string read, IReadOnlyList<string> known)
    {
        var best = known.MaxBy(k => Similarity(read, k));
        return best is not null && Similarity(read, best) >= MinSimilarity ? (best, true) : (read, false);
    }

    /// <summary>1 minus the edit distance over the longer length, comparing letters and digits only, ignoring case.</summary>
    public static double Similarity(string a, string b)
    {
        string Key(string s) => new([.. s.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant)]);
        var (x, y) = (Key(a), Key(b));
        if (x.Length + y.Length == 0) return 1;
        var row = Enumerable.Range(0, y.Length + 1).ToArray();
        for (var i = 1; i <= x.Length; i++)
        {
            var diagonal = row[0];
            row[0] = i;
            for (var j = 1; j <= y.Length; j++)
            {
                var next = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (x[i - 1] == y[j - 1] ? 0 : 1));
                (diagonal, row[j]) = (row[j], next);
            }
        }
        return 1 - (double)row[y.Length] / Math.Max(x.Length, y.Length);
    }

    static string Clean(string line) => string.Join(' ', line.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
