using TagTuner.Core.Audio;
using TagTuner.Core.Model;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;
using Windows.Foundation;

using TagTuner.Core.Settings;

namespace TagTuner.App;

/// <summary>
/// Die Zeile einer Disc in der Liste. Eine Klasse statt eines Records: Die
/// Liste erkennt ihre Einträge am Objekt, und zwei Zeilen „Disc 1" wären als
/// Record dasselbe.
/// </summary>
public sealed class DiscHeader(uint disc)
{
    public uint Disc { get; } = disc;
}

/// <summary>Dateien aus dem Explorer, auf einer Hälfte abgelegt.</summary>
public sealed record FilesDroppedArgs(TrackPane Pane, IReadOnlyList<string> Paths, int Index);

/// <summary>Tracks, die aus der anderen Hälfte herübergezogen wurden.</summary>
public sealed record TracksMovedArgs(
    TrackPane From, TrackPane To, IReadOnlyList<AudioTrack> Tracks, int Index, bool Copy);

/// <summary>
/// Eine Hälfte der Listenansicht: Kopfzeile, Spaltenüberschriften, Trackliste.
///
/// Als eigenes Steuerelement, damit die geteilte Ansicht zweimal dieselbe
/// Liste zeigen kann, ohne das Markup zu verdoppeln.
/// </summary>
public sealed partial class TrackPane : UserControl
{
    public FolderTab? Tab { get; private set; }

    public event EventHandler<TrackPane>? SelectionChanged;
    public event EventHandler<TrackPane>? Activated;
    public event EventHandler<FilesDroppedArgs>? FilesDropped;
    public event EventHandler<TrackPane>? ReorderCompleted;
    public event EventHandler<TracksMovedArgs>? TracksMoved;
    public event EventHandler<IReadOnlyList<AudioTrack>>? DeleteRequested;
    public event EventHandler<string>? NavigateRequested;
    public event EventHandler? ColumnsResized;
    public event EventHandler<AudioTrack>? PlayRequested;
    public event EventHandler<AudioTrack>? CopyTagsRequested;
    public event EventHandler<IReadOnlyList<AudioTrack>>? PasteTagsRequested;

    /// <summary>
    /// Ob etwas zum Einfügen bereitliegt. Die Liste weiß das nicht von selbst,
    /// das Hauptfenster hält den Zwischenspeicher.
    /// </summary>
    public bool TagsCopied { get; set; }
    public event EventHandler<TrackSort>? SortRequested;

    /// <summary>Zurück aus der Sicht mit Unterordnern in den einzelnen Ordner.</summary>
    public event EventHandler<TrackPane>? ScopeExitRequested;

    /// <summary>
    /// Der laufende Zug innerhalb der App. Das Datenpaket der Liste trägt beim
    /// Umsortieren nur listeninterne Objekte — quer über zwei Hälften ist davon
    /// nichts zu gebrauchen. Die Quelle merken ist verlässlicher als raten.
    /// </summary>
    private static (TrackPane Pane, List<AudioTrack> Tracks)? _drag;

    private bool _suppress;
    private bool _droppedHere;

    // ── Spalten ──────────────────────────────────────────────────

    private readonly Dictionary<TrackSort, TextBlock> _sortMarks = [];
    private List<TrackColumn> _columns = [];
    private bool _combined = true;
    private AppSettings? _settings;

    /// <summary>
    /// Ob ein Ordner im Album-Modus steht. Die Liste kennt die Regeln nicht
    /// selbst; das Hauptfenster reicht die Frage durch.
    /// </summary>
    public Func<string, bool>? IsAlbumFolder { get; set; }

    /// <summary>Eine Spalte wurde in der Kopfzeile an eine andere Stelle gezogen.</summary>
    public event EventHandler? ColumnsReordered;

    /// <summary>
    /// Was in der Track-Zelle steht, je Pfad. Wird bei jedem Auffrischen neu
    /// berechnet, weil es an der Reihenfolge hängt: „die erste Datei jeder
    /// Disc" ist nach einem Umsortieren eine andere.
    /// </summary>
    private Dictionary<string, string> _trackText = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ob die Liste gerade Disc-Zeilen zeigt. Dann zeigt sie nicht die Lieder
    /// des Ordners selbst, sondern <see cref="_view"/>: dieselben Lieder mit
    /// den Disc-Zeilen dazwischen.
    /// </summary>
    private bool _sectioned;

    private readonly System.Collections.ObjectModel.ObservableCollection<object> _view = [];

    /// <summary>
    /// Die Disc-Zeilen, wiederverwendet: Eine neue Zeile bei jedem Abgleich
    /// hieße, dass die Liste sie jedes Mal herausnimmt und neu einsetzt.
    /// </summary>
    private readonly Dictionary<uint, DiscHeader> _headers = [];

    private bool _syncQueued;

