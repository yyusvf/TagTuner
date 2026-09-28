using ObjCRuntime;
using TagTuner.Core.Folders;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>Ein Ordner im Baum. Die Kinder werden erst beim Aufklappen gelesen.</summary>
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

/// <summary>
/// Die Seitenleiste: eigene Ordner, Musik, Downloads und die Laufwerke.
/// Ein Klick öffnet den Ordner als Playlist.
/// </summary>
internal sealed class LibraryController : NSViewController
{
    public event Action<string>? FolderChosen;

    private readonly NSOutlineView _outline = new();
    private List<FolderNode> _roots = [];
    private bool _quiet;

    private static AppSettings Settings => AppDelegate.Settings;

    public override void LoadView()
    {
        _outline.Style = NSTableViewStyle.SourceList;
        _outline.HeaderView = null;
        _outline.FloatsGroupRows = false;
        _outline.RowSizeStyle = NSTableViewRowSizeStyle.Default;
        _outline.AutosaveExpandedItems = false;
        _outline.IndentationPerLevel = 13;

        var column = new NSTableColumn("name") { Editable = false };
        _outline.AddColumn(column);
        _outline.OutlineTableColumn = column;
        _outline.Delegate = new Delegate(this);
        _outline.DataSource = new Source(this);
        _outline.Menu = BuildMenu();

        var scroll = new NSScrollView
        {
            DocumentView = _outline,
            HasVerticalScroller = true,
            DrawsBackground = false,
            AutohidesScrollers = true,
        };

        // Unten eine kleine Leiste zum Hinzufügen, wie in Finder und Mail.
        var add = NSButton.CreateButton(NSImage.GetSystemSymbol("plus", null)!, () => AddLibraryFolder(this));
        add.Bordered = false;
        add.ToolTip = Strings.T("Add folder…");

        var root = new NSView();
        foreach (var v in new NSView[] { scroll, add })
        {
            v.TranslatesAutoresizingMaskIntoConstraints = false;
            root.AddSubview(v);
        }
        NSLayoutConstraint.ActivateConstraints([
            scroll.TopAnchor.ConstraintEqualTo(root.TopAnchor),
            scroll.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor),
            scroll.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor),
            scroll.BottomAnchor.ConstraintEqualTo(add.TopAnchor, -4),
            add.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor, 14),
            add.BottomAnchor.ConstraintEqualTo(root.BottomAnchor, -10),
            add.WidthAnchor.ConstraintEqualTo(22),
            add.HeightAnchor.ConstraintEqualTo(22),
        ]);
        View = root;
        Reload();
    }

    public void Reload()
    {
        _roots = [.. FolderScanner.Roots(Settings.LibraryPaths, Settings.HiddenRoots).Select(r => new FolderNode(r, null))];
        _outline.ReloadData();
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

    // ── Kontextmenü ──────────────────────────────────────────────

    private NSMenu BuildMenu()
    {
        var menu = new NSMenu { AutoEnablesItems = false };
        menu.Delegate = new MenuDelegate(this);
        return menu;
    }

    private sealed class MenuDelegate(LibraryController owner) : NSMenuDelegate
    {
        public override void MenuWillHighlightItem(NSMenu menu, NSMenuItem item) { }

        public override void NeedsUpdate(NSMenu menu)
        {
            menu.RemoveAllItems();
            if (owner.Clicked is not { } node) return;

            menu.AddItem(new NSMenuItem(Strings.T("Show in Finder"), (_, _) =>
                NSWorkspace.SharedWorkspace.ActivateFileViewer([NSUrl.FromFilename(node.Entry.Path)])));
            menu.AddItem(new NSMenuItem(Strings.T("Copy path"), (_, _) =>
            {
                NSPasteboard.GeneralPasteboard.ClearContents();
                NSPasteboard.GeneralPasteboard.SetStringForType(node.Entry.Path, NSPasteboard.NSPasteboardTypeString);
            }));

            if (node.Parent is null)
            {
                menu.AddItem(NSMenuItem.SeparatorItem);
                menu.AddItem(new NSMenuItem(node.Entry.IsCustomRoot
                    ? Strings.T("Remove from the library")
                    : Strings.T("Hide from the library"), (_, _) => owner.RemoveRoot(node)));
            }
            else
            {
                menu.AddItem(NSMenuItem.SeparatorItem);
                menu.AddItem(new NSMenuItem(Strings.T("Add to the library"), (_, _) => owner.AddRoot(node.Entry.Path)));
            }

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

    // ── Datenquelle und Darstellung ──────────────────────────────

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
        public override NSView GetView(NSOutlineView outlineView, NSTableColumn? tableColumn, NSObject item)
        {
            var node = (FolderNode)item;
            var cell = outlineView.MakeView("folder", this) as NSTableCellView ?? NewCell();
            cell.TextField!.StringValue = node.Entry.Name;
            cell.ImageView!.Image = NSImage.GetSystemSymbol(Symbol(node), null);
            return cell;
        }

        private static string Symbol(FolderNode node)
        {
            if (node.Parent is not null) return "folder";
            if (node.Entry.Path.StartsWith("/Volumes/", StringComparison.Ordinal)) return "externaldrive";
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (node.Entry.Path == Path.Combine(home, "Music")) return "music.note";
            if (node.Entry.Path == Path.Combine(home, "Downloads")) return "arrow.down.circle";
            if (node.Entry.Path == home) return "house";
            return "folder.badge.person.crop";
        }

        private static NSTableCellView NewCell()
        {
            var cell = new NSTableCellView { Identifier = "folder" };
            var image = new NSImageView { TranslatesAutoresizingMaskIntoConstraints = false };
            var text = NSTextField.CreateLabel("");
            text.TranslatesAutoresizingMaskIntoConstraints = false;
            text.LineBreakMode = NSLineBreakMode.TruncatingTail;
            cell.AddSubview(image);
            cell.AddSubview(text);
            cell.ImageView = image;
            cell.TextField = text;
            NSLayoutConstraint.ActivateConstraints([
                image.LeadingAnchor.ConstraintEqualTo(cell.LeadingAnchor, 2),
                image.CenterYAnchor.ConstraintEqualTo(cell.CenterYAnchor),
                image.WidthAnchor.ConstraintEqualTo(18),
                text.LeadingAnchor.ConstraintEqualTo(image.TrailingAnchor, 6),
                text.TrailingAnchor.ConstraintEqualTo(cell.TrailingAnchor, -2),
                text.CenterYAnchor.ConstraintEqualTo(cell.CenterYAnchor),
            ]);
            return cell;
        }

        public override void SelectionDidChange(NSNotification notification)
        {
            if (owner._quiet) return;
            if (owner._outline.ItemAtRow(owner._outline.SelectedRow) is FolderNode n)
                owner.FolderChosen?.Invoke(n.Entry.Path);
        }
    }
}
