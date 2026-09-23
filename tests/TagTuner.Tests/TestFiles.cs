using TagTuner.Core.Model;
using TagTuner.Core.Settings;

namespace TagTuner.Tests;

/// <summary>
/// Ein Temp-Ordner für einen Test, in den auch Einstellungen und Verlauf
/// umgeleitet werden. Ohne das schriebe jeder Test in die echte
/// history.json dessen, der die Tests laufen lässt.
/// </summary>
public sealed class Workspace : IDisposable
{
    private readonly string? _previous = AppSettings.DirectoryOverride;

    public string Root { get; } =
        Path.Combine(Path.GetTempPath(), "tagtuner-test-" + Guid.NewGuid().ToString("n")[..8]);

    public Workspace()
    {
        Directory.CreateDirectory(Root);
        AppSettings.DirectoryOverride = Path.Combine(Root, "settings");
        Directory.CreateDirectory(AppSettings.DirectoryOverride);
    }

    public string PathTo(params string[] parts) => Path.Combine([Root, .. parts]);

    /// <summary>
    /// Eine echte, abspielbare WAV-Datei mit Tags. Selbst gebaut statt über
    /// ffmpeg, damit die Tests nichts außer .NET brauchen.
    /// </summary>
    public string Wav(string relative, string title = "", string artist = "", string album = "",
                      uint track = 0, uint disc = 0, int sampleRate = 44100)
    {
        var path = PathTo(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var samples = sampleRate / 10;
        var data = samples * 2;
        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write("RIFF"u8); w.Write(36 + data); w.Write("WAVE"u8);
            w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(sampleRate); w.Write(sampleRate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data"u8); w.Write(data); w.Write(new byte[data]);
        }

        using var file = TagLib.File.Create(path);
        file.Tag.Title = title;
        file.Tag.Performers = artist.Length > 0 ? [artist] : [];
        file.Tag.Album = album;
        file.Tag.Track = track;
        file.Tag.Disc = disc;
        file.Save();
        return path;
    }

    public void Dispose()
    {
        AppSettings.DirectoryOverride = _previous;
        try { Directory.Delete(Root, true); } catch { }
    }
}

/// <summary>Tracks nur im Speicher, für alles, was keine Datei braucht.</summary>
public static class Tracks
{
    public static AudioTrack Make(string name, uint track = 0, uint disc = 0, string album = "Nachtfahrt",
                                  string artist = "Kollektiv Halle", string title = "x", uint year = 2021) =>
        new()
        {
            Path = name,
            FileName = name + ".mp3",
            Track = track,
            Disc = disc,
            Album = album,
            Artist = artist,
            Title = title,
            Year = year,
            Format = "MP3",
            SampleRate = 44100,
        };

    public static string Order(IEnumerable<AudioTrack> list) => string.Join(" ", list.Select(t => t.Path));
}
