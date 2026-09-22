using TagTuner.Core.Audio;
using TagTuner.Core.Safety;
using TagTuner.Core.Settings;
using TagTuner.Core.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TagTuner.App;

/// <summary>
/// Die Einstellungsseite. Bewusst ein Dialog und kein eigenes Fenster —
/// TagTuner arbeitet in einem Fenster.
/// </summary>
public static class SettingsDialog
{
    private static readonly int[] Rates = [44100, 48000, 88200, 96000, 176400, 192000];
    private static readonly int[] Bitrates = [128, 192, 256];

    private static readonly (string Label, string Value)[] Retentions =
    [
        ("Never", "never"),
        ("After 7 days", "7"),
        ("After 30 days", "30"),
    ];

    /// <summary>
    /// Zeigt die Einstellungen. Zurück kommt die Aktualisierung, die der
    /// Nutzer installieren möchte, sonst null. Der Dialog kann das nicht
    /// selbst tun: Die App muss sich dafür beenden, und dazu müsste sie sich
    /// erst schließen.
    /// </summary>
    public static async Task<UpdateCheck?> ShowAsync(
        XamlRoot root, AppSettings settings, HistoryStore history)
    {
        var backups = new BackupStore(settings.ResolvedBackupFolder);

        // ── Sprache ──────────────────────────────────────────────
        // Die Namen bleiben unübersetzt: Jede Sprache nennt sich selbst, so
        // wie ihre Sprecher sie schreiben. Wer Türkisch sucht, sucht nach
        // „Türkçe", nicht nach dem türkischen Wort in seiner eigenen Sprache.
        var language = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = Strings.SupportedNames.ToList(),
            SelectedIndex = Math.Max(0, Array.IndexOf(Strings.Supported, Strings.Current)),
        };

        // ── Standardprofil ───────────────────────────────────────
        var fmt = Combo(AudioFormats.Targets, settings.DefaultFormat);
        var rate = Combo(Rates.Select(FormatRate), FormatRate(settings.DefaultSampleRate));

