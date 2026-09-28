using ObjCRuntime;
using TagTuner.Core.Folders;
using TagTuner.Core.Metadata;
using TagTuner.Core.Model;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>
/// Das Hauptfenster: Bibliothek links, Trackliste in der Mitte, Metadaten
/// rechts. Die drei Teile kennen sich nicht, verbunden werden sie hier.
/// </summary>
public sealed partial class MainWindowController : NSWindowController
{
    private readonly NSSplitViewController _split = new();
    private readonly LibraryController _library = new();
    private readonly TrackListController _tracks = new();
    private readonly InspectorController _inspector = new();
    private readonly Player _player;
    private readonly ToolbarDelegate _toolbarDelegate;
    private NSSearchToolbarItem? _search;
    private NSTimer? _ticker;

    private static AppSettings Settings => AppDelegate.Settings;

    public MainWindowController() : base(NewWindow())
    {
        _player = new Player(Settings.Volume);

        var side = NSSplitViewItem.CreateSidebar(_library);
        side.MinimumThickness = 180;
        side.MaximumThickness = 360;
        side.PreferredThicknessFraction = 0.17f;
        var list = NSSplitViewItem.CreateContentList(_tracks);
        list.MinimumThickness = 360;
        var insp = NSSplitViewItem.CreateInspector(_inspector);
        insp.MinimumThickness = 270;
        insp.MaximumThickness = 440;
        insp.PreferredThicknessFraction = 0.22f;
        _split.AddSplitViewItem(side);
        _split.AddSplitViewItem(list);
        _split.AddSplitViewItem(insp);
        _split.SplitView.AutosaveName = "MainSplit";

        Window.ContentViewController = _split;
        Window.FrameAutosaveName = "MainWindow";
        if (!Window.SetFrameUsingName("MainWindow"))
        {
            Window.SetContentSize(new CGSize(1320, 780));
            Window.Center();
        }
        Window.WeakDelegate = this;

        _toolbarDelegate = new ToolbarDelegate(this);
        var toolbar = new NSToolbar("MainToolbar")
        {
            Delegate = _toolbarDelegate,
            DisplayMode = NSToolbarDisplayMode.Icon,
            AllowsUserCustomization = false,
        };
        Window.Toolbar = toolbar;
        Window.ToolbarStyle = NSWindowToolbarStyle.Unified;

        _library.FolderChosen += path => OpenFolder(path, fromLibrary: true);
        _tracks.SelectionChanged += () => _inspector.Show(_tracks.SelectedTracks);
        _tracks.PlayRequested += Play;
        _inspector.Writing += tracks => _resume = _player.Release(tracks.Select(t => t.Path));
        _inspector.Written += (paths, status) => AfterWrite(paths, status);
        _player.Changed += UpdatePlayer;
        _player.Finished += () => BeginInvokeOnMainThread(() => Step(+1, onlyIfPlaying: false));

        _ticker = NSTimer.CreateRepeatingScheduledTimer(0.5, _ => Tick());

        UpdateTitle();
    }

    /// <summary>Beim Start: den Ordner vom letzten Mal wieder öffnen.</summary>
    public void OpenLastFolder()
    {
        if (Settings.LastFolder is { } last && Directory.Exists(last)) OpenFolder(last);
    }

    private static NSWindow NewWindow()
    {
        var w = new NSWindow(new CGRect(0, 0, 1320, 780),
            NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable |
            NSWindowStyle.Resizable | NSWindowStyle.FullSizeContentView,
            NSBackingStore.Buffered, false)
        {
            Title = "TagTuner",
            TitlebarAppearsTransparent = false,
            TabbingMode = NSWindowTabbingMode.Disallowed,
        };
        w.MinSize = new CGSize(900, 480);
        return w;
    }

    private Action? _resume;

    // ── Ordner ───────────────────────────────────────────────────

    public void OpenFolder(string path) => OpenFolder(path, fromLibrary: false);

