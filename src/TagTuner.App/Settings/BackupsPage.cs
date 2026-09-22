using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using TagTuner.Core.Audio;
using TagTuner.Core.Model;
using TagTuner.Core.Safety;
using TagTuner.Core.Settings;

using static TagTuner.App.Settings.SettingsUi;

namespace TagTuner.App.Settings;

/// <summary>
/// Die Seite „Sicherungen": oben die Einstellungen, darunter jede einzelne
/// Sicherung, die jüngste zuerst, zum Wiederherstellen oder Löschen.
///
/// Wiederherstellen fragt nicht nach: Die Fassung, die gerade an der Stelle
/// liegt, wird vorher selbst gesichert, und der Verlauf kann es zurücknehmen.
/// Löschen ist endgültig und fragt darum.
/// </summary>
internal static class BackupsPage
{
    /// <summary>Eine Zeile der Liste. Die Tags werden erst gelesen, wenn die Zeile zu sehen ist.</summary>
    private sealed class Row(BackupItem item)
    {
        public BackupItem Item { get; } = item;
        public AudioTrack? Track { get; set; }
        public bool Reading { get; set; }
    }

    public static IEnumerable<FrameworkElement> Build(SettingsContext c)
    {
        var store = new BackupStore(c.Settings.ResolvedBackupFolder);
        var rows = new ObservableCollection<Row>();

        // ── Einstellungen ────────────────────────────────────────
        var info = Hint("");
        var deleteAllBtn = new Button { Content = Strings.T("Delete all backups") };

        yield return Group("Backups",
            Field("Delete automatically", Choice(
                [("Never", "never"), ("After 7 days", "7"), ("After 30 days", "30")],
                c.Settings.BackupRetention,
                value => { c.Settings.BackupRetention = value; c.Save(); })),
            Hint("Runs on start. Older backups are removed, and the history entries " +
                 "that belong to them can no longer be undone afterwards."),
            info,
            Buttons(deleteAllBtn, Action("Open backup folder", () =>
            {
                // Anlegen, falls es ihn noch nicht gibt: Sonst öffnet der
                // Explorer stattdessen „Dokumente", und man sucht an der
                // falschen Stelle.
                var folder = c.Settings.ResolvedBackupFolder;
                try { System.IO.Directory.CreateDirectory(folder); } catch { }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folder}\"",
                    UseShellExecute = true,
                });
            })),
            Hint(c.Settings.ResolvedBackupFolder));

        // ── Liste ────────────────────────────────────────────────
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Multiple,
            Height = 420,
            ItemsSource = rows,
            ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Grid /></DataTemplate>"),
            ItemContainerStyle = RowStyle(),
        };

        var selectAll = new CheckBox { Content = Strings.T("Select all"), MinWidth = 0 };
        var restoreBtn = new Button { Content = Strings.T("Restore"), IsEnabled = false };
        var deleteBtn = new Button { Content = Strings.T("Delete"), IsEnabled = false };
        var status = Hint("");

        var empty = Hint("No backups stored.");

        void Refresh()
        {
            var items = BackupCatalog.List(store, c.History);
            rows.Clear();
            foreach (var item in items) rows.Add(new Row(item));

            var bytes = items.Sum(i => i.Size);
            info.Text = items.Count == 0
                ? Strings.T("No backups stored.")
                : Strings.T("{0} file(s), {1} MB", items.Count, $"{bytes / 1024.0 / 1024.0:0.#}");
            deleteAllBtn.IsEnabled = items.Count > 0;
            empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            list.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            UpdateButtons();
        }

        void UpdateButtons()
        {
            var n = list.SelectedItems.Count;
            restoreBtn.Content = n > 0 ? Strings.T("Restore ({0})", n) : Strings.T("Restore");
            deleteBtn.Content = n > 0 ? Strings.T("Delete ({0})", n) : Strings.T("Delete");
            restoreBtn.IsEnabled = deleteBtn.IsEnabled = n > 0;

            selectAll.Checked -= OnSelectAll;
            selectAll.Unchecked -= OnSelectNone;
            selectAll.IsChecked = n > 0 && n == rows.Count ? true : n == 0 ? false : null;
            selectAll.Checked += OnSelectAll;
            selectAll.Unchecked += OnSelectNone;
        }

        void OnSelectAll(object sender, RoutedEventArgs e) => list.SelectAll();
        void OnSelectNone(object sender, RoutedEventArgs e) => list.SelectedItems.Clear();
        selectAll.Checked += OnSelectAll;
        selectAll.Unchecked += OnSelectNone;
        selectAll.Indeterminate += (_, _) => list.SelectedItems.Clear();

        list.SelectionChanged += (_, _) => UpdateButtons();

        // ── Wiederherstellen ─────────────────────────────────────
        async Task RestoreAsync(IReadOnlyList<Row> chosen)
        {
            if (chosen.Count == 0) return;

            // Für Sicherungen ohne Verlaufseintrag ist der alte Ort unbekannt.
            // Gefragt wird einmal für alle.
            string? folder = null;
            if (chosen.Any(r => r.Item.OriginalPath is null))
            {
                folder = await PickFolderAsync(c.Window);
                if (folder is null) return;
            }

            var files = new List<HistoryFile>();
            var failed = new List<string>();

            // Nie dieselbe Datei zweimal: Von einer Datei gibt es oft mehrere
            // Sicherungen, und zurück soll die jüngste der gewählten.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in chosen.OrderByDescending(r => r.Item.Created))
            {
                var target = row.Item.OriginalPath ?? Path.Combine(folder!, row.Item.OriginalName);
                if (!seen.Add(target)) continue;

                try
                {
                    c.Changed("release:" + target);
                    files.AddRange(c.History.Restore(row.Item.BackupPath, store,
                        row.Item.OriginalPath is null ? target : null));
                }
                catch (Exception ex)
                {
                    failed.Add($"{row.Item.OriginalName}: {ex.Message}");
                }
            }

            if (files.Count > 0)
            {
                c.History.Add("restore", Strings.T("{0} backup(s) restored", seen.Count - failed.Count), files);
                c.Changed("files");
            }

            Refresh();
            status.Text = failed.Count == 0
                ? Strings.T("{0} file(s) restored. The history can undo it.", seen.Count)
                : Strings.T("{0} restored, {1} failed: {2}", seen.Count - failed.Count, failed.Count,
                            string.Join("; ", failed));
        }

        // ── Löschen ──────────────────────────────────────────────
        void Delete(IReadOnlyList<Row> chosen)
        {
            var gone = new List<string>();
            foreach (var row in chosen)
            {
                try { File.Delete(row.Item.BackupPath); gone.Add(row.Item.BackupPath); } catch { }
            }
            c.History.ForgetBackups(gone);
            Refresh();
            status.Text = Strings.T("{0} backup(s) deleted.", gone.Count);
        }

        restoreBtn.Click += async (_, _) => await RestoreAsync(list.SelectedItems.OfType<Row>().ToList());
        deleteBtn.Flyout = ConfirmFlyout(Strings.T("Delete the selected backups for good?"),
            Strings.T("Delete"), () => Delete(list.SelectedItems.OfType<Row>().ToList()));

        deleteAllBtn.Click += (_, _) =>
        {
            var gone = store.DeleteAll();

            // Der Verlauf muss es erfahren, sonst bietet er ein Rückgängig
            // an, das ins Leere läuft.
            c.History.ForgetBackups(gone);
            Refresh();
            info.Text += Strings.T(". Affected history entries can no longer be undone.");
        };

        // ── Zeilen ───────────────────────────────────────────────
        list.ContainerContentChanging += (_, args) =>
        {
            if (args.InRecycleQueue || args.Item is not Row row) return;
            if (args.ItemContainer is not ListViewItem { ContentTemplateRoot: Grid grid }) return;

            if (grid.Tag is not RowParts parts)
            {
                parts = BuildRow(grid);
                grid.Tag = parts;
                parts.Restore.Click += async (_, _) =>
                {
                    if (grid.DataContext is Row r) await RestoreAsync([r]);
                };
                parts.Delete.Flyout = ConfirmFlyout(Strings.T("Delete this backup for good?"),
                    Strings.T("Delete"), () => { if (grid.DataContext is Row r) Delete([r]); });
            }

            grid.DataContext = row;
            Fill(parts, row);
            args.Handled = true;

            // Tags erst lesen, wenn die Zeile zu sehen ist: Bei hunderten
            // Sicherungen wäre alles vorab zu lesen eine lange Pause.
            if (row.Track is null && !row.Reading)
            {
                row.Reading = true;
                Task.Run(() => AudioProbe.Read(row.Item.BackupPath)).ContinueWith(t =>
                {
                    list.DispatcherQueue.TryEnqueue(() =>
                    {
                        row.Track = t.Result;
                        if (ReferenceEquals(grid.DataContext, row)) Fill(parts, row);
                    });
                });
            }
        };

        Refresh();

        yield return Group("Stored backups",
            Hint("Newest first. Restoring puts the file back where it came from; the "
                 + "version there now is backed up first, so the history can undo it."),
            new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
                Children = { selectAll, WithColumn(Buttons(restoreBtn, deleteBtn), 2) },
            },
            status,
            empty,
            list);
    }

    private static FrameworkElement WithColumn(FrameworkElement element, int column)
    {
        Grid.SetColumn(element, column);
        element.VerticalAlignment = VerticalAlignment.Center;
        return element;
    }

    // ══ Zeile ════════════════════════════════════════════════════

    private sealed record RowParts(
        Image Cover, TextBlock Title, TextBlock Artist, TextBlock Where,
        TextBlock When, TextBlock Size, Button Restore, Button Delete)
    {
        /// <summary>Bei welcher Aktion die Sicherung entstand.</summary>
        public TextBlock? Action { get; init; }
    }

    /// <summary>
    /// Eine Zeile wie in der Trackliste, nur knapper: Cover, Titel mit
    /// Interpret, woher die Datei stammt, wann gesichert, und die beiden
    /// Knöpfe.
    /// </summary>
    private static RowParts BuildRow(Grid grid)
    {
        var dim = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
        var fill = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"];

        grid.Height = 50;
        grid.ColumnSpacing = 10;
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var cover = new Image { Stretch = Stretch.UniformToFill };
        var coverBox = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(4),
            Background = fill,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Grid
            {
                Children =
                {
                    new FontIcon { Glyph = "", FontSize = 13, Foreground = dim },
                    cover,
                },
            },
        };

        var title = new TextBlock { FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
        var artist = new TextBlock { FontSize = 11.5, Foreground = dim, TextTrimming = TextTrimming.CharacterEllipsis };
        var names = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 1,
            Children = { title, artist },
        };

        var where = new TextBlock { FontSize = 11.5, Foreground = dim, TextTrimming = TextTrimming.CharacterEllipsis };
        var action = new TextBlock { FontSize = 11, Foreground = dim, TextTrimming = TextTrimming.CharacterEllipsis };
        var origin = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 1,
            Children = { where, action },
        };

        var when = new TextBlock { FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Right };
        var size = new TextBlock
        {
            FontSize = 11,
            Foreground = dim,
            FontFamily = new FontFamily("Consolas"),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var stamp = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 1,
            Children = { when, size },
        };

        var restore = IconButton("", "Restore");
        var delete = IconButton("", "Delete");
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { restore, delete },
        };

        Grid.SetColumn(names, 1);
        Grid.SetColumn(origin, 2);
        Grid.SetColumn(stamp, 3);
        Grid.SetColumn(buttons, 4);
        grid.Children.Add(coverBox);
        grid.Children.Add(names);
        grid.Children.Add(origin);
        grid.Children.Add(stamp);
        grid.Children.Add(buttons);

        // Die Beschreibung der Aktion hängt an der zweiten Zeile der Herkunft.
        return new RowParts(cover, title, artist, where, when, size, restore, delete) { Action = action };
    }

    private static void Fill(RowParts parts, Row row)
    {
        var item = row.Item;

        parts.Title.Text = row.Track is { } t && !string.IsNullOrWhiteSpace(t.Title)
            ? t.Title : Path.GetFileNameWithoutExtension(item.OriginalName);
        parts.Artist.Text = row.Track?.Artist ?? "";

        parts.Where.Text = item.OriginalPath is { } original
            ? Path.GetFileName(Path.GetDirectoryName(original)) + " › " + Path.GetFileName(original)
            : item.OriginalName;
        ToolTipService.SetToolTip(parts.Where, item.OriginalPath ?? Strings.T("The original location is unknown."));

        parts.Action!.Text = item.Action ?? Strings.T("Not in the history");

        parts.When.Text = Relative(item.Created);
        ToolTipService.SetToolTip(parts.When, item.Created.ToString("g"));
        parts.Size.Text = $"{item.Size / 1024.0 / 1024.0:0.0} MB";

        // Das Cover steckt in der Sicherung selbst: So sieht man, wie die
        // Datei damals aussah, nicht wie sie jetzt aussieht.
        TrackArt.SetPath(parts.Cover, row.Track?.HasCover == true ? item.BackupPath : null);
    }

    /// <summary>Wann, so wie man es sagt: „vor 5 Min.", „gestern 14:03", „12.03.2025".</summary>
    private static string Relative(DateTime when)
    {
        var span = DateTime.Now - when;
        if (span.TotalMinutes < 1) return Strings.T("just now");
        if (span.TotalMinutes < 60) return Strings.T("{0} min ago", (int)span.TotalMinutes);
        if (when.Date == DateTime.Today) return Strings.T("today {0}", when.ToString("t"));
        if (when.Date == DateTime.Today.AddDays(-1)) return Strings.T("yesterday {0}", when.ToString("t"));
        return when.ToString("d");
    }

    private static Button IconButton(string glyph, string tip)
    {
        var button = new Button
        {
            Content = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 12,
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(button, Strings.T(tip));
        return button;
    }

    /// <summary>
    /// Eine Rückfrage direkt am Knopf. Die Einstellungen sind selbst ein
    /// Dialog, und WinUI erlaubt keinen zweiten darüber.
    /// </summary>
    private static Flyout ConfirmFlyout(string question, string confirm, System.Action run)
    {
        var flyout = new Flyout();
        var yes = new Button
        {
            Content = confirm,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
        };
        yes.Click += (_, _) => { flyout.Hide(); run(); };

        flyout.Content = new StackPanel
        {
            Spacing = 10,
            MaxWidth = 260,
            Children =
            {
                new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap },
                yes,
            },
        };
        return flyout;
    }

    private static Style RowStyle()
    {
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 0, 6, 0)));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 50.0));
        return style;
    }

    private static async Task<string?> PickFolderAsync(IntPtr window)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, window);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}
