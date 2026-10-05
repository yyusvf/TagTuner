using ObjCRuntime;
using TagTuner.Core.Folders;
using TagTuner.Core.Metadata;
using TagTuner.Core.Model;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>
/// Das Hauptfenster, aufgebaut wie unter Windows: Metadaten links, daneben
/// die Bibliothek, in der Mitte die Trackliste, rechts die Ordner-Analyse,
/// unten der Player mit der Statuszeile. Oben Zurück, Vor und der Pfad.
/// Die Teile kennen sich nicht, verbunden werden sie hier.
/// </summary>
public sealed partial class MainWindowController : NSWindowController
{
    private readonly NSSplitViewController _split = new();
    private readonly InspectorController _inspector = new();
    private readonly LibraryController _library = new();
    // ── Geteilte Ansicht, wie unter Windows: zwei Hälften übereinander ──
    private readonly TrackListController _paneA = new();
    private TrackListController? _paneB;
    private readonly NSSplitViewController _center = new();

    /// <summary>
    /// Die Hälfte, in der zuletzt gearbeitet wurde. Metadaten, Ordner-Analyse,
    /// Menübefehle und die Bibliothek beziehen sich auf sie.
    /// </summary>
    private TrackListController _tracks;
    private readonly FolderPanel _panel = new();
    private readonly PlayerBar _bar = new();
    private static Player _player => AppDelegate.Player;
    private readonly ToolbarDelegate _toolbarDelegate;
    private NSSearchToolbarItem? _search;
    private NSPathControl? _pathControl;
    private NSTimer? _ticker;

    /// <summary>Zurück und Vor, wie im Explorer: die zuletzt offenen Ordner.</summary>
    private readonly Stack<string> _back = new(), _forward = new();

    private static AppSettings Settings => AppDelegate.Settings;

    public MainWindowController(bool first = true) : base(NewWindow())
    {
        var meta = NSSplitViewItem.CreateSidebar(_inspector);
        meta.MinimumThickness = 250;
        meta.MaximumThickness = 380;
        meta.PreferredThicknessFraction = 0.17f;
        meta.CanCollapse = true;
        var lib = NSSplitViewItem.FromViewController(_library);
        lib.MinimumThickness = 200;
        lib.MaximumThickness = 360;
        lib.PreferredThicknessFraction = 0.15f;
        lib.HoldingPriority = 260;
        _tracks = _paneA;
        _center.SplitView.IsVertical = false;
        _center.SplitView.DividerStyle = NSSplitViewDividerStyle.Thin;
        var top = NSSplitViewItem.FromViewController(_paneA);
        top.MinimumThickness = 160;
        _center.AddSplitViewItem(top);
        var list = NSSplitViewItem.CreateContentList(_center);
        list.MinimumThickness = 380;
        list.HoldingPriority = 200;
        var panel = NSSplitViewItem.CreateInspector(_panel);
        // Ein Inspektor, kein zweiter Inhalt: schmal wie in Apples eigenen Apps.
        panel.MinimumThickness = 220;
        panel.MaximumThickness = 290;
        panel.PreferredThicknessFraction = 0.15f;
        _split.AddSplitViewItem(meta);
        _split.AddSplitViewItem(lib);
        _split.AddSplitViewItem(list);
        _split.AddSplitViewItem(panel);
        _split.SplitView.AutosaveName = "MainSplit6";

        Window.ContentViewController = new RootController(_split, _bar);
        if (first) Window.FrameAutosaveName = "MainWindow";
        if (!first || !Window.SetFrameUsingName("MainWindow"))
        {
            Window.SetContentSize(new CGSize(1480, 860));
            Window.Center();
        }
        Window.WeakDelegate = this;

        _toolbarDelegate = new ToolbarDelegate(this);
        Window.Toolbar = new NSToolbar("MainToolbar4")
        {
            Delegate = _toolbarDelegate,
            DisplayMode = NSToolbarDisplayMode.Icon,
            AllowsUserCustomization = false,
        };
        Window.ToolbarStyle = NSWindowToolbarStyle.Unified;
        Window.TitleVisibility = NSWindowTitleVisibility.Hidden;

        _library.FolderChosen += path => OpenFolder(path, fromLibrary: true);
        _library.TrackChosen += async (folder, track) =>
        {
            OpenFolder(folder, fromLibrary: true);
            await Task.Delay(50);
            while (_tracks.Folder == folder && _tracks.Tracks.Count == 0) await Task.Delay(50);
            _tracks.Select([track]);
        };
        Wire(_paneA);
        _panel.AlbumRequested += () => ApplyAlbumMode(Window);
        _panel.RenameRequested += () => RenameFiles(Window);
        _panel.CoverAllRequested += () => CoverForAll(Window);
        _panel.AlignRequested += (outliers, target) => _ = WriteAsync(
            [.. outliers.Select(t => new Job(t, new TagEdit(), target.Format, target.SampleRate))],
            "align", Strings.T("Align folder"));
        _inspector.ApplyRequested += (jobs, label) => _ = WriteAsync(jobs, "batch", label);
        _inspector.FolderSource = () => (_tracks.Folder, _tracks.Tracks);
        _bar.Owner = this;
        _player.Changed += UpdatePlayer;
        _player.Finished += OnFinished;
        AppDelegate.History.Added += OnHistoryAdded;
        BackupsWindow.Restored += OnRestored;
        SettingsWindow.Changed += OnSettingsChanged;

        _ticker = NSTimer.CreateRepeatingScheduledTimer(0.5, _ => Tick());
        UpdatePlayer();
        UpdateTitle();
    }