    private async void OpenFolder(string path, bool fromLibrary)
    {
        if (!Directory.Exists(path)) return;
        if (_search is not null) _search.SearchField.StringValue = "";
        _tracks.Filter("");
        Settings.LastFolder = path;
        Settings.Save();
        if (!fromLibrary) _library.Reveal(path);
        Window.RepresentedUrl = NSUrl.FromFilename(path);
        UpdateTitle();
        await _tracks.LoadAsync(path);
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        if (_tracks.Folder is not { } folder)
        {
            Window.Title = "TagTuner";
            Window.Subtitle = "";
            return;
        }
        Window.Title = Path.GetFileName(folder.TrimEnd('/')) is { Length: > 0 } n ? n : folder;
        var all = _tracks.Tracks;
        var total = TimeSpan.FromTicks(all.Sum(t => t.Duration.Ticks));
        var len = total.TotalHours >= 1 ? total.ToString(@"h\:mm\:ss") : total.ToString(@"m\:ss");
        Window.Subtitle = all.Count == 0 ? "" : $"{Strings.T("{0} files", all.Count)} · {len}";
    }

    private async void AfterWrite(IReadOnlyList<string> paths, string status)
    {
        FolderScanner.ForgetAudioScan();
        _tracks.ForgetArt(paths);
        if (_tracks.Folder is { } f)
            await _tracks.LoadAsync(f, [.. _tracks.SelectedTracks.Select(t => t.Path)]);
        _resume?.Invoke();
        _resume = null;
        UpdateTitle();
        Window.Subtitle = status;
    }

    public void SaveState()
    {
        Settings.Volume = _player.Volume;
        Settings.Save();
    }

    [Export("windowWillClose:")]
    public void WindowWillClose(NSNotification n)
    {
        _ticker?.Invalidate();
        _player.Stop();
        SaveState();
    }

    // ── Wiedergabe ───────────────────────────────────────────────

    private void Play(AudioTrack track)
    {
        var err = _player.Play(track);
        if (err is not null)
        {
            var a = new NSAlert
            {
                MessageText = Strings.T("{0} cannot be played ({1}).", track.FileName, err),
            };
            a.BeginSheet(Window);
        }
    }

    private void Step(int step, bool onlyIfPlaying = true)
    {
        var next = _tracks.Neighbour(_player.Track?.Path, step);
        if (next is null) { if (!onlyIfPlaying) _player.Stop(); return; }
        Play(next);
    }

    private void UpdatePlayer()
    {
        _tracks.PlayingPath = _player.Track?.Path;
        _tracks.RedrawRows();
        _toolbarDelegate.UpdatePlayer(_player);
    }

    private void Tick()
    {
        if (_player.Track is not null) _toolbarDelegate.UpdateProgress(_player);
    }

    // ── Menübefehle ──────────────────────────────────────────────

    [Export("openFolder:")]
    public void OpenFolderMenu(NSObject sender)
    {
        var panel = NSOpenPanel.OpenPanel;
        panel.CanChooseDirectories = true;
        panel.CanChooseFiles = false;
        panel.BeginSheet(Window, r =>
        {
            if (r == (nint)(long)NSModalResponse.OK && panel.Url?.Path is { } p) OpenFolder(p);
        });
    }

    [Export("addLibraryFolder:")]
    public void AddLibraryFolder(NSObject sender) => _library.AddLibraryFolder(sender);

    [Export("revealInFinder:")]
    public void RevealInFinder(NSObject sender)
    {
        var sel = _tracks.SelectedTracks;
        if (sel.Count > 0)
            NSWorkspace.SharedWorkspace.ActivateFileViewer([.. sel.Select(t => NSUrl.FromFilename(t.Path))]);
        else if (_tracks.Folder is { } f)
            NSWorkspace.SharedWorkspace.ActivateFileViewer([NSUrl.FromFilename(f)]);
    }

