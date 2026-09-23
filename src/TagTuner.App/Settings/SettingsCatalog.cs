using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using TagTuner.Core.Audio;
using TagTuner.Core.Model;
using TagTuner.Core.Safety;
using TagTuner.Core.Settings;
using TagTuner.Core.Shell;

using static TagTuner.App.Settings.SettingsUi;

namespace TagTuner.App.Settings;

/// <summary>
/// Welche Kategorien es gibt und was in ihnen steht.
///
/// Das ist die einzige Stelle, die von einzelnen Einstellungen weiß. Eine
/// Einstellung woandershin zu verschieben heißt, ihre Zeile in eine andere
/// Liste zu schieben; eine neue Kategorie ist ein Eintrag in
/// <see cref="Sections"/> mehr. Das Gerüst darum herum bleibt unberührt.
/// </summary>
internal static class SettingsCatalog
{
    public static readonly IReadOnlyList<SettingsSection> Sections =
    [
        new("TagTuner", "", App),
        new("Folders", "", Folders),
        new("Library", "", Library),
        new("Tags", "", Tags),
        new("Track list", "", TrackList),
        new("Backups", "", BackupsPage.Build),
    ];

    private static readonly int[] Rates = [44100, 48000, 88200, 96000, 176400, 192000];

    private static string RateLabel(int hz) =>
        hz % 1000 == 0 ? $"{hz / 1000} kHz" : $"{hz / 1000.0:0.0} kHz";

    // ══ TagTuner ═════════════════════════════════════════════════

    private static IEnumerable<FrameworkElement> App(SettingsContext c)
    {
        // ── Sprache ──────────────────────────────────────────────
        var language = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,

            // Unübersetzt: Jede Sprache nennt sich selbst. Wer Türkisch
            // sucht, sucht nach „Türkçe".
            ItemsSource = Strings.SupportedNames.ToList(),
            SelectedIndex = Math.Max(0, Array.IndexOf(Strings.Supported, Strings.Current)),
        };
        language.SelectionChanged += (_, _) =>
        {
            if (language.SelectedIndex < 0) return;
            c.Settings.Language = Strings.Supported[language.SelectedIndex];
            c.Save();
        };

        yield return Group("Language",
            language,
            Hint("Takes effect after a restart."));

        // ── Darstellung ──────────────────────────────────────────
        yield return Group("Appearance",
            Field("Theme", Choice(
                [("Follow the system", "system"), ("Dark", "dark"), ("Light", "light")],
                c.Settings.Theme,
                value => { c.Settings.Theme = value; c.Save(); c.Changed("theme"); })),
            Hint("Follows Windows and changes with it."));

        // ── Aktualisierung ───────────────────────────────────────
        var updateState = Hint(c.Settings.SkippedVersion is { Length: > 0 } skipped
            ? Strings.T("Version {0} was skipped.", skipped) : "");

        UpdateCheck? offered = null;

        var installBtn = new Button
        {
            Content = Strings.T("Download and install"),
            Visibility = Visibility.Collapsed,
        };
        installBtn.Click += (_, _) => { c.WantsInstall = offered; c.Close(); };

        var checkBtn = new Button { Content = Strings.T("Check for updates now") };
        checkBtn.Click += async (_, _) =>
        {
            checkBtn.IsEnabled = false;
            installBtn.Visibility = Visibility.Collapsed;
            offered = null;
            updateState.Text = Strings.T("Searching…");

            var found = await UpdateService.CheckAsync(AppInfo.Version);
            checkBtn.IsEnabled = true;

            updateState.Text = found switch
            {
                { Failed: true } => Strings.T("Check failed: {0}", found.Error ?? ""),
                { HasUpdate: true } => Strings.T("Version {0} is available.", found.Version ?? ""),
                _ => Strings.T("TagTuner is up to date ({0}).", AppInfo.Version),
            };

            if (found is { HasUpdate: true, SetupUrl: not null })
            {
                offered = found;
                installBtn.Visibility = Visibility.Visible;
            }
        };

        yield return Group("Updates",
            Field("On start", Choice(
                [("Never", "never"), ("Ask", "ask"), ("Automatically", "auto")],
                c.Settings.UpdateBehavior,
                value => { c.Settings.UpdateBehavior = value; c.Save(); })),
            Hint("\"Never\" stops any connection. \"Ask\" speaks up when there is something new. " +
                 "\"Automatically\" downloads and installs without asking."),
            updateState,
            Buttons(checkBtn, installBtn));

