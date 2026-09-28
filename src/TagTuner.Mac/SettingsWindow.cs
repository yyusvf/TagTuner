using TagTuner.Core.Safety;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>
/// Die Einstellungen, wie auf dem Mac üblich als eigenes Fenster (⌘,).
/// Jede Änderung gilt sofort und wird gleich gespeichert; einen
/// Speichern-Knopf gibt es auf dem Mac dafür nicht.
/// </summary>
internal static class SettingsWindow
{
    private static NSWindow? _window;

    public static void Show()
    {
        if (_window is null)
        {
            _window = new NSWindow(new CGRect(0, 0, 560, 600),
                NSWindowStyle.Titled | NSWindowStyle.Closable, NSBackingStore.Buffered, false)
            {
                Title = Strings.T("Settings"),
                ReleasedWhenClosed = false,
            };
            _window.ContentView = Build();
            _window.SetContentSize(_window.ContentView.FittingSize);
            _window.Center();
        }
        _window.MakeKeyAndOrderFront(null);
    }

    private static AppSettings S => AppDelegate.Settings;

    private static NSView Build()
    {
        // Sprache
        var lang = new NSPopUpButton();
        lang.AddItems(Strings.SupportedNames);
        lang.SelectItem(Math.Max(0, Array.IndexOf(Strings.Supported, Strings.Current)));
        var langNote = Note(Strings.T("Takes effect after a restart."));
        lang.Activated += (_, _) =>
        {
            S.Language = Strings.Supported[(int)lang.IndexOfSelectedItem];
            S.Save();
        };

        // Bibliothek
        var onlyAudio = Check(Strings.T("Only show folders containing audio"), S.OnlyAudioFolders,
            on => S.OnlyAudioFolders = on);

        // Trackliste
        var discFirst = Check(Strings.T("Sort by disc, then by track"), S.SortByDiscThenTrack,
            on => S.SortByDiscThenTrack = on);
        var combine = Check(Strings.T("Disc and track in one column"), S.CombineDiscAndTrack,
            on => S.CombineDiscAndTrack = on);

        // Standardprofil: greift nur, wenn ein Ordner leer oder gemischt ist.
        var fmt = new NSPopUpButton();
        fmt.AddItems([.. Core.Audio.AudioFormats.Targets]);
        fmt.SelectItem(S.DefaultFormat.ToUpperInvariant());
        fmt.Activated += (_, _) => { S.DefaultFormat = fmt.TitleOfSelectedItem!; S.Save(); };
        int[] rates = [44100, 48000, 88200, 96000, 176400, 192000];
        var rate = new NSPopUpButton();
        rate.AddItems([.. rates.Select(r => r % 1000 == 0 ? $"{r / 1000} kHz" : $"{r / 1000.0:0.0} kHz")]);
        rate.SelectItem(Math.Max(0, Array.IndexOf(rates, S.DefaultSampleRate)));
        rate.Activated += (_, _) => { S.DefaultSampleRate = rates[(int)rate.IndexOfSelectedItem]; S.Save(); };
        var profile = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Spacing = 8 }.Arranged(fmt, rate);

        // Was beim Ablegen und im Album-Modus gilt, solange ein Ordner nichts Eigenes hat.
        var conform = Check(Strings.T("Align format and sample rate"), S.DefaultAutoConform, on => S.DefaultAutoConform = on);
        var album = Check(Strings.T("Album mode"), S.DefaultAlbumMode, on => S.DefaultAlbumMode = on);
        var baseTags = Check(Strings.T("Base metadata"), S.DefaultBaseTags, on => S.DefaultBaseTags = on);
        var cover = Check(Strings.T("Cover"), S.DefaultCover, on => S.DefaultCover = on);
        var numbering = Check(Strings.T("Track numbering"), S.DefaultNumbering, on => S.DefaultNumbering = on);
        var names = Check(Strings.T("File names follow"), S.DefaultRenameFiles, on => S.DefaultRenameFiles = on);