    /// <summary>
    /// Nach einem Umsortieren mit Disc-Zeilen: jedes Lied mit der Disc, unter
    /// der es jetzt steht. Wer ein Lied in eine andere Disc zieht, meint,
    /// dass es dorthin gehört. Ohne Disc-Zeilen leer.
    /// </summary>
    public IReadOnlyList<(AudioTrack Track, uint Disc)>? ReorderedSections { get; private set; }

    /// <summary>
    /// Übernimmt Auswahl, Reihenfolge und Breiten der Spalten. Baut die
    /// Kopfzeile neu und wirft die vorhandenen Zeilen weg, damit sie beim
    /// nächsten Zeichnen mit der neuen Aufteilung entstehen.
    /// </summary>
    public void ApplyColumns(AppSettings settings)
    {
        _settings = settings;
        _columns = TrackColumns.Resolve(settings);
        _combined = settings.CombineTitleAndArtist;

        Columns.Set(TrackColumns.Widths(settings, _columns));

        TrackColumns.BuildHeader(
            HeaderRow, _columns, Columns, _combined, _sortMarks,
            OnHeaderTapped, OnColumnPressed,
            (Style)Application.Current.Resources["ColumnGrip"],
            OnGripPressed, OnGripMoved, OnGripReleased, OnGripExited);

        HeaderRow.Children.Add(_dropMark);

        UpdateSortMarks();
        ComputeTrackText();
        UpdateWidth();

        // Die Liste hält gebaute Zeilen für das Wiederverwenden bereit. Nach
        // einer Änderung der Spalten passen die nicht mehr, und ohne diesen
        // Stups behielte man die alte Aufteilung bis zum Neustart.
        var items = List.ItemsSource;
        List.ItemsSource = null;
        List.ItemsSource = items;
    }

    /// <summary>
    /// Füllt eine Zeile. Beim ersten Mal wird ihr Inhalt gebaut, danach nur
    /// noch gesetzt: Beim Scrollen durch tausend Lieder dieselben vierzig
    /// Zeilen wiederzuverwenden ist der Unterschied zwischen flüssig und
    /// ruckelig.
    /// </summary>
    private void OnRowRealizing(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        if (args.ItemContainer is not ListViewItem container) return;

        // Die Vorlage der Liste ist ein einziges, leeres Grid. Hineingebaut
        // wird im Code: ContentTemplateRoot lässt sich nicht setzen, und eine
        // Vorlage mit den Spalten darin gäbe es zur Bauzeit noch nicht.
        if (container.ContentTemplateRoot is not Grid row) return;

        if (args.Item is DiscHeader header)
        {
            // Zeilen werden zwischen Liedern und Discs weitergereicht; die
            // Mindesthöhe eines Lieds passt nicht zu einer Disc-Zeile.
            container.MinHeight = 0;
            container.Background = null;
            if (row.Tag as string != TrackColumns.DiscRowTag) TrackColumns.BuildDiscRow(row);
            PaintHeader(container, header, SelectedSet());
            row.DataContext = header;
            TrackColumns.FillDiscRow(row, header.Disc);
            args.Handled = true;
            return;
        }

        if (args.Item is not AudioTrack track) return;
        container.MinHeight = 56;

        if (row.Tag as string != Stamp())
        {
            TrackColumns.BuildRow(row, _columns, Columns, _combined);
            row.Tag = Stamp();
        }

        // Ausdrücklich setzen: Mit Handled = true übernimmt die Liste das
        // Binden nicht mehr, und ob die Zeile ihren DataContext dann noch
        // bekommt, ist nicht zugesichert.
        row.DataContext = track;

        TrackColumns.FillRow(row, track, _combined, TrackTextFor);
        Paint(container, track);
        args.Handled = true;
    }

    /// <summary>
    /// Der Track unter einem Zeiger- oder Tastenereignis.
    ///
    /// Über den Container statt über DataContext: Seit die Zeilen im Code
    /// gebaut werden, ist der DataContext der Zelle nicht mehr verlässlich
    /// gesetzt. Rechtsklick und Doppelklick fanden deshalb keinen Track und
    /// taten einfach nichts.
    /// </summary>
    private AudioTrack? TrackAt(object? source)
    {
        for (var node = source as DependencyObject; node is not null;
             node = VisualTreeHelper.GetParent(node))
        {
            if (node is ListViewItem item) return List.ItemFromContainer(item) as AudioTrack;
            if (ReferenceEquals(node, List)) break;
        }
        return null;
    }

    // ── Disc und Track ───────────────────────────────────────────

    private string TrackTextFor(AudioTrack track) =>
        _trackText.TryGetValue(track.Path, out var text) ? text : track.TrackLabel;

