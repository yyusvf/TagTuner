using LocalPrep.Core.Audio;
using LocalPrep.Core.Model;

namespace LocalPrep.Core.Folders;

public sealed record FolderEntry(string Path, string Name, bool IsDirectory)
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
    public static IReadOnlyList<FolderEntry> Subfolders(string path)
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
    public static bool HasSubfolders(string path)
    {
        try { return Subfolders(path).Count > 0; }
        catch { return false; }
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

    public static List<AudioTrack> Sort(IEnumerable<AudioTrack> tracks) =>
        tracks
            .OrderBy(t => t.Disc == 0 ? uint.MaxValue : t.Disc)
            .ThenBy(t => t.Track == 0 ? uint.MaxValue : t.Track)
            .ThenBy(t => t.FileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Einstiegspunkte im Baum: Bibliotheken zuerst, dann Laufwerke.</summary>
    public static IReadOnlyList<FolderEntry> Roots()
    {
        var roots = new List<FolderEntry>();

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
                roots.Insert(Math.Min(1, roots.Count), new FolderEntry(downloads, "Downloads", true));
        }
        catch { }

        try
        {
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
                roots.Add(new FolderEntry(d.RootDirectory.FullName,
                                          d.Name.TrimEnd('\\'), true));
        }
        catch { }

        return roots;
    }
}
