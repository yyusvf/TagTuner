using System.Numerics;
using TagTuner.Core.Folders;
using TagTuner.Core.Model;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace TagTuner.App;

/// <summary>
/// Umsortieren per Zeiger, wie auf der Website: Die gezogene Zeile hängt am
/// Zeiger, die übrigen gleiten zur Seite, beim Loslassen wird verschoben, und
/// was eine neue Nummer bekommt, leuchtet kurz auf.
///
/// Warum nicht mehr das Umsortieren der ListView: Deren Zug ist ein Zug des
/// Systems mit einem Abbild der Zeile unter dem Zeiger und einer Lücke, die
/// springt statt gleitet. Die Zeilen selbst lassen sich dabei nicht bewegen.
/// Erst wenn der Zeiger die Liste verlässt, übernimmt der Zug des Systems —
/// nur der kommt in die andere Hälfte und in den Explorer.
/// </summary>
public sealed partial class TrackPane
{
    /// <summary>Ab so vielen Pixeln ist ein Druck ein Zug und kein Klick mehr.</summary>
    private const double RowDragThreshold = 6;

    /// <summary>
    /// So weit darf der Zeiger über und unter die Liste hinaus, bevor der Zug
    /// an das System geht. Solange scrollt die Liste am Rand mit — wer nach
    /// oben zieht, schießt leicht ein Stück über die Kante.
    /// </summary>
    private const double LeaveMargin = 40;

    private const double EdgeZone = 48;
    private const double MaxScrollStep = 22;

    private static readonly TimeSpan GlideTime = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(140);

    /// <summary>Ein Druck, der vielleicht ein Zug wird.</summary>
    private sealed class Pending
    {
        public required uint PointerId;
        public required Point Start;
        public required object Item;
    }

    /// <summary>Ein laufender Zug innerhalb der Liste.</summary>
    private sealed class RowDrag
    {
        public required uint PointerId;
        public required List<object> Items;
        public required Dictionary<object, int> IndexOf;
        public required double[] Heights;
        public required double[] Tops;
        public required int[] Block;
        public required HashSet<int> InBlock;
        public required int Grabbed;

        /// <summary>Wie weit die gegriffene Zeile im zusammengeschobenen Block unten steht.</summary>
        public required double GrabOffset;

        /// <summary>Höhen der Zeilen, die stehen bleiben, für die Wahl der Lücke.</summary>
        public required double[] RestHeights;

        /// <summary>Der Zeiger beim Beginn, in Koordinaten des Listeninhalts.</summary>
        public required double StartY;

        /// <summary>Verschiebung zwischen Listeninhalt und Liste, ohne Bildlauf.</summary>
        public required double ContentOffset;

        public required List<AudioTrack> Tracks;
        public required bool Animate;
        public required DateTime Began;

        public Point Pointer;
        public double Dy;
        public int Gap = -1;
        public int[] Order = [];
        public double[] Shifts = [];
        public bool Settling;
    }

    private Pending? _pending;
    private RowDrag? _rowDrag;
    private DispatcherQueueTimer? _scrollTimer;
    private ScrollViewer? _listScroll;
    private TransitionCollection? _savedTransitions;

    /// <summary>
    /// Ob sich die Liste gerade per Hand umsortieren lässt. Nur in der
    /// Playlist-Reihenfolge eines einzelnen Ordners; sonst wäre ein Zug eine
    /// Nummernvergabe, die niemand so gemeint hat.
    /// </summary>
    private bool CanReorder => Tab is { Recursive: false, Sort: TrackSort.Natural } && !ReadOnly;

    /// <summary>Ob sich Zeilen überhaupt ziehen lassen, etwa in die andere Hälfte.</summary>
    private bool CanDragOut => Tab is { Recursive: false } && !ReadOnly;

