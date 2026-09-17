using TagTuner.Core.Audio;
using TagTuner.Core.Model;

namespace TagTuner.Core.Folders;

public sealed record FolderEntry(string Path, string Name, bool IsDirectory, bool IsCustomRoot = false)
{
    /// <summary>
    /// Die TreeView stellt ihren Inhalt über ToString() dar. Ohne diese
    /// Überschreibung zeigt sie die generierte Record-Darstellung
    /// („FolderEntry { Path = C:\… }") statt des Ordnernamens.
    /// </summary>
    public override string ToString() => Name;
}

/// <summary>Liest Verzeichnisse für den Explorer — flach, nie rekursiv.</summary>
public static class FolderScanner
{
    /// <summary>
    /// Unterordner eines Verzeichnisses. Unlesbare Ordner (Netzlaufwerk weg,
    /// keine Rechte) liefern eine leere Liste statt einer Ausnahme, damit ein
    /// einzelner Zweig nicht den ganzen Baum sprengt.
    /// </summary>
    public static IReadOnlyList<FolderEntry> Subfolders(string path, bool onlyAudio = false)
    {
        var all = Read(path);
        return onlyAudio ? [.. all.Where(f => HasAudioBelow(f.Path))] : all;
    }

    private static IReadOnlyList<FolderEntry> Read(string path)
    {
        try
        {
            return new DirectoryInfo(path)
                .EnumerateDirectories()
                .Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                // Junctions und Symlinks sichtbar lassen, aber nicht auflösen —
                // sonst läuft der Baum bei einer Schleife endlos.
                .Where(d => (d.Attributes & FileAttributes.ReparsePoint) == 0)
                .OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(d => new FolderEntry(d.FullName, d.Name, true))
                .ToList();
        }
        catch { return []; }
    }

    /// <summary>Hat der Ordner mindestens einen Unterordner? Für den Aufklapp-Pfeil.</summary>
    public static bool HasSubfolders(string path, bool onlyAudio = false)
    {
        try { return Subfolders(path, onlyAudio).Count > 0; }
        catch { return false; }
    }

    /// <summary>
    /// Liegt irgendwo unter diesem Ordner eine Audiodatei?
    ///
    /// Geprüft wird bis in die Tiefe, nicht nur direkt darin — sonst wäre ein
    /// Ordner, der nur Alben enthält, ausgeblendet und man käme an die Alben
    /// nicht heran. Bei der ersten gefundenen Datei ist Schluss, und das
    /// Ergebnis wird gemerkt: Beim Aufklappen wird dieselbe Frage sonst für
    /// jeden Nachbarn immer wieder gestellt.
    /// </summary>
    public static bool HasAudioBelow(string path, int maxDepth = 6)
    {
        var key = path.TrimEnd(Path.DirectorySeparatorChar);
        lock (AudioBelow)
            if (AudioBelow.TryGetValue(key, out var known)) return known;

        var found = Look(path, 0);

        lock (AudioBelow)
        {
            if (AudioBelow.Count > 20000) AudioBelow.Clear();
            AudioBelow[key] = found;
        }
        return found;

        bool Look(string folder, int depth)
        {
            if (depth > maxDepth) return false;
            try
            {
                if (new DirectoryInfo(folder).EnumerateFiles()
                        .Any(f => AudioFormats.IsAudioFile(f.Name)
                                  && (f.Attributes & FileAttributes.Hidden) == 0))
                    return true;
            }
            catch { return false; }

            return Read(folder).Any(sub => Look(sub.Path, depth + 1));
        }
    }

    private static readonly Dictionary<string, bool> AudioBelow =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Nach Schreibvorgängen vergessen, was einmal gemessen wurde.</summary>
    public static void ForgetAudioScan()
    {
        lock (AudioBelow) AudioBelow.Clear();
    }