    /// <summary>
    /// Rechnet aus, wie Disc und Track erscheinen.
    ///
    /// Im Album mit mehreren Discs und in Playlist-Reihenfolge bekommt jede
    /// Disc eine eigene Zeile, wie bei Spotify. Sonst, wenn überhaupt mehrere
    /// Discs vorkommen, steht sie kompakt als „2-04" in der Nummer. Bei einer
    /// einzigen Disc bleibt es bei der Nummer: Eine „1" vor jedem Lied sagt
    /// nichts.
    /// </summary>
    private void ComputeTrackText()
    {
        _trackText = new(StringComparer.OrdinalIgnoreCase);
        _sectioned = false;
        if (Tab is null || _settings is not { CombineDiscAndTrack: true }) return;

        var discs = Tab.Tracks.Select(t => t.Disc).Where(d => d > 0).Distinct().Count();
        if (discs < 2) return;

        _sectioned = !Tab.Recursive
                     && Tab.Sort == TrackSort.Natural
                     && (IsAlbumFolder?.Invoke(Tab.Path) ?? false);
        if (_sectioned) return;

        foreach (var track in Tab.Tracks)
        {
            _trackText[track.Path] =
                track.Disc > 0 && track.Track > 0 ? $"{track.Disc}-{track.Track:00}" : track.TrackLabel;
        }
    }

    /// <summary>
    /// Stellt die Liste auf das ein, was sie zeigen soll: die Lieder selbst,
    /// oder die Lieder mit Disc-Zeilen. Gleicht dabei nur ab, was sich
    /// geändert hat, damit Auswahl und Bildlauf stehen bleiben.
    /// </summary>
    private void UpdateView()
    {
        if (Tab is null) return;

        if (!_sectioned)
        {
            if (!ReferenceEquals(List.ItemsSource, Tab.Tracks)) List.ItemsSource = Tab.Tracks;
            _view.Clear();
            return;
        }

        var wanted = new List<object>(Tab.Tracks.Count + 4);
        var used = new HashSet<DiscHeader>();
        uint? previous = null;

        foreach (var track in Tab.Tracks)
        {
            if (track.Disc > 0 && track.Disc != previous)
            {
                // Kommt eine Disc ein zweites Mal vor, braucht sie eine eigene
                // Zeile: Dasselbe Objekt zweimal verträgt die Liste nicht.
                if (!_headers.TryGetValue(track.Disc, out var header) || !used.Add(header))
                {
                    header = new DiscHeader(track.Disc);
                    if (!_headers.ContainsKey(track.Disc)) _headers[track.Disc] = header;
                    used.Add(header);
                }
                wanted.Add(header);
            }
            wanted.Add(track);
            previous = track.Disc;
        }

        Sync(_view, wanted);
        if (!ReferenceEquals(List.ItemsSource, _view)) List.ItemsSource = _view;
    }

