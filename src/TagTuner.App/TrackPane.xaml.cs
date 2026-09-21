using TagTuner.Core.Audio;
using TagTuner.Core.Model;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;
using Windows.Foundation;

using TagTuner.Core.Settings;

namespace TagTuner.App;

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

    /// <summary>
    /// Der laufende Zug innerhalb der App. Das Datenpaket der Liste trägt beim
    /// Umsortieren nur listeninterne Objekte — quer über zwei Hälften ist davon
    /// nichts zu gebrauchen. Die Quelle merken ist verlässlicher als raten.
    /// </summary>
    private static (TrackPane Pane, List<AudioTrack> Tracks)? _drag;

    private bool _suppress;
    private bool _droppedHere;

    public TrackPane() => InitializeComponent();

    /// <summary>Hebt die Hälfte hervor, die gerade die Metadatenspalte speist.</summary>
    public void SetActive(bool active)
    {
        PaneBar.Opacity = active ? 1.0 : 0.65;
        FolderName.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
            active ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"];
    }

    public void Bind(FolderTab tab)
    {
        Tab = tab;
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

        // Auswahl nach einem Neueinlesen wiederherstellen — sonst verliert
        // man nach jeder Operation, woran man gerade gearbeitet hat.
        _suppress = true;
        List.SelectedItems.Clear();
        foreach (var t in Tab.Tracks.Where(t => Tab.SelectedPaths.Contains(t.Path)))
            List.SelectedItems.Add(t);
        _suppress = false;
    }

    public List<AudioTrack> Selected() => List.SelectedItems.OfType<AudioTrack>().ToList();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || Tab is null) return;
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
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not AudioTrack track) return;
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
    private int IndexAt(Point p)
    {
        var count = Tab?.Tracks.Count ?? 0;
        for (var i = 0; i < count; i++)
        {
            if (List.ContainerFromIndex(i) is not ListViewItem row) continue;
            var top = row.TransformToVisual(List).TransformPoint(new Point(0, 0)).Y;
            if (p.Y < top + row.ActualHeight / 2) return i;
        }
        return count;
    }

    private void ShowLine(int index)
    {
        double y;
        if (List.ContainerFromIndex(Math.Min(index, (Tab?.Tracks.Count ?? 1) - 1)) is ListViewItem row)
        {
            var top = row.TransformToVisual(List).TransformPoint(new Point(0, 0)).Y;
            y = index >= (Tab?.Tracks.Count ?? 0) ? top + row.ActualHeight : top;
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

    private void OnHeaderTapped(object sender, TappedRoutedEventArgs e)
    {
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
    [
        (SortTrack, TrackSort.Track),
        (SortTitle, TrackSort.Title),
        (SortArtist, TrackSort.Artist),
        (SortAlbum, TrackSort.Album),
        (SortFormat, TrackSort.Format),
        (SortSampleRate, TrackSort.SampleRate),
        (SortDuration, TrackSort.Duration),
    ];

    // ══ Kontextmenü ══════════════════════════════════════════════

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not AudioTrack hit) return;

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
