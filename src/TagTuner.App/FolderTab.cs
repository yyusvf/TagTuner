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
        if (Sort != key) { Sort = key; SortDescending = false; }
        else if (!SortDescending) SortDescending = true;
        else { Sort = TrackSort.Natural; SortDescending = false; }
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
