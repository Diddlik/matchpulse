using OpenCvSharp;

namespace MatchPulse.Tests;

public class MatchNamesTests
{
    static readonly string Names = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", "names");

    [Theory]
    [InlineData("redsec-trios", "RAPID ROYALE – TRIOS", "MIRAK-TAL: FLUSSUFER")]
    [InlineData("tdm-deploy", "TEAM DEATHMATCH", "EUROPÄISCHES CASINO: CASINO-ETAGEN")]
    public void Lobby_header_yields_mode_and_map(string picture, string mode, string map)
    {
        // Windows on the CI runner may have no German OCR; the header is then not read at all.
        if (MatchNames.Create("de") is not { } names || !Windows.Media.Ocr.OcrEngine.IsLanguageSupported(new("de-DE"))) return;

        var read = names.Read(Screen(picture));

        Assert.NotNull(read);
        Assert.True(MatchNames.Similarity(mode, read.Value.Mode) >= MatchNames.MinSimilarity, read.Value.Mode);
        Assert.True(MatchNames.Similarity(map, read.Value.Map) >= MatchNames.MinSimilarity, read.Value.Map);
    }

    [Fact]
    public void Screen_without_a_map_line_yields_no_name()
    {
        if (MatchNames.Create("de") is not { } names) return;

        Assert.Null(names.Read(Screen("tdm-loadout")));
    }

    [Fact]
    public void Parse_takes_the_map_line_and_the_line_above()
    {
        Assert.Equal(("RAPID ROYALE – TRIOS", "MIRAK-TAL: FLUSSUFER"), MatchNames.Parse(["SPIELZEIT", "RAPID  ROYALE – TRIOS ", "MIRAK-TAL: FLUSSUFER"]));
        Assert.Null(MatchNames.Parse(["CZ3A1-KONFIGURATION WÄHLEN"]));
    }

    [Fact]
    public void Several_reads_agree_on_the_one_read_most_often()
    {
        Assert.Equal("EUROPÄISCHES CASINO: CASINO-ETAGEN",
            MatchNames.Consensus(["V EUROPÄI CASINO: CASINO-ETAGEN", "EUROPÄISCHES CASINO: CASINO-ETAGEN", "EUROPÄI CHES CASINO: CASINO-ETAGEN", "EUROPÄISCHES CASINO: CASINO-ETAGEN"]));
    }

    [Fact]
    public void A_misread_name_becomes_the_known_one_but_a_different_mode_stays_new()
    {
        string[] known = ["RAPID ROYALE – TRIOS", "MIRAK-TAL: FLUSSUFER"];

        Assert.Equal(("MIRAK-TAL: FLUSSUFER", true), MatchNames.Resolve("MIRAK-TAL: FLUSSUFFR", known));
        Assert.Equal(("RAPID ROYALE – DUOS", false), MatchNames.Resolve("RAPID ROYALE – DUOS", known));
        Assert.Equal(("X", false), MatchNames.Resolve("X", []));
    }

    // The fixture is the top left of a 2560x1440 screen.
    static Mat Screen(string picture)
    {
        var screen = new Mat(1440, 2560, MatType.CV_8UC3, Scalar.Black);
        using var header = Cv2.ImRead(Path.Combine(Names, picture + ".png"));
        header.CopyTo(screen[new Rect(0, 0, header.Width, header.Height)]);
        return screen;
    }
}