    /// <summary>
    /// Die Audiodateien eines Ordners, in Track-Reihenfolge.
    /// Sortiert nach Disc und Track, alles ohne Nummer danach nach Name —
    /// so sieht die Liste schon beim Öffnen wie eine Playlist aus.
    /// </summary>
    public static IReadOnlyList<AudioTrack> Tracks(string path, CancellationToken ct = default)
    {
        List<string> files;
        try
        {
            files = new DirectoryInfo(path)
                .EnumerateFiles()
                .Where(f => AudioFormats.IsAudioFile(f.Name))
                .Where(f => (f.Attributes & FileAttributes.Hidden) == 0)
                .Select(f => f.FullName)
                .ToList();
        }
        catch { return []; }

        var tracks = new List<AudioTrack>(files.Count);
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            var t = AudioProbe.Read(f);
            if (t is not null) tracks.Add(t);
        }

        return Sort(tracks);
    }

    /// <summary>
    /// Alle Audiodateien unterhalb eines Ordners, sortiert nach Ordner und
    /// darin nach Track-Reihenfolge.
    ///
    /// Gedacht zum Sichten und für Sammelaktionen — nicht zum Umsortieren:
    /// eine Reihenfolge über Ordnergrenzen hinweg ergibt keine Playlist.
    /// </summary>
    public static IReadOnlyList<AudioTrack> TracksRecursive(
        string path, int maxFiles = 5000, CancellationToken ct = default)
    {
        var result = new List<AudioTrack>();
        Walk(path, 0);
        return result;

        void Walk(string folder, int depth)
        {
            if (depth > 12 || result.Count >= maxFiles) return;
            ct.ThrowIfCancellationRequested();

            foreach (var t in Tracks(folder, ct))
            {
                if (result.Count >= maxFiles) return;
                result.Add(t);
            }

            foreach (var sub in Subfolders(folder))
                Walk(sub.Path, depth + 1);
        }
    }

    /// <summary>Grobe Vorabzählung für die Rückfrage vor dem rekursiven Sichten.</summary>
    public static int CountRecursive(string path, int stopAt = 5000)
    {
        var n = 0;
        Walk(path, 0);
        return n;

        void Walk(string folder, int depth)
        {
            if (depth > 12 || n >= stopAt) return;
            try
            {
                n += new DirectoryInfo(folder).EnumerateFiles()
                    .Count(f => AudioFormats.IsAudioFile(f.Name)
                                && (f.Attributes & FileAttributes.Hidden) == 0);
            }
            catch { }
            foreach (var sub in Subfolders(folder)) Walk(sub.Path, depth + 1);
        }
    }

    public static List<AudioTrack> Sort(IEnumerable<AudioTrack> tracks) =>
        TrackSorting.FolderSort(tracks);

    /// <summary>
    /// Einstiegspunkte im Baum: eigene Ordner zuerst, dann die Bibliotheken
    /// des Systems, dann die Laufwerke. Eigene stehen oben, weil man sie
    /// bewusst hinzugefügt hat.
    /// </summary>
    public static IReadOnlyList<FolderEntry> Roots(
        IEnumerable<string>? custom = null, IEnumerable<string>? hidden = null)
    {
        var skip = new HashSet<string>(
            (hidden ?? []).Select(p => p.TrimEnd(Path.DirectorySeparatorChar)),
            StringComparer.OrdinalIgnoreCase);

        var roots = new List<FolderEntry>();

        foreach (var path in custom ?? [])
        {
            try
            {
                if (!Directory.Exists(path)) continue;
                var name = new DirectoryInfo(path).Name;
                roots.Add(new FolderEntry(path,
                    string.IsNullOrEmpty(name) ? path : name, true, IsCustomRoot: true));
            }
            catch { }
        }

        void Add(Environment.SpecialFolder folder, string label)
        {
            try
            {
                var p = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(p) && Directory.Exists(p))
                    roots.Add(new FolderEntry(p, label, true));
            }
            catch { }
        }

        Add(Environment.SpecialFolder.MyMusic, "Musik");
        Add(Environment.SpecialFolder.UserProfile, "Benutzer");

        try
        {
            var downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloads))
                roots.Add(new FolderEntry(downloads, "Downloads", true));
        }
        catch { }

        try
        {
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
                roots.Add(new FolderEntry(d.RootDirectory.FullName,
                                          d.Name.TrimEnd('\\'), true));
        }
        catch { }

        return [.. roots.Where(r =>
            !skip.Contains(r.Path.TrimEnd(Path.DirectorySeparatorChar)))];
    }
}