        // Tags einfügen
        var paste = new NSPopUpButton();
        paste.AddItems([Strings.T("Everything"), Strings.T("Everything except title and track number")]);
        paste.SelectItem(S.TagPasteMode == "all" ? 0 : 1);
        paste.Activated += (_, _) => { S.TagPasteMode = paste.IndexOfSelectedItem == 0 ? "all" : "format"; S.Save(); };

        // Sicherungen
        var keep = new NSPopUpButton();
        var keepValues = new[] { "7", "30", "never" };
        keep.AddItems([Strings.T("After 7 days"), Strings.T("After 30 days"), Strings.T("Never")]);
        keep.SelectItem(Math.Max(0, Array.IndexOf(keepValues, S.BackupRetention)));
        keep.Activated += (_, _) => { S.BackupRetention = keepValues[(int)keep.IndexOfSelectedItem]; S.Save(); };

        var store = new BackupStore(S.ResolvedBackupFolder);
        var (count, bytes) = store.Info();
        var info = Note($"{Strings.T("Stored backups")}: {count} · {bytes / 1024.0 / 1024.0:0.#} MB");
        var open = NSButton.CreateButton(Strings.T("Open backup folder"), () =>
        {
            Directory.CreateDirectory(store.Folder);
            NSWorkspace.SharedWorkspace.OpenUrl(NSUrl.FromFilename(store.Folder));
        });

        var grid = NSGridView.Create(new NSView[][]
        {
            [Head(Strings.T("Language")), Stack(lang, langNote)],
            [Head(Strings.T("Library")), onlyAudio],
            [Head(Strings.T("Track list")), Stack(discFirst, combine)],
            [Head(Strings.T("Default profile")), profile],
            [Head(Strings.T("On drop")), Stack(conform, album, Indent(1, baseTags), Indent(1, cover),
                                               Indent(1, numbering), Indent(2, names))],
            [Head(Strings.T("Paste tags")), paste],
            [Head(Strings.T("Delete automatically")), keep],
            [Head(Strings.T("Backups")), Stack(info, open)],
        });
        grid.RowSpacing = 16;
        grid.ColumnSpacing = 12;
        grid.GetColumn(0).X = NSGridCellPlacement.Trailing;
        grid.RowAlignment = NSGridRowAlignment.FirstBaseline;
        grid.TranslatesAutoresizingMaskIntoConstraints = false;

        var root = new NSView();
        root.AddSubview(grid);
        NSLayoutConstraint.ActivateConstraints([
            grid.TopAnchor.ConstraintEqualTo(root.TopAnchor, 24),
            grid.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor, 30),
            grid.TrailingAnchor.ConstraintLessThanOrEqualTo(root.TrailingAnchor, -30),
            grid.BottomAnchor.ConstraintEqualTo(root.BottomAnchor, -24),
        ]);
        return root;
    }

    /// <summary>Unterpunkte eingerückt, Kästchen samt Text.</summary>
    private static NSView Indent(int level, NSView v) => new NSStackView
    {
        Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
        EdgeInsets = new NSEdgeInsets(0, 20 * level, 0, 0),
    }.Arranged(v);

    private static NSTextField Head(string text) => NSTextField.CreateLabel(text + ":");

    private static NSTextField Note(string text)
    {
        var l = NSTextField.CreateLabel(text);
        l.Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize);
        l.TextColor = NSColor.SecondaryLabel;
        return l;
    }

    private static NSButton Check(string title, bool value, Action<bool> set)
    {
        var b = NSButton.CreateCheckbox(title, () => { });
        b.State = value ? NSCellStateValue.On : NSCellStateValue.Off;
        b.Activated += (_, _) => { set(b.State == NSCellStateValue.On); S.Save(); };
        return b;
    }

    private static NSStackView Stack(params NSView[] views) => new NSStackView
    {
        Orientation = NSUserInterfaceLayoutOrientation.Vertical,
        Alignment = NSLayoutAttribute.Leading,
        Spacing = 6,
    }.Arranged(views);
}
