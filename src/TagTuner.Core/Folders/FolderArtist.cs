using TagTuner.Core.Audio;
using TagTuner.Core.Model;

namespace TagTuner.Core.Folders;

/// <summary>
/// Wer hinter einem Ordner steckt: der Name, der unter dem Ordnernamen in der
/// Bibliothek steht.
///
/// In drei Stufen, von sicher nach geraten:
///
/// 1. Sind sich alle Tracks beim Album-Interpreten einig, ist das die Antwort.
///    Dieses Feld gibt es genau für diesen Zweck — es bleibt gleich, auch wenn
///    einzelne Titel Gäste haben.
/// 2. Sonst derselbe Test am Interpreten-Feld.
/// 3. Sonst wird gezählt. Interpretenfelder wie „¥$, Kanye West, Ty Dolla $ign"
///    werden in ihre Namen zerlegt, und es gewinnt der Name, der in den
///    meisten Tracks überhaupt vorkommt — nicht der, der am häufigsten
///    vornesteht. Sonst würde ein Album, auf dem der Künstler mal zuerst und
///    mal als Gast geführt ist, an sich selbst vorbeizählen.
///
/// Reicht auch das nicht für eine deutliche Mehrheit, bleibt die Zeile leer.
/// Ein geratener Name wäre schlimmer als gar keiner.
/// </summary>
public static class FolderArtist
{
    /// <summary>Ab diesem Anteil der Tracks gilt ein Name als der des Ordners.</summary>
    private const double Majority = 0.6;

    /// <summary>
    /// Mehr Dateien zu lesen lohnt nicht: Ein Album ist selten länger, und
    /// bei einer Sammlung ändert die vierzigste Datei das Bild nicht mehr.
    /// </summary>
    private const int MaxProbed = 40;

    /// <summary>Trennzeichen zwischen mehreren Interpreten in einem Feld.</summary>
    private static readonly string[] Separators =
        [",", ";", "/", "&", " feat.", " feat ", " ft.", " ft ", " featuring ",
         " with ", " x ", " × ", " vs.", " vs ", " + ", "、"];

    public static string? Of(string folderPath, CancellationToken ct = default)
    {
        List<AudioTrack> tracks;
        try
        {
            tracks = new DirectoryInfo(folderPath)
                .EnumerateFiles()
                .Where(f => AudioFormats.IsAudioFile(f.Name))
                .Where(f => (f.Attributes & FileAttributes.Hidden) == 0)
                .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(MaxProbed)
                .Select(f => { ct.ThrowIfCancellationRequested(); return AudioProbe.Read(f.FullName); })
                .OfType<AudioTrack>()
                .ToList();
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }

        return Of(tracks);
    }

    /// <summary>Dieselbe Entscheidung für bereits gelesene Tracks.</summary>
    public static string? Of(IReadOnlyList<AudioTrack> tracks)
    {
        if (tracks.Count == 0) return null;

        // Stufe 1 und 2: Einigkeit im Tag schlägt jede Zählung.
        if (Agreed(tracks.Select(t => t.AlbumArtist)) is { } albumArtist) return albumArtist;
        if (Agreed(tracks.Select(t => t.Artist)) is { } artist) return artist;

        // Stufe 3: In wie vielen Tracks kommt ein Name überhaupt vor?
        var appearances = new Dictionary<string, (int Tracks, int Leading, string Display)>(
            StringComparer.CurrentCultureIgnoreCase);

        foreach (var track in tracks)
        {
            var field = string.IsNullOrWhiteSpace(track.Artist) ? track.AlbumArtist : track.Artist;
            var names = Split(field);

            // Ein Name je Track nur einmal zählen, sonst gewinnt eine Datei,
            // die denselben Künstler doppelt führt.
            for (var i = 0; i < names.Count; i++)
            {
                var name = names[i];
                var seenBefore = names.Take(i).Any(n => n.Equals(name, StringComparison.CurrentCultureIgnoreCase));
                if (seenBefore) continue;

                appearances.TryGetValue(name, out var stat);
                appearances[name] = (stat.Tracks + 1,
                                     stat.Leading + (i == 0 ? 1 : 0),
                                     stat.Display ?? name);
            }
        }

        if (appearances.Count == 0) return null;

        var best = appearances
            .OrderByDescending(p => p.Value.Tracks)
            // Gleichstand: Wer öfter vorne steht, ist eher der Hauptkünstler.
            .ThenByDescending(p => p.Value.Leading)
            .ThenBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase)
            .First();

        return best.Value.Tracks >= Math.Max(2, tracks.Count * Majority)
            ? best.Value.Display
            : null;
    }

    /// <summary>Der Wert, auf den sich alle einigen — oder null.</summary>
    private static string? Agreed(IEnumerable<string> values)
    {
        string? agreed = null;

        foreach (var raw in values)
        {
            var value = raw?.Trim();
            if (string.IsNullOrEmpty(value)) return null;

            if (agreed is null) agreed = value;
            else if (!agreed.Equals(value, StringComparison.CurrentCultureIgnoreCase)) return null;
        }

        return agreed;
    }

    /// <summary>Zerlegt „¥$, Kanye West, Ty Dolla $ign" in seine Namen.</summary>
    public static List<string> Split(string field)
    {
        if (string.IsNullOrWhiteSpace(field)) return [];

        var parts = new List<string> { field };

        foreach (var separator in Separators)
        {
            parts = [.. parts.SelectMany(p =>
                p.Split(separator, StringSplitOptions.RemoveEmptyEntries |
                                   StringSplitOptions.TrimEntries))];
        }

        return [.. parts
            .Select(p => p.Trim(' ', '-', '–', '(', ')', '[', ']'))
            .Where(p => p.Length > 0)];
    }
}