    public string? Folder => _tracks.Folder;

    /// <summary>Die Ereignisse einer Hälfte. Wer etwas in ihr tut, macht sie zur aktiven.</summary>
    private void Wire(TrackListController pane)
    {
        pane.SelectionChanged += () =>
        {
            if (pane.SelectedTracks.Count > 0) Activate(pane);
            if (pane == _tracks) _inspector.Show(pane.SelectedTracks);
        };
        pane.PlayRequested += t => { Activate(pane); Play(t); };
        pane.Reordered += (order, discs) => { Activate(pane); OnReordered(order, discs); };
        pane.FilesDropped += (paths, at, move) => { Activate(pane); OnFilesDropped(paths, at, move); };
        pane.Loaded += () => { if (pane == _tracks) _panel.Show(pane.Folder, pane.Tracks); };
        pane.Clicked += () => Activate(pane);
    }

    private void Activate(TrackListController pane)
    {
        if (_tracks == pane) return;
        _tracks = pane;
        _paneA.Active = pane == _paneA && _paneB is not null;
        if (_paneB is not null) _paneB.Active = pane == _paneB;
        _inspector.Show(pane.SelectedTracks);
        _panel.Show(pane.Folder, pane.Tracks);
        if (pane.Folder is { } f) _library.Reveal(f);
        UpdateTitle();
    }

    /// <summary>Geteilte Ansicht an oder aus. Die untere Hälfte öffnet zunächst denselben Ordner.</summary>
    [Export("toggleSplit:")]
    public void ToggleSplit(NSObject sender)
    {
        if (_paneB is { } b)
        {
            Activate(_paneA);
            _center.RemoveSplitViewItem(_center.SplitViewItems[1]);
            _paneB = null;
            _paneA.Active = false;
        }
        else
        {
            var pane = _paneB = new TrackListController();
            Wire(pane);
            var item = NSSplitViewItem.FromViewController(pane);
            item.MinimumThickness = 160;
            _center.AddSplitViewItem(item);
            // Hälfte-hälfte, sobald die neue Liste ihre Größe kennt.
            BeginInvokeOnMainThread(() =>
                _center.SplitView.SetPositionOfDivider(_center.SplitView.Bounds.Height / 2, 0));
            if (_paneA.Folder is { } f) _ = pane.LoadAsync(f);
            Activate(pane);
        }
        _toolbarDelegate.UpdateSplit(_paneB is not null);
    }

