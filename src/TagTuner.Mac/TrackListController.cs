using ObjCRuntime;
using TagTuner.Core.Audio;
using TagTuner.Core.Folders;
using TagTuner.Core.Model;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>Eine Disc-Zeile zwischen den Liedern eines Albums mit mehreren Discs.</summary>
internal sealed record DiscRow(uint Disc);

/// <summary>Die Leiste „UNTERORDNER n" über den Abschnitten.</summary>
internal sealed record SubfoldersTitle(int Count);

/// <summary>Der Kopf eines Unterordner-Abschnitts, aufklappbar.</summary>
internal sealed record SubfolderRow(string Path, string Name, int Count);

/// <summary>
/// Die Trackliste, aufgebaut wie unter Windows: oben der Ordnername mit der
/// Zahl der Tracks, darunter die Spalten aus <see cref="TrackColumn.All"/>.
/// Titel und Interpret stehen auf Wunsch zusammen in einer Spalte mit dem
/// Cover davor, und ein Album mit mehreren Discs bekommt Disc-Zeilen.
/// </summary>
internal sealed partial class TrackListController : NSViewController
{
    public event Action? SelectionChanged;

    /// <summary>Der Ordner ist (neu) eingelesen.</summary>
    public event Action? Loaded;

    /// <summary>In die Liste geklickt: Sie wird die aktive Hälfte.</summary>
    public event Action? Clicked;

    /// <summary>
    /// In der geteilten Ansicht die Hälfte, auf die sich alles bezieht. Sie
    /// trägt ihren Titel in Grün, wie unter Windows die Markierung am Rand.
    /// </summary>
    public bool Active
    {
        set { if (_title is not null) _title.TextColor = value ? Theme.Accent : NSColor.Label; }
    }
    public event Action<AudioTrack>? PlayRequested;

    /// <summary>Audiodateien von außen abgelegt: Pfade, Stelle in der Playlist, verschieben?</summary>
    public event Action<List<string>, int, bool>? FilesDropped;

    /// <summary>
    /// Zeilen wurden gezogen: die Lieder in der neuen Reihenfolge und, mit
    /// Disc-Zeilen, die Disc, unter der jedes jetzt steht.
    /// </summary>
    public event Action<List<AudioTrack>, uint[]?>? Reordered;

    private const string RowType = "app.tagtuner.track";
    private int[]? _dragRows;

    /// <summary>
    /// Umsortieren ergibt nur in der Playlist-Reihenfolge Sinn: nach Titel
    /// sortiert oder gefiltert gibt es keine Stelle, an die man etwas zieht.
    /// </summary>
    public bool CanReorder => _sort == TrackSort.Natural && _filter.Length == 0 && Folder is not null;

    private readonly TrackTable _table = new();
    private readonly NSTextField _empty = NSTextField.CreateLabel("");
    private readonly NSTextField _title = NSTextField.CreateLabel("");
    private readonly NSTextField _count = NSTextField.CreateLabel("");
    private List<AudioTrack> _all = [];
    private List<AudioTrack> _shown = [];

    /// <summary>Was die Tabelle zeigt: Lieder und, im Album mit mehreren Discs, Disc-Zeilen.</summary>
    private List<object> _rows = [];
    private bool _sectioned;

    // ── Unterordner, wie unter Windows unter der Liste ───────────
    private List<(string Path, string Name, int Count)> _subfolders = [];
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<AudioTrack>> _subTracks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Zeilen bis hierher gehören zum Ordner selbst, danach kommen die Unterordner.</summary>
    private int _ownEnd;
    private string _filter = "";
    private TrackSort _sort = TrackSort.Natural;
    private bool _descending;
    private CancellationTokenSource? _loading;

    public string? Folder { get; private set; }
    public IReadOnlyList<AudioTrack> Tracks => _all;
    public IReadOnlyList<AudioTrack> Shown => _shown;

    /// <summary>Das Lied, das gerade läuft, bekommt ein Zeichen in der Nummernspalte.</summary>
    public string? PlayingPath { get; set; }

    private static AppSettings Settings => AppDelegate.Settings;

    /// <summary>Titel und Interpret in einer Spalte, der Interpret klein darunter.</summary>
    private static bool Combined => Settings.CombineTitleAndArtist;

