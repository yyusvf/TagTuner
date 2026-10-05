using ObjCRuntime;
using TagTuner.Core.Folders;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>
/// Ein Ordner im Baum, mit Cover und Interpret aus seinen Liedern. Beides
/// kommt nachträglich aus dem Hintergrund: erst steht der Name da, dann
/// schiebt sich das Bild nach (wie LibraryFolder unter Windows).
/// </summary>
internal sealed class FolderNode(FolderEntry entry, FolderNode? parent) : NSObject
{
    public FolderEntry Entry { get; } = entry;
    public FolderNode? Parent { get; } = parent;
    private List<FolderNode>? _children;
    private bool? _expandable;

    public List<FolderNode> Children(bool onlyAudio) =>
        _children ??= [.. FolderScanner.Subfolders(Entry.Path, onlyAudio).Select(e => new FolderNode(e, this))];

    public bool Expandable(bool onlyAudio) =>
        _expandable ??= _children is { } c ? c.Count > 0 : FolderScanner.HasSubfolders(Entry.Path, onlyAudio);

    public void Forget() { _children = null; _expandable = null; }
}

/// <summary>Cover und Interpret je Ordner, einmal gelesen und gemerkt.</summary>
internal static class FolderLook
{
    private static readonly Dictionary<string, (NSImage? Cover, string Artist)> Known = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Loading = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Nur wenige Ordner gleichzeitig lesen. Ein Verzeichnis mit zweihundert
    /// Unterordnern würde sonst zweihundert Dateizugriffe auf einmal auslösen.
    /// </summary>
    private static readonly SemaphoreSlim Gate = new(3);

    public static bool TryGet(string path, out (NSImage? Cover, string Artist) look) => Known.TryGetValue(path, out look);

    /// <summary>Lädt im Hintergrund und meldet sich auf dem Hauptfaden.</summary>
    public static void Load(string path, Action done)
    {
        if (Known.ContainsKey(path) || !Loading.Add(path)) return;
        Task.Run(async () =>
        {
            NSImage? cover = null;
            string? artist = null;
            await Gate.WaitAsync();
            try
            {
                // In einer Zeile sind 32 Punkte zu sehen, auf Retina das Doppelte.
                cover = Covers.Thumbnail(FolderCover.Read(path), 96);
                artist = FolderArtist.Of(path);
            }
            catch { }
            finally { Gate.Release(); }

            NSApplication.SharedApplication.BeginInvokeOnMainThread(() =>
            {
                Loading.Remove(path);
                if (Known.Count > 2000) Known.Clear();
                Known[path] = (cover, artist ?? "");
                done();
            });
        });
    }

    /// <summary>Nach dem Schreiben kann sich Cover oder Interpret geändert haben.</summary>
    public static void Forget(string path) => Known.Remove(path.TrimEnd('/'));
}

/// <summary>
/// Die Bibliothek: eigene Ordner, Musik, Downloads, Benutzerordner und die
/// Laufwerke, darüber die Suche nach Ordnern und Liedern.
/// </summary>
internal sealed partial class LibraryController : NSViewController
{
    public event Action<string>? FolderChosen;

    /// <summary>Ein Lied aus der Suche: seinen Ordner öffnen und es darin wählen.</summary>
    public event Action<string, string>? TrackChosen;

    private readonly NSOutlineView _outline = new();
    private readonly NSTableView _results = new();
    private readonly NSScrollView _treeScroll = new();
    private readonly NSScrollView _resultScroll = new();
    private readonly NSSearchField _search = new();
    private readonly NSTextField _searchInfo = Theme.Small("");
    private List<FolderNode> _roots = [];
    private IReadOnlyList<SearchHit> _hits = [];
    private Task<LibraryIndex>? _index;
    private CancellationTokenSource? _searching;
    private bool _quiet;

    private static AppSettings Settings => AppDelegate.Settings;

