using ObjCRuntime;
using TagTuner.Core.Audio;
using TagTuner.Core.Metadata;
using TagTuner.Core.Model;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>
/// Die Einstellungen wie unter Windows (SettingsShell und SettingsCatalog):
/// links die Kategorien, rechts je Einstellung eine Zeile mit Symbol, Titel,
/// Beschreibung und dem Bedienelement. Jede Änderung gilt sofort und wird
/// gleich gespeichert; was offene Fenster angeht, meldet <see cref="Changed"/>.
/// </summary>
internal static class SettingsWindow
{
    /// <summary>
    /// Etwas geändert, das die Hauptfenster angeht: „library", „columns",
    /// „sorting" oder „rules". Der Name sagt was, damit dort nicht
    /// vorsichtshalber alles neu aufgebaut wird.
    /// </summary>
    public static event Action<string>? Changed;

    private static NSWindow? _window;
    private static Shell? _shell;

    private static AppSettings S => AppDelegate.Settings;

    /// <param name="section">Gleich diese Kategorie zeigen, etwa „Backups".</param>
    public static void Show(string? section = null)
    {
        if (_window is null)
        {
            _shell = new Shell();
            _window = new NSWindow(new CGRect(0, 0, 860, 640),
                NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable |
                NSWindowStyle.Miniaturizable | NSWindowStyle.FullSizeContentView,
                NSBackingStore.Buffered, false)
            {
                Title = Strings.T("Settings"),
                ReleasedWhenClosed = false,
                MinSize = new CGSize(720, 480),
                TitlebarAppearsTransparent = true,
                ContentViewController = _shell,
            };
            _window.SetContentSize(new CGSize(860, 640));
            _window.Center();
        }
        if (section is not null) _shell!.Select(section);
        _window.MakeKeyAndOrderFront(null);
    }

#if DEBUG
    /// <summary>Für das Testskript: eine Änderung melden, als käme sie aus dem Fenster.</summary>
    public static void Notify(string what) => Changed?.Invoke(what);
#endif

    private static void Save(string? changed = null)
    {
        S.Save();
        if (changed is not null) Changed?.Invoke(changed);
    }

    // ══ Gerüst: Leiste links, Inhalt rechts ══════════════════════

    private sealed record Section(string Title, string Symbol, Func<IEnumerable<NSView>> Build);

    private static readonly Section[] Sections =
    [
        new("General", "gearshape", General),
        new("Folders", "folder", Folders),
        new("Library", "books.vertical", Library),
        new("Tags", "tag", Tags),
        new("Track list", "list.bullet", TrackList),
        new("Backups", "clock.arrow.circlepath", Backups),
    ];

    private sealed class Shell : NSSplitViewController
    {
        private readonly NSTableView _rail = new();
        private readonly ContentPage _content = new();

        public Shell()
        {
            _rail.Style = NSTableViewStyle.Plain;
            _rail.BackgroundColor = NSColor.Clear;
            _rail.HeaderView = null;
            _rail.RowHeight = 30;
            _rail.ColumnAutoresizingStyle = NSTableViewColumnAutoresizingStyle.FirstColumnOnly;
            _rail.AddColumn(new NSTableColumn("s") { ResizingMask = NSTableColumnResizing.Autoresizing });
            _rail.DataSource = new RailSource();
            _rail.Delegate = new RailDelegate(this);
            var scroll = new NSScrollView { DocumentView = _rail, DrawsBackground = false };
            var railVc = new NSViewController { View = scroll };

            var side = NSSplitViewItem.CreateSidebar(railVc);
            side.MinimumThickness = 180;
            side.MaximumThickness = 220;
            side.CanCollapse = false;
            AddSplitViewItem(side);
            AddSplitViewItem(NSSplitViewItem.FromViewController(_content));
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            if (_rail.SelectedRow < 0) Select("General");
        }

        public void Select(string title)
        {
            var i = Array.FindIndex(Sections, s => s.Title == title);
            if (i < 0) return;
            _rail.SelectRow(i, false);
            _content.Show(Sections[i]);
        }

        private sealed class RailSource : NSTableViewDataSource
        {
            public override nint GetRowCount(NSTableView tableView) => Sections.Length;
        }

