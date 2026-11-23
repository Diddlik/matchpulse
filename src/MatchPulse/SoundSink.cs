using NAudio.Wave;

namespace MatchPulse;

/// <summary>Sound plugin: plays a sound on Game Events.</summary>
public sealed record SoundSettings(bool Enabled = false, IReadOnlyList<SoundRule>? Rules = null)
{
    public static readonly string[] BuiltIn = ["ding", "double", "low", "fanfare"];

    public IReadOnlyList<SoundRule> Rules { get; init; } = Rules ?? [];
}

/// <summary>When <see cref="Event"/> happens, play <see cref="Sound"/> — a built-in sound name or the path of a WAV or MP3
/// file — at <see cref="Volume"/> percent.</summary>
public sealed record SoundRule(string Id, string Event, string Sound = "ding", int Volume = 80);

/// <summary>Plays the sounds of the matching rules on the default output device. Playback runs in the background,
/// so neither detection nor the next Game Event waits for a sound to finish; sounds may overlap.</summary>
public sealed class SoundSink(SoundSettings settings, Action<string, float>? play = null) : IEventSink
{
    readonly Action<string, float> _play = play ?? Play;

    public SoundSettings Settings => settings;

    public Task SendAsync(GameEvent gameEvent, CancellationToken cancellationToken)
    {
        foreach (var rule in settings.Rules.Where(r => r.Event == gameEvent.Type))
            _play(rule.Sound, Math.Clamp(rule.Volume, 0, 100) / 100f);
        return Task.CompletedTask;
    }

    /// <summary>Plays a sound now; throws when a file is missing or cannot be read.</summary>
    public static void Play(string sound, float volume)
    {
        WaveStream source = SoundSettings.BuiltIn.Contains(sound)
            ? new RawSourceWaveStream(new MemoryStream(Tones.Render(sound)), Tones.Format)
            : File.Exists(sound) ? new AudioFileReader(sound) : throw new FileNotFoundException($"Sound file {sound} not found.", sound);
        var output = new WaveOutEvent { Volume = volume };
        output.PlaybackStopped += (_, _) =>
        {
            output.Dispose();
            source.Dispose();
        };
        output.Init(source);
        output.Play();
    }

    public override string ToString() => "Sound";
}

/// <summary>The built-in sounds, synthesised as 16-bit mono PCM (no sound files to ship).</summary>
public static class Tones
{
    public static readonly WaveFormat Format = new(44100, 16, 1);

    // (frequency Hz, length s) notes; 0 Hz is a pause.
    static readonly Dictionary<string, (double Hz, double Seconds)[]> Notes = new()
    {
        ["ding"] = [(1318.5, 0.18)],
        ["double"] = [(1318.5, 0.11), (0, 0.05), (1760, 0.16)],
        ["low"] = [(196, 0.35)],
        ["fanfare"] = [(523.3, 0.12), (659.3, 0.12), (784, 0.12), (1046.5, 0.32)],
    };

    public static byte[] Render(string name)
    {
        var samples = new List<short>();
        foreach (var (hz, seconds) in Notes[name])
        {
            var count = (int)(Format.SampleRate * seconds);
            for (var i = 0; i < count; i++)
            {
                var t = (double)i / Format.SampleRate;
                // Quick attack, exponential decay: a bell-like ping instead of a hard beep.
                var envelope = hz == 0 ? 0 : Math.Min(1, t / 0.005) * Math.Exp(-t * 9);
                var value = envelope * (Math.Sin(2 * Math.PI * hz * t) + 0.3 * Math.Sin(4 * Math.PI * hz * t)) / 1.3;
                samples.Add((short)(value * short.MaxValue * 0.8));
            }
        }
        var bytes = new byte[samples.Count * 2];
        Buffer.BlockCopy(samples.ToArray(), 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
