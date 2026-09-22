using System.Collections.ObjectModel;
using TagTuner.Core.Safety;
using TagTuner.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using TagTuner.App.Settings;

namespace TagTuner.App;

/// <summary>
/// Der Verlauf mit Rückgängig, im selben Aufbau wie die Sicherungen in den
/// Einstellungen: eine knappe Liste, der jüngste Eintrag zuerst, mit Haken
/// zum Wählen und Rückgängig für einen oder mehrere Einträge auf einmal.
///
/// Ohne diese Ansicht wären die Sicherungen wertlos: Sie werden bei jedem
/// Schreibvorgang angelegt, aber ohne Weg, sie zurückzuspielen, kosten sie
/// nur Platz.
/// </summary>
public static class HistoryDialog
{
    private sealed class Row(HistoryEntry entry)
    {
        public HistoryEntry Entry { get; } = entry;
        public bool Selected { get; set; }
    }

    private sealed record RowParts(
        CheckBox Check, Image Cover, TextBlock Title, TextBlock Files, TextBlock Where,
        TextBlock State, TextBlock When, TextBlock Count, Button Undo);

    /// <summary>Gibt true zurück, wenn etwas zurückgespielt wurde — dann muss neu eingelesen werden.</summary>
    /// <param name="release">Gibt Dateien frei, die gleich überschrieben werden, etwa im Player.</param>
    public static async Task<bool> ShowAsync(XamlRoot root, HistoryStore history, Action<IEnumerable<string>> release)
    {
        var undone = false;
        ContentDialog? dialog = null;
        var rows = new ObservableCollection<Row>();
        var filling = false;

        // ── Kopf ─────────────────────────────────────────────────
        var close = BackupsPage.IconButton("", "Close");
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 0, 8, 0);
        close.Click += (_, _) => dialog?.Hide();

        var top = new Grid
        {
            Height = 52,
            Children =
            {
                new TextBlock
                {
                    Text = Strings.T("History"),
                    FontSize = 18,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(20, 0, 0, 0),
                },
                close,
            },
        };