    /// <summary>Nach dem Schreiben: auch die andere Hälfte neu lesen, falls sie betroffen sein kann.</summary>
    private async void ReloadOther()
    {
        var other = _tracks == _paneA ? _paneB : _paneA;
        if (other?.Folder is { } f) await other.LoadAsync(f, [.. other.SelectedTracks.Select(t => t.Path)]);
    }

    /// <summary>Beim Start: den Ordner vom letzten Mal wieder öffnen.</summary>
    public void OpenLastFolder()
    {
        if (Settings.LastFolder is { } last && Directory.Exists(last)) OpenFolder(last);
    }

    private static NSWindow NewWindow()
    {
        var w = new NSWindow(new CGRect(0, 0, 1480, 860),
            NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable |
            NSWindowStyle.Resizable | NSWindowStyle.FullSizeContentView,
            NSBackingStore.Buffered, false)
        {
            Title = "TagTuner",
            TabbingMode = NSWindowTabbingMode.Preferred,
            TabbingIdentifier = "TagTuner",
        };
        w.MinSize = new CGSize(1100, 560);
        return w;
    }

    private Action? _resume;

    /// <summary>Lieder, die nach dem nächsten Neulesen aufleuchten sollen.</summary>
    private List<string>? _glowAfterWrite;

    /// <summary>Meldung unten in der Statuszeile.</summary>
    private void Status(string text) => _bar.Status(text);

    // ── Ordner ───────────────────────────────────────────────────

    public void OpenFolder(string path) => OpenFolder(path, fromLibrary: false);

    /// <summary>Den Ordner öffnen und darin bestimmte Dateien wählen, etwa aus dem Finder.</summary>
    public async void OpenFolder(string path, IReadOnlyList<string> select)
    {
        OpenFolder(path, fromLibrary: false);
        for (var i = 0; i < 100 && (_tracks.Folder != path || _tracks.Tracks.Count == 0); i++) await Task.Delay(50);
        _tracks.Select(select);
    }

    private void OpenFolder(string path, bool fromLibrary) => Navigate(path, fromLibrary, remember: true);

    private async void Navigate(string path, bool fromLibrary, bool remember)
    {
        if (!Directory.Exists(path)) return;
        if (remember && _tracks.Folder is { } previous && previous != path)
        {
            _back.Push(previous);
            _forward.Clear();
        }
        if (_search is not null) _search.SearchField.StringValue = "";
        _tracks.Filter("");
        Settings.LastFolder = path;
        Settings.Save();
        if (!fromLibrary) _library.Reveal(path);
        UpdateTitle();
        await _tracks.LoadAsync(path);
        UpdateTitle();
    }

    [Export("goBack:")]
    public void GoBack(NSObject sender)
    {
        if (!_back.TryPop(out var path)) return;
        if (_tracks.Folder is { } now) _forward.Push(now);
        Navigate(path, fromLibrary: false, remember: false);
    }

    [Export("goForward:")]
    public void GoForward(NSObject sender)
    {
        if (!_forward.TryPop(out var path)) return;
        if (_tracks.Folder is { } now) _back.Push(now);
        Navigate(path, fromLibrary: false, remember: false);
    }

    private void UpdateTitle()
    {
        var folder = _tracks.Folder;
        _toolbarDelegate.UpdateNavigation(_back.Count > 0, _forward.Count > 0);
        // Der Titel bleibt verborgen, er zählt aber für die Tab-Leiste und das Fenster-Menü.
        Window.Title = folder is null ? "TagTuner"
            : Path.GetFileName(folder.TrimEnd('/')) is { Length: > 0 } n ? n : folder;
        if (folder is null) return;
        Window.RepresentedUrl = NSUrl.FromFilename(folder);
        if (_pathControl is not null) _pathControl.Url = NSUrl.FromFilename(folder);
    }