    [Export("reloadFolder:")]
    public async void ReloadFolder(NSObject sender)
    {
        if (_tracks.Folder is not { } f) return;
        _tracks.ForgetArt();
        _library.Reload();
        _library.Reveal(f);
        await _tracks.LoadAsync(f, [.. _tracks.SelectedTracks.Select(t => t.Path)]);
        UpdateTitle();
    }

    [Export("undo:")]
    public void Undo(NSObject sender)
    {
        var entry = AppDelegate.History.Entries.FirstOrDefault(e => e.CanUndo);
        if (entry is null) return;

        var paths = entry.Files.Select(f => f.Original).ToList();
        _resume = _player.Release(paths);
        try
        {
            var r = AppDelegate.History.Undo(entry.Id);
            var status = r.Failed == 0
                ? Strings.T("Undone: {0}", entry.Description)
                : Strings.T("{0} restored, {1} failed.", r.Restored, r.Failed);
            AfterWrite(paths, status);
        }
        catch (Exception ex)
        {
            new NSAlert { MessageText = Strings.T("Undo"), InformativeText = ex.Message }.BeginSheet(Window);
        }
    }

    [Export("applyChanges:")]
    public void ApplyChanges(NSObject sender) => _inspector.Apply();

    [Export("revertChanges:")]
    public void RevertChanges(NSObject sender) => _inspector.Revert();

    [Export("selectAll:")]
    public void SelectAllTracks(NSObject sender) => _tracks.SelectAllTracks();

    [Export("chooseCover:")]
    public void ChooseCover(NSObject sender) => _inspector.ChooseCover();

    [Export("pasteCover:")]
    public void PasteCover(NSObject sender) => _inspector.PasteCover();

    [Export("removeCover:")]
    public void RemoveCover(NSObject sender) => _inspector.RemoveCover();

    [Export("focusSearch:")]
    public void FocusSearch(NSObject sender)
    {
        if (_search is null) return;
        _search.BeginSearchInteraction();
    }

    [Export("playlistOrder:")]
    public void PlaylistOrder(NSObject sender) => _tracks.PlaylistOrder();

    [Export("playPause:")]
    public void PlayPause(NSObject sender)
    {
        if (_player.Track is not null) _player.Toggle();
        else if ((_tracks.SelectedTracks.FirstOrDefault() ?? _tracks.Shown.FirstOrDefault()) is { } t) Play(t);
    }

    [Export("nextTrack:")]
    public void NextTrack(NSObject sender) => Step(+1);

    [Export("previousTrack:")]
    public void PreviousTrack(NSObject sender)
    {
        // Wie überall: nach ein paar Sekunden erst an den Anfang, dann zurück.
        if (_player.Position > 3) _player.Seek(0);
        else Step(-1);
    }

    [Export("showHistory:")]
    public void ShowHistory(NSObject sender) => HistoryWindow.Show(Window, entry =>
    {
        var paths = entry.Files.Select(f => f.Original).ToList();
        _resume = _player.Release(paths);
        var r = AppDelegate.History.Undo(entry.Id);
        AfterWrite(paths, Strings.T("Undone: {0}", entry.Description));
        return r;
    });

    // ── Tags kopieren und einfügen ───────────────────────────────

    private static AudioTrack? _copied;

    [Export("copyTags:")]
    public void CopyTags(NSObject sender)
    {
        if (_tracks.SelectedTracks is not [var t]) return;
        _copied = t;
        Window.Subtitle = Strings.T("Tags copied from \"{0}\"", t.FileName);
    }

