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
        ("Nie", "never"),
        ("Nach 7 Tagen", "7"),
        ("Nach 30 Tagen", "30"),
    ];

    public static async Task ShowAsync(XamlRoot root, AppSettings settings, HistoryStore history)
    {
        var backups = new BackupStore(settings.ResolvedBackupFolder);

        // ── Sprache ──────────────────────────────────────────────
        var languages = new[] { ("Deutsch", "de"), ("Englisch", "en") };
        var language = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = languages.Select(l => Strings.T(l.Item1)).ToList(),
            SelectedIndex = Strings.Current == "en" ? 1 : 0,
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
            renameExample.Text = "Ergebnis: " + rename.Text
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
            ItemsSource = Retentions.Select(r => r.Label).ToList(),
            SelectedIndex = Math.Max(0, Array.FindIndex(Retentions, r => r.Value == settings.BackupRetention)),
        };

        var info = Hint("");
        var deleteBtn = new Button { Content = "Alle Sicherungen löschen" };
        void RefreshInfo()
        {
            var (count, bytes) = backups.Info();
            info.Text = count == 0
                ? "Keine Sicherungen gespeichert."
                : $"{count} Datei(en), {bytes / 1024.0 / 1024.0:0.#} MB";
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
            info.Text += ". Betroffene Verlaufseinträge sind nicht mehr rückgängig machbar.";
        };

        // ── Aufbau ───────────────────────────────────────────────
        var panel = new StackPanel { Spacing = 14, Width = 460 };

        panel.Children.Add(Group("Sprache",
            language,
            Hint(Strings.T("Wirkt nach einem Neustart der App."))));

        panel.Children.Add(Group("Standardprofil",
            Row(Field("Dateiformat", fmt), Field("Samplerate", rate)),
            Hint("Gilt nur, wenn ein Ordner leer oder uneinheitlich ist. " +
                 "Einheitliche Ordner bestimmen ihr Ziel selbst.")));

        var onlyAudio = new CheckBox
        {
            Content = "Nur Ordner mit Audiodateien zeigen",
            IsChecked = settings.OnlyAudioFolders,
        };
        onlyAudio.Checked += (_, _) => settings.OnlyAudioFolders = true;
        onlyAudio.Unchecked += (_, _) => settings.OnlyAudioFolders = false;

        panel.Children.Add(Group("Bibliothek",
            onlyAudio,
            Hint("Blendet Ordner aus, unter denen nirgends Musik liegt. Das Aufklappen " +
                 "dauert dadurch etwas länger, weil dafür in jeden Unterordner geschaut " +
                 "werden muss.")));

        var conform = new CheckBox
        {
            Content = "Format und Samplerate angleichen",
            IsChecked = settings.DefaultAutoConform,
        };
        conform.Checked += (_, _) => settings.DefaultAutoConform = true;
        conform.Unchecked += (_, _) => settings.DefaultAutoConform = false;

        var inherit = new CheckBox
        {
            Content = "Tags vom Ordner übernehmen",
            IsChecked = settings.DefaultInheritTags,
        };
        inherit.Checked += (_, _) => settings.DefaultInheritTags = true;
        inherit.Unchecked += (_, _) => settings.DefaultInheritTags = false;

        var ownRules = Hint("");
        void RefreshRules()
        {
            var n = settings.FolderRules.Count;
            ownRules.Text = n == 0
                ? "Kein Ordner weicht davon ab."
                : $"{n} Ordner {(n == 1 ? "hat" : "haben")} eine eigene Einstellung und " +
                  "bleibt davon unberührt.";
        }
        RefreshRules();

        var clearRules = new Button { Content = "Eigene Ordnereinstellungen zurücksetzen" };
        clearRules.Click += (_, _) =>
        {
            settings.FolderRules.Clear();
            settings.Save();
            RefreshRules();
        };

        panel.Children.Add(Group("Beim Ablegen",
            conform,
            inherit,
            Hint("Gilt für alle Ordner. In der Ordner-Analyse rechts lässt sich das " +
                 "für einzelne Ordner abweichend einstellen; eine solche Einstellung " +
                 "gewinnt und bleibt auch bestehen, wenn du hier etwas änderst."),
            ownRules,
            clearRules));

        panel.Children.Add(Group("Namensschema",
            Field("Dateiname → Titel", parse),
            Field("Titel → Dateiname", rename),
            renameExample));

        panel.Children.Add(Group("Kodierung",
            Field("Standard-Bitrate", kbps),
            Hint("Wird nur herangezogen, wenn eine Konvertierung ohnehin verlustbehaftet " +
                 "kodiert. Vorhandene Dateien werden nie wegen ihrer Bitrate angefasst.")));

        panel.Children.Add(Group("Sicherungen",
            Field("Automatisch löschen", retention),
            Hint("Läuft beim Start. Ältere Sicherungen werden entfernt, die zugehörigen " +
                 "Verlaufseinträge lassen sich danach nicht mehr rückgängig machen."),
            info,
            deleteBtn,
            Hint(settings.ResolvedBackupFolder)));

        // ── Explorer-Kontextmenü ─────────────────────────────────
        var shellState = Hint("");
        var regBtn = new Button { Content = "Registrieren" };
        var unregBtn = new Button { Content = "Entfernen" };

        void RefreshShell()
        {
            var on = ContextMenuRegistration.IsRegistered();
            shellState.Text = on ? "Eingetragen ✓" : "Nicht eingetragen";
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
            catch (Exception ex) { shellState.Text = "Fehlgeschlagen: " + ex.Message; }
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

        panel.Children.Add(Group("Explorer-Kontextmenü",
            shellState,
            Hint("Fügt „TagTuner“ zum Rechtsklick-Menü für Audiodateien und Ordner hinzu. " +
                 "Ein einzelner Eintrag. Welcher Bereich geöffnet wird, fragt die App. " +
                 "Keine Administratorrechte nötig."),
            shellRow));

        // ── Aktualisierung ───────────────────────────────────────
        var behaviours = new[] { ("Nie", "never"), ("Fragen", "ask"), ("Automatisch", "auto") };
        var updateMode = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = behaviours.Select(b => Strings.T(b.Item1)).ToList(),
            SelectedIndex = Math.Max(0, Array.FindIndex(behaviours, b => b.Item2 == settings.UpdateBehavior)),
        };

        var updateState = Hint(settings.SkippedVersion is { Length: > 0 } skipped
            ? Strings.T("Version {0} wurde übersprungen.", skipped)
            : "");

        var checkBtn = new Button { Content = Strings.T("Jetzt nach Updates suchen") };
        checkBtn.Click += async (_, _) =>
        {
            checkBtn.IsEnabled = false;
            updateState.Text = Strings.T("Wird gesucht…");

            var found = await UpdateService.CheckAsync(AppInfo.Version);
            checkBtn.IsEnabled = true;

            updateState.Text = found switch
            {
                { Failed: true } => Strings.T("Suche fehlgeschlagen: {0}", found.Error ?? ""),
                { HasUpdate: true } => Strings.T("Version {0} ist verfügbar.", found.Version ?? ""),
                _ => Strings.T("TagTuner ist aktuell ({0}).", AppInfo.Version),
            };
        };

        panel.Children.Add(Group("Aktualisierung",
            Field(Strings.T("Beim Start"), updateMode),
            Hint(Strings.T("„Nie“ verhindert jede Verbindung. „Fragen“ meldet sich, wenn es etwas Neues gibt. " +
                           "„Automatisch“ lädt und installiert ohne Rückfrage.")),
            updateState,
            checkBtn));

        panel.Children.Add(Group("ffmpeg",
            Hint(FfmpegLocator.Find(settings.FfmpegPath) ?? "nicht gefunden")));

        var dlg = new ContentDialog
        {
            Title = "Einstellungen",
            Content = new ScrollViewer { Content = panel, MaxHeight = 560 },
            PrimaryButtonText = "Speichern",
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root,
        };

        Localizer.Apply(dlg);
        if (await dlg.ShowAsync() != ContentDialogResult.Primary) return;

        settings.DefaultFormat = fmt.SelectedItem as string ?? settings.DefaultFormat;
        if (rate.SelectedIndex >= 0) settings.DefaultSampleRate = Rates[rate.SelectedIndex];
        if (kbps.SelectedIndex >= 0) settings.DefaultKbps = Bitrates[kbps.SelectedIndex];
        settings.ParsePattern = parse.SelectedItem as string ?? settings.ParsePattern;
        settings.RenamePattern = rename.Text;
        if (retention.SelectedIndex >= 0) settings.BackupRetention = Retentions[retention.SelectedIndex].Value;
        if (language.SelectedIndex >= 0) settings.Language = languages[language.SelectedIndex].Item2;
        if (updateMode.SelectedIndex >= 0) settings.UpdateBehavior = behaviours[updateMode.SelectedIndex].Item2;
        settings.Save();
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
