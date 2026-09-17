using System.Diagnostics;
using TagTuner.Core.Audio;

namespace TagTuner.Core.Folders;

/// <summary>
/// Die Namen aller Ordner und Lieder der Bibliothek, einmal eingelesen.
///
/// Vorher lief jede Suche über die Platte: bei jedem Tastendruck ein
/// vollständiger Durchlauf über alle Verzeichnisse. Das dauerte selbst mit
/// Verzögerung spürbar. Jetzt wird einmal gelaufen und danach nur noch im
/// Speicher gefiltert — das sind ein paar tausend Zeichenkettenvergleiche und
/// damit nicht mehr messbar.
///
/// Der Index altert: Wer außerhalb der App Dateien verschiebt, sieht das erst
/// nach einem Neuaufbau. Die App wirft ihn nach jedem eigenen Schreibvorgang
/// weg, und das reicht in der Praxis.
/// </summary>
public sealed class LibraryIndex
{
    public IReadOnlyList<SearchHit> Entries { get; }
    public int Folders { get; }
    public int Tracks { get; }
    public TimeSpan BuildTime { get; }

    private LibraryIndex(List<SearchHit> entries, TimeSpan time)
    {
        Entries = entries;
        Folders = entries.Count(e => e.Kind == HitKind.Folder);
        Tracks = entries.Count - Folders;
        BuildTime = time;
    }

    public static LibraryIndex Build(
        IEnumerable<string> roots, bool onlyAudioFolders = false, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        var entries = new List<SearchHit>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots) Walk(root, 0);

        watch.Stop();
        return new LibraryIndex(entries, watch.Elapsed);

        void Walk(string folder, int depth)
        {
            if (depth > 12) return;
            ct.ThrowIfCancellationRequested();

            var folderName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));

            try
            {
                // EnumerateFiles statt DirectoryInfo: Hier zählt nur der Name,
                // und die Zeichenkettenfassung spart den Umweg über die
                // Dateiattribute jedes einzelnen Eintrags.
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    if (!AudioFormats.IsAudioFile(file)) continue;
                    if (!seen.Add(file)) continue;

                    entries.Add(new SearchHit(HitKind.Track, file,
                        Path.GetFileNameWithoutExtension(file), folderName));
                }
            }
            catch { }

            foreach (var sub in FolderScanner.Subfolders(folder, onlyAudioFolders))
            {
                if (seen.Add(sub.Path))
                    entries.Add(new SearchHit(HitKind.Folder, sub.Path, sub.Name, folderName));

                Walk(sub.Path, depth + 1);
            }
        }
    }
}
