using ObjCRuntime;
using TagTuner.Core.Audio;
using TagTuner.Core.Folders;
using TagTuner.Core.Model;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>
/// Die Trackliste. Die Spalten kommen aus <see cref="TrackColumn.All"/>,
/// genau wie unter Windows; was dort eine Spalte ist, ist es hier auch.
/// </summary>
internal sealed partial class TrackListController : NSViewController
{
    public event Action? SelectionChanged;
    public event Action<AudioTrack>? PlayRequested;

    private readonly TrackTable _table = new();
    private readonly NSTextField _empty = NSTextField.CreateLabel("");
    private List<AudioTrack> _all = [];
    private List<AudioTrack> _shown = [];
    private string _filter = "";
    private TrackSort _sort = TrackSort.Natural;
    private bool _descending;
    private CancellationTokenSource? _loading;

    public string? Folder { get; private set; }
    public IReadOnlyList<AudioTrack> Tracks => _all;
    public IReadOnlyList<AudioTrack> Shown => _shown;

    /// <summary>Das Lied, das gerade läuft, bekommt einen Lautsprecher in der Nummernspalte.</summary>
    public string? PlayingPath { get; set; }

    private static AppSettings Settings => AppDelegate.Settings;

    public override void LoadView()
    {
        _table.Owner = this;
        _table.Style = NSTableViewStyle.FullWidth;
        _table.UsesAlternatingRowBackgroundColors = true;
        _table.AllowsMultipleSelection = true;
        _table.AllowsColumnReordering = true;
        _table.AllowsColumnResizing = true;
        _table.ColumnAutoresizingStyle = NSTableViewColumnAutoresizingStyle.Uniform;
        _table.RowHeight = 26;
        _table.IntercellSpacing = new CGSize(8, 0);
        _table.DoubleAction = new Selector("rowDoubleClicked:");
        _table.Target = this;
        _table.AutosaveName = "TrackTable";
        _table.AutosaveTableColumns = true;

        foreach (var col in VisibleColumns())
        {
            var c = new NSTableColumn(col.Id)
            {
                Title = Strings.T(col.Header),
                Width = (nfloat)(col.IsCover ? 28 : col.Width),
                MinWidth = col.IsCover ? 28 : 30,
                Editable = false,
            };
            if (col.IsCover) c.MaxWidth = 28;
            // Nur die Textspalten wachsen mit dem Fenster, Nummern und Kürzel bleiben schmal.
            c.ResizingMask = col.Look == ColumnLook.Text && !col.IsCover
                ? NSTableColumnResizing.Autoresizing | NSTableColumnResizing.UserResizingMask
                : NSTableColumnResizing.UserResizingMask;
            if (col.Look == ColumnLook.MonoRight) c.HeaderCell.Alignment = NSTextAlignment.Right;
            if (col.Sort is not null) c.SortDescriptorPrototype = new NSSortDescriptor(col.Id, true);
            _table.AddColumn(c);
        }

        _table.Delegate = new Delegate(this);
        _table.DataSource = new Source(this);
        _table.Menu = new NSMenu { Delegate = new MenuDelegate(this) };
        _table.RegisterForDraggedTypes([NSPasteboard.NSPasteboardTypeFileUrl]);

        var scroll = new NSScrollView
        {
            DocumentView = _table,
            HasVerticalScroller = true,
            HasHorizontalScroller = true,
            AutohidesScrollers = true,
        };

        _empty.TextColor = NSColor.SecondaryLabel;
        _empty.Font = NSFont.SystemFontOfSize(15);
        _empty.Alignment = NSTextAlignment.Center;

        var root = new NSView();
        foreach (var v in new NSView[] { scroll, _empty })
        {
            v.TranslatesAutoresizingMaskIntoConstraints = false;
            root.AddSubview(v);
        }
        NSLayoutConstraint.ActivateConstraints([
            scroll.TopAnchor.ConstraintEqualTo(root.TopAnchor),
            scroll.BottomAnchor.ConstraintEqualTo(root.BottomAnchor),
            scroll.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor),
            scroll.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor),
            _empty.CenterXAnchor.ConstraintEqualTo(root.CenterXAnchor),
            _empty.CenterYAnchor.ConstraintEqualTo(root.CenterYAnchor),
            _empty.WidthAnchor.ConstraintLessThanOrEqualTo(root.WidthAnchor, 0.8f),
        ]);
        View = root;
        ShowEmpty();
    }

    private static IEnumerable<TrackColumn> VisibleColumns()
    {
        if (Settings.TrackColumns.Count == 0)
            return TrackColumn.All.Where(c => c.OnByDefault);

        return Settings.TrackColumns
            .Where(s => s.Visible)
            .Select(s => TrackColumn.ById(s.Id))
            .OfType<TrackColumn>();
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
            _table.ReloadData();
            _empty.StringValue = Strings.T("Reading…");
            _empty.Hidden = false;
        }

        List<AudioTrack> tracks;
        try
        {
            tracks = await Task.Run(() => FolderScanner.Tracks(folder, cts.Token).ToList(), cts.Token);
        }
        catch (OperationCanceledException) { return; }
        if (cts.IsCancellationRequested) return;

        _all = tracks;
        Refresh();
        ShowEmpty();

        if (keepSelection is { Count: > 0 })
            Select(keepSelection);
        else
            SelectionChanged?.Invoke();
    }

    private void ShowEmpty()
    {
        _empty.StringValue = Folder is null
            ? Strings.T("Choose a folder on the left.")
            : _all.Count == 0 ? Strings.T("No audio files in this folder.")
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

    /// <summary>Filtert und sortiert neu, ohne die Platte anzufassen.</summary>
    private void Refresh()
    {
        IEnumerable<AudioTrack> list = _all;
        if (_filter.Length > 0)
            list = list.Where(t => Matches(t, _filter));
        _shown = TrackSorting.Apply(list, _sort, _descending, Settings.SortByDiscThenTrack);
        _table.ReloadData();
    }

    private static bool Matches(AudioTrack t, string q) =>
        new[] { t.Title, t.Artist, t.Album, t.AlbumArtist, t.Genre, t.FileName }
            .Any(s => s.Contains(q, StringComparison.CurrentCultureIgnoreCase));

    // ── Auswahl ──────────────────────────────────────────────────

    public List<AudioTrack> SelectedTracks =>
        [.. _table.SelectedRows.Select(i => (int)i).Where(i => i < _shown.Count).Select(i => _shown[i])];

    public void Select(IEnumerable<string> paths)
    {
        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        var rows = new NSMutableIndexSet();
        for (var i = 0; i < _shown.Count; i++)
            if (set.Contains(_shown[i].Path)) rows.Add((nuint)i);
        _table.SelectRows(rows, false);
        if (rows.Count > 0) _table.ScrollRowToVisible((nint)rows.FirstIndex);
        SelectionChanged?.Invoke();
    }

    public void SelectAllTracks() => _table.SelectAll(this);

    public void RedrawRows() =>
        _table.ReloadData(NSIndexSet.FromNSRange(new NSRange(0, _shown.Count)),
                          NSIndexSet.FromNSRange(new NSRange(0, _table.ColumnCount)));

    /// <summary>Das Lied nach oder vor dem laufenden, in der angezeigten Reihenfolge.</summary>
    public AudioTrack? Neighbour(string? path, int step)
    {
        var i = _shown.FindIndex(t => t.Path == path);
        var j = i + step;
        return i >= 0 && j >= 0 && j < _shown.Count ? _shown[j] : null;
    }

    [Export("rowDoubleClicked:")]
    public void RowDoubleClicked(NSObject sender)
    {
        var row = _table.ClickedRow;
        if (row >= 0 && row < _shown.Count) PlayRequested?.Invoke(_shown[(int)row]);
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
                var small = Covers.Thumbnail(AudioProbe.ReadCover(path)?.Data, 64);
                InvokeOnMainThread(() =>
                {
                    _artLoading.Remove(path);
                    _art[path] = small;
                    var row = _shown.FindIndex(x => x.Path == path);
                    var col = _table.FindColumn(new NSString("cover"));
                    if (row >= 0 && col >= 0)
                        _table.ReloadData(NSIndexSet.FromIndex(row), NSIndexSet.FromIndex(col));
                });
            });
        }
        return null;
    }

    // ── Tabelle ──────────────────────────────────────────────────

    private sealed class Source(TrackListController owner) : NSTableViewDataSource
    {
        public override nint GetRowCount(NSTableView tableView) => owner._shown.Count;

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

        // Dateien aus dem Finder: vorerst nur annehmen, wenn es Ordner sind –
        // die öffnen sich dann. Übernehmen und Verschieben kommt später.
        public override NSDragOperation ValidateDrop(NSTableView tableView, INSDraggingInfo info, nint row,
                                                     NSTableViewDropOperation dropOperation)
        {
            return DroppedFolder(info) is null ? NSDragOperation.None : NSDragOperation.Generic;
        }

        public override bool AcceptDrop(NSTableView tableView, INSDraggingInfo info, nint row,
                                        NSTableViewDropOperation dropOperation)
        {
            if (DroppedFolder(info) is not { } folder) return false;
            (owner.View.Window?.WindowController as MainWindowController)?.OpenFolder(folder);
            return true;
        }

        private static string? DroppedFolder(INSDraggingInfo info)
        {
            var urls = info.DraggingPasteboard.ReadObjectsForClasses(
                [new Class(typeof(NSUrl))], null);
            return urls?.OfType<NSUrl>().Select(u => u.Path).FirstOrDefault(p => p is not null && Directory.Exists(p));
        }
    }

    private sealed class Delegate(TrackListController owner) : NSTableViewDelegate
    {
        private static readonly NSFont Mono = NSFont.MonospacedDigitSystemFontOfSize(NSFont.SystemFontSize, NSFontWeight.Regular);
        private static readonly NSFont Small = NSFont.MonospacedSystemFont(NSFont.SmallSystemFontSize, NSFontWeight.Regular);

        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
        {
            var track = owner._shown[(int)row];
            var col = TrackColumn.ById(tableColumn.Identifier);

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
                _ => NSFont.SystemFontOfSize(NSFont.SystemFontSize),
            };
            tf.TextColor = col?.Look == ColumnLook.Text ? NSColor.Label : NSColor.SecondaryLabel;

            // Der laufende Titel: Lautsprecher statt Nummer, wie in Musik.
            if (col?.Id == "track" && track.Path == owner.PlayingPath)
            {
                tf.StringValue = "\U0001F50A";
                tf.Font = NSFont.SystemFontOfSize(10);
            }
            // Ohne Titel-Tag steht der Dateiname da, aber zurückhaltend.
            if (col?.Id == "title" && string.IsNullOrWhiteSpace(track.Title))
            {
                tf.StringValue = Path.GetFileNameWithoutExtension(track.FileName);
                tf.TextColor = NSColor.TertiaryLabel;
            }
            return cell;
        }

        private static string Value(AudioTrack t, TrackColumn c) => c.Id switch
        {
            "track" => Settings.CombineDiscAndTrack && t.Disc > 1 && t.Track > 0
                ? $"{t.Disc}-{t.Track:00}" : t.TrackLabel,
            _ => typeof(AudioTrack).GetProperty(c.Property)?.GetValue(t)?.ToString() ?? "",
        };

        private static NSTableCellView NewCell()
        {
            var cell = new NSTableCellView { Identifier = "text" };
            var text = NSTextField.CreateLabel("");
            text.TranslatesAutoresizingMaskIntoConstraints = false;
            text.LineBreakMode = NSLineBreakMode.TruncatingTail;
            text.Cell.Scrollable = false;
            cell.AddSubview(text);
            cell.TextField = text;
            NSLayoutConstraint.ActivateConstraints([
                text.LeadingAnchor.ConstraintEqualTo(cell.LeadingAnchor, 2),
                text.TrailingAnchor.ConstraintEqualTo(cell.TrailingAnchor, -2),
                text.CenterYAnchor.ConstraintEqualTo(cell.CenterYAnchor),
            ]);
            return cell;
        }

        public override void SelectionDidChange(NSNotification notification) =>
            owner.SelectionChanged?.Invoke();
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
            if (t.ClickedRow >= 0 && !t.IsRowSelected(t.ClickedRow))
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