        // ── Werkzeuge ────────────────────────────────────────────
        var selectAll = new CheckBox { Content = Strings.T("Select all"), MinWidth = 0 };
        var undoBtn = new Button { Content = Strings.T("Undo"), IsEnabled = false };
        var clearBtn = new Button { Content = Strings.T("Clear the history") };
        var status = new TextBlock
        {
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        };
        var empty = new TextBlock
        {
            Text = Strings.T("Nothing has happened yet."),
            Opacity = 0.6,
            Margin = new Thickness(0, 8, 0, 8),
        };

        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = true,
            ItemsSource = rows,
            ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Grid /></DataTemplate>"),
            ItemContainerStyle = BackupsPage.RowStyle(58),
        };

        void Refresh()
        {
            rows.Clear();
            foreach (var entry in history.Entries.OrderByDescending(e => e.Timestamp)) rows.Add(new Row(entry));

            var none = rows.Count == 0;
            empty.Visibility = none ? Visibility.Visible : Visibility.Collapsed;
            list.Visibility = none ? Visibility.Collapsed : Visibility.Visible;
            clearBtn.IsEnabled = !none;
            UpdateButtons();
        }

        void RefreshChecks()
        {
            filling = true;
            foreach (var row in rows)
            {
                if (list.ContainerFromItem(row) is ListViewItem { ContentTemplateRoot: Grid { Tag: RowParts parts } })
                    parts.Check.IsChecked = row.Selected;
            }
            filling = false;
        }

        void UpdateButtons()
        {
            var chosen = rows.Where(r => r.Selected).ToList();
            var n = chosen.Count;
            undoBtn.Content = n > 0 ? Strings.T("Undo ({0})", n) : Strings.T("Undo");
            undoBtn.IsEnabled = chosen.Any(r => r.Entry.CanUndo);

            selectAll.Checked -= OnSelectAll;
            selectAll.Unchecked -= OnSelectNone;
            selectAll.IsChecked = n > 0 && n == rows.Count ? true : n == 0 ? false : null;
            selectAll.Checked += OnSelectAll;
            selectAll.Unchecked += OnSelectNone;
        }

        void SelectAll(bool on)
        {
            foreach (var row in rows) row.Selected = on;
            RefreshChecks();
            UpdateButtons();
        }

        void OnSelectAll(object sender, RoutedEventArgs e) => SelectAll(true);
        void OnSelectNone(object sender, RoutedEventArgs e) => SelectAll(false);
        selectAll.Checked += OnSelectAll;
        selectAll.Unchecked += OnSelectNone;

        list.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is not Row row) return;
            row.Selected = !row.Selected;
            RefreshChecks();
            UpdateButtons();
        };

        // ── Rückgängig ───────────────────────────────────────────
        void Undo(IReadOnlyList<Row> chosen)
        {
            int restored = 0, failed = 0;
            var errors = new List<string>();

            // Vom jüngsten zum ältesten: Hat dieselbe Datei mehrere Einträge,
            // landet sie so beim Stand vor dem ältesten gewählten.
            foreach (var row in chosen.Where(r => r.Entry.CanUndo).OrderByDescending(r => r.Entry.Timestamp))
            {
                try
                {
                    release(row.Entry.Files.Select(f => f.Original));
                    var r = history.Undo(row.Entry.Id);
                    restored += r.Restored;
                    failed += r.Failed;
                    undone = true;
                }
                catch (Exception ex)
                {
                    errors.Add($"{row.Entry.Description}: {ex.Message}");
                }
            }

            Refresh();
            status.Text = (failed == 0 && errors.Count == 0
                    ? Strings.T("{0} file(s) restored.", restored)
                    : Strings.T("{0} restored, {1} failed.", restored, failed + errors.Count))
                + (errors.Count > 0 ? " " + string.Join("; ", errors) : "");
        }

        undoBtn.Click += (_, _) => Undo(rows.Where(r => r.Selected).ToList());

        clearBtn.Flyout = BackupsPage.ConfirmFlyout(
            Strings.T("Clear the whole history? The backups stay, but can no longer be undone from here."),
            Strings.T("Clear the history"),
            () =>
            {
                history.Clear();
                Refresh();
                status.Text = "";
            });

        // ── Zeilen ───────────────────────────────────────────────
        list.ContainerContentChanging += (_, args) =>
        {
            if (args.InRecycleQueue || args.Item is not Row row) return;
            if (args.ItemContainer is not ListViewItem { ContentTemplateRoot: Grid grid }) return;

            if (grid.Tag is not RowParts parts)
            {
                parts = BuildRow(grid);
                grid.Tag = parts;

                parts.Undo.Click += (_, _) => { if (grid.DataContext is Row r) Undo([r]); };

                void Toggle(bool on)
                {
                    if (filling || grid.DataContext is not Row r) return;
                    r.Selected = on;
                    UpdateButtons();
                }
                parts.Check.Checked += (_, _) => Toggle(true);
                parts.Check.Unchecked += (_, _) => Toggle(false);
            }

            grid.DataContext = row;
            Fill(parts, row.Entry);

            filling = true;
            parts.Check.IsChecked = row.Selected;
            filling = false;
            args.Handled = true;
        };

        Refresh();

        // ── Zusammensetzen ───────────────────────────────────────
        var tools = new Grid
        {
            Margin = new Thickness(20, 0, 20, 6),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { undoBtn, clearBtn } };
        Grid.SetColumn(buttons, 2);
        tools.Children.Add(selectAll);
        tools.Children.Add(buttons);

        var hint = new TextBlock
        {
            Text = Strings.T("Newest first. Undo puts the files back as they were before; "
                             + "with several entries chosen, the newest is undone first."),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.65,
            Margin = new Thickness(20, 0, 20, 8),
        };
        status.Margin = new Thickness(20, 0, 20, 6);
        empty.Margin = new Thickness(20, 8, 20, 8);
        list.Margin = new Thickness(14, 0, 14, 14);

        var layout = new Grid
        {
            Width = 860,
            Height = 620,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
            },
        };
        Grid.SetRow(hint, 1);
        Grid.SetRow(tools, 2);
        Grid.SetRow(status, 3);
        Grid.SetRow(empty, 4);
        Grid.SetRow(list, 5);
        foreach (var part in new FrameworkElement[] { top, hint, tools, status, empty, list })
            layout.Children.Add(part);

        dialog = new ContentDialog { Content = layout, XamlRoot = root };

        // Wie die Einstellungen: ohne den Rand und den leeren Knopfbalken der
        // Vorlage, und ohne ihre Klemme auf rund 548 Pixel.
        dialog.Resources["ContentDialogPadding"] = new Thickness(0);
        dialog.Resources["ContentDialogSeparatorThickness"] = new Thickness(0);
        dialog.Resources["ContentDialogTitleMargin"] = new Thickness(0);
        dialog.Resources["ContentDialogMaxWidth"] = 1400.0;
        dialog.Resources["ContentDialogMaxHeight"] = 1200.0;

        // Ohne Knopfleiste schließt der Dialog nicht von selbst auf Escape.
        layout.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Escape) return;
            dialog.Hide();
            e.Handled = true;
        };

        await dialog.ShowAsync();
        return undone;
    }

    // ══ Zeile ════════════════════════════════════════════════════

    private static RowParts BuildRow(Grid grid)
    {
        var dim = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
        var fill = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"];

        grid.Height = 58;
        grid.ColumnSpacing = 10;
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var check = new CheckBox { MinWidth = 0, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };

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
                    new FontIcon { Glyph = "", FontSize = 13, Foreground = dim },
                    cover,
                },
            },
        };

        TextBlock Line(double size, Brush? brush = null) => new()
        {
            FontSize = size,
            Foreground = brush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var title = Line(13);
        var files = Line(11.5, dim);
        var where = Line(11.5, dim);
        var state = Line(11);
        var when = Line(11.5);
        when.HorizontalAlignment = HorizontalAlignment.Right;
        var count = Line(11, dim);
        count.HorizontalAlignment = HorizontalAlignment.Right;

        StackPanel Stack(params TextBlock[] lines)
        {
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
            foreach (var line in lines) panel.Children.Add(line);
            return panel;
        }

        var undo = BackupsPage.IconButton("", "Undo");

        var names = Stack(title, files);
        var origin = Stack(where, state);
        var stamp = Stack(when, count);

        Grid.SetColumn(coverBox, 1);
        Grid.SetColumn(names, 2);
        Grid.SetColumn(origin, 3);
        Grid.SetColumn(stamp, 4);
        Grid.SetColumn(undo, 5);
        undo.VerticalAlignment = VerticalAlignment.Center;
        foreach (var part in new FrameworkElement[] { check, coverBox, names, origin, stamp, undo })
            grid.Children.Add(part);

        return new RowParts(check, cover, title, files, where, state, when, count, undo);
    }

    private static void Fill(RowParts parts, HistoryEntry entry)
    {
        parts.Title.Text = entry.Description;

        // Die betroffenen Dateien, so viele wie in die Zeile passen; alle im Tooltip.
        var names = entry.Files.Select(f => Path.GetFileName(f.Original)).Distinct().ToList();
        parts.Files.Text = names.Count <= 2
            ? string.Join(", ", names)
            : Strings.T("{0}, {1} and {2} more", names[0], names[1], names.Count - 2);
        ToolTipService.SetToolTip(parts.Files, string.Join("\n", names));

        var folders = entry.Files
            .Select(f => Path.GetFileName(Path.GetDirectoryName(f.Original) ?? ""))
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        parts.Where.Text = folders.Count switch
        {
            0 => "",
            1 => folders[0],
            _ => Strings.T("{0} folders", folders.Count),
        };

        var canUndo = entry.CanUndo;
        parts.State.Text = canUndo ? Strings.T("Can be undone") : Strings.T("No backup left");
        parts.State.Foreground = canUndo
            ? (Brush)Application.Current.Resources["AccentBrush"]
            : (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
        parts.Undo.IsEnabled = canUndo;
        ToolTipService.SetToolTip(parts.Undo, Strings.T(canUndo ? "Undo" : "The backup was deleted or has expired."));

        parts.When.Text = BackupsPage.Relative(entry.Timestamp);
        ToolTipService.SetToolTip(parts.When, entry.Timestamp.ToString("g"));
        parts.Count.Text = Strings.T("{0} file(s)", entry.Files.Count);

        // Das Cover aus der Sicherung, also wie die Datei vorher aussah. Ohne
        // Sicherung das der Datei, wie sie jetzt ist.
        var sample = entry.Files.FirstOrDefault(f => f.BackupPath is not null && File.Exists(f.BackupPath))?.BackupPath
                     ?? entry.Files.Select(f => f.Original).FirstOrDefault(File.Exists);
        TrackArt.SetPath(parts.Cover, sample);
    }
}