    [Export("pasteTags:")]
    public void PasteTags(NSObject sender)
    {
        if (_copied is not { } src) return;
        var targets = _tracks.SelectedTracks.Where(t => t.Path != src.Path).ToList();
        if (targets.Count == 0) return;

        // „format" lässt Titel und Nummer stehen: die sind je Datei verschieden.
        var all = Settings.TagPasteMode == "all";
        var cover = Core.Audio.AudioProbe.ReadCover(src.Path);
        TagEdit For(AudioTrack t) => new()
        {
            Title = all ? src.Title : null,
            Track = all ? src.Track : null,
            Artist = src.Artist,
            Album = src.Album,
            AlbumArtist = src.AlbumArtist,
            Genre = src.Genre,
            Composer = src.Composer,
            Comment = src.Comment,
            Year = src.Year,
            Disc = src.Disc,
            Cover = cover is not null && Core.Audio.AudioFormats.CanCarryCover(t.Path) ? cover.Data : null,
            CoverMimeType = cover?.MimeType,
        };
        _inspector.Write(targets, For, Strings.T("From \"{0}\": {1}", src.FileName, Strings.T("Paste tags")));
    }

    [Export("validateMenuItem:")]
    public bool ValidateMenuItem(NSMenuItem item)
    {
        var sel = _tracks.SelectedTracks;
        return item.Action?.Name switch
        {
            "undo:" => AppDelegate.History.Entries.Any(e => e.CanUndo),
            "applyChanges:" or "revertChanges:" => _inspector.HasChanges,
            "copyTags:" => sel.Count == 1,
            "pasteTags:" => _copied is not null && sel.Count > 0,
            "chooseCover:" or "pasteCover:" or "removeCover:" => sel.Count > 0,
            "revealInFinder:" or "reloadFolder:" => _tracks.Folder is not null,
            "playPause:" => _player.Track is not null || _tracks.Shown.Count > 0,
            "nextTrack:" or "previousTrack:" => _player.Track is not null,
            _ => true,
        };
    }

    // ── Symbolleiste ─────────────────────────────────────────────

    private sealed class ToolbarDelegate(MainWindowController owner) : NSToolbarDelegate
    {
        private const string PlayerId = "player";
        private const string SearchId = "search";

        private NSButton? _play;
        private NSTextField? _now;
        private NSSlider? _progress;
        private NSTextField? _time;
        private bool _dragging;

        public override string[] AllowedItemIdentifiers(NSToolbar toolbar) => DefaultItemIdentifiers(toolbar);

        public override string[] DefaultItemIdentifiers(NSToolbar toolbar) =>
        [
            NSToolbar.NSToolbarToggleSidebarItemIdentifier,
            NSToolbar.NSToolbarSidebarTrackingSeparatorItemIdentifier,
            NSToolbar.NSToolbarFlexibleSpaceItemIdentifier,
            PlayerId,
            NSToolbar.NSToolbarFlexibleSpaceItemIdentifier,
            SearchId,
            NSToolbar.NSToolbarInspectorTrackingSeparatorItemIdentifier,
            NSToolbar.NSToolbarFlexibleSpaceItemIdentifier,
            NSToolbar.NSToolbarToggleInspectorItemIdentifier,
        ];

        public override NSToolbarItem WillInsertItem(NSToolbar toolbar, string itemIdentifier, bool willBeInserted)
        {
            if (itemIdentifier == SearchId)
            {
                var s = new NSSearchToolbarItem(SearchId);
                s.SearchField.PlaceholderString = Strings.T("Find in this folder…").TrimEnd('…');
                s.SearchField.Changed += (_, _) => owner._tracks.Filter(s.SearchField.StringValue);
                s.PreferredWidthForSearchField = 220;
                owner._search = s;
                return s;
            }

            if (itemIdentifier == PlayerId)
                return new NSToolbarItem(PlayerId) { View = BuildPlayer(), Label = Strings.T("Play") };

            return new NSToolbarItem(itemIdentifier);
        }