        // ── Namensschema ─────────────────────────────────────────
        var parse = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[]
            {
                "{track} - {title}", "{track}. {title}",
                "{disc}-{track} {title}", "{artist} - {title}", "{title}",
            },
            SelectedItem = settings.ParsePattern,
        };
        if (parse.SelectedItem is null) parse.SelectedIndex = 0;

        var rename = new TextBox { Text = settings.RenamePattern };
        var renameExample = Hint("");
        void UpdateExample() =>
            renameExample.Text = Strings.T("Result: ") + rename.Text
                .Replace("{track}", "04").Replace("{title}", "Nebelfeld")
                .Replace("{artist}", "Kollektiv Halle").Replace("{album}", "Nachtfahrt")
                .Replace("{disc}", "1").Replace("{year}", "2025") + ".flac";
        rename.TextChanged += (_, _) => UpdateExample();
        UpdateExample();

        // ── Kodierung ────────────────────────────────────────────
        var kbps = Combo(Bitrates.Select(b => $"{b} kbps"), $"{settings.DefaultKbps} kbps");

        // ── Sicherungen ──────────────────────────────────────────
        var retention = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = Retentions.Select(r => Strings.T(r.Label)).ToList(),
            SelectedIndex = Math.Max(0, Array.FindIndex(Retentions, r => r.Value == settings.BackupRetention)),
        };

        var info = Hint("");
        var deleteBtn = new Button { Content = Strings.T("Delete all backups") };
        void RefreshInfo()
        {
            var (count, bytes) = backups.Info();
            info.Text = count == 0
                ? Strings.T("No backups stored.")
                : Strings.T("{0} file(s), {1} MB", count, $"{bytes / 1024.0 / 1024.0:0.#}");
            deleteBtn.IsEnabled = count > 0;
        }
        RefreshInfo();

        deleteBtn.Click += (_, _) =>
        {
            var gone = backups.DeleteAll();
            // Der Verlauf muss es erfahren, sonst bietet er ein Rückgängig an,
            // das ins Leere läuft.
            history.ForgetBackups(gone);
            RefreshInfo();
            info.Text += Strings.T(". Affected history entries can no longer be undone.");
        };

        // ── Aufbau ───────────────────────────────────────────────
        var panel = new StackPanel { Spacing = 14, Width = 460 };

        panel.Children.Add(Group("Language",
            language,
            Hint(Strings.T("Takes effect after a restart."))));

        panel.Children.Add(Group("Default profile",
            Row(Field("File format", fmt), Field("Sample rate", rate)),
            Hint("Only applies when a folder is empty or mixed. " +
                 "Uniform folders decide their own target.")));

        var onlyAudio = new CheckBox
        {
            Content = "Only show folders containing audio",
            IsChecked = settings.OnlyAudioFolders,
        };
        onlyAudio.Checked += (_, _) => settings.OnlyAudioFolders = true;
        onlyAudio.Unchecked += (_, _) => settings.OnlyAudioFolders = false;

        panel.Children.Add(Group("Library",
            onlyAudio,
            Hint("Hides folders with no music anywhere below them. Expanding takes a " +
                 "little longer because every subfolder has to be checked.")));

        var conform = new CheckBox
        {
            Content = "Align format and sample rate",
            IsChecked = settings.DefaultAutoConform,
        };
        conform.Checked += (_, _) => settings.DefaultAutoConform = true;
        conform.Unchecked += (_, _) => settings.DefaultAutoConform = false;

        var inherit = new CheckBox
        {
            Content = "Album mode",
            IsChecked = settings.DefaultAlbumMode,
        };
        inherit.Checked += (_, _) => settings.DefaultAlbumMode = true;
        inherit.Unchecked += (_, _) => settings.DefaultAlbumMode = false;

        var ownRules = Hint("");
        void RefreshRules()
        {
            var n = settings.FolderRules.Count;
            ownRules.Text = n == 0
                ? Strings.T("No folder deviates from this.")
                : Strings.T(n == 1
                    ? "{0} folder has its own setting and is not affected."
                    : "{0} folders have their own setting and are not affected.", n);
        }
        RefreshRules();

        var clearRules = new Button { Content = Strings.T("Reset per-folder settings") };
        clearRules.Click += (_, _) =>
        {
            settings.FolderRules.Clear();
            settings.Save();
            RefreshRules();
        };

        panel.Children.Add(Group("On drop",
            conform,
            inherit,
            Hint("Applies to every folder. The folder analysis on the right can set " +
                 "this differently for a single folder; that setting wins and stays " +
                 "even when you change something here."),
            ownRules,
            clearRules));

        var pasteModes = new[]
        {
            ("Everything except title and track number", "format"),
            ("Everything", "all"),
        };
        var pasteMode = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = pasteModes.Select(m => Strings.T(m.Item1)).ToList(),
            SelectedIndex = Math.Max(0,
                Array.FindIndex(pasteModes, m => m.Item2 == settings.TagPasteMode)),
        };

        panel.Children.Add(Group("Tags",
            Field(Strings.T("Paste tags"), pasteMode),
            Hint("Copying takes artist, album, album artist, year, disc, genre, " +
                 "composer, comment and the cover. Title and track number are " +
                 "different in every file, so they only come along with \"everything\".")));

        panel.Children.Add(Group("Naming scheme",
            Field("File name → title", parse),
            Field("Title → file name", rename),
            renameExample));

        panel.Children.Add(Group("Encoding",
            Field("Default bitrate", kbps),
            Hint("Only used when a conversion encodes lossily anyway. Existing files " +
                 "are never touched because of their bitrate.")));

        panel.Children.Add(Group("Backups",
            Field("Delete automatically", retention),
            Hint("Runs on start. Older backups are removed, and the history entries " +
                 "that belong to them can no longer be undone afterwards."),
            info,
            deleteBtn,
            Hint(settings.ResolvedBackupFolder)));

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
        unregBtn.Click += (_, _) =>
        {
            ContextMenuRegistration.Unregister();
            RefreshShell();
        };

        var shellRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Children = { regBtn, unregBtn },
        };

        panel.Children.Add(Group("Explorer context menu",
            shellState,
            Hint(Strings.T(
                "Adds a TagTuner entry to the right click menu for audio files and folders. " +
                "One entry, no submenu: it opens the folder the file is in. " +
                "No administrator rights needed.")),
            shellRow));

        // ── Aktualisierung ───────────────────────────────────────
        var behaviours = new[] { ("Never", "never"), ("Ask", "ask"), ("Automatically", "auto") };
        var updateMode = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = behaviours.Select(b => Strings.T(b.Item1)).ToList(),
            SelectedIndex = Math.Max(0, Array.FindIndex(behaviours, b => b.Item2 == settings.UpdateBehavior)),
        };

        var updateState = Hint(settings.SkippedVersion is { Length: > 0 } skipped
            ? Strings.T("Version {0} was skipped.", skipped)
            : "");

        // Der Dialog schliesst sich zum Installieren, deshalb muss der Klick
        // beides erreichen: das Gefundene und das Fenster.
        UpdateCheck? offered = null;
        UpdateCheck? wanted = null;
        ContentDialog? host = null;

        var installBtn = new Button
        {
            Content = Strings.T("Download and install"),
            Visibility = Visibility.Collapsed,
        };
        installBtn.Click += (_, _) => { wanted = offered; host?.Hide(); };

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

            // Ohne Setup in der Veröffentlichung gäbe es nichts zu starten;
            // dann bleibt es bei der Meldung.
            if (found is { HasUpdate: true, SetupUrl: not null })
            {
                offered = found;
                installBtn.Visibility = Visibility.Visible;
            }
        };

        var updateRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Children = { checkBtn, installBtn },
        };

        panel.Children.Add(Group("Updates",
            Field(Strings.T("On start"), updateMode),
            Hint(Strings.T("\"Never\" stops any connection. \"Ask\" speaks up when there is something new. " +
                           "\"Automatically\" downloads and installs without asking.")),
            updateState,
            updateRow));

        panel.Children.Add(Group("ffmpeg",
            Hint(FfmpegLocator.Find(settings.FfmpegPath) ?? Strings.T("not found"))));

        var dlg = new ContentDialog
        {
            Title = "Settings",
            Content = new ScrollViewer { Content = panel, MaxHeight = 560 },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root,
        };

        Localizer.Apply(dlg);
        host = dlg;

        var answer = await dlg.ShowAsync();

        // Hide() beim Installieren liefert None. Die übrigen Änderungen sollen
        // trotzdem erhalten bleiben, denn gleich startet die App neu.
        if (wanted is null && answer != ContentDialogResult.Primary) return null;

        settings.DefaultFormat = fmt.SelectedItem as string ?? settings.DefaultFormat;
        if (rate.SelectedIndex >= 0) settings.DefaultSampleRate = Rates[rate.SelectedIndex];
        if (kbps.SelectedIndex >= 0) settings.DefaultKbps = Bitrates[kbps.SelectedIndex];
        settings.ParsePattern = parse.SelectedItem as string ?? settings.ParsePattern;
        settings.RenamePattern = rename.Text;
        if (retention.SelectedIndex >= 0) settings.BackupRetention = Retentions[retention.SelectedIndex].Value;
        if (pasteMode.SelectedIndex >= 0) settings.TagPasteMode = pasteModes[pasteMode.SelectedIndex].Item2;
        if (language.SelectedIndex >= 0)
            settings.Language = Strings.Supported[language.SelectedIndex];
        if (updateMode.SelectedIndex >= 0) settings.UpdateBehavior = behaviours[updateMode.SelectedIndex].Item2;

        // Wer jetzt installiert, hat die Version offensichtlich nicht mehr
        // übersprungen.
        if (wanted is not null) settings.SkippedVersion = null;

        settings.Save();
        return wanted;
    }

    // ── Bausteine ────────────────────────────────────────────────

    private static string FormatRate(int hz) =>
        hz % 1000 == 0 ? $"{hz / 1000} kHz" : $"{hz / 1000.0:0.0} kHz";

    private static ComboBox Combo(IEnumerable<string> items, string? selected)
    {
        var list = items.ToList();
        var box = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = list,
        };
        var idx = list.FindIndex(x => string.Equals(x, selected, StringComparison.OrdinalIgnoreCase));
        box.SelectedIndex = idx >= 0 ? idx : 0;
        return box;
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 11.5,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.65,
    };

    private static StackPanel Field(string label, FrameworkElement control)
    {
        var sp = new StackPanel { Spacing = 3 };
        sp.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Opacity = 0.8 });
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        sp.Children.Add(control);
        return sp;
    }

    private static Grid Row(params FrameworkElement[] cells)
    {
        var g = new Grid { ColumnSpacing = 10 };
        for (var i = 0; i < cells.Length; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(cells[i], i);
            g.Children.Add(cells[i]);
        }
        return g;
    }

    private static Border Group(string title, params FrameworkElement[] content)
    {
        var sp = new StackPanel { Spacing = 9 };
        sp.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Opacity = 0.6,
        });
        foreach (var c in content) sp.Children.Add(c);

        return new Border
        {
            Padding = new Thickness(13),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)
                Application.Current.Resources["ControlStrokeColorDefaultBrush"],
            Child = sp,
        };
    }
}