        private sealed class RailDelegate(Shell owner) : NSTableViewDelegate
        {
            public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
            {
                var s = Sections[(int)row];
                var cell = new NSTableCellView();
                var icon = NSImageView.FromImage(NSImage.GetSystemSymbol(s.Symbol, null)!);
                icon.ContentTintColor = Theme.Accent;
                var text = NSTextField.CreateLabel(Strings.T(s.Title));
                foreach (var v in new NSView[] { icon, text })
                {
                    v.TranslatesAutoresizingMaskIntoConstraints = false;
                    cell.AddSubview(v);
                }
                NSLayoutConstraint.ActivateConstraints([
                    icon.LeadingAnchor.ConstraintEqualTo(cell.LeadingAnchor, 4),
                    icon.CenterYAnchor.ConstraintEqualTo(cell.CenterYAnchor),
                    icon.WidthAnchor.ConstraintEqualTo(20),
                    text.LeadingAnchor.ConstraintEqualTo(icon.TrailingAnchor, 8),
                    text.CenterYAnchor.ConstraintEqualTo(cell.CenterYAnchor),
                ]);
                cell.TextField = text;
                return cell;
            }

            public override NSTableRowView CoreGetRowView(NSTableView tableView, nint row) => new SelectionRowView(bar: true);

            public override void SelectionDidChange(NSNotification notification)
            {
                var i = owner._rail.SelectedRow;
                if (i >= 0) owner._content.Show(Sections[(int)i]);
            }
        }
    }

    /// <summary>
    /// Der Inhalt einer Kategorie. Gebaut wird erst beim Wählen, wie unter
    /// Windows: Beim Öffnen jede Seite aufzubauen kostet Zeit, und die
    /// meisten sieht man nie.
    /// </summary>
    private sealed class ContentPage : NSViewController
    {
        private readonly NSTextField _heading = NSTextField.CreateLabel("");
        private readonly FlippedStack _body = new()
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Alignment = NSLayoutAttribute.Leading,
            Spacing = 7,
            EdgeInsets = new NSEdgeInsets(4, 24, 24, 24),
        };