    public override void LoadView()
    {
        // ── Kopf: BIBLIOTHEK und Plus ────────────────────────────
        var head = Theme.Section(Strings.T("LIBRARY"));
        var add = NSButton.CreateButton(NSImage.GetSystemSymbol("plus", null)!, () => AddLibraryFolder(this));
        add.Bordered = false;
        add.ToolTip = Strings.T("Add folder…");

        _search.PlaceholderString = Strings.T("Search folders and songs…");
        _search.Changed += (_, _) => Search(_search.StringValue);
        _search.SendsSearchStringImmediately = true;

        // ── Baum ─────────────────────────────────────────────────
        // Schlichter Stil statt Seitenleiste: Nur so zeichnet SelectionRowView die
        // Auswahl, die Seitenleiste nähme sonst die volle Akzentfarbe.
        _outline.Style = NSTableViewStyle.Plain;
        _outline.BackgroundColor = NSColor.Clear;
        // Die eine Spalte füllt immer die Breite, damit Namen gekürzt statt abgeschnitten werden.
        _outline.ColumnAutoresizingStyle = NSTableViewColumnAutoresizingStyle.FirstColumnOnly;
        _outline.HeaderView = null;
        _outline.RowHeight = 44;
        _outline.IndentationPerLevel = 14;
        _outline.AutosaveExpandedItems = false;
        var column = new NSTableColumn("name") { Editable = false, ResizingMask = NSTableColumnResizing.Autoresizing };
        _outline.AddColumn(column);
        _outline.OutlineTableColumn = column;
        _outline.Delegate = new Delegate(this);
        _outline.DataSource = new Source(this);
        _outline.Menu = new NSMenu { AutoEnablesItems = false, Delegate = new MenuDelegate(this) };
        _treeScroll.DocumentView = _outline;
        _treeScroll.HasVerticalScroller = true;
        _treeScroll.DrawsBackground = false;
        _treeScroll.AutohidesScrollers = true;

        // ── Suchtreffer ──────────────────────────────────────────
        _results.Style = NSTableViewStyle.Plain;
        _results.BackgroundColor = NSColor.Clear;
        _results.ColumnAutoresizingStyle = NSTableViewColumnAutoresizingStyle.FirstColumnOnly;
        _results.HeaderView = null;
        _results.RowHeight = 36;
        _results.AddColumn(new NSTableColumn("hit") { Editable = false, ResizingMask = NSTableColumnResizing.Autoresizing });
        _results.DataSource = new HitSource(this);
        _results.Delegate = new HitDelegate(this);
        _resultScroll.DocumentView = _results;
        _resultScroll.HasVerticalScroller = true;
        _resultScroll.DrawsBackground = false;
        _resultScroll.AutohidesScrollers = true;
        _resultScroll.Hidden = true;
        _searchInfo.Hidden = true;

        var root = new NSView();
        foreach (var v in new NSView[] { head, add, _search, _searchInfo, _treeScroll, _resultScroll })
        {
            v.TranslatesAutoresizingMaskIntoConstraints = false;
            root.AddSubview(v);
        }
        NSLayoutConstraint.ActivateConstraints([
            head.TopAnchor.ConstraintEqualTo(root.SafeAreaLayoutGuide.TopAnchor, 10),
            head.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor, 12),
            add.CenterYAnchor.ConstraintEqualTo(head.CenterYAnchor),
            add.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor, -10),
            _search.TopAnchor.ConstraintEqualTo(head.BottomAnchor, 8),
            _search.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor, 10),
            _search.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor, -10),
            _searchInfo.TopAnchor.ConstraintEqualTo(_search.BottomAnchor, 6),
            _searchInfo.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor, 12),
            _searchInfo.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor, -12),
            _treeScroll.TopAnchor.ConstraintEqualTo(_search.BottomAnchor, 8),
            _treeScroll.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor),
            _treeScroll.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor),
            _treeScroll.BottomAnchor.ConstraintEqualTo(root.BottomAnchor),
            _resultScroll.TopAnchor.ConstraintEqualTo(_searchInfo.BottomAnchor, 4),
            _resultScroll.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor),
            _resultScroll.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor),
            _resultScroll.BottomAnchor.ConstraintEqualTo(root.BottomAnchor),
        ]);
        View = root;
        Reload();
    }

    public void Reload()
    {
        _roots = [.. FolderScanner.Roots(Settings.LibraryPaths, Settings.HiddenRoots).Select(r => new FolderNode(r, null))];
        _outline.ReloadData();
        _index = null;
    }

    /// <summary>Nach dem Schreiben: Cover und Interpret dieses Ordners neu lesen.</summary>
    public void Refresh(string folder)
    {
        FolderLook.Forget(folder);
        _index = null;
        for (nint i = 0; i < _outline.RowCount; i++)
            if (_outline.ItemAtRow(i) is FolderNode n && Same(n.Entry.Path, folder))
                _outline.ReloadItem(n);
    }

    /// <summary>
    /// Den Ordner im Baum zeigen: die Vorfahren aufklappen und ihn wählen.
    /// Klappt nur, wenn er unter einer der Wurzeln liegt.
    /// </summary>
    public void Reveal(string path)
    {
        var root = _roots
            .Where(r => IsUnder(path, r.Entry.Path))
            .OrderByDescending(r => r.Entry.Path.Length)
            .FirstOrDefault();
        if (root is null) { _outline.DeselectAll(this); return; }

        var node = root;
        while (!Same(node.Entry.Path, path))
        {
            _outline.ExpandItem(node);
            var next = node.Children(Settings.OnlyAudioFolders).FirstOrDefault(c => IsUnder(path, c.Entry.Path));
            if (next is null) break;
            node = next;
        }

        var row = _outline.RowForItem(node);
        if (row < 0) return;
        _quiet = true;
        _outline.SelectRow(row, false);
        _outline.ScrollRowToVisible(row);
        _quiet = false;
    }

    private static bool Same(string a, string b) =>
        string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string root)
    {
        var r = root.TrimEnd('/') + "/";
        return Same(path, root) || path.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    private FolderNode? Clicked =>
        _outline.ClickedRow >= 0 ? _outline.ItemAtRow(_outline.ClickedRow) as FolderNode
        : _outline.SelectedRow >= 0 ? _outline.ItemAtRow(_outline.SelectedRow) as FolderNode
        : null;

    public void FocusSearch() => View.Window?.MakeFirstResponder(_search);

    // ── Suche ────────────────────────────────────────────────────

    /// <summary>Alles, worin gesucht wird: eigene Pfade plus die Systemwurzeln ohne Laufwerke.</summary>
    private static List<string> SearchRoots()
    {
        var roots = new List<string>(Settings.LibraryPaths);
        // Laufwerke bleiben draußen, sie abzusuchen dauert ewig.
        foreach (var entry in FolderScanner.Roots())
            if (!entry.Path.StartsWith("/Volumes/", StringComparison.Ordinal))
                roots.Add(entry.Path);
        return [.. roots.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private async void Search(string text)
    {
        _searching?.Cancel();
        var query = text.Trim();
        var on = query.Length >= 2;
        _treeScroll.Hidden = on;
        _resultScroll.Hidden = _searchInfo.Hidden = !on;
        if (!on) { _hits = []; _results.ReloadData(); return; }

        var cts = _searching = new CancellationTokenSource();
        try
        {
            // Nur so lange warten, dass zügiges Tippen nicht jeden Buchstaben einzeln durchreicht.
            await Task.Delay(120, cts.Token);
            if (_index is null) _searchInfo.StringValue = Strings.T("Reading the library once…");
            var roots = SearchRoots();
            var onlyAudio = Settings.OnlyAudioFolders;
            var index = await (_index ??= Task.Run(() => LibraryIndex.Build(roots, onlyAudio)));
            if (cts.IsCancellationRequested) return;

            _hits = LibrarySearch.Find(index, query);
            _results.ReloadData();
            var folders = _hits.Count(h => h.Kind == HitKind.Folder);
            _searchInfo.StringValue = _hits.Count == 0
                ? Strings.T("Nothing found for \"{0}\".", query)
                : Strings.T("{0} folders, {1} songs", folders, _hits.Count - folders)
                  + (_hits.Count >= LibrarySearch.DefaultLimit ? Strings.T(" (more available)") : "");
        }
        catch (OperationCanceledException) { }
    }

    private sealed class HitSource(LibraryController owner) : NSTableViewDataSource
    {
        public override nint GetRowCount(NSTableView tableView) => owner._hits.Count;
    }

    private sealed class HitDelegate(LibraryController owner) : NSTableViewDelegate
    {
        public override NSTableRowView CoreGetRowView(NSTableView tableView, nint row) => new SelectionRowView(bar: true);

        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
        {
            var hit = owner._hits[(int)row];
            var cell = tableView.MakeView("hit", this) as FolderCell ?? new FolderCell { Identifier = "hit" };
            cell.Show(hit.Name, hit.Context,
                NSImage.GetSystemSymbol(hit.Kind == HitKind.Folder ? "folder" : "music.note", null), 24);
            return cell;
        }

        public override void SelectionDidChange(NSNotification notification)
        {
            var row = owner._results.SelectedRow;
            if (row < 0 || row >= owner._hits.Count) return;
            var hit = owner._hits[(int)row];
            if (hit.Kind == HitKind.Folder) owner.FolderChosen?.Invoke(hit.Path);
            else if (Path.GetDirectoryName(hit.Path) is { } folder) owner.TrackChosen?.Invoke(folder, hit.Path);
        }
    }

    // ── Kontextmenü ──────────────────────────────────────────────

    private sealed class MenuDelegate(LibraryController owner) : NSMenuDelegate
    {
        public override void MenuWillHighlightItem(NSMenu menu, NSMenuItem item) { }

        public override void NeedsUpdate(NSMenu menu)
        {
            menu.RemoveAllItems();
            if (owner.Clicked is not { } node) return;

            menu.AddItem(new NSMenuItem(Strings.T("Open in a new tab"), (_, _) =>
                ((AppDelegate)NSApplication.SharedApplication.Delegate).NewWindowForTab(null, node.Entry.Path)));
            menu.AddItem(NSMenuItem.SeparatorItem);
            menu.AddItem(new NSMenuItem(Strings.T("Show in Finder"), (_, _) =>
                NSWorkspace.SharedWorkspace.ActivateFileViewer([NSUrl.FromFilename(node.Entry.Path)])));
            menu.AddItem(new NSMenuItem(Strings.T("Copy path"), (_, _) =>
            {
                NSPasteboard.GeneralPasteboard.ClearContents();
                NSPasteboard.GeneralPasteboard.SetStringForType(node.Entry.Path, NSPasteboard.NSPasteboardTypeString);
            }));

            menu.AddItem(NSMenuItem.SeparatorItem);
            if (node.Parent is null)
                menu.AddItem(new NSMenuItem(node.Entry.IsCustomRoot
                    ? Strings.T("Remove from the library")
                    : Strings.T("Hide from the library"), (_, _) => owner.RemoveRoot(node)));
            else
                menu.AddItem(new NSMenuItem(Strings.T("Add to the library"), (_, _) => owner.AddRoot(node.Entry.Path)));

            if (Settings.HiddenRoots.Count > 0)
            {
                menu.AddItem(NSMenuItem.SeparatorItem);
                menu.AddItem(new NSMenuItem(Strings.T("Show hidden again ({0})", Settings.HiddenRoots.Count), (_, _) =>
                {
                    Settings.HiddenRoots.Clear();
                    Settings.Save();
                    owner.Reload();
                }));
            }
        }
    }

    private void RemoveRoot(FolderNode node)
    {
        if (node.Entry.IsCustomRoot)
            Settings.LibraryPaths.RemoveAll(p => Same(p, node.Entry.Path));
        else if (!Settings.HiddenRoots.Any(p => Same(p, node.Entry.Path)))
            Settings.HiddenRoots.Add(node.Entry.Path);
        Settings.Save();
        Reload();
    }

    public void AddRoot(string path)
    {
        if (!Settings.LibraryPaths.Any(p => Same(p, path)))
        {
            Settings.LibraryPaths.Add(path);
            Settings.Save();
        }
        Reload();
        Reveal(path);
    }

    [Export("addLibraryFolder:")]
    public void AddLibraryFolder(NSObject sender)
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.CanChooseDirectories = true;
        panel.CanChooseFiles = false;
        panel.AllowsMultipleSelection = true;
        panel.Prompt = Strings.T("Add to the library");
        panel.BeginSheet(View.Window, result =>
        {
            if (result != (nint)(long)NSModalResponse.OK) return;
            foreach (var url in panel.Urls)
                if (url.Path is { } p) AddRoot(p);
        });
    }

    // ── Baum: Datenquelle und Zeilen ─────────────────────────────

    private sealed class Source(LibraryController owner) : NSOutlineViewDataSource
    {
        public override nint GetChildrenCount(NSOutlineView outlineView, NSObject? item) =>
            item is FolderNode n ? n.Children(Settings.OnlyAudioFolders).Count : owner._roots.Count;

        public override NSObject GetChild(NSOutlineView outlineView, nint childIndex, NSObject? item) =>
            item is FolderNode n ? n.Children(Settings.OnlyAudioFolders)[(int)childIndex] : owner._roots[(int)childIndex];

        public override bool ItemExpandable(NSOutlineView outlineView, NSObject item) =>
            item is FolderNode n && n.Expandable(Settings.OnlyAudioFolders);
    }

    private sealed class Delegate(LibraryController owner) : NSOutlineViewDelegate
    {
        public override NSTableRowView RowViewForItem(NSOutlineView outlineView, NSObject item) => new SelectionRowView(bar: true);

        public override NSView GetView(NSOutlineView outlineView, NSTableColumn? tableColumn, NSObject item)
        {
            var node = (FolderNode)item;
            var cell = outlineView.MakeView("folder", this) as FolderCell ?? new FolderCell { Identifier = "folder" };
            var path = node.Entry.Path;

            // Die Wurzeln sind Orte, keine Alben: dafür nur ihr Symbol.
            if (node.Parent is null)
            {
                cell.Show(node.Entry.Name, "", NSImage.GetSystemSymbol(Symbol(node), null), 32);
                return cell;
            }

            if (FolderLook.TryGet(path, out var look))
            {
                cell.Show(node.Entry.Name, look.Artist, look.Cover ?? NSImage.GetSystemSymbol("folder", null), 32,
                          isCover: look.Cover is not null);
            }
            else
            {
                cell.Show(node.Entry.Name, "", NSImage.GetSystemSymbol("folder", null), 32);
                FolderLook.Load(path, () =>
                {
                    var row = outlineView.RowForItem(node);
                    if (row >= 0) outlineView.ReloadData(NSIndexSet.FromIndex(row), NSIndexSet.FromIndex(0));
                });
            }
            return cell;
        }

        private static string Symbol(FolderNode node)
        {
            if (node.Entry.Path.StartsWith("/Volumes/", StringComparison.Ordinal)) return "externaldrive";
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (node.Entry.Path == Path.Combine(home, "Music")) return "music.note";
            if (node.Entry.Path == Path.Combine(home, "Downloads")) return "arrow.down.circle";
            if (node.Entry.Path == home) return "house";
            return "folder";
        }

        public override void SelectionDidChange(NSNotification notification)
        {
            if (owner._quiet) return;
            if (owner._outline.ItemAtRow(owner._outline.SelectedRow) is FolderNode n)
                owner.FolderChosen?.Invoke(n.Entry.Path);
        }
    }
}

/// <summary>Eine Zeile der Bibliothek: Bild, Name, darunter klein der Interpret.</summary>
internal sealed class FolderCell : NSTableCellView
{
    private readonly NSImageView _image = new() { ImageScaling = NSImageScale.ProportionallyUpOrDown, WantsLayer = true };
    private readonly NSTextField _name = NSTextField.CreateLabel("");
    private readonly NSTextField _artist = NSTextField.CreateLabel("");
    private readonly NSLayoutConstraint _size;
    private readonly NSStackView _text;

    public FolderCell()
    {
        _image.Layer!.CornerRadius = 4;
        _image.Layer.MasksToBounds = true;
        _name.LineBreakMode = _artist.LineBreakMode = NSLineBreakMode.TruncatingTail;
        _name.Font = NSFont.SystemFontOfSize(13);
        _artist.Font = NSFont.SystemFontOfSize(11);
        _artist.TextColor = NSColor.SecondaryLabel;
        _text = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Alignment = NSLayoutAttribute.Leading,
            Spacing = 1,
        }.Arranged(_name, _artist);

        foreach (var v in new NSView[] { _image, _text })
        {
            v.TranslatesAutoresizingMaskIntoConstraints = false;
            AddSubview(v);
        }
        _size = _image.WidthAnchor.ConstraintEqualTo(32);
        NSLayoutConstraint.ActivateConstraints([
            _image.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 2),
            _image.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            _size,
            _image.HeightAnchor.ConstraintEqualTo(_image.WidthAnchor),
            _text.LeadingAnchor.ConstraintEqualTo(_image.TrailingAnchor, 9),
            _text.TrailingAnchor.ConstraintLessThanOrEqualTo(TrailingAnchor, -4),
            _text.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            _name.WidthAnchor.ConstraintLessThanOrEqualTo(_text.WidthAnchor),
        ]);
        ImageView = _image;
        TextField = _name;
    }

    public void Show(string name, string artist, NSImage? image, float size, bool isCover = false)
    {
        _name.StringValue = name;
        _artist.StringValue = artist;
        _artist.Hidden = artist.Length == 0;
        _size.Constant = size;
        _image.Image = image;
        // Ein Symbol wird klein und grau gezeichnet, ein Cover füllt das Feld.
        _image.ImageScaling = isCover ? NSImageScale.ProportionallyUpOrDown : NSImageScale.ProportionallyDown;
        _image.ContentTintColor = isCover ? null : NSColor.SecondaryLabel;
        _image.SymbolConfiguration = NSImageSymbolConfiguration.Create(17, NSFontWeight.Regular);
        _image.Layer!.BackgroundColor = isCover ? null : NSColor.FromWhite(1, 0.05f).CGColor;
        ToolTip = artist.Length > 0 ? $"{name}\n{artist}" : name;
    }
}
