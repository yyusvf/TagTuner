using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using TagTuner.Core.Settings;

namespace TagTuner.App.Settings;

/// <summary>
/// Die Bausteine der Einstellungsseiten.
///
/// Eine Einstellung soll eine Zeile Code sein, nicht fünfzehn. Alles, was
/// sich wiederholt — Überschrift, Abstand, Rahmen, das Schreiben und
/// Speichern — steckt hier drin, damit eine neue Einstellung nur noch aus
/// ihrer Beschriftung und dem besteht, was sie tut.
///
/// Alle Bausteine schreiben sofort. Ein „Speichern"-Knopf am Ende wäre bei
/// einer Seitenleiste eine Falle: Man wechselt die Kategorie, glaubt fertig
/// zu sein, und verliert beim Schließen alles.
/// </summary>
internal static class SettingsUi
{
    public static TextBlock Hint(string text) => new()
    {
        Text = Strings.T(text),
        FontSize = 11.5,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.65,
    };

    public static StackPanel Field(string label, FrameworkElement control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = Strings.T(label), FontSize = 11.5, Opacity = 0.8 },
                control,
            },
        };
    }

    public static Grid Row(params FrameworkElement[] cells)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        for (var i = 0; i < cells.Length; i++)
        {
            grid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(cells[i], i);
            grid.Children.Add(cells[i]);
        }
        return grid;
    }

    public static Border Group(string title, params FrameworkElement?[] content)
    {
        var panel = new StackPanel { Spacing = 5 };
        panel.Children.Add(new TextBlock
        {
            Text = Strings.T(title).ToUpperInvariant(),
            Style = (Style)Application.Current.Resources["SectionLabel"],
        });

        // null ist erlaubt: So kann eine Seite einen Teil weglassen, ohne
        // die Liste drumherum umzubauen.
        foreach (var item in content)
            if (item is not null) panel.Children.Add(item);

        return new Border
        {
            Padding = new Thickness(11, 8, 11, 9),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
            Child = panel,
        };
    }

    /// <summary>Ein Haken, der sofort schreibt.</summary>
    public static CheckBox Tick(string label, bool on, Action<bool> apply)
    {
        var box = new CheckBox { Content = Strings.T(label), IsChecked = on, MinHeight = 26 };
        box.Checked += (_, _) => apply(true);
        box.Unchecked += (_, _) => apply(false);
        return box;
    }

    /// <summary>
    /// Eine Auswahl aus festen Möglichkeiten. Die Beschriftungen werden
    /// übersetzt, der gespeicherte Wert bleibt englisch — sonst hinge der
    /// Inhalt der Einstellungsdatei an der Sprache.
    /// </summary>
    public static TagTuner.App.Dropdown Choice(
        (string Label, string Value)[] options, string current, Action<string> apply) =>
        Select(options.Select(o => Strings.T(o.Label)).ToList(),
                 Math.Max(0, Array.FindIndex(options, o => o.Value == current)),
                 i => apply(options[i].Value));

    /// <summary>Eine Auswahl aus einer Liste gleichartiger Werte, etwa Formaten.</summary>
    public static TagTuner.App.Dropdown Pick(
        IEnumerable<string> items, string? current, Action<int> apply)
    {
        var list = items.ToList();
        var at = list.FindIndex(x => string.Equals(x, current, StringComparison.OrdinalIgnoreCase));
        return Select(list, at >= 0 ? at : 0, apply);
    }

    /// <summary>Ein <see cref="TagTuner.App.Dropdown"/>, das jede Wahl gleich weitergibt.</summary>
    public static TagTuner.App.Dropdown Select(IReadOnlyList<string> labels, int selected, Action<int> apply)
    {
        var box = new TagTuner.App.Dropdown { Items = labels, SelectedIndex = selected, HorizontalAlignment = HorizontalAlignment.Stretch };
        box.SelectionChanged += (_, _) => { if (box.SelectedIndex >= 0) apply(box.SelectedIndex); };
        return box;
    }

    public static Button Action(string label, Action run)
    {
        var button = new Button { Content = Strings.T(label) };
        button.Click += (_, _) => run();
        return button;
    }

    /// <summary>
    /// Ein Knopf für etwas, das wartet.
    ///
    /// Eine asynchrone Lambda in einen Action-Parameter zu geben ergibt
    /// „async void": Eine Ausnahme darin verschwindet spurlos, der Knopf tut
    /// dann einfach nichts und sagt nicht warum. Hier wird gewartet und der
    /// Fehler landet sichtbar am Knopf.
    /// </summary>
    public static Button AsyncAction(string label, Func<Task> run)
    {
        var button = new Button { Content = Strings.T(label) };
        var text = Strings.T(label);

        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await run(); }
            catch (Exception ex) { button.Content = Strings.T("Failed: ") + ex.Message; }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    // ══ Zeilen im Stil der Windows-Einstellungen ═════════════════

    /// <summary>
    /// Eine kleine Überschrift über einer Gruppe von Zeilen. Normal gesetzt,
    /// nicht in Großbuchstaben: Sie ordnet, sie ruft nicht.
    /// </summary>
    public static TextBlock Heading(string text) => new()
    {
        Text = Strings.T(text),
        FontSize = 13,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Margin = new Thickness(2, 14, 0, 2),
    };

    /// <summary>
    /// Eine Einstellung als Zeile: Icon, Titel und eine knappe Erklärung
    /// links, der Regler rechts. So liest man von links nach rechts, was es
    /// ist und wie es steht, statt einen Kasten nach dem anderen zu öffnen.
    /// </summary>
    /// <param name="description">Darf null sein; eine Zeile ohne Erklärung ist kürzer.</param>
    /// <param name="control">Der Regler rechts, oder null für eine reine Anzeige.</param>
    public static Border Row(string glyph, string title, string? description, FrameworkElement? control)
    {
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = Strings.T(title),
            FontSize = 13.5,
            TextWrapping = TextWrapping.Wrap,
        });

        TextBlock? detail = null;
        if (description is not null)
        {
            detail = new TextBlock
            {
                Text = Strings.T(description),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            };
            text.Children.Add(detail);
        }

        var grid = new Grid
        {
            ColumnSpacing = 16,
            MinHeight = 44,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        var icon = new FontIcon
        {
            Glyph = glyph,
            FontSize = 18,
            Width = 22,
            VerticalAlignment = VerticalAlignment.Center,
        };
        grid.Children.Add(icon);

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (control is not null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            control.HorizontalAlignment = HorizontalAlignment.Right;

            // Auswahl und Eingabe brauchen Platz für ihren Text; sonst
            // schrumpfen sie auf die Breite des Pfeils.
            if (control is ComboBox or TextBox or TagTuner.App.Dropdown && control.MinWidth == 0) control.MinWidth = 220;
            Grid.SetColumn(control, 2);
            grid.Children.Add(control);
        }

        return new Border
        {
            Padding = new Thickness(16, 12, 16, 12),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            Child = grid,

            // Die Erklärung, damit eine Seite sie nachträglich ändern kann,
            // etwa nach dem Ein- und Ausschalten.
            Tag = detail,
        };
    }

    /// <summary>
    /// Eine Zeile, die zu der darüber gehört, etwa ein Unterpunkt des
    /// Album-Modus: eingerückt und ohne eigenes Icon.
    /// </summary>
    public static Border SubRow(string title, string? description, FrameworkElement? control)
    {
        var row = Row("", title, description, control);
        row.Margin = new Thickness(38, -4, 0, 0);
        return row;
    }

    /// <summary>
    /// Eine Fläche im Stil der Zeilen für das, was keine Zeile ist: Listen,
    /// Knopfreihen, längere Angaben.
    /// </summary>
    public static Border Panel(params FrameworkElement?[] content)
    {
        var stack = new StackPanel { Spacing = 8 };
        foreach (var item in content)
            if (item is not null) stack.Children.Add(item);

        return new Border
        {
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, -4, 0, 0),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            Child = stack,
        };
    }

    /// <summary>Ändert die Erklärung einer Zeile.</summary>
    public static void Describe(Border row, string text)
    {
        if (row.Tag is TextBlock detail) detail.Text = text;
    }

    /// <summary>Ein Ein/Aus-Schalter ohne Beschriftung daneben: Die Zeile sagt schon, was er schaltet.</summary>
    public static ToggleSwitch Switch(bool on) => new()
    {
        IsOn = on,
        OnContent = "",
        OffContent = "",
        MinWidth = 0,
    };

    public static StackPanel Buttons(params FrameworkElement[] items)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var item in items) panel.Children.Add(item);
        return panel;
    }
}