        public override void LoadView()
        {
            _heading.Font = NSFont.SystemFontOfSize(20, NSFontWeight.Semibold);
            var scroll = new NSScrollView { DocumentView = _body, HasVerticalScroller = true, AutohidesScrollers = true, DrawsBackground = false };
            _body.TranslatesAutoresizingMaskIntoConstraints = false;
            var root = new NSView();
            foreach (var v in new NSView[] { _heading, scroll })
            {
                v.TranslatesAutoresizingMaskIntoConstraints = false;
                root.AddSubview(v);
            }
            NSLayoutConstraint.ActivateConstraints([
                _heading.TopAnchor.ConstraintEqualTo(root.SafeAreaLayoutGuide.TopAnchor, 14),
                _heading.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor, 24),
                scroll.TopAnchor.ConstraintEqualTo(_heading.BottomAnchor, 10),
                scroll.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor),
                scroll.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor),
                scroll.BottomAnchor.ConstraintEqualTo(root.BottomAnchor),
                _body.TopAnchor.ConstraintEqualTo(scroll.ContentView.TopAnchor),
                _body.LeadingAnchor.ConstraintEqualTo(scroll.ContentView.LeadingAnchor),
                _body.WidthAnchor.ConstraintEqualTo(scroll.ContentView.WidthAnchor),
            ]);
            View = root;
        }

        public void Show(Section s)
        {
            _ = View;
            _heading.StringValue = Strings.T(s.Title);
            foreach (var v in _body.ArrangedSubviews) { _body.RemoveArrangedSubview(v); v.RemoveFromSuperview(); }
            foreach (var v in s.Build())
            {
                _body.AddArrangedSubview(v);
                _body.FillWidth(v);
                if (v is HeadingLabel) _body.SetCustomSpacing(4, v);
            }
        }
    }

    // ══ Bausteine ════════════════════════════════════════════════

    private sealed class HeadingLabel : NSView;

    /// <summary>Eine Zwischenüberschrift wie „App" oder „Album-Modus".</summary>
    private static NSView Heading(string title)
    {
        var box = new HeadingLabel();
        var l = NSTextField.CreateLabel(Strings.T(title));
        l.Font = NSFont.SystemFontOfSize(13, NSFontWeight.Semibold);
        l.TranslatesAutoresizingMaskIntoConstraints = false;
        box.AddSubview(l);
        NSLayoutConstraint.ActivateConstraints([
            l.TopAnchor.ConstraintEqualTo(box.TopAnchor, 14),
            l.BottomAnchor.ConstraintEqualTo(box.BottomAnchor),
            l.LeadingAnchor.ConstraintEqualTo(box.LeadingAnchor, 2),
        ]);
        return box;
    }

    /// <summary>Eine Einstellung: Symbol, Titel, darunter klein die Erklärung, rechts das Bedienelement.</summary>
    private sealed class SettingRow : NSView
    {
        private readonly NSTextField _detail;

        public SettingRow(string? symbol, string title, string? detail, NSView? control, int indent = 0, bool translate = true)
        {
            WantsLayer = true;
            Layer!.CornerRadius = 8;
            Layer.BackgroundColor = NSColor.FromWhite(1, 0.045f).CGColor;

            var icon = symbol is null ? new NSView() : NSImageView.FromImage(NSImage.GetSystemSymbol(symbol, null) ?? new NSImage());
            if (icon is NSImageView iv) iv.ContentTintColor = NSColor.SecondaryLabel;
            var name = NSTextField.CreateWrappingLabel(translate ? Strings.T(title) : title);
            name.Font = NSFont.SystemFontOfSize(13);
            _detail = Theme.Small(detail is null ? "" : translate ? Strings.T(detail) : detail);
            _detail.Hidden = detail is null;
            var text = new NSStackView
            {
                Orientation = NSUserInterfaceLayoutOrientation.Vertical,
                Alignment = NSLayoutAttribute.Leading,
                Spacing = 2,
            }.Arranged(name, _detail);
            text.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);

            var views = new List<NSView> { icon, text };
            if (control is not null) views.Add(control);
            foreach (var v in views)
            {
                v.TranslatesAutoresizingMaskIntoConstraints = false;
                AddSubview(v);
            }
            var list = new List<NSLayoutConstraint>
            {
                icon.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 14 + indent * 26),
                icon.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
                icon.WidthAnchor.ConstraintEqualTo(symbol is null ? 0 : 20),
                text.LeadingAnchor.ConstraintEqualTo(icon.TrailingAnchor, symbol is null ? 0 : 12),
                text.TopAnchor.ConstraintGreaterThanOrEqualTo(TopAnchor, 11),
                text.BottomAnchor.ConstraintLessThanOrEqualTo(BottomAnchor, -11),
                text.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
                HeightAnchor.ConstraintGreaterThanOrEqualTo(48),
            };
            if (control is not null)
            {
                control.SetContentHuggingPriorityForOrientation(750, NSLayoutConstraintOrientation.Horizontal);
                control.SetContentCompressionResistancePriority(750, NSLayoutConstraintOrientation.Horizontal);
                list.Add(control.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -14));
                list.Add(control.CenterYAnchor.ConstraintEqualTo(CenterYAnchor));
                list.Add(text.TrailingAnchor.ConstraintLessThanOrEqualTo(control.LeadingAnchor, -16));
            }
            else list.Add(text.TrailingAnchor.ConstraintLessThanOrEqualTo(TrailingAnchor, -14));
            NSLayoutConstraint.ActivateConstraints([.. list]);
        }

        public void Describe(string text)
        {
            _detail.StringValue = text;
            _detail.Hidden = text.Length == 0;
        }
    }

    private static SettingRow Row(string? symbol, string title, string? detail, NSView? control) =>
        new(symbol, title, detail, control);

    /// <summary>Ein Unterpunkt, eingerückt unter seiner Einstellung.</summary>
    private static SettingRow SubRow(string title, NSView control) => new(null, title, null, control, indent: 1);

    private static NSSwitch Switch(bool on, Action<bool> changed)
    {
        var sw = new NSSwitch { State = on ? 1 : 0 };
        sw.Activated += (_, _) => changed(sw.State == 1);
        return sw;
    }

    private static NSPopUpButton Choice((string Label, string Value)[] options, string current, Action<string> set)
    {
        var p = new NSPopUpButton();
        p.AddItems([.. options.Select(o => Strings.T(o.Label))]);
        p.SelectItem(Math.Max(0, Array.FindIndex(options, o => o.Value == current)));
        p.Activated += (_, _) => set(options[(int)p.IndexOfSelectedItem].Value);
        return p;
    }

    private static NSButton Button(string title, Action run) => NSButton.CreateButton(Strings.T(title), run);

    private static NSButton IconButton(string symbol, string tip, Action run)
    {
        var b = NSButton.CreateButton(NSImage.GetSystemSymbol(symbol, null)!, run);
        b.Bordered = false;
        b.ToolTip = Strings.T(tip);
        return b;
    }

    private static NSStackView Buttons(params NSView[] views) =>
        new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Spacing = 6 }.Arranged(views);

    private static readonly int[] Rates = [44100, 48000, 88200, 96000, 176400, 192000];
    private static string RateLabel(int hz) => hz % 1000 == 0 ? $"{hz / 1000} kHz" : $"{hz / 1000.0:0.0} kHz";

    // ══ Allgemein ════════════════════════════════════════════════

    private static IEnumerable<NSView> General()
    {
        yield return AboutCard();

        yield return Heading("App");
        // Unübersetzt: Jede Sprache nennt sich selbst. Wer Türkisch sucht, sucht „Türkçe".
        var language = new NSPopUpButton();
        language.AddItems(Strings.SupportedNames);
        language.SelectItem(Math.Max(0, Array.IndexOf(Strings.Supported, Strings.Current)));
        language.Activated += (_, _) => { S.Language = Strings.Supported[(int)language.IndexOfSelectedItem]; Save(); };
        yield return Row("globe", "Language", "Takes effect after a restart.", language);

        yield return Row("arrow.down.circle", "Updates on start",
            "\"Never\" stops any connection. \"Ask\" speaks up when there is something new. "
            + "\"Automatically\" checks on every start and downloads the new version right away.",
            Choice([("Never", "never"), ("Ask", "ask"), ("Automatically", "auto")], S.UpdateBehavior,
                v => { S.UpdateBehavior = v; Save(); }));

        // ── macOS ───────────────────────────────────────────────
        yield return Heading("macOS");
        // Das Gegenstück zum Explorer-Eintrag: Dienste sind immer da, ein- und
        // ausgeschaltet werden sie in den Systemeinstellungen.
        yield return Row("contextualmenu.and.cursorarrow", "Finder services",
            "\"Open in TagTuner\" and \"Edit metadata\" in the Finder right click menu under Services. "
            + "They are switched on and off in the system settings, under Keyboard shortcuts.",
            Button("Open system settings", () => NSWorkspace.SharedWorkspace.OpenUrl(
                new NSUrl("x-apple.systempreferences:com.apple.Keyboard-Settings.extension"))));

        var ffmpeg = FfmpegLocator.Find(S.FfmpegPath);
        var found = NSTextField.CreateLabel(Strings.T(ffmpeg is null ? "Not found" : "Found"));
        found.TextColor = ffmpeg is null ? Theme.Warn : Theme.Accent;
        yield return new SettingRow("waveform", "ffmpeg",
            ffmpeg ?? Strings.T("Needed for converting. Without it, TagTuner only edits tags.")
                     + " " + Strings.T("Install it with Homebrew: brew install ffmpeg"),
            found, translate: false);
    }

    /// <summary>
    /// Der Kopf der Seite: Logo, Name, Version und die Wege nach draußen,
    /// rechts der Stand der Aktualisierung. Wie unter Windows sagt das
    /// Erste, was man in den Einstellungen sieht, was man vor sich hat.
    /// </summary>
    private static NSView AboutCard()
    {
        var logo = NSImageView.FromImage(NSApplication.SharedApplication.ApplicationIconImage);
        var name = NSTextField.CreateLabel("TagTuner");
        name.Font = NSFont.SystemFontOfSize(22, NSFontWeight.Semibold);
        var version = NSTextField.CreateLabel(Strings.T("Version {0}", AppInfo.Version));
        version.Font = NSFont.MonospacedSystemFont(11.5f, NSFontWeight.Regular);
        version.TextColor = NSColor.TertiaryLabel;
        var claim = Theme.Small(Strings.T("Music folders as playlists: tags, covers, format and order in one place."));

        const string repo = "https://github.com/yyusvf/TagTuner";
        NSButton Link(string title, string url)
        {
            var b = NSButton.CreateButton(Strings.T(title), () => NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl(url)));
            b.Bordered = false;
            b.AttributedTitle = new NSAttributedString(Strings.T(title), new NSStringAttributes
            {
                ForegroundColor = Theme.Accent, Font = NSFont.SystemFontOfSize(12),
            });
            return b;
        }
        var links = new NSStackView { Spacing = 16 }.Arranged(
            Link("What's new", $"{repo}/releases/tag/v{AppInfo.Version}"),
            Link("Report a problem", $"{repo}/issues"),
            Link("GitHub", repo));
        var about = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Alignment = NSLayoutAttribute.Leading,
            Spacing = 2,
        }.Arranged(name, version, claim, links);
        about.SetCustomSpacing(6, version);
        about.SetCustomSpacing(6, claim);

        // ── Aktualisierung rechts ───────────────────────────────
        var status = Theme.Small(S.SkippedVersion is { Length: > 0 } skipped && UpdateService.IsNewer(skipped, AppInfo.Version)
            ? Strings.T("Version {0} was skipped.", skipped) : "");
        status.Alignment = NSTextAlignment.Right;
        UpdateCheck? offered = null;
        var install = NSButton.CreateButton(Strings.T("Download and install"), () =>
        {
            if (offered is not null) _ = MacUpdates.InstallAsync(offered, _window);
        });
        Theme.MakePrimary(install);
        install.Hidden = true;
        NSButton check = null!;
        check = NSButton.CreateButton(Strings.T("Check for updates"), async () =>
        {
            check.Enabled = false;
            install.Hidden = true;
            status.StringValue = Strings.T("Searching…");
            var found = await UpdateService.CheckAsync(AppInfo.Version, UpdateService.IsMacImage);
            check.Enabled = true;
            status.StringValue = found switch
            {
                { Failed: true } => Strings.T("Check failed: {0}", found.Error ?? ""),
                { HasUpdate: true } => Strings.T("Version {0} is available.", found.Version ?? ""),
                _ => Strings.T("TagTuner is up to date."),
            };
            if (found.HasUpdate) { offered = found; install.Hidden = false; }
        });
        var update = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Alignment = NSLayoutAttribute.Trailing,
            Spacing = 8,
        }.Arranged(status, check, install);
        status.WidthAnchor.ConstraintLessThanOrEqualTo(220).Active = true;

        var card = new NSView { WantsLayer = true };
        card.Layer!.CornerRadius = 10;
        card.Layer.BackgroundColor = NSColor.FromWhite(1, 0.05f).CGColor;
        foreach (var v in new NSView[] { logo, about, update })
        {
            v.TranslatesAutoresizingMaskIntoConstraints = false;
            card.AddSubview(v);
        }
        NSLayoutConstraint.ActivateConstraints([
            logo.LeadingAnchor.ConstraintEqualTo(card.LeadingAnchor, 18),
            logo.TopAnchor.ConstraintEqualTo(card.TopAnchor, 18),
            logo.WidthAnchor.ConstraintEqualTo(72),
            logo.HeightAnchor.ConstraintEqualTo(72),
            about.LeadingAnchor.ConstraintEqualTo(logo.TrailingAnchor, 16),
            about.TopAnchor.ConstraintEqualTo(card.TopAnchor, 18),
            about.BottomAnchor.ConstraintEqualTo(card.BottomAnchor, -18),
            update.TrailingAnchor.ConstraintEqualTo(card.TrailingAnchor, -18),
            update.CenterYAnchor.ConstraintEqualTo(card.CenterYAnchor),
            about.TrailingAnchor.ConstraintLessThanOrEqualTo(update.LeadingAnchor, -16),
            claim.WidthAnchor.ConstraintLessThanOrEqualTo(320),
        ]);
        return card;
    }

    // ══ Ordner ═══════════════════════════════════════════════════

    private static IEnumerable<NSView> Folders()
    {
        yield return Heading("Default profile");
        var format = new NSPopUpButton();
        format.AddItems([.. AudioFormats.Targets]);
        format.SelectItem(S.DefaultFormat.ToUpperInvariant());
        format.Activated += (_, _) => { S.DefaultFormat = format.TitleOfSelectedItem!; Save("rules"); };
        yield return Row("doc.badge.gearshape", "File format",
            "Only applies when a folder is empty or mixed. Uniform folders decide their own target.", format);
        var rate = new NSPopUpButton();
        rate.AddItems([.. Rates.Select(RateLabel)]);
        rate.SelectItem(Math.Max(0, Array.IndexOf(Rates, S.DefaultSampleRate)));
        rate.Activated += (_, _) => { S.DefaultSampleRate = Rates[(int)rate.IndexOfSelectedItem]; Save("rules"); };
        yield return Row("waveform.path", "Sample rate", null, rate);

        // ── Album-Modus ──────────────────────────────────────────
        yield return Heading("Album mode");
        NSSwitch baseTags = null!, cover = null!, numbering = null!, fileNames = null!;
        void Enable()
        {
            var on = S.DefaultAlbumMode;
            baseTags.Enabled = cover.Enabled = numbering.Enabled = on;
            fileNames.Enabled = on && S.DefaultNumbering;
        }
        baseTags = Switch(S.DefaultBaseTags, on => { S.DefaultBaseTags = on; Save("rules"); });
        cover = Switch(S.DefaultCover, on => { S.DefaultCover = on; Save("rules"); });
        numbering = Switch(S.DefaultNumbering, on => { S.DefaultNumbering = on; Save("rules"); Enable(); });
        fileNames = Switch(S.DefaultRenameFiles, on => { S.DefaultRenameFiles = on; Save("rules"); });
        var album = Switch(S.DefaultAlbumMode, on => { S.DefaultAlbumMode = on; Save("rules"); Enable(); });
        Enable();
        yield return Row("square.stack", "Album mode",
            "For folders that are one release: an album, an EP, a single. It makes metadata uniform, " +
            "so leave it off for folders where you collect mixed music. This is the default for " +
            "folders without their own setting.", album);
        yield return SubRow("Base metadata", baseTags);
        yield return SubRow("Cover", cover);
        yield return SubRow("Track numbering", numbering);
        yield return SubRow("File names follow the track numbers", fileNames);

        // ── Beim Ablegen ─────────────────────────────────────────
        yield return Heading("On drop");
        yield return Row("arrow.triangle.2.circlepath", "Align format and sample rate",
            "Applies to every folder. The folder analysis on the right can set this differently for " +
            "a single folder; that setting wins and stays even when you change something here.",
            Switch(S.DefaultAutoConform, on => { S.DefaultAutoConform = on; Save("rules"); }));
        yield return Row("questionmark.bubble", "Ask before aligning dropped files",
            null, Switch(!S.SkipConformDialog, on => { S.SkipConformDialog = !on; Save(); }));

        static string OwnRules()
        {
            var n = S.FolderRules.Count;
            return n == 0
                ? Strings.T("No folder deviates from this.")
                : Strings.T(n == 1 ? "{0} folder has its own setting and is not affected."
                                   : "{0} folders have their own setting and are not affected.", n);
        }
        SettingRow rules = null!;
        NSButton clear = null!;
        clear = Button("Reset", () =>
        {
            S.FolderRules.Clear();
            Save("rules");
            rules.Describe(OwnRules());
            clear.Enabled = false;
        });
        clear.Enabled = S.FolderRules.Count > 0;
        rules = new SettingRow("folder.badge.gearshape", "Per-folder settings", OwnRules(), clear);
        yield return rules;
    }

    // ══ Bibliothek ═══════════════════════════════════════════════

    private static IEnumerable<NSView> Library()
    {
        yield return Heading("Library");
        yield return Row("eye.slash", "Only show folders containing audio",
            "Hides folders with no music anywhere below them. Expanding takes a little longer " +
            "because every subfolder has to be checked.",
            Switch(S.OnlyAudioFolders, on => { S.OnlyAudioFolders = on; Save("library"); }));

        // ── Eigene Ordner ────────────────────────────────────────
        // Jeder Ordner eine eigene Zeile mit seinen Knöpfen: Man sieht, was
        // man anfasst, statt erst eine Zeile in einer Liste zu markieren.
        yield return Heading("Your folders");
        var roots = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 4 };
        var hidden = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 4 };

        void Put(NSStackView stack, NSView row) { stack.AddArrangedSubview(row); stack.FillWidth(row); }
        void Clear(NSStackView stack) { foreach (var v in stack.ArrangedSubviews) { stack.RemoveArrangedSubview(v); v.RemoveFromSuperview(); } }

        void Refresh()
        {
            Clear(roots);
            var paths = S.LibraryPaths;
            if (paths.Count == 0)
                Put(roots, Row("folder", "No folders of your own yet.", "Add one here or with the + above the library.", null));
            for (var i = 0; i < paths.Count; i++)
            {
                var at = i;
                var up = IconButton("chevron.up", "Up", () => Move(at, -1));
                var down = IconButton("chevron.down", "Down", () => Move(at, 1));
                // Wo es nicht weitergeht, fehlt der Pfeil, statt grau dazustehen;
                // unsichtbar statt weg, damit die Knöpfe untereinander fluchten.
                up.AlphaValue = at > 0 ? 1 : 0;
                down.AlphaValue = at < paths.Count - 1 ? 1 : 0;
                Put(roots, FolderRow("folder", paths[i], Buttons(up, down,
                    IconButton("xmark", "Remove from the library", () => { S.LibraryPaths.RemoveAt(at); Changed(); }))));
            }

            Clear(hidden);
            var gone = S.HiddenRoots;
            if (gone.Count == 0)
            {
                Put(hidden, Row("eye", "Nothing is hidden.",
                    "Music, Downloads, your user folder or a drive that you remove from the library shows up here.", null));
                return;
            }
            Put(hidden, Row("eye.slash", "Hidden from the library", "They stay on disk and are only left out of the library.",
                gone.Count > 1 ? Button("Show all again", () => { S.HiddenRoots.Clear(); Changed(); }) : null));
            foreach (var path in gone.ToList())
                Put(hidden, FolderRow("eye.slash", path, Button("Show again", () => { S.HiddenRoots.Remove(path); Changed(); })));
        }

        void Move(int at, int by)
        {
            var to = at + by;
            var paths = S.LibraryPaths;
            if (to < 0 || to >= paths.Count) return;
            (paths[at], paths[to]) = (paths[to], paths[at]);
            Changed();
        }

        void Changed() { Save("library"); Refresh(); }

        var add = Button("Add folder…", () =>
        {
            var panel = NSOpenPanel.OpenPanel;
            panel.CanChooseDirectories = true;
            panel.CanChooseFiles = false;
            panel.BeginSheet(_window!, r =>
            {
                if (r != (nint)(long)NSModalResponse.OK || panel.Url?.Path is not { } p) return;
                if (S.LibraryPaths.Any(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase))) return;
                // Wer einen ausgeblendeten Ordner selbst wieder hinzufügt, will ihn sehen.
                S.HiddenRoots.RemoveAll(x => string.Equals(x, p, StringComparison.OrdinalIgnoreCase));
                S.LibraryPaths.Add(p);
                Changed();
            });
        });
        Refresh();

        yield return Row("folder.badge.plus", "Folders in the library",
            "Music, Downloads, your user folder and the drives are always there and are found fresh " +
            "on every start. Folders you add yourself are listed below, in the order of the library.", add);
        yield return roots;
        yield return Heading("Hidden folders");
        yield return hidden;
    }

    /// <summary>Ein Ordner als Zeile: sein Name groß, der ganze Pfad klein darunter.</summary>
    private static SettingRow FolderRow(string symbol, string path, NSView control)
    {
        var name = Path.GetFileName(path.TrimEnd('/'));
        var row = string.IsNullOrEmpty(name)
            ? new SettingRow(symbol, path, null, control, translate: false)
            : new SettingRow(symbol, name, path, control, translate: false);
        row.ToolTip = path;
        return row;
    }

    // ══ Tags ═════════════════════════════════════════════════════

    private static IEnumerable<NSView> Tags()
    {
        yield return Heading("Tags");
        yield return Row("doc.on.clipboard", "Paste tags",
            "Copying takes artist, album, album artist, year, disc, genre, composer, comment and the " +
            "cover. Title and track number are different in every file, so they only come along " +
            "with \"everything\".",
            Choice([("Everything except title and track number", "format"), ("Everything", "all")],
                S.TagPasteMode, v => { S.TagPasteMode = v; Save(); }));

        yield return Heading("Naming scheme");
        var pattern = new NSTextField { StringValue = S.RenamePattern, BezelStyle = NSTextFieldBezelStyle.Rounded };
        pattern.Font = NSFont.MonospacedSystemFont(12, NSFontWeight.Regular);
        pattern.WidthAnchor.ConstraintEqualTo(260).Active = true;
        var placeholders = string.Join("  ", FileNaming.Placeholders);
        string Example() => placeholders + "\n" + Strings.T("Result: ") + pattern.StringValue
            .Replace("{track}", "04").Replace("{title}", "Nebelfeld")
            .Replace("{artist}", "Kollektiv Halle").Replace("{album}", "Nachtfahrt")
            .Replace("{albumartist}", "Kollektiv Halle").Replace("{genre}", "Electronic")
            .Replace("{disc}", "1").Replace("{year}", "2025") + ".flac";
        var row = new SettingRow("character.cursor.ibeam", Strings.T("Title → file name"), Example(), pattern, translate: false);
        pattern.Changed += (_, _) => { S.RenamePattern = pattern.StringValue; Save(); row.Describe(Example()); };
        yield return row;
    }

    // ══ Trackliste ═══════════════════════════════════════════════

    private static IEnumerable<NSView> TrackList()
    {
        S.EnsureTrackColumns();
        var states = S.TrackColumns;

        // Die Spalten als Liste mit Haken; die Reihenfolge hier ist die in der Liste.
        var table = new NSTableView { Style = NSTableViewStyle.Inset, HeaderView = null, RowHeight = 26, UsesAlternatingRowBackgroundColors = true };
        table.AddColumn(new NSTableColumn("c") { Width = 300 });
        table.DataSource = new ColumnSource(() => states.Count);
        table.Delegate = new ColumnDelegate(() => states, () => { Save("columns"); });
        var scroll = new NSScrollView { DocumentView = table, HasVerticalScroller = true, AutohidesScrollers = true };
        scroll.HeightAnchor.ConstraintEqualTo(300).Active = true;

        void Move(int by)
        {
            var at = (int)table.SelectedRow;
            var to = at + by;
            if (at < 0 || to < 0 || to >= states.Count) return;
            (states[at], states[to]) = (states[to], states[at]);
            Save("columns");
            table.ReloadData();
            table.SelectRow(to, false);
        }

        yield return Heading("Columns");
        yield return Row("list.bullet.rectangle", "Columns",
            "The order here is the order in the list. Pick a row and move it.",
            Buttons(Button("Up", () => Move(-1)), Button("Down", () => Move(1))));
        yield return scroll;

        yield return Heading("Display");
        yield return Row("text.below.photo", "Show the artist under the title in one column",
            "Off means two columns of their own. The artist column is then moved and sized like any other.",
            Switch(S.CombineTitleAndArtist, on => { S.CombineTitleAndArtist = on; Save("columns"); }));
        yield return Row("opticaldisc", "Show disc and track number in one column",
            "In an album with several discs the disc number appears once, at the first track of " +
            "each disc. Elsewhere it stands in front of the number, like 2-04. With a single disc " +
            "only the number is shown.",
            Switch(S.CombineDiscAndTrack, on => { S.CombineDiscAndTrack = on; Save("columns"); }));

        yield return Heading("Sorting");
        yield return Row("arrow.up.arrow.down", "In playlist order, sort by disc first, then by track",
            "Only for releases that span several discs. Without it the track number alone decides.",
            Switch(S.SortByDiscThenTrack, on => { S.SortByDiscThenTrack = on; Save("sorting"); }));
    }

    private sealed class ColumnSource(Func<int> count) : NSTableViewDataSource
    {
        public override nint GetRowCount(NSTableView tableView) => count();
    }

    private sealed class ColumnDelegate(Func<List<TrackColumnState>> states, Action changed) : NSTableViewDelegate
    {
        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
        {
            var state = states()[(int)row];
            var column = TrackColumn.ById(state.Id)!;
            var label = column.Header.Length > 0 ? Strings.T(column.Header) : Strings.T("Cover");
            var box = NSButton.CreateCheckbox(label, () => { });
            box.State = state.Visible ? NSCellStateValue.On : NSCellStateValue.Off;
            box.Activated += (_, _) => { state.Visible = box.State == NSCellStateValue.On; changed(); };
            return box;
        }
    }

    // ══ Sicherungen ══════════════════════════════════════════════

    private static IEnumerable<NSView> Backups()
    {
        yield return Heading("Backups");
        yield return Row("trash.circle", "Delete automatically",
            "Runs on start. Older backups are removed, and the history entries that belong to them " +
            "can no longer be undone afterwards.",
            Choice([("Never", "never"), ("After 7 days", "7"), ("After 30 days", "30")], S.BackupRetention,
                v => { S.BackupRetention = v; Save(); }));
        yield return Row("folder", "Backup folder", S.ResolvedBackupFolder, Button("Open backup folder", () =>
        {
            Directory.CreateDirectory(S.ResolvedBackupFolder);
            NSWorkspace.SharedWorkspace.OpenUrl(NSUrl.FromFilename(S.ResolvedBackupFolder));
        }));

        // Die Liste mit Wiederherstellen und Löschen, wie unter Windows auf derselben Seite.
        var page = new BackupsPage();
        var view = page.View;
        view.HeightAnchor.ConstraintEqualTo(440).Active = true;
        page.Reload();
        yield return view;
    }
}