    /// <summary>
    /// Schreibt, lädt den Ordner neu und behält die Auswahl. Der Player lässt
    /// betroffene Dateien vorher los und macht danach an derselben Stelle weiter.
    /// </summary>
    /// <param name="renameAfter">Danach die Dateinamen den Nummern nachziehen, wenn der Ordner das will.</param>
    private async Task WriteAsync(IReadOnlyList<Job> jobs, string kind, string label, bool renameAfter = false)
    {
        if (_inspector.Busy || jobs.Count == 0) return;
        if (Batch.MissingFfmpeg(jobs))
        {
            new NSAlert
            {
                MessageText = Strings.T("ffmpeg is missing"),
                InformativeText = Strings.T("Converting needs ffmpeg. Install it with Homebrew: brew install ffmpeg"),
            }.BeginSheet(Window);
            return;
        }

        _inspector.Busy = true;
        var paths = jobs.Select(j => j.Track.Path).ToList();
        _resume = _player.Release(paths);

        var converting = jobs.Any(j => j.Converts);
        var doing = converting
            ? Strings.T("Converting {0} file(s)", jobs.Count)
            : Strings.T("Writing tags to {0} file(s)", jobs.Count);
        Status(doing + "…");
        _bar.Progress(0);

        var r = await Batch.RunAsync(jobs, kind, label, (i, pct) => BeginInvokeOnMainThread(() =>
        {
            Status($"{doing} · {Strings.T("{0} of {1}", i + 1, jobs.Count)}" + (converting ? $" · {pct} %" : ""));
            _bar.Progress((i * 100 + pct) / (double)jobs.Count);
        }));
        _bar.Progress(null);
        var errors = r.Errors;
        var status = Strings.T("{0} file(s) processed, backup created", r.Written);

        // Umgewandelte Dateien haben eine neue Endung: Die Auswahl zieht mit.
        var moved = r.Files.Where(f => f.OutputPath is not null)
                           .ToDictionary(f => f.Original, f => f.OutputPath!, StringComparer.OrdinalIgnoreCase);

        if (renameAfter && _tracks.Folder is { } folder)
        {
            // Mit den frisch geschriebenen Nummern, nicht mit denen von vorher.
            var fresh = await Task.Run(() => FolderScanner.Tracks(folder).ToList());
            _resume ??= _player.Release(fresh.Select(t => t.Path));
            var renamed = await Batch.RenameToNumbersAsync(folder, fresh);
            errors.AddRange(renamed.Errors);
            foreach (var f in renamed.Files)
            {
                var from = moved.FirstOrDefault(kv => kv.Value == f.Original).Key ?? f.Original;
                moved[from] = f.OutputPath!;
            }
            if (renamed.Written > 0)
                status += " · " + Strings.T("{0} file name(s) numbered in \"{1}\"", renamed.Written, Path.GetFileName(folder));
        }

        _inspector.Busy = false;
        if (errors.Count > 0 || r.Notes.Count > 0)
            new NSAlert
            {
                MessageText = errors.Count > 0 ? Strings.T("Finished with errors") : Strings.T("Finished"),
                InformativeText = string.Join("\n", errors.Concat(r.Notes).Take(10)),
            }.BeginSheet(Window);
        AfterWrite(paths, status, moved);
    }

    private async void AfterWrite(IReadOnlyList<string> paths, string status,
                                  IReadOnlyDictionary<string, string>? moved = null)
    {
        FolderScanner.ForgetAudioScan();
        _tracks.ForgetArt(paths);
        var keep = _tracks.SelectedTracks
            .Select(t => moved is not null && moved.TryGetValue(t.Path, out var to) ? to : t.Path).ToList();
        if (_tracks.Folder is { } f)
        {
            _library.Refresh(f);
            await _tracks.LoadAsync(f, keep);
            if (_glowAfterWrite is { } glow)
            {
                _glowAfterWrite = null;
                _tracks.Glow(glow.Select(p => moved is not null && moved.TryGetValue(p, out var to) ? to : p));
            }
        }
        ReloadOther();
        _resume?.Invoke();
        _resume = null;
        UpdateTitle();
        Status(status);
    }

