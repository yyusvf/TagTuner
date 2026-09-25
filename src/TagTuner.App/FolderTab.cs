using System.Collections.ObjectModel;
using TagTuner.Core.Folders;
using TagTuner.Core.Model;

namespace TagTuner.App;

/// <summary>
/// Ein geöffneter Ordner. Jeder Tab hält seinen eigenen Inhalt, seine eigene
/// Auswahl und seine eigene Navigationsgeschichte — sonst springt beim
/// Tabwechsel die Auswahl des anderen Ordners heraus.
/// </summary>
public sealed class FolderTab(string path)
{
    public string Path { get; private set; } = path;

    public ObservableCollection<AudioTrack> Tracks { get; } = [];
    public FolderAnalysis? Analysis { get; set; }
    public FolderTarget? Target { get; set; }

    /// <summary>Pfade der ausgewählten Tracks — überlebt ein Neueinlesen.</summary>
    public List<string> SelectedPaths { get; } = [];

    /// <summary>
    /// Zeigt auch die Unterordner. Dann ist die Liste eine Sicht zum Sichten
    /// und für Sammelaktionen, keine Playlist — Umsortieren und Ablegen sind
    /// abgeschaltet, weil beides einen einzelnen Ordner voraussetzt.
    /// </summary>
    public bool Recursive { get; set; }

    /// <summary>
    /// Zeigt nur diese Dateien aus dem Ordner. Für das kleine Fenster aus dem
    /// Explorer, das genau die dort markierten Lieder listet. Null heißt: alle.
    /// </summary>
    public HashSet<string>? Only { get; set; }

    /// <summary>Wendet <see cref="Only"/> auf frisch gelesene Tracks an.</summary>
    public IReadOnlyList<AudioTrack> Filter(IReadOnlyList<AudioTrack> tracks) =>
        Only is { } only ? [.. tracks.Where(t => only.Contains(t.Path))] : tracks;

    /// <summary>
    /// Wonach die Liste gerade sortiert ist. „Natural" ist die
    /// Playlist-Reihenfolge und die einzige, in der von Hand umsortiert
    /// werden darf.
    /// </summary>
    public TrackSort Sort { get; set; } = TrackSort.Natural;

    public bool SortDescending { get; set; }

    /// <summary>
    /// Ein Klick auf dieselbe Spalte dreht die Richtung; der dritte Klick
    /// führt zurück zur Playlist-Reihenfolge, damit man nicht im Sortieren
    /// festhängt.
    /// </summary>
    public void ToggleSort(TrackSort key)
    {
        if (key == TrackSort.Track) { ToggleNumberSort(); return; }

        if (Sort != key) { Sort = key; SortDescending = false; }
        else if (!SortDescending) SortDescending = true;
        else { Sort = TrackSort.Natural; SortDescending = false; }
    }

    /// <summary>
    /// Die Spalte # schaltet mit vier Klicks durch: Track auf, Track ab,
    /// Disc auf, Disc ab, und der fünfte führt zurück zur
    /// Playlist-Reihenfolge. Gibt es nur eine Disc, fallen die beiden
    /// Disc-Stufen weg: Nach einer einzigen Disc zu sortieren ändert nichts.
    /// </summary>
    private void ToggleNumberSort()
    {
        var discs = Tracks.Select(t => t.Disc).Where(d => d > 0).Distinct().Count() > 1;

        (Sort, SortDescending) = (Sort, SortDescending) switch
        {
            (TrackSort.Track, false) => (TrackSort.Track, true),
            (TrackSort.Track, true) when discs => (TrackSort.Disc, false),
            (TrackSort.Disc, false) => (TrackSort.Disc, true),
            (TrackSort.Track or TrackSort.Disc, _) => (TrackSort.Natural, false),
            _ => (TrackSort.Track, false),
        };
    }

    public string Name
    {
        get
        {
            var n = System.IO.Path.GetFileName(Path.TrimEnd(System.IO.Path.DirectorySeparatorChar));
            return string.IsNullOrEmpty(n) ? Path : n;
        }
    }

    /// <summary>Der Ordner weicht von seinem eigenen Ziel ab — im Tab als Punkt sichtbar.</summary>
    public bool IsMixed => Analysis is { IsEmpty: false, IsUniform: false };

    // ── Navigationsgeschichte ────────────────────────────────────
    private readonly List<string> _history = [path];
    private int _index;

    public bool CanGoBack => _index > 0;
    public bool CanGoForward => _index < _history.Count - 1;

    /// <summary>Wechselt den Ordner und schneidet dabei einen abgezweigten Vorwärtszweig ab.</summary>
    public void Navigate(string newPath)
    {
        if (string.Equals(newPath, Path, StringComparison.OrdinalIgnoreCase)) return;

        if (_index < _history.Count - 1)
            _history.RemoveRange(_index + 1, _history.Count - _index - 1);

        _history.Add(newPath);
        _index = _history.Count - 1;
        Path = newPath;
        SelectedPaths.Clear();
        Recursive = false;
    }

    public string? Back()
    {
        if (!CanGoBack) return null;
        _index--;
        Path = _history[_index];
        SelectedPaths.Clear();
        Recursive = false;
        return Path;
    }

    public string? Forward()
    {
        if (!CanGoForward) return null;
        _index++;
        Path = _history[_index];
        SelectedPaths.Clear();
        Recursive = false;
        return Path;
    }

    /// <summary>Die Pfadteile für die Breadcrumb-Leiste.</summary>
    public IReadOnlyList<string> Crumbs()
    {
        var parts = Path.Split(System.IO.Path.DirectorySeparatorChar,
                               StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? [Path] : parts;
    }
}
