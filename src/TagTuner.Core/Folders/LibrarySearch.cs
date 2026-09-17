using TagTuner.Core.Audio;

namespace TagTuner.Core.Folders;

public enum HitKind { Folder, Track }

/// <summary>Ein Treffer: ein Ordner oder eine Audiodatei.</summary>
public sealed record SearchHit(HitKind Kind, string Path, string Name, string Context)
{
    /// <summary>Das Symbol für die Trefferliste — wie die *Label-Eigenschaften am Track.</summary>
    public string Glyph => Kind == HitKind.Folder ? "\uE8B7" : "\uEC4F";

    private string? _lower;

    /// <summary>
    /// Der Name in Kleinschreibung, einmal berechnet.
    ///
    /// Beim Filtern über tausende Einträge ist ein ordinaler Vergleich auf
    /// vorbereiteten Zeichenketten um ein Vielfaches schneller als
    /// <c>Contains(…, CurrentCultureIgnoreCase)</c>, das jedes Mal die
    /// Kulturregeln anwirft.
    /// </summary>
    internal string Lower => _lower ??= Name.ToLowerInvariant();
}

/// <summary>
/// Sucht in den Wurzeln der Bibliothek nach Ordnern und Liedern.
///
/// Gesucht wird in Ordner- und Dateinamen, nicht in Tags. Für Tags müsste
/// jede Datei geöffnet werden — das dauert für eine Bibliothek dieser Größe
/// über zehn Sekunden und wäre für eine Suche, die beim Tippen mitläuft,
/// unbrauchbar. Namen reichen in der Praxis, weil Musikdateien fast immer
/// nach Titel benannt sind.
/// </summary>
public static class LibrarySearch
{
    public const int DefaultLimit = 300;

    /// <summary>
    /// Sucht im vorbereiteten Index. Ordner stehen vor Liedern: Wer sucht,
    /// will meist das Album, und die Datei findet sich darin ohnehin.
    /// </summary>
    public static IReadOnlyList<SearchHit> Find(
        LibraryIndex index, string query, int limit = DefaultLimit)
    {
        var needle = query.Trim();
        if (needle.Length < 2) return [];

        var parts = needle.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return [];

        var folders = new List<SearchHit>();
        var tracks = new List<SearchHit>();

        foreach (var entry in index.Entries)
        {
            if (folders.Count + tracks.Count >= limit) break;

            var hit = true;
            foreach (var part in parts)
            {
                if (!entry.Lower.Contains(part, StringComparison.Ordinal)) { hit = false; break; }
            }
            if (!hit) continue;

            (entry.Kind == HitKind.Folder ? folders : tracks).Add(entry);
        }

        return [.. folders, .. tracks];
    }

    public static IReadOnlyList<SearchHit> Find(
        IEnumerable<string> roots,
        string query,
        int limit = DefaultLimit,
        CancellationToken ct = default)
    {
        var needle = query.Trim();
        if (needle.Length < 2) return [];

        var folders = new List<SearchHit>();
        var tracks = new List<SearchHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (folders.Count + tracks.Count >= limit) break;
            Walk(root, 0);
        }

        // Ordner zuerst: Wer sucht, will meist das Album, nicht die einzelne
        // Datei — und die Datei findet sich darin ohnehin.
        return [.. folders, .. tracks];

        void Walk(string folder, int depth)
        {
            if (depth > 12 || folders.Count + tracks.Count >= limit) return;
            ct.ThrowIfCancellationRequested();

            try
            {
                foreach (var file in new DirectoryInfo(folder).EnumerateFiles())
                {
                    if (tracks.Count + folders.Count >= limit) return;
                    if (!AudioFormats.IsAudioFile(file.Name)) continue;
                    if ((file.Attributes & FileAttributes.Hidden) != 0) continue;

                    if (Matches(Path.GetFileNameWithoutExtension(file.Name), needle)
                        && seen.Add(file.FullName))
                    {
                        tracks.Add(new SearchHit(HitKind.Track, file.FullName,
                            Path.GetFileNameWithoutExtension(file.Name),
                            Path.GetFileName(folder)));
                    }
                }
            }
            catch { }

            foreach (var sub in FolderScanner.Subfolders(folder))
            {
                if (folders.Count + tracks.Count >= limit) return;

                if (Matches(sub.Name, needle) && seen.Add(sub.Path))
                    folders.Add(new SearchHit(HitKind.Folder, sub.Path, sub.Name,
                        Path.GetFileName(folder)));

                Walk(sub.Path, depth + 1);
            }
        }
    }

    /// <summary>
    /// Alle Wortteile müssen vorkommen, in beliebiger Reihenfolge — so findet
    /// „carti die" auch „Die Lit — Playboi Carti".
    /// </summary>
    private static bool Matches(string text, string query)
    {
        foreach (var part in query.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (text.Contains(part, StringComparison.CurrentCultureIgnoreCase) == false)
                return false;
        return true;
    }
}