    public override void LoadView()
    {
        _table.Owner = this;
        _table.Style = NSTableViewStyle.FullWidth;
        _table.UsesAlternatingRowBackgroundColors = false;
        _table.GridStyleMask = NSTableViewGridStyle.None;
        _table.AllowsMultipleSelection = true;
        _table.AllowsColumnReordering = true;
        _table.AllowsColumnResizing = true;
        _table.ColumnAutoresizingStyle = NSTableViewColumnAutoresizingStyle.Uniform;
        _table.IntercellSpacing = new CGSize(12, 0);
        _table.DoubleAction = new Selector("rowDoubleClicked:");
        _table.Action = new Selector("rowClicked:");
        _table.Target = this;
        _table.FloatsGroupRows = false;
        // Reihenfolge und Breiten stehen wie unter Windows in den Einstellungen
        // (TrackColumns), nicht in den macOS-Voreinstellungen: Sonst stritten
        // sich zwei Stellen darum, und die Spaltenliste der Einstellungen
        // hätte nicht das letzte Wort.

        foreach (var col in VisibleColumns()) _table.AddColumn(NewColumn(col));

        // Rechtsklick auf die Kopfzeile: Spalten ein- und ausblenden, wie im Finder.
        _table.HeaderView!.Menu = new NSMenu { Delegate = new ColumnMenu(this) };

        _table.Delegate = new Delegate(this);
        _table.DataSource = new Source(this);
        _table.Menu = new NSMenu { Delegate = new MenuDelegate(this) };
        _table.RegisterForDraggedTypes([NSPasteboard.NSPasteboardTypeFileUrl, RowType]);
        _table.SetDraggingSourceOperationMask(NSDragOperation.Move, true);
        _table.SetDraggingSourceOperationMask(NSDragOperation.Copy, false);

        var scroll = new NSScrollView
        {
            DocumentView = _table,
            HasVerticalScroller = true,
            HasHorizontalScroller = true,
            AutohidesScrollers = true,
            DrawsBackground = false,
        };

        // ── Kopf: Ordnername, Zahl der Tracks, „Alle wählen" ─────
        _title.Font = NSFont.SystemFontOfSize(20, NSFontWeight.Semibold);
        _title.LineBreakMode = NSLineBreakMode.TruncatingTail;
        _title.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        _count.Font = NSFont.MonospacedSystemFont(11, NSFontWeight.Regular);
        _count.TextColor = NSColor.TertiaryLabel;
        var selectAll = NSButton.CreateButton(Strings.T("Select all"), () => SelectAllTracks());
        selectAll.Bordered = false;
        selectAll.AttributedTitle = new NSAttributedString(Strings.T("Select all"), new NSStringAttributes
        {
            ForegroundColor = Theme.Accent,
            Font = NSFont.SystemFontOfSize(11.5f),
        });

        _empty.TextColor = NSColor.SecondaryLabel;
        _empty.Font = NSFont.SystemFontOfSize(15);
        _empty.Alignment = NSTextAlignment.Center;

        var root = new NSView();
        foreach (var v in new NSView[] { _title, _count, selectAll, scroll, _empty })
        {
            v.TranslatesAutoresizingMaskIntoConstraints = false;
            root.AddSubview(v);
        }
        NSLayoutConstraint.ActivateConstraints([
            _title.TopAnchor.ConstraintEqualTo(root.SafeAreaLayoutGuide.TopAnchor, 10),
            _title.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor, 16),
            _count.LastBaselineAnchor.ConstraintEqualTo(_title.LastBaselineAnchor),
            _count.LeadingAnchor.ConstraintEqualTo(_title.TrailingAnchor, 10),
            selectAll.CenterYAnchor.ConstraintEqualTo(_title.CenterYAnchor),
            selectAll.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor, -14),
            _count.TrailingAnchor.ConstraintLessThanOrEqualTo(selectAll.LeadingAnchor, -10),
            scroll.TopAnchor.ConstraintEqualTo(_title.BottomAnchor, 8),
            scroll.BottomAnchor.ConstraintEqualTo(root.BottomAnchor),
            scroll.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor),
            scroll.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor),
            _empty.CenterXAnchor.ConstraintEqualTo(scroll.CenterXAnchor),
            _empty.CenterYAnchor.ConstraintEqualTo(scroll.CenterYAnchor),
            _empty.WidthAnchor.ConstraintLessThanOrEqualTo(root.WidthAnchor, 0.8f),
        ]);
        View = root;
        ShowEmpty();
    }

    // ── Spalten ──────────────────────────────────────────────────

    private static IEnumerable<TrackColumn> VisibleColumns()
    {
        Settings.EnsureTrackColumns();
        var cols = Settings.TrackColumns.Where(s => s.Visible).Select(s => TrackColumn.ById(s.Id)).OfType<TrackColumn>();
        // Vereint steckt das Cover in der Titelspalte, und der Interpret darunter.
        return Combined ? cols.Where(c => !c.IsCover && c.Id != "artist") : cols;
    }

    private bool _rebuilding;

    /// <summary>Die Spalten neu aufbauen, nach einer Änderung in den Einstellungen.</summary>
    public void RebuildColumns()
    {
        _rebuilding = true;
        foreach (var c in _table.TableColumns()) _table.RemoveColumn(c);
        foreach (var col in VisibleColumns()) _table.AddColumn(NewColumn(col));
        _rebuilding = false;
        _table.ReloadData();
    }

    /// <summary>
    /// Reihenfolge und Breiten aus der Tabelle in die Einstellungen. Was die
    /// Tabelle nicht zeigt (ausgeblendet oder vereint), behält seinen Platz
    /// relativ zu den anderen.
    /// </summary>
    private void RememberColumns()
    {
        if (_rebuilding) return;
        Settings.EnsureTrackColumns();
        var shown = _table.TableColumns().Select(c => c.Identifier).ToList();
        var states = Settings.TrackColumns;
        foreach (var c in _table.TableColumns())
            if (states.FirstOrDefault(s => s.Id == c.Identifier) is { } st && !(c.Identifier == "title" && Combined))
                st.Width = c.Width;

        // Die gezeigten Spalten in ihrer neuen Reihenfolge auf die Plätze, die
        // gezeigte Spalten vorher hatten.
        var slots = states.Select((st, i) => (st, i)).Where(x => shown.Contains(x.st.Id)).Select(x => x.i).ToList();
        var ordered = shown.Select(id => states.First(st => st.Id == id)).ToList();
        for (var k = 0; k < slots.Count && k < ordered.Count; k++) states[slots[k]] = ordered[k];
        Settings.Save();
    }

    private static NSTableColumn NewColumn(TrackColumn col)
    {
        var saved = Settings.TrackColumns.FirstOrDefault(s => s.Id == col.Id)?.Width ?? 0;
        var width = col.IsCover ? 36 : col.Id == "title" && Combined ? 280 : saved > 20 ? saved : col.Width;
        var c = new NSTableColumn(col.Id)
        {
            Title = col.Id == "title" && Combined
                ? $"{Strings.T("TITLE")} · {Strings.T("ARTIST")}"
                : Strings.T(col.Header),
            Width = (nfloat)width,
            MinWidth = col.IsCover ? 36 : 30,
            Editable = false,
        };
        if (col.IsCover) c.MaxWidth = 36;
        // Nur die Textspalten wachsen mit dem Fenster, Nummern und Kürzel bleiben schmal.
        c.ResizingMask = col.Look == ColumnLook.Text && !col.IsCover
            ? NSTableColumnResizing.Autoresizing | NSTableColumnResizing.UserResizingMask
            : NSTableColumnResizing.UserResizingMask;
        if (col.Look == ColumnLook.MonoRight) c.HeaderCell.Alignment = NSTextAlignment.Right;
        // Die Dauer trägt eine Uhr statt eines Wortes, wie unter Windows.
        if (col.Id == "duration")
        {
            c.Title = "";
            c.HeaderCell.Image = NSImage.GetSystemSymbol("clock", null);
            c.HeaderToolTip = Strings.T(col.Header);
        }
        if (col.Sort is not null) c.SortDescriptorPrototype = new NSSortDescriptor(col.Id, true);
        return c;
    }

    private sealed class ColumnMenu(TrackListController owner) : NSMenuDelegate
    {
        public override void MenuWillHighlightItem(NSMenu menu, NSMenuItem item) { }

        public override void NeedsUpdate(NSMenu menu)
        {
            menu.RemoveAllItems();
            foreach (var col in TrackColumn.All.Where(c => !Combined || (!c.IsCover && c.Id != "artist")))
            {
                var shown = owner._table.FindColumn(new NSString(col.Id)) >= 0;
                var title = col.IsCover ? Strings.T("Cover") : Strings.T(col.Header);
                menu.AddItem(new NSMenuItem(title, (_, _) => owner.ToggleColumn(col))
                {
                    State = shown ? NSCellStateValue.On : NSCellStateValue.Off,
                });
            }
        }
    }

    private void ToggleColumn(TrackColumn col)
    {
        Settings.EnsureTrackColumns();
        var state = Settings.TrackColumns.First(s => s.Id == col.Id);
        if (state.Visible && _table.ColumnCount <= 2) return;   // eine Textspalte bleibt immer
        state.Visible = !state.Visible;
        Settings.Save();
        RebuildColumns();
    }

    // ── Laden ────────────────────────────────────────────────────

    public async Task LoadAsync(string folder, IReadOnlyCollection<string>? keepSelection = null)
    {
        _loading?.Cancel();
        var cts = _loading = new CancellationTokenSource();
        var changed = Folder != folder;
        Folder = folder;

        if (changed)
        {
            _all = [];
            _shown = [];
            _rows = [];
            _subfolders = [];
            _expanded.Clear();
            _subTracks.Clear();
            _table.ReloadData();
            _title.StringValue = Path.GetFileName(folder.TrimEnd('/'));
            _count.StringValue = "";
            _empty.StringValue = Strings.T("Reading…");
            _empty.Hidden = false;
        }

        List<AudioTrack> tracks;
        List<(string Path, string Name, int Count)> subs;
        try
        {
            tracks = await Task.Run(() => FolderScanner.Tracks(folder, cts.Token).ToList(), cts.Token);
            subs = await Task.Run(() => FolderScanner.AudioSubfolders(folder), cts.Token);
        }
        catch (OperationCanceledException) { return; }
        if (cts.IsCancellationRequested) return;

        _all = tracks;
        _subfolders = subs;
        // Ein Ordner ohne eigene Lieder, etwa der eines Interpreten: Dann
        // gehört der Platz den Unterordnern, und sie stehen offen.
        if (changed && tracks.Count == 0)
            foreach (var sub in subs.Take(12)) _expanded.Add(sub.Path);
        _subTracks.Clear();
        foreach (var path in _expanded.ToList())
        {
            try { _subTracks[path] = await Task.Run(() => FolderScanner.Tracks(path, cts.Token).ToList(), cts.Token); }
            catch (OperationCanceledException) { return; }
        }
        Refresh();
        ShowEmpty();
        Loaded?.Invoke();

        if (keepSelection is { Count: > 0 })
            Select(keepSelection);
        else
            SelectionChanged?.Invoke();
    }

    private void ShowEmpty()
    {
        _count.StringValue = Folder is null ? "" : $"{_all.Count} Track{(_all.Count == 1 ? "" : "s")}";
        _empty.StringValue = Folder is null
            ? Strings.T("Choose a folder on the left.")
            : _all.Count == 0 && _subfolders.Count == 0 ? Strings.T("No audio files in this folder.")
            : _all.Count == 0 ? ""
            : _shown.Count == 0 ? Strings.T("Nothing found for \"{0}\".", _filter)
            : "";
        _empty.Hidden = _empty.StringValue.Length == 0;
    }

    public void Filter(string text)
    {
        _filter = text.Trim();
        var keep = SelectedTracks.Select(t => t.Path).ToList();
        Refresh();
        Select(keep);
        ShowEmpty();
    }

    public void PlaylistOrder()
    {
        _sort = TrackSort.Natural;
        _descending = false;
        _table.SortDescriptors = [];
        Refresh();
    }

    /// <summary>Filtert, sortiert und setzt die Disc-Zeilen, ohne die Platte anzufassen.</summary>
    public void Refresh()
    {
        IEnumerable<AudioTrack> list = _all;
        if (_filter.Length > 0)
            list = list.Where(t => Matches(t, _filter));
        _shown = TrackSorting.Apply(list, _sort, _descending, Settings.SortByDiscThenTrack);

        // Disc-Zeilen nur im Album mit mehreren Discs und nur, solange die
        // Discs beisammen stehen: nach Titel sortiert gehören sie nirgendwohin.
        _sectioned = Settings.CombineDiscAndTrack
                     && _filter.Length == 0
                     && _sort is TrackSort.Natural or TrackSort.Disc
                     && _all.Select(t => t.Disc).Where(d => d > 0).Distinct().Count() > 1
                     && Folder is not null && Settings.RuleFor(Folder).AlbumMode;

        _rows = [];
        uint? current = null;
        foreach (var t in _shown)
        {
            if (_sectioned && t.Disc != current)
            {
                current = t.Disc;
                _rows.Add(new DiscRow(t.Disc));
            }
            _rows.Add(t);
        }

        _ownEnd = _rows.Count;
        if (_subfolders.Count > 0)
        {
            _rows.Add(new SubfoldersTitle(_subfolders.Count));
            foreach (var (path, name, count) in _subfolders)
            {
                _rows.Add(new SubfolderRow(path, name, count));
                if (!_expanded.Contains(path) || !_subTracks.TryGetValue(path, out var subTracks)) continue;
                IEnumerable<AudioTrack> subList = subTracks;
                if (_filter.Length > 0) subList = subList.Where(t => Matches(t, _filter));
                _rows.AddRange(TrackSorting.Apply(subList, _sort, _descending, Settings.SortByDiscThenTrack));
            }
        }
        _table.ReloadData();
    }

    /// <summary>Einen Unterordner auf- oder zuklappen; seine Lieder werden beim ersten Mal gelesen.</summary>
    private async void Toggle(SubfolderRow sub)
    {
        if (!_expanded.Remove(sub.Path))
        {
            _expanded.Add(sub.Path);
            if (!_subTracks.ContainsKey(sub.Path))
                _subTracks[sub.Path] = await Task.Run(() => FolderScanner.Tracks(sub.Path).ToList());
        }
        var keep = SelectedTracks.Select(t => t.Path).ToList();
        Refresh();
        Select(keep);
    }

    /// <summary>Alle Lieder, die gerade zu sehen sind, in Reihenfolge, Unterordner eingeschlossen.</summary>
    private List<AudioTrack> Visible => [.. _rows.OfType<AudioTrack>()];

    [Export("rowClicked:")]
    public void RowClicked(NSObject sender)
    {
        Clicked?.Invoke();
        var row = _table.ClickedRow;
        if (row >= 0 && row < _rows.Count && _rows[(int)row] is SubfolderRow sub) Toggle(sub);
    }

    private static bool Matches(AudioTrack t, string q) =>
        new[] { t.Title, t.Artist, t.Album, t.AlbumArtist, t.Genre, t.FileName }
            .Any(s => s.Contains(q, StringComparison.CurrentCultureIgnoreCase));

    // ── Auswahl ──────────────────────────────────────────────────

    public List<AudioTrack> SelectedTracks =>
        [.. _table.SelectedRows.Select(i => (int)i).Where(i => i < _rows.Count)
                               .Select(i => _rows[i]).OfType<AudioTrack>()];

    public void Select(IEnumerable<string> paths)
    {
        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        var rows = new NSMutableIndexSet();
        for (var i = 0; i < _rows.Count; i++)
            if (_rows[i] is AudioTrack t && set.Contains(t.Path)) rows.Add((nuint)i);
        _table.SelectRows(rows, false);
        if (rows.Count > 0) _table.ScrollRowToVisible((nint)rows.FirstIndex);
        SelectionChanged?.Invoke();
    }

    public void SelectAllTracks()
    {
        var rows = new NSMutableIndexSet();
        for (var i = 0; i < _rows.Count; i++)
            if (_rows[i] is AudioTrack) rows.Add((nuint)i);
        _table.SelectRows(rows, false);
        View.Window?.MakeFirstResponder(_table);
    }

    /// <summary>
    /// Verschiebt die Zeilen <paramref name="block"/> vor die Zeile
    /// <paramref name="row"/> (beides in Tabellenzeilen gezählt, Disc-Zeilen
    /// eingeschlossen) und meldet die neue Reihenfolge. Geschrieben wird woanders.
    /// </summary>
    public bool MoveRows(int[] block, int row)
    {
        if (!CanReorder) return false;
        // Umsortiert wird nur im Ordner selbst; die Unterordner sind eigene Playlists.
        if (block.Any(i => i >= _ownEnd) || row > _ownEnd) return false;
        var moving = block.Where(i => i >= 0 && i < _ownEnd && _rows[i] is AudioTrack).Order().ToList();
        if (moving.Count == 0) return false;

        var own = _rows.Take(_ownEnd).ToList();
        var tail = _rows.Skip(_ownEnd).ToList();
        var items = moving.Select(i => own[i]).ToList();
        var rest = own.Where((_, i) => !moving.Contains(i)).ToList();
        var at = Math.Clamp(row - moving.Count(i => i < row), 0, rest.Count);
        var rows = rest.Take(at).Concat(items).Concat(rest.Skip(at)).ToList();
        if (rows.SequenceEqual(own)) return false;

        var tracks = rows.OfType<AudioTrack>().ToList();
        uint[]? discs = _sectioned
            ? RowReorder.Sections([.. rows.Select(r => r is DiscRow d ? d.Disc : (uint?)null)])
            : null;

        _rows = [.. rows, .. tail];
        _shown = tracks;
        _table.ReloadData();
        Select(items.OfType<AudioTrack>().Select(t => t.Path));
        Reordered?.Invoke(tracks, discs);
        return true;
    }

    /// <summary>
    /// Lässt Zeilen kurz grün aufleuchten, wie unter Windows nach dem
    /// Umsortieren: nur die Lieder, deren Nummer oder Disc sich geändert hat.
    /// </summary>
    public void Glow(IEnumerable<string> paths)
    {
        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _rows.Count; i++)
        {
            if (_rows[i] is not AudioTrack t || !set.Contains(t.Path)) continue;
            if (_table.GetRowView(i, false) is not { } rowView) continue;
            var glow = new NSView(rowView.Bounds) { WantsLayer = true, AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable };
            glow.Layer!.BackgroundColor = Theme.Accent.ColorWithAlphaComponent(0.28f).CGColor;
            glow.Layer.CornerRadius = 6;
            rowView.AddSubview(glow, NSWindowOrderingMode.Below, null);
            NSAnimationContext.RunAnimation(ctx =>
            {
                ctx.Duration = 1.6;
                ((NSView)glow.Animator).AlphaValue = 0;
            }, glow.RemoveFromSuperview);
        }
    }

    public void RedrawRows() =>
        _table.ReloadData(NSIndexSet.FromNSRange(new NSRange(0, _rows.Count)),
                          NSIndexSet.FromNSRange(new NSRange(0, _table.ColumnCount)));

    /// <summary>Das Lied nach oder vor dem laufenden, in der angezeigten Reihenfolge.</summary>
    public AudioTrack? Neighbour(string? path, int step)
    {
        var list = Visible;
        var i = list.FindIndex(t => t.Path == path);
        var j = i + step;
        return i >= 0 && j >= 0 && j < list.Count ? list[j] : null;
    }

    [Export("rowDoubleClicked:")]
    public void RowDoubleClicked(NSObject sender)
    {
        var row = _table.ClickedRow;
        if (row < 0 || row >= _rows.Count) return;
        if (_rows[(int)row] is AudioTrack t) PlayRequested?.Invoke(t);
        else if (_rows[(int)row] is SubfolderRow sub)
            (View.Window?.WindowController as MainWindowController)?.OpenFolder(sub.Path);
    }

    public void FocusTable() => View.Window?.MakeFirstResponder(_table);

    // ── Cover-Vorschau ───────────────────────────────────────────

    private readonly Dictionary<string, NSImage?> _art = [];
    private readonly HashSet<string> _artLoading = [];

    /// <summary>Nach dem Schreiben kann jedes Cover ein anderes sein.</summary>
    public void ForgetArt(IEnumerable<string>? paths = null)
    {
        if (paths is null) _art.Clear();
        else foreach (var p in paths) _art.Remove(p);
    }

    private NSImage? Art(AudioTrack t)
    {
        if (!t.HasCover) return null;
        if (_art.TryGetValue(t.Path, out var img)) return img;
        if (_artLoading.Add(t.Path))
        {
            var path = t.Path;
            Task.Run(() =>
            {
                var small = Covers.Thumbnail(AudioProbe.ReadCover(path)?.Data, 96);
                InvokeOnMainThread(() =>
                {
                    _artLoading.Remove(path);
                    _art[path] = small;
                    var row = _rows.FindIndex(x => x is AudioTrack a && a.Path == path);
                    if (row >= 0)
                        _table.ReloadData(NSIndexSet.FromIndex(row),
                            NSIndexSet.FromNSRange(new NSRange(0, _table.ColumnCount)));
                });
            });
        }
        return null;
    }

    // ── Tabelle ──────────────────────────────────────────────────

    private sealed class Source(TrackListController owner) : NSTableViewDataSource
    {
        public override nint GetRowCount(NSTableView tableView) => owner._rows.Count;

        public override void SortDescriptorsChanged(NSTableView tableView, NSSortDescriptor[] oldDescriptors)
        {
            var first = tableView.SortDescriptors.FirstOrDefault();
            if (first?.Key is { } key && TrackColumn.ById(key)?.Sort is { } sort)
            {
                owner._sort = sort;
                owner._descending = !first.Ascending;
            }
            else owner._sort = TrackSort.Natural;
            var keep = owner.SelectedTracks.Select(t => t.Path).ToList();
            owner.Refresh();
            owner.Select(keep);
        }

        // Eine Zeile trägt ihren Pfad als Datei-URL mit: So lässt sie sich
        // auch in den Finder ziehen, und dort landet eine Kopie.
        public override INSPasteboardWriting? GetPasteboardWriterForRow(NSTableView tableView, nint row)
        {
            if (owner._rows[(int)row] is not AudioTrack t) return null;
            var item = new NSPasteboardItem();
            item.SetStringForType(t.Path, RowType);
            item.SetStringForType(NSUrl.FromFilename(t.Path).AbsoluteString!, NSPasteboard.NSPasteboardTypeFileUrl);
            return item;
        }

        public override void DraggingSessionWillBegin(NSTableView tableView, NSDraggingSession draggingSession,
                                                      CGPoint willBeginAtScreenPoint, NSIndexSet rowIndexes) =>
            owner._dragRows = [.. rowIndexes.Select(i => (int)i)];

        public override void DraggingSessionEnded(NSTableView tableView, NSDraggingSession draggingSession,
                                                  CGPoint endedAtScreenPoint, NSDragOperation operation) =>
            owner._dragRows = null;

        public override NSDragOperation ValidateDrop(NSTableView tableView, INSDraggingInfo info, nint row,
                                                     NSTableViewDropOperation dropOperation)
        {
            if (info.DraggingSource == tableView)
            {
                if (!owner.CanReorder || owner._dragRows is null) return NSDragOperation.None;
                if (row > owner._ownEnd || owner._dragRows.Any(i => i >= owner._ownEnd)) return NSDragOperation.None;
                if (dropOperation == NSTableViewDropOperation.On)
                    tableView.SetDropRowDropOperation(row, NSTableViewDropOperation.Above);
                return NSDragOperation.Move;
            }
            if (DroppedAudio(info).Count > 0 && owner.Folder is not null)
            {
                if (dropOperation == NSTableViewDropOperation.On)
                    tableView.SetDropRowDropOperation(row, NSTableViewDropOperation.Above);
                return Moves(info) ? NSDragOperation.Move : NSDragOperation.Copy;
            }
            // Ordner aus dem Finder öffnen sich.
            return DroppedFolder(info) is null ? NSDragOperation.None : NSDragOperation.Generic;
        }

        public override bool AcceptDrop(NSTableView tableView, INSDraggingInfo info, nint row,
                                        NSTableViewDropOperation dropOperation)
        {
            if (info.DraggingSource == tableView)
                return owner._dragRows is { Length: > 0 } block && owner.MoveRows(block, (int)row);

            var audio = DroppedAudio(info);
            if (audio.Count > 0 && owner.Folder is not null)
            {
                // Die Stelle in der Playlist, ohne Disc-Zeilen gezählt. Sortiert
                // oder gefiltert gibt es keine: ans Ende.
                var at = owner.CanReorder && row <= owner._ownEnd
                    ? owner._rows.Take((int)row).Count(r => r is AudioTrack)
                    : owner._all.Count;
                owner.FilesDropped?.Invoke(audio, at, Moves(info));
                return true;
            }

            if (DroppedFolder(info) is not { } folder) return false;
            (owner.View.Window?.WindowController as MainWindowController)?.OpenFolder(folder);
            return true;
        }

        /// <summary>
        /// Aus der anderen Hälfte wird verschoben, mit ⌥ kopiert, wie unter
        /// Windows mit Strg. Aus dem Finder wird kopiert, mit ⌘ verschoben.
        /// </summary>
        private static bool Moves(INSDraggingInfo info)
        {
            var mods = NSEvent.CurrentModifierFlags;
            return info.DraggingSource is TrackTable
                ? !mods.HasFlag(NSEventModifierMask.AlternateKeyMask)
                : mods.HasFlag(NSEventModifierMask.CommandKeyMask);
        }

        /// <summary>Abgelegte Audiodateien, die nicht schon in diesem Ordner liegen.</summary>
        private List<string> DroppedAudio(INSDraggingInfo info)
        {
            var urls = info.DraggingPasteboard.ReadObjectsForClasses([new Class(typeof(NSUrl))], null);
            return [.. (urls?.OfType<NSUrl>() ?? []).Select(u => u.Path).OfType<string>()
                .Where(p => File.Exists(p) && AudioFormats.IsAudioFile(p))
                .Where(p => !string.Equals(Path.GetDirectoryName(p), owner.Folder?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))];
        }

        private static string? DroppedFolder(INSDraggingInfo info)
        {
            var urls = info.DraggingPasteboard.ReadObjectsForClasses([new Class(typeof(NSUrl))], null);
            return urls?.OfType<NSUrl>().Select(u => u.Path).FirstOrDefault(p => p is not null && Directory.Exists(p));
        }
    }

    private sealed class Delegate(TrackListController owner) : NSTableViewDelegate
    {
        private static readonly NSFont Mono = NSFont.MonospacedDigitSystemFontOfSize(12, NSFontWeight.Regular);
        private static readonly NSFont Small = NSFont.MonospacedSystemFont(10.5f, NSFontWeight.Regular);

        public override nfloat GetRowHeight(NSTableView tableView, nint row) => owner._rows[(int)row] switch
        {
            DiscRow => 36,
            SubfoldersTitle => 34,
            SubfolderRow => 46,
            _ => Combined ? 46 : 28,
        };

        /// <summary>Kopfzeilen der Unterordner laufen über die ganze Breite.</summary>
        public override bool IsGroupRow(NSTableView tableView, nint row) =>
            owner._rows[(int)row] is SubfoldersTitle or SubfolderRow;

        public override bool ShouldSelectRow(NSTableView tableView, nint row) => owner._rows[(int)row] is AudioTrack;

        public override NSTableRowView CoreGetRowView(NSTableView tableView, nint row) => new SelectionRowView(bar: false);

        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn? tableColumn, nint row)
        {
            if (owner._rows[(int)row] is SubfoldersTitle title)
            {
                var l = Theme.Section($"{Strings.T("SUBFOLDERS")}  {title.Count}");
                var box = new NSView();
                l.TranslatesAutoresizingMaskIntoConstraints = false;
                box.AddSubview(l);
                NSLayoutConstraint.ActivateConstraints([
                    l.LeadingAnchor.ConstraintEqualTo(box.LeadingAnchor, 14),
                    l.BottomAnchor.ConstraintEqualTo(box.BottomAnchor, -6),
                ]);
                return box;
            }
            if (owner._rows[(int)row] is SubfolderRow sub)
            {
                var sc = tableView.MakeView("sub", this) as SubfolderCell ?? new SubfolderCell { Identifier = "sub" };
                sc.Show(sub, owner._expanded.Contains(sub.Path), () => owner.Toggle(sub),
                        () => (owner.View.Window?.WindowController as MainWindowController)?.OpenFolder(sub.Path));
                return sc;
            }
            if (tableColumn is null) return new NSView();

            if (owner._rows[(int)row] is DiscRow disc)
            {
                // Die Disc-Zeile steht in der ersten Spalte, die übrigen bleiben leer.
                var dv = tableView.MakeView("disc", this) as NSTableCellView ?? NewDiscCell();
                var first = tableView.TableColumns().FirstOrDefault() == tableColumn;
                dv.TextField!.StringValue = first ? Strings.T("Disc {0}", disc.Disc) : "";
                dv.ImageView!.Hidden = !first;
                return dv;
            }

            var track = (AudioTrack)owner._rows[(int)row];
            var col = TrackColumn.ById(tableColumn.Identifier);

            if (col?.Id == "title" && Combined)
            {
                var tc = tableView.MakeView("titleartist", this) as TitleCell ?? new TitleCell { Identifier = "titleartist" };
                tc.Show(Title(track), track.Artist, owner.Art(track), track.HasCover, string.IsNullOrWhiteSpace(track.Title));
                return tc;
            }

            if (col?.IsCover == true)
            {
                var iv = tableView.MakeView("cover", this) as NSImageView ?? new NSImageView
                {
                    Identifier = "cover",
                    ImageScaling = NSImageScale.ProportionallyUpOrDown,
                    WantsLayer = true,
                };
                iv.Layer!.CornerRadius = 3;
                iv.Layer.MasksToBounds = true;
                iv.Image = owner.Art(track);
                return iv;
            }

            var cell = tableView.MakeView("text", this) as NSTableCellView ?? NewCell();
            var tf = cell.TextField!;
            tf.StringValue = col is null ? "" : Value(track, col);
            tf.Alignment = col?.Look == ColumnLook.MonoRight ? NSTextAlignment.Right : NSTextAlignment.Left;
            tf.Font = col?.Look switch
            {
                ColumnLook.MonoRight => Mono,
                ColumnLook.Mono => Small,
                _ => NSFont.SystemFontOfSize(12),
            };
            tf.TextColor = col?.Look == ColumnLook.Text ? NSColor.Label : NSColor.SecondaryLabel;

            // Der laufende Titel: ein grünes Zeichen statt der Nummer.
            if (col?.Id == "track" && track.Path == owner.PlayingPath)
            {
                tf.StringValue = "▶";
                tf.TextColor = Theme.Accent;
            }
            if (col?.Id == "title" && string.IsNullOrWhiteSpace(track.Title))
            {
                tf.StringValue = Title(track);
                tf.TextColor = NSColor.TertiaryLabel;
            }
            return cell;
        }

        private static string Title(AudioTrack t) =>
            string.IsNullOrWhiteSpace(t.Title) ? Path.GetFileNameWithoutExtension(t.FileName) : t.Title;

        private string Value(AudioTrack t, TrackColumn c) => c.Id switch
        {
            // In Disc-Zeilen gegliedert zählt jede Disc von 1; sonst steht die
            // Disc kompakt vor der Nummer.
            "track" => !owner._sectioned && Settings.CombineDiscAndTrack && t.Disc > 0 && t.Track > 0
                       && owner._all.Select(x => x.Disc).Where(d => d > 0).Distinct().Count() > 1
                ? $"{t.Disc}-{t.Track:00}" : t.TrackLabel,
            _ => typeof(AudioTrack).GetProperty(c.Property)?.GetValue(t)?.ToString() ?? "",
        };

        private static NSTableCellView NewCell()
        {
            var cell = new NSTableCellView { Identifier = "text" };
            var text = NSTextField.CreateLabel("");
            text.TranslatesAutoresizingMaskIntoConstraints = false;
            text.LineBreakMode = NSLineBreakMode.TruncatingTail;
            cell.AddSubview(text);
            cell.TextField = text;
            NSLayoutConstraint.ActivateConstraints([
                text.LeadingAnchor.ConstraintEqualTo(cell.LeadingAnchor, 2),
                text.TrailingAnchor.ConstraintEqualTo(cell.TrailingAnchor, -2),
                text.CenterYAnchor.ConstraintEqualTo(cell.CenterYAnchor),
            ]);
            return cell;
        }

        private static NSTableCellView NewDiscCell()
        {
            var cell = new NSTableCellView { Identifier = "disc" };
            var icon = NSImageView.FromImage(NSImage.GetSystemSymbol("opticaldisc", null)!);
            icon.ContentTintColor = NSColor.SecondaryLabel;
            var text = NSTextField.CreateLabel("");
            text.Font = NSFont.SystemFontOfSize(12, NSFontWeight.Medium);
            foreach (var v in new NSView[] { icon, text })
            {
                v.TranslatesAutoresizingMaskIntoConstraints = false;
                cell.AddSubview(v);
            }
            cell.ImageView = icon;
            cell.TextField = text;
            NSLayoutConstraint.ActivateConstraints([
                icon.LeadingAnchor.ConstraintEqualTo(cell.LeadingAnchor, 2),
                icon.CenterYAnchor.ConstraintEqualTo(cell.CenterYAnchor, 2),
                text.LeadingAnchor.ConstraintEqualTo(icon.TrailingAnchor, 6),
                text.CenterYAnchor.ConstraintEqualTo(icon.CenterYAnchor),
            ]);
            return cell;
        }

        public override void SelectionDidChange(NSNotification notification) =>
            owner.SelectionChanged?.Invoke();

        public override void ColumnDidMove(NSNotification notification) => owner.RememberColumns();

        public override void ColumnDidResize(NSNotification notification) => owner.RememberColumns();
    }

    /// <summary>Cover, daneben der Titel, klein darunter der Interpret.</summary>
    private sealed class TitleCell : NSTableCellView
    {
        private readonly NSImageView _art = new() { ImageScaling = NSImageScale.ProportionallyUpOrDown, WantsLayer = true };
        private readonly NSTextField _title = NSTextField.CreateLabel("");
        private readonly NSTextField _artist = NSTextField.CreateLabel("");

        public TitleCell()
        {
            _art.Layer!.CornerRadius = 4;
            _art.Layer.MasksToBounds = true;
            _title.Font = NSFont.SystemFontOfSize(13);
            _artist.Font = NSFont.SystemFontOfSize(11);
            _artist.TextColor = NSColor.SecondaryLabel;
            _title.LineBreakMode = _artist.LineBreakMode = NSLineBreakMode.TruncatingTail;
            var text = new NSStackView
            {
                Orientation = NSUserInterfaceLayoutOrientation.Vertical,
                Alignment = NSLayoutAttribute.Leading,
                Spacing = 1,
            }.Arranged(_title, _artist);
            foreach (var v in new NSView[] { _art, text })
            {
                v.TranslatesAutoresizingMaskIntoConstraints = false;
                AddSubview(v);
            }
            NSLayoutConstraint.ActivateConstraints([
                _art.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
                _art.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
                _art.WidthAnchor.ConstraintEqualTo(34),
                _art.HeightAnchor.ConstraintEqualTo(34),
                text.LeadingAnchor.ConstraintEqualTo(_art.TrailingAnchor, 10),
                text.TrailingAnchor.ConstraintLessThanOrEqualTo(TrailingAnchor, -2),
                text.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
                _title.WidthAnchor.ConstraintLessThanOrEqualTo(text.WidthAnchor),
            ]);
            TextField = _title;
        }

        public void Show(string title, string artist, NSImage? art, bool hasCover, bool guessed)
        {
            _title.StringValue = title;
            _title.TextColor = guessed ? NSColor.TertiaryLabel : NSColor.Label;
            _artist.StringValue = artist;
            _artist.Hidden = artist.Length == 0;
            _art.Image = art ?? (hasCover ? null : NSImage.GetSystemSymbol("music.note", null));
            _art.ContentTintColor = NSColor.TertiaryLabel;
            _art.ImageScaling = art is null ? NSImageScale.ProportionallyDown : NSImageScale.ProportionallyUpOrDown;
            _art.Layer!.BackgroundColor = NSColor.FromWhite(1, 0.05f).CGColor;
        }
    }

    /// <summary>
    /// Kopf eines Unterordner-Abschnitts: Pfeil, Cover, Name, Zahl der Dateien
    /// und ein Knopf, der den Unterordner selbst öffnet, wie unter Windows.
    /// </summary>
    private sealed class SubfolderCell : NSTableCellView
    {
        private readonly NSButton _chevron = NSButton.CreateButton(NSImage.GetSystemSymbol("chevron.right", null)!, () => { });
        private readonly NSImageView _art = new() { ImageScaling = NSImageScale.ProportionallyUpOrDown, WantsLayer = true };
        private readonly NSTextField _name = NSTextField.CreateLabel("");
        private readonly NSTextField _count = NSTextField.CreateLabel("");
        private readonly NSButton _open = NSButton.CreateButton(NSImage.GetSystemSymbol("arrow.up.forward.square", null)!, () => { });
        private Action? _toggle, _openFolder;

        public SubfolderCell()
        {
            WantsLayer = true;
            Layer!.BackgroundColor = NSColor.FromWhite(1, 0.04f).CGColor;
            Layer.CornerRadius = 8;
            _chevron.Bordered = _open.Bordered = false;
            _chevron.Activated += (_, _) => _toggle?.Invoke();
            _open.Activated += (_, _) => _openFolder?.Invoke();
            _open.ToolTip = Strings.T("Open this folder");
            _art.Layer!.CornerRadius = 4;
            _art.Layer.MasksToBounds = true;
            _name.Font = NSFont.SystemFontOfSize(13, NSFontWeight.Semibold);
            _count.Font = NSFont.MonospacedSystemFont(10.5f, NSFontWeight.Regular);
            _count.TextColor = NSColor.TertiaryLabel;
            foreach (var v in new NSView[] { _chevron, _art, _name, _count, _open })
            {
                v.TranslatesAutoresizingMaskIntoConstraints = false;
                AddSubview(v);
            }
            NSLayoutConstraint.ActivateConstraints([
                _chevron.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 10),
                _chevron.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
                _art.LeadingAnchor.ConstraintEqualTo(_chevron.TrailingAnchor, 8),
                _art.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
                _art.WidthAnchor.ConstraintEqualTo(30),
                _art.HeightAnchor.ConstraintEqualTo(30),
                _name.LeadingAnchor.ConstraintEqualTo(_art.TrailingAnchor, 10),
                _name.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
                _open.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -12),
                _open.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
                _count.TrailingAnchor.ConstraintEqualTo(_open.LeadingAnchor, -10),
                _count.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
                _name.TrailingAnchor.ConstraintLessThanOrEqualTo(_count.LeadingAnchor, -10),
            ]);
        }

        public void Show(SubfolderRow sub, bool open, Action toggle, Action openFolder)
        {
            _toggle = toggle;
            _openFolder = openFolder;
            _chevron.Image = NSImage.GetSystemSymbol(open ? "chevron.down" : "chevron.right", null);
            _name.StringValue = sub.Name;
            _count.StringValue = Strings.T("{0} files", sub.Count);
            if (FolderLook.TryGet(sub.Path, out var look))
                _art.Image = look.Cover ?? NSImage.GetSystemSymbol("folder", null);
            else
            {
                _art.Image = NSImage.GetSystemSymbol("folder", null);
                FolderLook.Load(sub.Path, () =>
                {
                    if (_name.StringValue == sub.Name && FolderLook.TryGet(sub.Path, out var l))
                        _art.Image = l.Cover ?? NSImage.GetSystemSymbol("folder", null);
                });
            }
        }
    }

    // ── Kontextmenü ──────────────────────────────────────────────

    private sealed class MenuDelegate(TrackListController owner) : NSMenuDelegate
    {
        public override void MenuWillHighlightItem(NSMenu menu, NSMenuItem item) { }

        public override void NeedsUpdate(NSMenu menu)
        {
            menu.RemoveAllItems();
            var t = owner._table;

            // Rechtsklick außerhalb der Auswahl wählt die Zeile, wie im Finder.
            if (t.ClickedRow >= 0 && !t.IsRowSelected(t.ClickedRow) && owner._rows[(int)t.ClickedRow] is AudioTrack)
                t.SelectRow(t.ClickedRow, false);

            var sel = owner.SelectedTracks;
            if (sel.Count == 0) return;

            menu.AddItem(new NSMenuItem(Strings.T("Play"), (_, _) => owner.PlayRequested?.Invoke(sel[0])));
            menu.AddItem(NSMenuItem.SeparatorItem);
            menu.AddItem(new NSMenuItem(Strings.T("Copy tags"), new Selector("copyTags:"), ""));
            menu.AddItem(new NSMenuItem(Strings.T("Paste tags"), new Selector("pasteTags:"), ""));
            menu.AddItem(NSMenuItem.SeparatorItem);
            menu.AddItem(new NSMenuItem(Strings.T("Set cover…"), new Selector("chooseCover:"), ""));
            menu.AddItem(new NSMenuItem(Strings.T("Paste cover"), new Selector("pasteCover:"), ""));
            menu.AddItem(new NSMenuItem(Strings.T("Remove cover"), new Selector("removeCover:"), ""));
            menu.AddItem(NSMenuItem.SeparatorItem);
            menu.AddItem(new NSMenuItem(Strings.T("Rename…"), new Selector("renameFiles:"), ""));
            menu.AddItem(NSMenuItem.SeparatorItem);
            menu.AddItem(new NSMenuItem(Strings.T("Show in Finder"), (_, _) =>
                NSWorkspace.SharedWorkspace.ActivateFileViewer([.. sel.Select(x => NSUrl.FromFilename(x.Path))])));
            menu.AddItem(new NSMenuItem(sel.Count == 1 ? Strings.T("Copy path") : Strings.T("Copy {0} paths", sel.Count), (_, _) =>
            {
                NSPasteboard.GeneralPasteboard.ClearContents();
                NSPasteboard.GeneralPasteboard.SetStringForType(
                    string.Join("\n", sel.Select(x => x.Path)), NSPasteboard.NSPasteboardTypeString);
            }));
        }
    }

    /// <summary>Leertaste spielt, Return auch, wie in Musik.</summary>
    private sealed class TrackTable : NSTableView
    {
        public TrackListController? Owner;

        public override void MouseDown(NSEvent theEvent)
        {
            Owner?.Clicked?.Invoke();
            base.MouseDown(theEvent);
        }

        public override void KeyDown(NSEvent theEvent)
        {
            var key = theEvent.CharactersIgnoringModifiers;
            if (key == " ")
            {
                NSApplication.SharedApplication.SendAction(new Selector("playPause:"), null, this);
                return;
            }
            if (key is "\r" or "\u0003" && Owner?.SelectedTracks.FirstOrDefault() is { } t)
            {
                Owner.PlayRequested?.Invoke(t);
                return;
            }
            base.KeyDown(theEvent);
        }
    }
}