    /// <summary>Bringt eine Liste mit möglichst wenigen Schritten auf den Stand einer anderen.</summary>
    private static void Sync(System.Collections.ObjectModel.ObservableCollection<object> target, List<object> wanted)
    {
        var keep = new HashSet<object>(wanted, ReferenceEqualityComparer.Instance);
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(target[i])) target.RemoveAt(i);
        }

        for (var i = 0; i < wanted.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], wanted[i])) continue;

            var at = -1;
            for (var j = i + 1; j < target.Count; j++)
            {
                if (ReferenceEquals(target[j], wanted[i])) { at = j; break; }
            }

            if (at < 0) target.Insert(i, wanted[i]);
            else target.Move(at, i);
        }
    }

    /// <summary>
    /// Die Lieder des Ordners haben sich geändert, etwa nach dem Einlesen.
    /// Der Abgleich wartet, bis alle Änderungen durch sind: Beim Einlesen
    /// kommt jedes Lied einzeln, und jedes Mal neu abzugleichen wäre Arbeit
    /// für nichts.
    /// </summary>
    private void OnTracksChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (!_sectioned || _syncQueued) return;
        _syncQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _syncQueued = false;
            ComputeTrackText();
            UpdateView();
            RefillRealized();
        });
    }

    /// <summary>
    /// Eine Disc-Zeile ist selbst nie in der Auswahl, sonst zöge sie beim
    /// Ziehen mit und zählte bei allem, was mit der Auswahl geschieht. Sie
    /// sieht aber gewählt aus, solange alle ihre Lieder es sind.
    /// </summary>
    private void PaintHeaders()
    {
        if (!_sectioned) return;
        var selected = SelectedSet();
        foreach (var item in _view)
        {
            if (item is DiscHeader header && List.ContainerFromItem(header) is ListViewItem container)
                PaintHeader(container, header, selected);
        }
    }

    private void PaintHeader(ListViewItem container, DiscHeader header, HashSet<AudioTrack> selected)
    {
        var any = false;
        var all = true;
        foreach (var track in SectionOf(header))
        {
            any = true;
            if (!selected.Contains(track)) { all = false; break; }
        }

        // In die Zeile selbst gemalt, nicht als Hintergrund des Eintrags:
        // Den zeichnet die Liste erst beim nächsten Überfahren mit der Maus
        // neu, und die Disc blieb so hervorgehoben, obwohl nichts mehr
        // gewählt war.
        if (container.ContentTemplateRoot is Grid row)
        {
            row.Background = any && all
                ? (Brush)Application.Current.Resources["ListViewItemBackgroundSelected"]
                : null;
        }
    }

    private HashSet<AudioTrack> SelectedSet() =>
        new(List.SelectedItems.OfType<AudioTrack>(), ReferenceEqualityComparer.Instance);

    /// <summary>Die Lieder unter einer Disc-Zeile, bis zur nächsten.</summary>
    private IEnumerable<AudioTrack> SectionOf(DiscHeader header)
    {
        var at = _view.IndexOf(header);
        if (at < 0) yield break;
        for (var i = at + 1; i < _view.Count && _view[i] is AudioTrack track; i++)
            yield return track;
    }

    /// <summary>
    /// Füllt die Zeilen neu, die gerade gezeichnet sind. Nach einem
    /// Umsortieren hat sich geändert, welche Datei die erste ihrer Disc ist,
    /// die Zeilen selbst aber nicht.
    /// </summary>
    private void RefillRealized()
    {
        foreach (var item in List.Items)
        {
            if (item is not AudioTrack track) continue;
            if (List.ContainerFromItem(item) is not ListViewItem container) continue;
            if (container.ContentTemplateRoot is not Grid row || row.Tag as string != Stamp()) continue;
            TrackColumns.FillRow(row, track, _combined, TrackTextFor);
        }
    }

    // ── Spalten verschieben ──────────────────────────────────────

    private int _dragColumn = -1;
    private double _dragStartX;
    private bool _columnDragging;
    private bool _columnDragged;
    private int _dropAt = -1;

    /// <summary>Die Einfügemarke beim Ziehen einer Spalte.</summary>
    private readonly Microsoft.UI.Xaml.Shapes.Rectangle _dropMark = new()
    {
        Width = 2,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Stretch,
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
        Fill = (Brush)Application.Current.Resources["AccentBrush"],
    };

    private const double DragThreshold = 8;

    private void OnColumnPressed(int index, PointerRoutedEventArgs e)
    {
        _dragColumn = index;
        _dragStartX = e.GetCurrentPoint(HeaderRow).Position.X;
        _columnDragging = false;
    }

    private void OnHeaderMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragColumn < 0) return;

        var x = e.GetCurrentPoint(HeaderRow).Position.X;

        // Erst ab ein paar Pixeln ist es ein Zug. Darunter bleibt es ein
        // Klick, und der sortiert.
        if (!_columnDragging)
        {
            if (Math.Abs(x - _dragStartX) < DragThreshold) return;
            _columnDragging = true;
            HeaderRow.CapturePointer(e.Pointer);
        }

        _dropAt = InsertionIndex(x);
        _dropMark.Margin = new Thickness(BoundaryX(_dropAt), 0, 0, 0);
        Grid.SetColumnSpan(_dropMark, _columns.Count + 1);
        _dropMark.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void OnHeaderReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragColumn < 0) return;

        var from = _dragColumn;
        var to = _dropAt;
        var dragged = _columnDragging;

        _dragColumn = -1;
        _columnDragging = false;
        _dropMark.Visibility = Visibility.Collapsed;
        HeaderRow.ReleasePointerCaptures();

        if (!dragged) return;

        _columnDragged = true;
        e.Handled = true;
        MoveColumn(from, to);
    }

    /// <summary>
    /// Vor welche sichtbare Spalte der Zeiger zielt. Die Grenze liegt in der
    /// Mitte jeder Spalte, sonst müsste man eine breite Spalte bis ganz
    /// hinüber ziehen, um an ihr vorbeizukommen.
    /// </summary>
    private int InsertionIndex(double x)
    {
        var left = HeaderRow.Padding.Left;
        for (var i = 0; i < _columns.Count; i++)
        {
            var width = Columns.WidthAt(i);
            if (x < left + width / 2) return i;
            left += width + HeaderRow.ColumnSpacing;
        }
        return _columns.Count;
    }

    /// <summary>Wo die Einfügemarke steht, gemessen vom Anfang der ersten Spalte.</summary>
    private double BoundaryX(int index)
    {
        var x = 0.0;
        for (var i = 0; i < index && i < _columns.Count; i++)
            x += Columns.WidthAt(i) + HeaderRow.ColumnSpacing;
        return Math.Max(0, x - HeaderRow.ColumnSpacing / 2 - 1);
    }

    /// <summary>
    /// Verschiebt eine Spalte in der gespeicherten Reihenfolge. Gerechnet wird
    /// über die Kürzel, nicht über Positionen: In der gespeicherten Liste
    /// stehen auch Spalten, die gerade ausgeblendet sind.
    /// </summary>
    private void MoveColumn(int from, int to)
    {
        if (_settings is null || from < 0 || from >= _columns.Count) return;
        if (to == from || to == from + 1) return;

        TrackColumns.EnsureStates(_settings);
        var states = _settings.TrackColumns;

        var moving = states.First(st => st.Id == _columns[from].Id);
        states.Remove(moving);

        if (to >= _columns.Count)
        {
            // Hinter die letzte sichtbare Spalte.
            var last = states.FindIndex(st => st.Id == _columns[^1].Id);
            states.Insert(last + 1, moving);
        }
        else
        {
            var before = states.FindIndex(st => st.Id == _columns[to].Id);
            states.Insert(before, moving);
        }

        _settings.Save();
        ColumnsReordered?.Invoke(this, EventArgs.Empty);
    }

    // ── Breite für waagerechtes Scrollen ─────────────────────────

    /// <summary>
    /// Wie breit Kopfzeile und Liste sein müssen. Passen die Spalten nicht
    /// ins Fenster, wird der Bereich breiter und lässt sich waagerecht
    /// schieben; passen sie, füllt er das Fenster wie bisher.
    /// </summary>
    private void UpdateWidth()
    {
        var needed = HeaderRow.Padding.Left + HeaderRow.Padding.Right;
        for (var i = 0; i < _columns.Count; i++)
            needed += Columns.WidthAt(i) + HeaderRow.ColumnSpacing;

        // Etwas Luft für die Zeilen, deren Innenabstand größer ist als der
        // der Kopfzeile, und für die senkrechte Laufleiste.
        needed += 24;

        Wide.Width = Math.Max(WideScroll.ActualWidth, needed);
    }

    private void OnWideScrollResized(object sender, SizeChangedEventArgs e) => UpdateWidth();

    // ── Hervorheben ──────────────────────────────────────────────

    private HashSet<string> _marked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Hebt Zeilen hervor, etwa die Treffer der Suche im Ordner.
    ///
    /// Die Liste gibt ihre Zeilen beim Scrollen weiter, darum wird die
    /// Markierung beim Zeichnen jeder Zeile neu gesetzt und nicht nur einmal
    /// auf das, was gerade zu sehen ist.
    /// </summary>
    public void Mark(IEnumerable<string> paths)
    {
        _marked = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);

        foreach (var item in List.Items)
        {
            if (item is not AudioTrack track) continue;
            if (List.ContainerFromItem(item) is ListViewItem container)
                Paint(container, track);
        }
    }

    private void Paint(ListViewItem container, AudioTrack track)
    {
        container.Background = _marked.Contains(track.Path)
            ? (Brush)Application.Current.Resources["AccentDimBrush"]
            : null;
    }

    /// <summary>Holt eine Zeile ins Bild und wählt sie aus.</summary>
    public void Reveal(AudioTrack track)
    {
        List.ScrollIntoView(track);
        _suppress = true;
        List.SelectedItems.Clear();
        List.SelectedItems.Add(track);
        _suppress = false;
        PaintHeaders();
        SelectionChanged?.Invoke(this, this);
    }

    /// <summary>
    /// Woran sich erkennen lässt, ob eine wiederverwendete Zeile noch zur
    /// aktuellen Spaltenaufteilung passt.
    /// </summary>
    private string Stamp() =>
        string.Join(",", _columns.Select(c => c.Id)) + (_combined ? "|1" : "|0");

    public TrackPane()
    {
        InitializeComponent();

        // Zieht jemand eine Spalte breiter, muss der Bereich mitwachsen,
        // sonst verschwindet ihr rechter Teil hinter dem Fensterrand.
        Columns.PropertyChanged += (_, _) => UpdateWidth();
    }

    /// <summary>Hebt die Hälfte hervor, die gerade die Metadatenspalte speist.</summary>
    public void SetActive(bool active)
    {
        PaneBar.Opacity = active ? 1.0 : 0.65;
        FolderName.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            active ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"];
    }

    public void Bind(FolderTab tab)
    {
        if (Tab is not null) Tab.Tracks.CollectionChanged -= OnTracksChanged;
        Tab = tab;
        tab.Tracks.CollectionChanged += OnTracksChanged;

        _view.Clear();
        _headers.Clear();
        List.ItemsSource = tab.Tracks;
        Refresh();
    }

    public void Refresh()
    {
        if (Tab is null) return;

        FolderName.Text = Tab.Name;
        var n = Tab.Tracks.Count;
        CountLabel.Text = $"{n} Track{(n == 1 ? "" : "s")}";

        // Mit Unterordnern ist die Liste eine Sicht, keine Playlist: Eine
        // Reihenfolge ueber Ordnergrenzen hinweg ergibt nichts, und Ablegen
        // wuesste nicht, in welchen der Ordner die Datei gehoert.
        ScopeBadge.Visibility = Tab.Recursive ? Visibility.Visible : Visibility.Collapsed;

        // Umsortieren per Hand setzt voraus, dass die Anzeige die
        // Playlist-Reihenfolge ist. Nach Interpret sortiert waere das Ziehen
        // einer Zeile eine Nummernvergabe, die niemand so gemeint hat.
        var natural = !Tab.Recursive && Tab.Sort == TrackSort.Natural;
        List.CanReorderItems = natural;
        List.CanDragItems = !Tab.Recursive;
        List.AllowDrop = !Tab.Recursive;

        UpdateSortMarks();
        ComputeTrackText();
        UpdateView();
        RefillRealized();

        // Auswahl nach einem Neueinlesen wiederherstellen — sonst verliert
        // man nach jeder Operation, woran man gerade gearbeitet hat.
        _suppress = true;
        List.SelectedItems.Clear();
        foreach (var t in Tab.Tracks.Where(t => Tab.SelectedPaths.Contains(t.Path)))
            List.SelectedItems.Add(t);
        _suppress = false;
        PaintHeaders();
    }

    public List<AudioTrack> Selected() => List.SelectedItems.OfType<AudioTrack>().ToList();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || Tab is null) return;

        // Eine Disc-Zeile ist selbst nichts zum Auswählen. Ein Klick auf sie
        // wählt ihre Lieder (mit Strg zusätzlich); bei „alle auswählen" oder
        // einem Bereich mit Umschalt fällt sie einfach wieder heraus.
        var headers = e.AddedItems.OfType<DiscHeader>().ToList();
        if (headers.Count > 0)
        {
            var single = e.AddedItems.Count == 1;
            _suppress = true;
            foreach (var header in headers) List.SelectedItems.Remove(header);
            if (single)
            {
                foreach (var track in SectionOf(headers[0]))
                {
                    if (!List.SelectedItems.Contains(track)) List.SelectedItems.Add(track);
                }
            }
            _suppress = false;
        }

        PaintHeaders();
        Tab.SelectedPaths.Clear();
        Tab.SelectedPaths.AddRange(Selected().Select(t => t.Path));
        SelectionChanged?.Invoke(this, this);
    }

    private void OnGotFocus(object sender, RoutedEventArgs e) => Activated?.Invoke(this, this);

    /// <summary>
    /// Leertaste spielt die Auswahl ab. Die Liste würde damit sonst die
    /// Markierung umschalten, darum wird das Ereignis hier abgefangen.
    /// </summary>
    private void OnListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Space) return;
        if (Selected().FirstOrDefault() is not { } track) return;

        PlayRequested?.Invoke(this, track);
        e.Handled = true;
    }

    private void OnListDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (TrackAt(e.OriginalSource) is not { } track) return;
        PlayRequested?.Invoke(this, track);
        e.Handled = true;
    }

    private void OnSelectAll(object sender, RoutedEventArgs e)
    {
        Activated?.Invoke(this, this);
        List.SelectAll();
    }

    // ══ Ziehen ═══════════════════════════════════════════════════

    private void OnDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Any(i => i is DiscHeader)) { e.Cancel = true; return; }
        _drag = (this, e.Items.OfType<AudioTrack>().ToList());
        _droppedHere = false;
    }

    private void OnReorderCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        var landedElsewhere = _drag?.Pane == this && !_droppedHere;
        _drag = null;

        // Ein Zug in die andere Hälfte darf hier nicht als Umsortieren gelten —
        // sonst würde der Quellordner neu durchnummeriert, obwohl sich an seiner
        // Reihenfolge nichts geändert hat.
        if (landedElsewhere) return;
        if (args.DropResult != DataPackageOperation.Move) return;

        ReorderedSections = null;
        if (_sectioned && Tab is not null)
        {
            // Die Liste hat nur die Anzeige umgestellt. Die Reihenfolge der
            // Lieder im Ordner folgt ihr, und jedes Lied merkt sich die Disc,
            // unter der es gelandet ist. Über der ersten Disc-Zeile zählt es
            // zur ersten Disc.
            var sections = new List<(AudioTrack, uint)>();
            var disc = _view.OfType<DiscHeader>().FirstOrDefault()?.Disc ?? 0;
            foreach (var item in _view)
            {
                if (item is DiscHeader header) disc = header.Disc;
                else if (item is AudioTrack track) sections.Add((track, disc));
            }

            _syncQueued = true;   // kein Abgleich mitten im Umstellen
            for (var i = 0; i < sections.Count; i++)
            {
                var at = Tab.Tracks.IndexOf(sections[i].Item1);
                if (at >= 0 && at != i) Tab.Tracks.Move(at, i);
            }
            _syncQueued = false;

            ReorderedSections = sections;
        }

        ReorderCompleted?.Invoke(this, this);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (Tab?.Recursive == true) { HideLine(); return; }

        var copy = e.Modifiers.HasFlag(DragDropModifiers.Control);

        // Innerhalb derselben Hälfte macht die Liste das Umsortieren selbst.
        if (_drag is { } d && d.Pane == this) { HideLine(); return; }

        var index = IndexAt(e.GetPosition(List));

        if (_drag is { } other)
        {
            ShowLine(index);
            e.AcceptedOperation = copy ? DataPackageOperation.Copy : DataPackageOperation.Move;
            e.DragUIOverride.Caption = Caption(Strings.T(copy ? "Copy to" : "Move to"));
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsGlyphVisible = false;
            e.Handled = true;
            return;
        }

        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) { HideLine(); return; }

        ShowLine(index);
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = Caption(Strings.T("Take into"));
        e.DragUIOverride.IsCaptionVisible = true;
        e.Handled = true;
    }

    private string Caption(string verb)
    {
        var where = Tab?.Name ?? Strings.T("Folder");
        return Tab?.Target is { } t
            ? Strings.T("{0} \"{1}\": {2} · {3}",
                        verb, where, t.Format, FormatRate(t.SampleRate))
            : Strings.T("{0} \"{1}\"", verb, where);
    }

    private void OnDragLeave(object sender, DragEventArgs e) => HideLine();

    private async void OnDrop(object sender, DragEventArgs e)
    {
        HideLine();
        if (Tab?.Recursive == true) return;
        var index = IndexAt(e.GetPosition(List));

        if (_drag is { } d)
        {
            if (d.Pane == this) { _droppedHere = true; return; }   // eigenes Umsortieren

            var copy = e.Modifiers.HasFlag(DragDropModifiers.Control);
            e.AcceptedOperation = copy ? DataPackageOperation.Copy : DataPackageOperation.Move;
            e.Handled = true;

            var source = d.Pane;
            var tracks = d.Tracks;
            _drag = null;

            Activated?.Invoke(this, this);
            TracksMoved?.Invoke(this, new TracksMovedArgs(source, this, tracks, index, copy));
            return;
        }

        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.OfType<Windows.Storage.StorageFile>()
                             .Select(f => f.Path)
                             .Where(AudioFormats.IsAudioFile)
                             .ToList();
            if (paths.Count > 0)
            {
                Activated?.Invoke(this, this);
                FilesDropped?.Invoke(this, new FilesDroppedArgs(this, paths, index));
            }
        }
        finally { deferral.Complete(); }
    }

    // ══ Einfügemarke ═════════════════════════════════════════════

    /// <summary>
    /// Die Position zwischen zwei Zeilen, auf die der Zeiger deutet. Gemessen
    /// wird an den tatsächlich erzeugten Zeilen, damit gescrollte Listen
    /// stimmen; unterhalb der letzten Zeile landet der Zug am Ende.
    /// </summary>
    /// <remarks>
    /// Gezählt wird in Liedern, nicht in Zeilen: Disc-Zeilen sind nichts,
    /// wovor etwas eingefügt wird.
    /// </remarks>
    private int IndexAt(Point p)
    {
        var tracks = 0;
        for (var i = 0; i < List.Items.Count; i++)
        {
            if (List.Items[i] is not AudioTrack) continue;
            if (List.ContainerFromIndex(i) is ListViewItem row)
            {
                var top = row.TransformToVisual(List).TransformPoint(new Point(0, 0)).Y;
                if (p.Y < top + row.ActualHeight / 2) return tracks;
            }
            tracks++;
        }
        return tracks;
    }

    /// <summary>Die Zeile, in der das Lied an dieser Stelle steht.</summary>
    private int RowOfTrack(int index)
    {
        var tracks = 0;
        for (var i = 0; i < List.Items.Count; i++)
        {
            if (List.Items[i] is not AudioTrack) continue;
            if (tracks == index) return i;
            tracks++;
        }
        return -1;
    }

    private void ShowLine(int index)
    {
        var count = Tab?.Tracks.Count ?? 0;
        double y;
        if (count > 0 && List.ContainerFromIndex(RowOfTrack(Math.Min(index, count - 1))) is ListViewItem row)
        {
            var top = row.TransformToVisual(List).TransformPoint(new Point(0, 0)).Y;
            y = index >= count ? top + row.ActualHeight : top;
        }
        else
        {
            y = List.Padding.Top;
        }

        DropLine.Margin = new Thickness(14, Math.Max(0, y - 1), 14, 0);
        DropLine.Visibility = Visibility.Visible;
    }

    private void HideLine() => DropLine.Visibility = Visibility.Collapsed;

    // ══ Sortierung ═══════════════════════════════════════════════

    private void OnScopeBadgeTapped(object sender, TappedRoutedEventArgs e)
    {
        Activated?.Invoke(this, this);
        ScopeExitRequested?.Invoke(this, this);
        e.Handled = true;
    }

    private void OnHeaderTapped(object sender, TappedRoutedEventArgs e)
    {
        // Ein Zug endet mit Loslassen, und darauf kann noch ein Tippen
        // folgen. Das war dann kein Klick zum Sortieren.
        if (_columnDragged) { _columnDragged = false; e.Handled = true; return; }

        Activated?.Invoke(this, this);
        if (Enum.TryParse<TrackSort>((string)((FrameworkElement)sender).Tag, out var key))
            SortRequested?.Invoke(this, key);
        e.Handled = true;
    }

    /// <summary>Der Pfeil steht an der Spalte, nach der gerade sortiert ist.</summary>
    private void UpdateSortMarks()
    {
        var arrow = Tab?.SortDescending == true ? "\u2193" : "\u2191";

        foreach (var (mark, key) in Marks())
            mark.Text = Tab?.Sort == key ? arrow : "";
    }

    private IEnumerable<(TextBlock Mark, TrackSort Key)> Marks() =>
        _sortMarks.Select(p => (p.Value, p.Key));

    // ══ Kontextmenü ══════════════════════════════════════════════

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (TrackAt(e.OriginalSource) is not { } hit) return;

        Activated?.Invoke(this, this);

        // Rechtsklick auf etwas Unmarkiertes wählt es aus — sonst bezöge sich
        // „Löschen" auf eine Auswahl, die man gerade gar nicht meint.
        if (!List.SelectedItems.Contains(hit))
        {
            List.SelectedItems.Clear();
            List.SelectedItems.Add(hit);
        }

        var picked = Selected();
        if (picked.Count == 0) return;

        var one = picked.Count == 1;
        var menu = new MenuFlyout();

        menu.Items.Add(Item("", Strings.T("Play"),
            () => PlayRequested?.Invoke(this, picked[0]), true));

        menu.Items.Add(new MenuFlyoutSeparator());

        menu.Items.Add(Item("", Strings.T("Show in Explorer"), () => Reveal(picked[0].Path), one));
        menu.Items.Add(Item("", Strings.T("Go to folder"),
            () => NavigateRequested?.Invoke(this, System.IO.Path.GetDirectoryName(picked[0].Path)!),
            one && Tab?.Recursive == true));
        menu.Items.Add(Item("", one ? Strings.T("Copy path")
                                    : Strings.T("Copy {0} paths", picked.Count),
            () => CopyPaths(picked), true));

        menu.Items.Add(new MenuFlyoutSeparator());

        menu.Items.Add(Item("\uE8C8", Strings.T("Copy tags"),
            () => CopyTagsRequested?.Invoke(this, picked[0]), one));
        menu.Items.Add(Item("\uE77F", Strings.T("Paste tags"),
            () => PasteTagsRequested?.Invoke(this, picked), TagsCopied));

        menu.Items.Add(new MenuFlyoutSeparator());

        var del = Item("", one ? Strings.T("Delete")
                                : Strings.T("Delete {0} files", picked.Count),
                       () => DeleteRequested?.Invoke(this, picked), true);
        ToolTipService.SetToolTip(del,
            Strings.T("Backed up first, can be undone from the history"));
        menu.Items.Add(del);

        menu.ShowAt(this, e.GetPosition(this));
        e.Handled = true;

        static MenuFlyoutItem Item(string glyph, string text, Action run, bool enabled)
        {
            var item = new MenuFlyoutItem
            {
                Text = text,
                Icon = new FontIcon { Glyph = glyph },
                IsEnabled = enabled,
            };
            item.Click += (_, _) => run();
            return item;
        }
    }

    private static void Reveal(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch { }
    }

    private static void CopyPaths(IReadOnlyList<AudioTrack> tracks)
    {
        try
        {
            var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            data.SetText(string.Join(Environment.NewLine, tracks.Select(t => t.Path)));
            Clipboard.SetContent(data);
        }
        catch { }
    }

    // ══ Spaltenbreiten ═══════════════════════════════════════════

    private static TrackColumnLayout Columns =>
        (TrackColumnLayout)Application.Current.Resources["TrackColumns"];

    private int _gripIndex = -1;
    private double _gripStartX;
    private double _gripStartWidth;

    private void OnGripPressed(object sender, PointerRoutedEventArgs e)
    {
        var grip = (Border)sender;
        _gripIndex = int.Parse((string)grip.Tag);
        _gripStartX = e.GetCurrentPoint(this).Position.X;
        _gripStartWidth = Columns.WidthAt(_gripIndex);
        grip.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnGripMoved(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        if (_gripIndex < 0) return;

        var dx = e.GetCurrentPoint(this).Position.X - _gripStartX;
        Columns.Resize(_gripIndex, _gripStartWidth + dx);
        e.Handled = true;
    }

    private void OnGripReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_gripIndex < 0) return;
        _gripIndex = -1;
        ((Border)sender).ReleasePointerCapture(e.Pointer);
        ColumnsResized?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private void OnGripExited(object sender, PointerRoutedEventArgs e)
    {
        if (_gripIndex < 0) ProtectedCursor = null;
    }

    private static string FormatRate(int hz) =>
        hz % 1000 == 0 ? $"{hz / 1000} kHz" : $"{hz / 1000.0:0.0} kHz";
}