    private void HookRowDrag()
    {
        // Die Zeilen behandeln Drücken und Loslassen selbst (Auswahl). Mit
        // handledEventsToo kommt es trotzdem hier an, und die Auswahl der
        // Liste bleibt, wie sie ist.
        List.AddHandler(PointerPressedEvent, new PointerEventHandler(OnRowPressed), true);
        List.AddHandler(PointerMovedEvent, new PointerEventHandler(OnRowMoved), true);
        List.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnRowReleased), true);
        List.PointerCaptureLost += OnRowCaptureLost;
        List.PointerCanceled += OnRowCaptureLost;

        // Wird die Liste gesperrt, etwa weil gerade Nummern geschrieben
        // werden, bekommt sie weder Loslassen noch Verlust des Zeigers
        // gemeldet. Ein Zug, der dann liefe, bliebe für immer hängen: Zeiger
        // als Doppelpfeil, Auswahl festgeklemmt, kein weiterer Zug möglich.
        IsEnabledChanged += (_, _) =>
        {
            if (!IsEnabled) { _pending = null; CancelRowDrag(animate: false); }
        };
    }

    private ScrollViewer? ListScroll => _listScroll ??= FindScroll(List);

    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            if (FindScroll(child) is { } found) return found;
        }
        return null;
    }

    private ListViewItem? ContainerAt(object? source)
    {
        for (var node = source as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ListViewItem item) return item;
            if (node is Microsoft.UI.Xaml.Controls.Primitives.ScrollBar || ReferenceEquals(node, List)) return null;
        }
        return null;
    }

    // ══ Zeiger ═══════════════════════════════════════════════════

    private void OnRowPressed(object sender, PointerRoutedEventArgs e)
    {
        _pending = null;

        // Ein Zug, der noch läuft, obwohl gerade neu gedrückt wird, hat sein
        // Loslassen verpasst. Er wird verworfen statt jeden weiteren Zug zu
        // blockieren. Nur einer, der gerade an seinen Platz gleitet, zählt
        // noch; der ist gleich von selbst fertig.
        if (_rowDrag is { Settling: false }) CancelRowDrag(animate: false);
        if (_rowDrag is not null || !CanDragOut) return;

        // Mit dem Finger scrollt die Liste; ein Zug per Hand käme ihr dabei
        // in die Quere.
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch) return;

        var point = e.GetCurrentPoint(List);
        if (!point.Properties.IsLeftButtonPressed) return;

        if (ContainerAt(e.OriginalSource) is not { } container) return;
        if (List.ItemFromContainer(container) is not AudioTrack track) return;

        _pending = new Pending { PointerId = e.Pointer.PointerId, Start = point.Position, Item = track };
    }

    private void OnRowMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_rowDrag is { } drag)
        {
            if (e.Pointer.PointerId != drag.PointerId || drag.Settling) return;

            // Taste schon los, ohne dass das Loslassen ankam: dort ablegen,
            // wo der Zeiger zuletzt war.
            var now = e.GetCurrentPoint(List);
            if (!now.Properties.IsLeftButtonPressed)
            {
                DropRow(drag);
                return;
            }
            drag.Pointer = now.Position;

            if (Left(drag.Pointer))
            {
                HandOver(e);
                return;
            }

            UpdateRowDrag();
            e.Handled = true;
            return;
        }

        if (_pending is not { } pending || e.Pointer.PointerId != pending.PointerId) return;

        var point = e.GetCurrentPoint(List);
        if (!point.Properties.IsLeftButtonPressed) { _pending = null; return; }

        var dx = point.Position.X - pending.Start.X;
        var dy = point.Position.Y - pending.Start.Y;
        if (dx * dx + dy * dy < RowDragThreshold * RowDragThreshold) return;

        _pending = null;
        BeginRowDrag(pending, e);
        e.Handled = true;
    }

    private void OnRowReleased(object sender, PointerRoutedEventArgs e)
    {
        _pending = null;
        if (_rowDrag is not { } drag || e.Pointer.PointerId != drag.PointerId || drag.Settling) return;

        drag.Pointer = e.GetCurrentPoint(List).Position;
        UpdateRowDrag();
        e.Handled = true;
        DropRow(drag);
    }

    /// <summary>
    /// Der Zeiger ging verloren, ohne Loslassen (Fenster gewechselt, anderer
    /// Zug). Dann gilt der Zug nicht: Die Zeilen gleiten zurück.
    /// </summary>
    private void OnRowCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_rowDrag is not { Settling: false } drag || e.Pointer.PointerId != drag.PointerId) return;
        CancelRowDrag(animate: true);
    }

    /// <summary>
    /// Ob der Zeiger die Liste verlassen hat: seitlich sofort, oben und unten
    /// erst ein Stück hinter dem Rand.
    /// </summary>
    private bool Left(Point p)
    {
        var x = List.TransformToVisual(WideScroll).TransformPoint(p).X;
        return x < 0 || x > WideScroll.ActualWidth
               || p.Y < -LeaveMargin || p.Y > List.ActualHeight + LeaveMargin;
    }

    // ══ Beginn ═══════════════════════════════════════════════════

    private void BeginRowDrag(Pending pending, PointerRoutedEventArgs e)
    {
        var grabbed = (AudioTrack)pending.Item;

        // Wer ein nicht markiertes Lied zieht, meint dieses eine, wie im
        // Explorer. Sonst zieht die ganze Auswahl mit.
        if (!List.SelectedItems.Contains(grabbed)) List.SelectedItem = grabbed;
        var selected = SelectedSet();

        var items = List.Items.ToList();
        var tracks = items.OfType<AudioTrack>().Where(selected.Contains).ToList();
        _drag = (this, tracks);

        if (!CanReorder)
        {
            // Nach Interpret sortiert gibt es nichts umzusortieren, in die
            // andere Hälfte darf es trotzdem.
            HandOver(e);
            return;
        }

        var (trackHeight, discHeight) = MeasureRows();
        var heights = items.Select(i => i is DiscHeader ? discHeight : trackHeight).ToArray();
        var tops = RowReorder.Tops(heights);

        var indexOf = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < items.Count; i++) indexOf[items[i]] = i;

        var block = tracks.Select(t => indexOf[t]).Order().ToArray();
        var grab = indexOf[grabbed];
        var grabOffset = block.TakeWhile(i => i != grab).Sum(i => heights[i]);
        var rest = RowReorder.Remaining(items.Count, block);

        if (List.ContainerFromIndex(grab) is not ListViewItem container) { _drag = null; return; }

        var scroll = ListScroll?.VerticalOffset ?? 0;
        var top = container.TransformToVisual(List).TransformPoint(new Point(0, 0)).Y;
        var contentOffset = top + scroll - tops[grab];

        _rowDrag = new RowDrag
        {
            PointerId = e.Pointer.PointerId,
            Items = items,
            IndexOf = indexOf,
            Heights = heights,
            Tops = tops,
            Block = block,
            InBlock = [.. block],
            Grabbed = grab,
            GrabOffset = grabOffset,
            RestHeights = [.. rest.Select(i => heights[i])],
            StartY = pending.Start.Y - contentOffset + scroll,
            ContentOffset = contentOffset,
            Tracks = tracks,
            Animate = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled,
            Began = DateTime.UtcNow,
            Pointer = e.GetCurrentPoint(List).Position,
        };

        // Kommt der Zeiger nicht zu fassen (Taste schon wieder los), gibt es
        // auch kein Loslassen mehr, das den Zug beenden würde.
        if (!List.CapturePointer(e.Pointer))
        {
            _rowDrag = null;
            _drag = null;
            return;
        }
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);

        if (ListScroll is { } sv) sv.ViewChanged += OnDragScrolled;
        _scrollTimer ??= CreateScrollTimer();
        _scrollTimer.Start();

        UpdateRowDrag();
    }

    /// <summary>
    /// Wie hoch ein Lied und eine Disc-Zeile sind, an den gezeichneten Zeilen
    /// gemessen. Die Liste zeichnet nur, was zu sehen ist; die übrigen Zeilen
    /// haben dieselbe Höhe wie ihresgleichen.
    /// </summary>
    private (double Track, double Disc) MeasureRows()
    {
        double track = 0, disc = 0;
        foreach (var container in RealizedContainers())
        {
            var item = List.ItemFromContainer(container);
            if (item is AudioTrack && track <= 0) track = container.ActualHeight;
            else if (item is DiscHeader && disc <= 0) disc = container.ActualHeight;
            if (track > 0 && disc > 0) break;
        }
        return (track > 0 ? track : 56, disc > 0 ? disc : 46);
    }

    private IEnumerable<ListViewItem> RealizedContainers() =>
        List.ItemsPanelRoot?.Children.OfType<ListViewItem>() ?? [];

    // ══ Während des Zugs ═════════════════════════════════════════

    /// <summary>
    /// Rechnet die Lücke unter dem Zeiger aus und stellt alle gezeichneten
    /// Zeilen dorthin, wo sie jetzt hingehören.
    /// </summary>
    private void UpdateRowDrag()
    {
        if (_rowDrag is not { } drag) return;

        var scroll = ListScroll?.VerticalOffset ?? 0;
        var y = drag.Pointer.Y - drag.ContentOffset + scroll;
        drag.Dy = y - drag.StartY;

        var blockTop = drag.Tops[drag.Grabbed] + drag.Dy - drag.GrabOffset;

        // Die übrigen Zeilen zählen von oben ohne den Block; die Oberkante
        // des Blocks ist dieselbe Rechnung.
        var gap = RowReorder.NearestGap(drag.RestHeights, blockTop);
        if (gap != drag.Gap)
        {
            drag.Gap = gap;
            drag.Order = RowReorder.Order(drag.Items.Count, drag.Block, gap);
            drag.Shifts = RowReorder.Shifts(drag.Heights, drag.Order);
        }

        foreach (var container in RealizedContainers()) PlaceRow(container);
    }

    /// <summary>
    /// Stellt eine Zeile an ihren Platz im laufenden Zug. Auch für Zeilen,
    /// die beim Scrollen während des Zugs neu entstehen.
    /// </summary>
    private void PlaceRow(ListViewItem container)
    {
        if (_rowDrag is not { } drag
            || List.ItemFromContainer(container) is not { } item
            || !drag.IndexOf.TryGetValue(item, out var i))
        {
            ResetRow(container);
            return;
        }

        double target;
        bool glide;

        if (drag.Settling)
        {
            // Beim Loslassen gleitet alles an den endgültigen Platz.
            target = drag.Shifts.Length > i ? drag.Shifts[i] : 0;
            glide = drag.Animate;
        }
        else if (i == drag.Grabbed)
        {
            // Die gegriffene Zeile hängt am Zeiger, ohne Verzögerung.
            target = drag.Dy;
            glide = false;
        }
        else if (drag.InBlock.Contains(i))
        {
            // Die übrigen markierten Lieder schließen zur gegriffenen auf und
            // hängen dann mit am Zeiger. Nur das Aufschließen gleitet.
            var offset = drag.Block.TakeWhile(b => b != i).Sum(b => drag.Heights[b]);
            target = drag.Tops[drag.Grabbed] + drag.Dy - drag.GrabOffset + offset - drag.Tops[i];
            glide = drag.Animate && DateTime.UtcNow - drag.Began < GlideTime;
        }
        else
        {
            target = drag.Shifts.Length > i ? drag.Shifts[i] : 0;
            glide = drag.Animate;
        }

        if (drag.InBlock.Contains(i))
        {
            Canvas.SetZIndex(container, i == drag.Grabbed ? 20 : 10);

            // Deckend, damit die Zeilen darunter nicht durchscheinen, über
            // die der Block gerade gleitet. Die Markierung liegt darüber.
            container.Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorTertiaryBrush"];
        }

        SetTranslation(container, target, glide);
    }

    private static void SetTranslation(UIElement element, double y, bool glide)
    {
        var wanted = new Vector3(0, (float)y, 0);
        if (element.Translation == wanted) return;

        if (glide)
        {
            if (element.TranslationTransition is null)
                element.TranslationTransition = new Vector3Transition { Duration = GlideTime };
        }
        else if (element.TranslationTransition is not null)
        {
            element.TranslationTransition = null;
        }
        element.Translation = wanted;
    }

    /// <summary>Nimmt einer Zeile alles, was der Zug ihr gegeben hat.</summary>
    private void ResetRow(ListViewItem container, bool glide = false)
    {
        SetTranslation(container, 0, glide);
        Canvas.SetZIndex(container, 0);
        if (List.ItemFromContainer(container) is AudioTrack track) Paint(container, track);
        else container.Background = null;
    }

    private DispatcherQueueTimer CreateScrollTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += (_, _) => AutoScroll();
        return timer;
    }

    /// <summary>
    /// Am oberen und unteren Rand scrollt die Liste von selbst, damit sich
    /// ein Lied auch an eine Stelle ziehen lässt, die gerade nicht zu sehen ist.
    /// </summary>
    private void AutoScroll()
    {
        if (_rowDrag is not { Settling: false } drag || ListScroll is not { } sv) { _scrollTimer?.Stop(); return; }

        var step = RowReorder.AutoScrollStep(drag.Pointer.Y, List.ActualHeight, EdgeZone, MaxScrollStep);
        if (step == 0) return;

        var to = Math.Clamp(sv.VerticalOffset + step, 0, sv.ScrollableHeight);
        if (Math.Abs(to - sv.VerticalOffset) < 0.5) return;
        sv.ChangeView(null, to, null, true);
    }

    private void OnDragScrolled(object? sender, ScrollViewerViewChangedEventArgs e) => UpdateRowDrag();

    // ══ Ende ═════════════════════════════════════════════════════

    private void EndRowDrag()
    {
        _scrollTimer?.Stop();
        if (ListScroll is { } sv) sv.ViewChanged -= OnDragScrolled;
        ProtectedCursor = null;
        _rowDrag = null;
    }

    private void CancelRowDrag(bool animate)
    {
        if (_rowDrag is not { } drag) return;
        var glide = animate && drag.Animate;
        EndRowDrag();
        _drag = null;
        foreach (var container in RealizedContainers()) ResetRow(container, glide);
        List.ReleasePointerCaptures();
    }

    /// <summary>
    /// Loslassen: Alles gleitet an seinen neuen Platz, dann wird die
    /// Reihenfolge wirklich umgestellt.
    /// </summary>
    private void DropRow(RowDrag drag)
    {
        var identity = drag.Order.Length == 0 || drag.Order.Select((o, i) => o == i).All(x => x);
        if (identity)
        {
            CancelRowDrag(animate: true);
            return;
        }

        drag.Settling = true;
        _scrollTimer?.Stop();
        foreach (var container in RealizedContainers()) PlaceRow(container);
        List.ReleasePointerCaptures();

        if (!drag.Animate) { Finish(); return; }

        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = SettleTime;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Finish();
        timer.Start();

        void Finish()
        {
            if (!ReferenceEquals(_rowDrag, drag)) return;
            EndRowDrag();
            _drag = null;
            ApplyOrder(drag.Items, drag.Order);
        }
    }

    /// <summary>
    /// Stellt die Reihenfolge um und meldet sie weiter, damit Nummern, Discs
    /// und Dateinamen geschrieben werden — dieselbe Folge wie früher nach dem
    /// Umsortieren der Liste.
    /// </summary>
    /// <param name="items">Die Zeilen, wie sie beim Beginn standen.</param>
    /// <param name="order">Die neue Reihenfolge als Indizes in <paramref name="items"/>.</param>
    private void ApplyOrder(IReadOnlyList<object> items, IReadOnlyList<int> order)
    {
        if (Tab is null) return;

        // Die Liste hätte sonst ihr eigenes Gleiten für verschobene Zeilen:
        // von der alten Stelle aus, an der die Zeile gar nicht mehr zu sehen
        // war. Einen Augenblick ohne, bis die Liste neu gelegt ist.
        _savedTransitions ??= List.ItemContainerTransitions;
        List.ItemContainerTransitions = new TransitionCollection();

        foreach (var container in RealizedContainers()) ResetRow(container);

        var before = items.OfType<AudioTrack>().ToList();
        var numbersBefore = SlotNumbers(items);

        var arranged = order.Select(i => items[i]).ToList();
        var after = arranged.OfType<AudioTrack>().ToList();
        var numbersAfter = SlotNumbers(arranged);

        // Verschieben heißt für die Liste Entfernen und Einfügen, und wer
        // entfernt wird, fällt aus der Auswahl. Darum festhalten und danach
        // wieder setzen, ohne dass es als neue Auswahl weitergemeldet wird.
        var selected = Selected();
        _suppress = true;

        ReorderedSections = null;
        if (_sectioned)
        {
            Sync(_view, arranged);
            var discs = RowReorder.Sections(arranged.Select(i => i is DiscHeader h ? h.Disc : (uint?)null).ToList());
            ReorderedSections = after.Select((t, i) => (t, discs[i])).ToList();
        }

        _syncQueued = true;   // kein Abgleich mitten im Umstellen
        for (var i = 0; i < after.Count; i++)
        {
            var at = Tab.Tracks.IndexOf(after[i]);
            if (at >= 0 && at != i) Tab.Tracks.Move(at, i);
        }
        _syncQueued = false;

        List.SelectedItems.Clear();
        foreach (var track in selected) List.SelectedItems.Add(track);
        _suppress = false;
        PaintHeaders();

        // Aufleuchten heißt „diese Nummer wurde neu geschrieben". Schreibt der
        // Ordner keine Nummern, ändert sich nur die Anzeige; dann bleibt es
        // dunkel, sonst sähe es nach einer Änderung aus, die es nicht gibt.
        if (NumbersFollowOrder?.Invoke(Tab) == true)
        {
            var renumbered = RowReorder.Renumbered(before, numbersBefore, after, numbersAfter);
            Flash(renumbered.Select(t => Tab.Tracks.IndexOf(t)).Where(i => i >= 0));
        }

        var restore = DispatcherQueue.CreateTimer();
        restore.Interval = TimeSpan.FromMilliseconds(300);
        restore.IsRepeating = false;
        restore.Tick += (_, _) =>
        {
            if (_savedTransitions is { } saved && _rowDrag is null)
            {
                List.ItemContainerTransitions = saved;
                _savedTransitions = null;
            }
        };
        restore.Start();

        ReorderCompleted?.Invoke(this, this);
    }

    /// <summary>
    /// Disc und Nummer jeder Liedzeile, wie das Hauptfenster sie nach dem
    /// Umsortieren vergibt: mit Disc-Zeilen nach der Disc darüber, sonst nach
    /// der Disc im Tag, und mehrere Discs zählen jede für sich.
    /// </summary>
    private List<(uint Disc, uint Number)> SlotNumbers(IReadOnlyList<object> rows)
    {
        var tracks = rows.OfType<AudioTrack>().ToList();
        if (_sectioned)
        {
            var discs = RowReorder.Sections(rows.Select(i => i is DiscHeader h ? h.Disc : (uint?)null).ToList());
            var numbers = RowReorder.Numbers(discs);
            return [.. discs.Select((d, i) => (d, numbers[i]))];
        }

        var plain = AlbumPlanner.Numbers(tracks);
        return [.. tracks.Select((t, i) => (t.Disc, plain[i]))];
    }

    // ══ An das System übergeben ══════════════════════════════════

    /// <summary>
    /// Der Zeiger hat die Liste verlassen: Die Zeilen gleiten zurück, und der
    /// Zug geht als Zug des Systems weiter — in die andere Hälfte, zu den
    /// Unterordnern oder in den Explorer.
    /// </summary>
    private async void HandOver(PointerRoutedEventArgs e)
    {
        var tracks = _rowDrag?.Tracks ?? _drag?.Tracks ?? Selected();
        CancelRowDrag(animate: true);
        List.ReleasePointerCaptures();
        if (tracks.Count == 0) return;

        _drag = (this, tracks);

        // Von der Zeile aus, damit das Bild unter dem Zeiger sie zeigt. Ist
        // sie beim Scrollen weggefallen, tut es die Liste.
        UIElement source = List.ContainerFromItem(tracks[0]) as ListViewItem ?? (UIElement)List;
        TypedEventHandler<UIElement, DragStartingEventArgs> starting = (_, args) => OnSystemDragStarting(args, tracks);
        source.DragStarting += starting;

        try
        {
            await source.StartDragAsync(e.GetCurrentPoint(source));
        }
        catch
        {
            // Ohne gedrückte Taste lässt Windows keinen Zug beginnen; dann
            // ist er eben vorbei.
        }
        finally
        {
            source.DragStarting -= starting;

            // Umsortiert wird hier nichts mehr; wer ablegt, hat sich den Zug
            // schon genommen.
            if (_drag is { } d && d.Pane == this) _drag = null;
        }
    }

    /// <summary>
    /// Was der Zug mitnimmt: die Dateien, damit der Explorer sie annimmt.
    ///
    /// Nur zum Kopieren. Der Explorer verschöbe auf demselben Laufwerk
    /// sonst, und die Dateien verschwänden ohne Sicherung und Verlauf aus
    /// dem Ordner. Die Hälften der App wissen über <see cref="_drag"/>
    /// selbst, was gemeint ist, und verschieben mit ihrer eigenen Sicherung.
    /// </summary>
    private static async void OnSystemDragStarting(DragStartingEventArgs args, IReadOnlyList<AudioTrack> tracks)
    {
        args.AllowedOperations = DataPackageOperation.Copy;
        args.Data.RequestedOperation = DataPackageOperation.Copy;

        var deferral = args.GetDeferral();
        try
        {
            var files = new List<Windows.Storage.IStorageItem>();
            foreach (var track in tracks)
            {
                try { files.Add(await Windows.Storage.StorageFile.GetFileFromPathAsync(track.Path)); }
                catch { }
            }
            if (files.Count > 0) args.Data.SetStorageItems(files, readOnly: true);
        }
        finally { deferral.Complete(); }
    }

    /// <summary>
    /// Ein Zug des Systems, der in derselben Hälfte endet: Er kam hinaus und
    /// wieder herein. Dann gilt, was die Einfügemarke zeigt.
    /// </summary>
    private void DropOwnTracks(IReadOnlyList<AudioTrack> tracks, int trackIndex)
    {
        if (!CanReorder) return;

        var items = List.Items.ToList();
        var indexOf = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < items.Count; i++) indexOf[items[i]] = i;

        var block = tracks.Where(indexOf.ContainsKey).Select(t => indexOf[t]).ToArray();
        if (block.Length == 0) return;

        var row = trackIndex < (Tab?.Tracks.Count ?? 0) ? RowOfTrack(trackIndex) : items.Count;
        if (row < 0) row = items.Count;

        var inBlock = new HashSet<int>(block);
        var gap = Enumerable.Range(0, row).Count(i => !inBlock.Contains(i));
        var order = RowReorder.Order(items.Count, block, gap);
        if (order.Select((o, i) => o == i).All(x => x)) return;

        ApplyOrder(items, order);
    }

    // ══ Aufleuchten ══════════════════════════════════════════════

    private const double FlashPeak = 0.32;
    private static readonly TimeSpan FlashHold = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan FlashTotal = TimeSpan.FromMilliseconds(1400);
    private const string FlashTag = "flash";

    /// <summary>
    /// Nach Stelle im Ordner, nicht nach Lied: Nach dem Schreiben liest das
    /// Hauptfenster die Dateien neu ein, und mit dem Album-Modus heißen sie
    /// danach auch anders. Die Stelle bleibt dieselbe.
    /// </summary>
    private HashSet<int> _flashAt = [];
    private DateTime _flashStart;
    private bool _flashAnimated;
    private DispatcherQueueTimer? _flashTimer;

    private void Flash(IEnumerable<int> positions)
    {
        _flashAt = [.. positions];
        if (_flashAt.Count == 0) return;

        _flashStart = DateTime.UtcNow;
        _flashAnimated = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;

        if (_flashTimer is null)
        {
            _flashTimer = DispatcherQueue.CreateTimer();
            _flashTimer.Interval = TimeSpan.FromMilliseconds(33);
            _flashTimer.Tick += (_, _) => PaintFlash();
        }
        _flashTimer.Start();
        PaintFlash();
    }

    /// <summary>
    /// Wie stark eine Zeile gerade leuchtet: erst voll, dann ausblendend.
    /// Ohne Animationen von Windows steht es kurz und ist dann weg.
    /// </summary>
    private double FlashLevel()
    {
        var t = DateTime.UtcNow - _flashStart;
        if (t >= FlashTotal) return 0;
        if (!_flashAnimated || t <= FlashHold) return FlashPeak;
        return FlashPeak * (1 - (t - FlashHold) / (FlashTotal - FlashHold));
    }

    private void PaintFlash()
    {
        var level = FlashLevel();
        if (level <= 0)
        {
            _flashTimer?.Stop();
            _flashAt = [];
        }

        foreach (var container in RealizedContainers()) PaintFlash(container, level);
    }

    private void PaintFlash(ListViewItem container) => PaintFlash(container, _flashAt.Count > 0 ? FlashLevel() : 0);

    private void PaintFlash(ListViewItem container, double level)
    {
        if (container.ContentTemplateRoot is not Grid row) return;

        var lit = level > 0 && Tab is not null
                  && List.ItemFromContainer(container) is AudioTrack track
                  && _flashAt.Contains(Tab.Tracks.IndexOf(track));

        var glow = row.Children.OfType<Border>().FirstOrDefault(b => b.Tag as string == FlashTag);
        if (!lit)
        {
            if (glow is not null) glow.Opacity = 0;
            return;
        }

        if (glow is null)
        {
            // Unter den Zellen, über der Markierung der Liste; fast bis an
            // den Rand des Eintrags. Ein Pixel Luft oben und unten, sonst
            // stoßen die runden Ecken leuchtender Nachbarn aneinander und
            // lassen am Rand kleine Kerben stehen.
            glow = new Border
            {
                Tag = FlashTag,
                Margin = new Thickness(-3, 1, -11, 1),
                CornerRadius = new CornerRadius(4),
                IsHitTestVisible = false,
                Background = (Brush)Application.Current.Resources["AccentBrush"],
            };
            Grid.SetColumnSpan(glow, 99);
            row.Children.Insert(0, glow);
        }
        glow.Opacity = level;
    }
}