    /// <summary>Nach jedem Vorgang bietet die Statuszeile „Rückgängig" an, wie unter Windows.</summary>
    private void OnHistoryAdded(Core.Safety.HistoryEntry entry) =>
        BeginInvokeOnMainThread(() => _bar.OfferUndo(entry.CanUndo));

    /// <summary>Aus der Sicherungen-Seite wiederhergestellt: betrifft es diesen Ordner, neu lesen.</summary>
    private void OnRestored(IReadOnlyList<string> paths)
    {
        if (_tracks.Folder is { } f && paths.Any(p => string.Equals(Path.GetDirectoryName(p), f.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
            AfterWrite(paths, Strings.T("{0} backup(s) restored", paths.Count));
    }

    /// <summary>Was die Einstellungen geändert haben, nur an der Stelle nachziehen, die es angeht.</summary>
    private void OnSettingsChanged(string what)
    {
        var panes = new[] { _paneA, _paneB }.OfType<TrackListController>().ToList();
        switch (what)
        {
            case "library":
                Core.Folders.FolderScanner.ForgetAudioScan();
                _library.Reload();
                if (_tracks.Folder is { } f) _library.Reveal(f);
                break;
            case "columns":
                foreach (var p in panes) { p.RebuildColumns(); p.Refresh(); }
                break;
            case "sorting":
                foreach (var p in panes) p.Refresh();
                break;
            case "rules":
                _panel.Show(_tracks.Folder, _tracks.Tracks);
                foreach (var p in panes) p.Refresh();
                break;
        }
    }

    public void SaveState()
    {
        Settings.Volume = _player.Volume;
        Settings.Save();
    }

    public event Action<MainWindowController>? Closed;

    [Export("windowWillClose:")]
    public void WindowWillClose(NSNotification n)
    {
        _ticker?.Invalidate();
        _player.Changed -= UpdatePlayer;
        _player.Finished -= OnFinished;
        AppDelegate.History.Added -= OnHistoryAdded;
        BackupsWindow.Restored -= OnRestored;
        SettingsWindow.Changed -= OnSettingsChanged;
        if (_player.Owner == this) _player.Stop();
        SaveState();
        Closed?.Invoke(this);
    }

    /// <summary>Am Ende eines Lieds weiter, aber nur im Fenster, aus dem es kam.</summary>
    private void OnFinished()
    {
        if (_player.Owner == this) BeginInvokeOnMainThread(() => Step(+1, onlyIfPlaying: false));
    }

    // ── Wiedergabe ───────────────────────────────────────────────

    private void Play(AudioTrack track)
    {
        var err = _player.Play(track, this);
        if (err is not null) Status(Strings.T("{0} cannot be played ({1}).", track.FileName, err));
    }

    private void Step(int step, bool onlyIfPlaying = true)
    {
        var next = _tracks.Neighbour(_player.Track?.Path, step)
                   ?? (_tracks == _paneA ? _paneB : _paneA)?.Neighbour(_player.Track?.Path, step);
        if (next is null) { if (!onlyIfPlaying) _player.Stop(); return; }
        Play(next);
    }

    private void UpdatePlayer()
    {
        foreach (var pane in new[] { _paneA, _paneB }.OfType<TrackListController>())
        {
            pane.PlayingPath = _player.Owner == this ? _player.Track?.Path : null;
            pane.RedrawRows();
        }
        _bar.Update(_player);
    }

    private void Tick()
    {
        if (_player.Track is not null) _bar.UpdateProgress(_player);
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
            _bar.OfferUndo(false);
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

    /// <summary>„Cover für alle setzen…": ein Bild wählen und in jede Datei des Ordners schreiben.</summary>
    [Export("coverForAll:")]
    public void CoverForAll(NSObject sender)
    {
        if (_tracks.Tracks.Count == 0) return;
        var panel = NSOpenPanel.OpenPanel;
        panel.AllowedContentTypes = [UniformTypeIdentifiers.UTTypes.Image];
        if (_tracks.Folder is { } f) panel.DirectoryUrl = NSUrl.FromFilename(f);
        panel.BeginSheet(Window, r =>
        {
            if (r != (nint)(long)NSModalResponse.OK || panel.Url is not { } url || NSData.FromUrl(url) is not { } data) return;
            if (Covers.Prepare(data) is not { } cover) return;
            var jobs = _tracks.Tracks.Where(t => Core.Audio.AudioFormats.CanCarryCover(t.Path))
                .Select(t => new Job(t, new TagEdit { Cover = cover.Data, CoverMimeType = cover.Mime })).ToList();
            _ = WriteAsync(jobs, "batch", Strings.T("Cover changed"));
        });
    }

    [Export("focusSearch:")]
    public void FocusSearch(NSObject sender) => _search?.BeginSearchInteraction();

    [Export("focusLibrarySearch:")]
    public void FocusLibrarySearch(NSObject sender) => _library.FocusSearch();

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
        Status(Strings.T("Tags copied from \"{0}\"", t.FileName));
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
        _ = WriteAsync([.. targets.Select(t => new Job(t, For(t)))], "batch",
            Strings.T("From \"{0}\": {1}", src.FileName, Strings.T("Paste tags")));
    }

    [Export("validateMenuItem:")]
    public bool ValidateMenuItem(NSMenuItem item)
    {
        var sel = _tracks.SelectedTracks;
        return item.Action?.Name switch
        {
            "toggleRule:" => ValidateRuleItem(item),
            "applyAlbumMode:" => CurrentRule?.AlbumMode == true && _tracks.Tracks.Count > 0,
            "undo:" => AppDelegate.History.Entries.Any(e => e.CanUndo),
            "applyChanges:" or "revertChanges:" => _inspector.HasChanges,
            "copyTags:" => sel.Count == 1,
            "pasteTags:" => _copied is not null && sel.Count > 0,
            "chooseCover:" or "pasteCover:" or "removeCover:" => sel.Count > 0,
            "coverForAll:" => _tracks.Tracks.Count > 0,
            "revealInFinder:" or "reloadFolder:" => _tracks.Folder is not null,
            "renameFiles:" => _tracks.Tracks.Count > 0 && !_inspector.Busy,
            "playPause:" => _player.Track is not null || _tracks.Shown.Count > 0,
            "nextTrack:" or "previousTrack:" => _player.Track is not null,
            "goBack:" => _back.Count > 0,
            "goForward:" => _forward.Count > 0,
            _ => true,
        };
    }

    // ── Symbolleiste ─────────────────────────────────────────────

    private sealed class ToolbarDelegate(MainWindowController owner) : NSToolbarDelegate
    {
        private const string NavId = "nav";
        private const string PathId = "path";
        private const string SearchId = "search";
        private const string HistoryId = "history";
        private const string SplitId = "split";
        private NSToolbarItem? _split;
        private const string SettingsId = "settings";

        private NSSegmentedControl? _nav;

        public override string[] AllowedItemIdentifiers(NSToolbar toolbar) => DefaultItemIdentifiers(toolbar);

        public override string[] DefaultItemIdentifiers(NSToolbar toolbar) =>
        [
            NavId,
            PathId,
            NSToolbar.NSToolbarFlexibleSpaceItemIdentifier,
            SearchId,
            SplitId,
            HistoryId,
            SettingsId,
        ];

        public override NSToolbarItem WillInsertItem(NSToolbar toolbar, string itemIdentifier, bool willBeInserted)
        {
            switch (itemIdentifier)
            {
                case SearchId:
                {
                    var s = new NSSearchToolbarItem(SearchId);
                    s.SearchField.PlaceholderString = Strings.T("Find in this folder…");
                    s.SearchField.Changed += (_, _) => owner._tracks.Filter(s.SearchField.StringValue);
                    s.PreferredWidthForSearchField = 200;
                    owner._search = s;
                    return s;
                }
                case NavId:
                {
                    // Zurück und Vor als ein Paar, wie im Finder.
                    _nav = NSSegmentedControl.FromImages(
                        [NSImage.GetSystemSymbol("chevron.left", null)!, NSImage.GetSystemSymbol("chevron.right", null)!],
                        NSSegmentSwitchTracking.Momentary, () =>
                        {
                            if (_nav!.SelectedSegment == 0) owner.GoBack(owner.Window);
                            else owner.GoForward(owner.Window);
                        });
                    _nav.SegmentStyle = NSSegmentStyle.Separated;
                    _nav.SetEnabled(false, 0);
                    _nav.SetEnabled(false, 1);
                    return new NSToolbarItem(NavId) { View = _nav, Label = Strings.T("Back") };
                }
                case PathId:
                {
                    // Der Pfad wie oben im Explorer; ein Klick auf ein Glied öffnet es.
                    var p = new NSPathControl { PathStyle = NSPathStyle.Standard, Editable = false };
                    p.Font = NSFont.SystemFontOfSize(12);
                    p.Activated += (_, _) =>
                    {
                        if (p.ClickedPathItem?.Url?.Path is { } path && Directory.Exists(path)) owner.OpenFolder(path);
                    };
                    p.SetContentCompressionResistancePriority(200, NSLayoutConstraintOrientation.Horizontal);
                    p.WidthAnchor.ConstraintGreaterThanOrEqualTo(200).Active = true;
                    p.WidthAnchor.ConstraintLessThanOrEqualTo(620).Active = true;
                    owner._pathControl = p;
                    return new NSToolbarItem(PathId) { View = p, Label = Strings.T("Folder") };
                }
                case SplitId:
                    return _split = Button(SplitId, "rectangle.split.1x2", Strings.T("Split the view"), "toggleSplit:", owner);
                case HistoryId:
                    return Button(HistoryId, "clock.arrow.circlepath", Strings.T("History"), "showHistory:", owner);
                case SettingsId:
                    return Button(SettingsId, "gearshape", Strings.T("Settings"), "showSettings:", null);
            }
            return new NSToolbarItem(itemIdentifier);
        }

        private static NSToolbarItem Button(string id, string symbol, string label, string selector, NSObject? target) =>
            new(id)
            {
                Image = NSImage.GetSystemSymbol(symbol, null),
                Label = label,
                ToolTip = label,
                Action = new Selector(selector),
                Target = target,
                Bordered = true,
            };

        public void UpdateSplit(bool on)
        {
            if (_split is null) return;
            _split.Image = NSImage.GetSystemSymbol(on ? "rectangle.split.1x2.fill" : "rectangle.split.1x2", null);
        }

        public void UpdateNavigation(bool back, bool forward)
        {
            _nav?.SetEnabled(back, 0);
            _nav?.SetEnabled(forward, 1);
        }
    }
}

/// <summary>Der Fensterinhalt: die Spalten oben, die Player-Leiste unten.</summary>
internal sealed class RootController(NSSplitViewController split, PlayerBar bar) : NSViewController
{
    public override void LoadView()
    {
        var root = new NSView();
        AddChildViewController(split);
        // Keine Trennlinie: Die Kapsel aus Glas schwebt über dem Inhalt.
        foreach (var v in new NSView[] { split.View, bar })
        {
            v.TranslatesAutoresizingMaskIntoConstraints = false;
            root.AddSubview(v);
        }
        NSLayoutConstraint.ActivateConstraints([
            split.View.TopAnchor.ConstraintEqualTo(root.TopAnchor),
            split.View.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor),
            split.View.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor),
            split.View.BottomAnchor.ConstraintEqualTo(bar.TopAnchor),
            bar.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor),
            bar.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor),
            bar.BottomAnchor.ConstraintEqualTo(root.BottomAnchor),
            bar.HeightAnchor.ConstraintEqualTo(64),
        ]);
        View = root;
    }
}
