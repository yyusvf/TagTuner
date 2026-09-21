using System.Text;
using TagTuner.Core.Model;

namespace TagTuner.Core.Metadata;

/// <summary>
/// Dateinamen aus Metadaten bauen.
///
/// Das Muster ist dasselbe, das in den Einstellungen steht, etwa
/// <c>{track} - {title}</c>. Die Endung kommt nicht aus dem Muster, sondern
/// bleibt immer die der Datei: Ein Muster, das versehentlich die Endung
/// ändert, würde aus einer FLAC eine Datei machen, die keiner mehr öffnet.
/// </summary>
public static class FileNaming
{
    /// <summary>Was Windows in einem Dateinamen nicht zulässt, plus die Pfadtrenner.</summary>
    private static readonly char[] Forbidden =
        [.. Path.GetInvalidFileNameChars(), '/', '\\', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>
    /// Namen, die unter Windows für Geräte stehen. Eine Datei namens „con.mp3"
    /// lässt sich nicht anlegen, egal wie viel Platz auf der Platte ist.
    /// </summary>
    private static readonly HashSet<string> Devices = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static readonly string[] Placeholders =
        ["{track}", "{title}", "{artist}", "{album}", "{albumartist}", "{disc}", "{year}", "{genre}"];

    /// <summary>
    /// Der neue Dateiname für diesen Track, mit Endung. Bleibt vom Muster
    /// nichts Brauchbares übrig, kommt der bisherige Name zurück: Lieber
    /// nichts tun als eine Datei „.mp3" ohne Namen erzeugen.
    /// </summary>
    public static string Build(AudioTrack track, string pattern)
    {
        var extension = Path.GetExtension(track.Path);

        if (string.IsNullOrWhiteSpace(pattern))
            return Path.GetFileName(track.Path);

        var text = pattern
            .Replace("{track}", track.Track > 0 ? $"{track.Track:00}" : "", StringComparison.OrdinalIgnoreCase)
            .Replace("{disc}", track.Disc > 0 ? track.Disc.ToString() : "", StringComparison.OrdinalIgnoreCase)
            .Replace("{year}", track.Year > 0 ? track.Year.ToString() : "", StringComparison.OrdinalIgnoreCase)
            .Replace("{title}", track.Title, StringComparison.OrdinalIgnoreCase)
            .Replace("{artist}", track.Artist, StringComparison.OrdinalIgnoreCase)
            .Replace("{albumartist}", track.AlbumArtist, StringComparison.OrdinalIgnoreCase)
            .Replace("{album}", track.Album, StringComparison.OrdinalIgnoreCase)
            .Replace("{genre}", track.Genre, StringComparison.OrdinalIgnoreCase);

        var name = Clean(text);

        // Nur noch Trennzeichen übrig, weil Titel und Nummer leer waren.
        if (name.Length == 0 || Devices.Contains(name))
            return Path.GetFileName(track.Path);

        // Windows begrenzt den Pfad; der Name allein darf 255 Zeichen haben,
        // und die Endung muss noch dazupassen.
        var room = 250 - extension.Length;
        if (name.Length > room) name = name[..room].TrimEnd();

        return name + extension;
    }

    /// <summary>Was an Zeichen nicht in einen Dateinamen darf, und was danach übrig bleibt.</summary>
    private static string Clean(string text)
    {
        var sb = new StringBuilder(text.Length);
        var space = false;

        foreach (var c in text)
        {
            if (Array.IndexOf(Forbidden, c) >= 0) continue;
            if (char.IsControl(c)) continue;

            // Mehrere Leerzeichen hintereinander entstehen dort, wo ein
            // Platzhalter leer geblieben ist.
            if (char.IsWhiteSpace(c))
            {
                if (space || sb.Length == 0) continue;
                space = true;
                sb.Append(' ');
                continue;
            }

            space = false;
            sb.Append(c);
        }

        // Ein Punkt oder Leerzeichen am Ende verschwindet unter Windows still.
        // Ebenso ein Trennstrich, der ohne seinen Platzhalter dasteht.
        return sb.ToString().Trim().Trim('.', '-', '_', ' ').Trim();
    }

    /// <summary>
    /// Ein freier Pfad daneben, falls der gewünschte schon belegt ist.
    /// Vergleicht ohne Rücksicht auf Groß- und Kleinschreibung, denn Windows
    /// tut das auch.
    /// </summary>
    public static string Free(string path, ISet<string>? alsoTaken = null)
    {
        bool Taken(string p) =>
            File.Exists(p) || (alsoTaken?.Contains(p) ?? false);

        if (!Taken(path)) return path;

        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!Taken(candidate)) return candidate;
        }
        return path;
    }
}