        // ── Explorer-Kontextmenü ─────────────────────────────────
        var shellState = Hint("");
        var regBtn = new Button { Content = Strings.T("Register") };
        var unregBtn = new Button { Content = Strings.T("Remove") };

        void RefreshShell()
        {
            var on = ContextMenuRegistration.IsRegistered();
            shellState.Text = Strings.T(on ? "Registered ✓" : "Not registered");
            regBtn.IsEnabled = !on;
            unregBtn.IsEnabled = on;
        }
        RefreshShell();

        regBtn.Click += (_, _) =>
        {
            try
            {
                ContextMenuRegistration.Register(Environment.ProcessPath!);
                RefreshShell();
            }
            catch (Exception ex) { shellState.Text = Strings.T("Failed: ") + ex.Message; }
        };
        unregBtn.Click += (_, _) => { ContextMenuRegistration.Unregister(); RefreshShell(); };

        yield return Group("Explorer context menu",
            shellState,
            Hint("Adds a TagTuner entry to the right click menu for audio files and folders. " +
                 "One entry, no submenu: it opens the folder the file is in. " +
                 "No administrator rights needed."),
            Buttons(regBtn, unregBtn));

        yield return Group("ffmpeg",
            Hint(FfmpegLocator.Find(c.Settings.FfmpegPath) ?? Strings.T("not found")));
    }

    // ══ Ordner ═══════════════════════════════════════════════════

    private static IEnumerable<FrameworkElement> Folders(SettingsContext c)
    {
        yield return Group("Default profile",
            Row(Field("File format", Pick(AudioFormats.Targets, c.Settings.DefaultFormat,
                    at => { c.Settings.DefaultFormat = AudioFormats.Targets[at]; c.Save(); })),
                Field("Sample rate", Pick(Rates.Select(RateLabel),
                    RateLabel(c.Settings.DefaultSampleRate),
                    at => { c.Settings.DefaultSampleRate = Rates[at]; c.Save(); }))),
            Hint("Only applies when a folder is empty or mixed. " +
                 "Uniform folders decide their own target."));

        // ── Album-Modus ──────────────────────────────────────────
        var baseTags = Tick("Base metadata", c.Settings.DefaultBaseTags,
            on => { c.Settings.DefaultBaseTags = on; c.Save(); });
        var cover = Tick("Cover", c.Settings.DefaultCover,
            on => { c.Settings.DefaultCover = on; c.Save(); });
        var numbering = Tick("Track numbering", c.Settings.DefaultNumbering,
            on => { c.Settings.DefaultNumbering = on; c.Save(); });

        var fileNames = Tick("File names follow the track numbers", c.Settings.DefaultRenameFiles,
            on => { c.Settings.DefaultRenameFiles = on; c.Save(); });

        void Enable(bool on) =>
            baseTags.IsEnabled = cover.IsEnabled = numbering.IsEnabled = fileNames.IsEnabled = on;
        Enable(c.Settings.DefaultAlbumMode);

        var albumMode = Tick("Album mode", c.Settings.DefaultAlbumMode, on =>
        {
            c.Settings.DefaultAlbumMode = on;
            c.Save();
            Enable(on);
        });

        var subTicks = new StackPanel { Spacing = 0, Margin = new Thickness(18, 0, 0, 0) };
        subTicks.Children.Add(baseTags);
        subTicks.Children.Add(cover);
        subTicks.Children.Add(numbering);
        subTicks.Children.Add(fileNames);

        yield return Group("Album mode",
            albumMode,
            Hint("For folders that are one release: an album, an EP, a single. It makes " +
                 "metadata uniform, so leave it off for folders where you collect mixed " +
                 "music. This is the default for folders without their own setting."),
            subTicks);

        // ── Beim Ablegen ─────────────────────────────────────────
        var ownRules = Hint("");
        void RefreshRules()
        {
            var n = c.Settings.FolderRules.Count;
            ownRules.Text = n == 0
                ? Strings.T("No folder deviates from this.")
                : Strings.T(n == 1
                    ? "{0} folder has its own setting and is not affected."
                    : "{0} folders have their own setting and are not affected.", n);
        }
        RefreshRules();

        var clearRules = Action("Reset per-folder settings", () =>
        {
            c.Settings.FolderRules.Clear();
            c.Save();
            RefreshRules();
            c.Changed("rules");
        });

        yield return Group("On drop",
            Tick("Align format and sample rate", c.Settings.DefaultAutoConform,
                on => { c.Settings.DefaultAutoConform = on; c.Save(); }),
            Hint("Applies to every folder. The folder analysis on the right can set " +
                 "this differently for a single folder; that setting wins and stays " +
                 "even when you change something here."),
            ownRules,
            clearRules);
    }

    // ══ Bibliothek ═══════════════════════════════════════════════

    private static IEnumerable<FrameworkElement> Library(SettingsContext c)
    {
        yield return Group("Library",
            Tick("Only show folders containing audio", c.Settings.OnlyAudioFolders,
                on => { c.Settings.OnlyAudioFolders = on; c.Save(); c.Changed("library"); }),
            Hint("Hides folders with no music anywhere below them. Expanding takes a " +
                 "little longer because every subfolder has to be checked."));

        // ── Eigene Wurzeln ───────────────────────────────────────
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 190,
            Margin = new Thickness(0, 2, 0, 2),
        };

        void RefreshRoots()
        {
            var at = list.SelectedIndex;
            list.ItemsSource = c.Settings.LibraryPaths.ToList();
            list.SelectedIndex = Math.Min(at, c.Settings.LibraryPaths.Count - 1);
        }
        RefreshRoots();

        var addBtn = AsyncAction("Add folder…", async () =>
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, c.Window);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null) return;

            if (c.Settings.LibraryPaths.Any(p =>
                    string.Equals(p, folder.Path, StringComparison.OrdinalIgnoreCase)))
                return;

            c.Settings.LibraryPaths.Add(folder.Path);
            c.Save();
            RefreshRoots();
            c.Changed("library");
        });

        var removeBtn = Action("Remove", () =>
        {
            var at = list.SelectedIndex;
            if (at < 0 || at >= c.Settings.LibraryPaths.Count) return;

            c.Settings.LibraryPaths.RemoveAt(at);
            c.Save();
            RefreshRoots();
            c.Changed("library");
        });

        // Die Reihenfolge ist die im Baum. Hoch und runter statt Ziehen:
        // In einer Liste von drei Einträgen ist Ziehen mehr Aufwand.
        void Move(int by)
        {
            var at = list.SelectedIndex;
            var to = at + by;
            if (at < 0 || to < 0 || to >= c.Settings.LibraryPaths.Count) return;

            (c.Settings.LibraryPaths[at], c.Settings.LibraryPaths[to]) =
                (c.Settings.LibraryPaths[to], c.Settings.LibraryPaths[at]);

            c.Save();
            RefreshRoots();
            list.SelectedIndex = to;
            c.Changed("library");
        }

        yield return Group("Your folders",
            list,
            Buttons(addBtn, removeBtn,
                    Action("Up", () => Move(-1)), Action("Down", () => Move(1))),
            Hint("Music, Downloads, your user folder and the drives are always there " +
                 "and are found fresh on every start. Only folders you added yourself " +
                 "are listed here."));

        // ── Ausgeblendete ────────────────────────────────────────
        var hidden = Hint("");
        var restore = Action("Show hidden again", () =>
        {
            c.Settings.HiddenRoots.Clear();
            c.Save();
            hidden.Text = Strings.T("The hidden folders are back");
            c.Changed("library");
        });

        void RefreshHidden()
        {
            var n = c.Settings.HiddenRoots.Count;
            hidden.Text = n == 0
                ? Strings.T("Nothing is hidden.")
                : string.Join(Environment.NewLine, c.Settings.HiddenRoots);
            restore.IsEnabled = n > 0;
        }
        RefreshHidden();

        yield return Group("Hidden folders",
            hidden,
            restore,
            Hint("Music, Downloads and the drives cannot be removed, only hidden. " +
                 "They do not come from a list that could be edited."));
    }

    // ══ Tags ═════════════════════════════════════════════════════

    private static IEnumerable<FrameworkElement> Tags(SettingsContext c)
    {
        yield return Group("Tags",
            Field("Paste tags", Choice(
                [("Everything except title and track number", "format"), ("Everything", "all")],
                c.Settings.TagPasteMode,
                value => { c.Settings.TagPasteMode = value; c.Save(); })),
            Hint("Copying takes artist, album, album artist, year, disc, genre, " +
                 "composer, comment and the cover. Title and track number are " +
                 "different in every file, so they only come along with \"everything\"."));

        var rename = new TextBox
        {
            Text = c.Settings.RenamePattern,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var example = Hint("");

        void RefreshExample() =>
            example.Text = Strings.T("Result: ") + rename.Text
                .Replace("{track}", "04").Replace("{title}", "Nebelfeld")
                .Replace("{artist}", "Kollektiv Halle").Replace("{album}", "Nachtfahrt")
                .Replace("{disc}", "1").Replace("{year}", "2025") + ".flac";

        rename.TextChanged += (_, _) =>
        {
            c.Settings.RenamePattern = rename.Text;
            c.Save();
            RefreshExample();
        };
        RefreshExample();

        yield return Group("Naming scheme",
            Field("Title → file name", rename),
            Hint(string.Join("  ", Core.Metadata.FileNaming.Placeholders)),
            example);
    }
    // ══ Trackliste ═══════════════════════════════════════════════

    private static IEnumerable<FrameworkElement> TrackList(SettingsContext c)
    {
        TrackColumns.EnsureStates(c.Settings);
        var states = c.Settings.TrackColumns;

        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 320,
            Margin = new Thickness(0, 2, 0, 2),
        };

        void Refresh()
        {
            var at = list.SelectedIndex;
            list.ItemsSource = null;
            list.ItemsSource = states.Select(Line).ToList();
            list.SelectedIndex = Math.Clamp(at, -1, states.Count - 1);
        }

        // Ein Haken je Zeile. Die Beschriftung kommt aus dem Katalog, damit
        // eine neue Spalte hier von selbst auftaucht.
        CheckBox Line(TrackColumnState state)
        {
            var column = TrackColumn.ById(state.Id)!;
            var label = column.Header.Length > 0
                ? Strings.T(column.Header) : Strings.T("Cover");

            var box = new CheckBox
            {
                Content = label,
                IsChecked = state.Visible,
                MinHeight = 28,
            };

            box.Checked += (_, _) => { state.Visible = true; c.Save(); c.Changed("columns"); };
            box.Unchecked += (_, _) => { state.Visible = false; c.Save(); c.Changed("columns"); };
            return box;
        }

        void Move(int by)
        {
            var at = list.SelectedIndex;
            var to = at + by;
            if (at < 0 || to < 0 || to >= states.Count) return;

            (states[at], states[to]) = (states[to], states[at]);
            c.Save();
            Refresh();
            list.SelectedIndex = to;
            c.Changed("columns");
        }

        Refresh();

        yield return Group("Columns",
            Hint("The order here is the order in the list. Pick a row and move it."),
            list,
            Buttons(Action("Up", () => Move(-1)), Action("Down", () => Move(1))));

        yield return Group("Title and artist",
            Tick("Show the artist under the title in one column",
                c.Settings.CombineTitleAndArtist,
                on =>
                {
                    c.Settings.CombineTitleAndArtist = on;
                    c.Save();
                    c.Changed("columns");
                }),
            Hint("Off means two columns of their own. The artist column is then " +
                 "moved and sized like any other."));

        yield return Group("Disc and track",
            Tick("Show disc and track number in one column",
                c.Settings.CombineDiscAndTrack,
                on =>
                {
                    c.Settings.CombineDiscAndTrack = on;
                    c.Save();
                    c.Changed("columns");
                }),
            Hint("In an album with several discs the disc number appears once, at " +
                 "the first track of each disc. Elsewhere it stands in front of the " +
                 "number, like 2-04. With a single disc only the number is shown."));

        yield return Group("Sorting",
            Tick("In playlist order, sort by disc first, then by track",
                c.Settings.SortByDiscThenTrack,
                on =>
                {
                    c.Settings.SortByDiscThenTrack = on;
                    c.Save();
                    c.Changed("sorting");
                }),
            Hint("Only for releases that span several discs. Without it the track " +
                 "number alone decides."));
    }
}