        private NSView BuildPlayer()
        {
            NSButton Btn(string symbol, string selector)
            {
                var b = NSButton.CreateButton(NSImage.GetSystemSymbol(symbol, null)!, () => { });
                b.Target = owner;
                b.Action = new Selector(selector);
                b.Bordered = false;
                b.SymbolConfiguration = NSImageSymbolConfiguration.Create(15, NSFontWeight.Medium);
                return b;
            }

            var prev = Btn("backward.fill", "previousTrack:");
            _play = Btn("play.fill", "playPause:");
            _play.SymbolConfiguration = NSImageSymbolConfiguration.Create(19, NSFontWeight.Medium);
            var next = Btn("forward.fill", "nextTrack:");

            _now = NSTextField.CreateLabel("");
            _now.Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize, NSFontWeight.Medium);
            _now.LineBreakMode = NSLineBreakMode.TruncatingTail;
            _now.Alignment = NSTextAlignment.Center;

            _time = NSTextField.CreateLabel("");
            _time.Font = NSFont.MonospacedDigitSystemFontOfSize(NSFont.SmallSystemFontSize - 1, NSFontWeight.Regular);
            _time.TextColor = NSColor.SecondaryLabel;

            _progress = new NSSlider
            {
                MinValue = 0, MaxValue = 1, DoubleValue = 0, ControlSize = NSControlSize.Mini, Enabled = false,
            };
            _progress.Activated += (_, _) =>
            {
                owner._player.Seek(_progress.DoubleValue * owner._player.Duration);
                _dragging = false;
            };

            var bar = new NSStackView
        { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Spacing = 6 }.Arranged(_progress, _time);
            var right = new NSStackView
        {
                Orientation = NSUserInterfaceLayoutOrientation.Vertical,
                Spacing = 0,
                Alignment = NSLayoutAttribute.CenterX,
            }.Arranged(_now, bar);
            bar.WidthAnchor.ConstraintEqualTo(right.WidthAnchor).Active = true;
            _now.WidthAnchor.ConstraintLessThanOrEqualTo(right.WidthAnchor).Active = true;

            var vol = NSImageView.FromImage(NSImage.GetSystemSymbol("speaker.wave.2.fill", null)!);
            vol.ContentTintColor = NSColor.SecondaryLabel;
            var volume = new NSSlider { MinValue = 0, MaxValue = 1, DoubleValue = owner._player.Volume, ControlSize = NSControlSize.Mini };
            volume.Activated += (_, _) => owner._player.Volume = (float)volume.DoubleValue;
            volume.WidthAnchor.ConstraintEqualTo(70).Active = true;

            var stack = new NSStackView
        {
                Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
                Spacing = 10,
            }.Arranged(prev, _play, next, right, vol, volume);
            stack.SetCustomSpacing(16, next);
            stack.SetCustomSpacing(14, right);
            stack.SetCustomSpacing(4, vol);
            right.WidthAnchor.ConstraintEqualTo(260).Active = true;
            UpdatePlayer(owner._player);
            return stack;
        }

        public void UpdatePlayer(Player p)
        {
            if (_play is null || _now is null || _progress is null) return;
            _play.Image = NSImage.GetSystemSymbol(p.IsPlaying ? "pause.fill" : "play.fill", null);
            _now.StringValue = p.Track is { } t
                ? (string.IsNullOrWhiteSpace(t.Title) ? Path.GetFileNameWithoutExtension(t.FileName) : t.Title)
                  + (string.IsNullOrWhiteSpace(t.Artist) ? "" : " – " + t.Artist)
                : "TagTuner";
            _now.TextColor = p.Track is null ? NSColor.TertiaryLabel : NSColor.Label;
            _progress.Enabled = p.Track is not null;
            UpdateProgress(p);
        }

        public void UpdateProgress(Player p)
        {
            if (_progress is null || _time is null || _dragging) return;
            _progress.DoubleValue = p.Duration > 0 ? p.Position / p.Duration : 0;
            _time.StringValue = p.Track is null ? "" : $"{Fmt(p.Position)} / {Fmt(p.Duration)}";
            static string Fmt(double s) => TimeSpan.FromSeconds(s).ToString(@"m\:ss");
        }
    }
}
