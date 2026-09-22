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
            Spacing = 3,
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
        var panel = new StackPanel { Spacing = 7 };
        panel.Children.Add(new TextBlock
        {
            Text = Strings.T(title).ToUpperInvariant(),
            FontSize = 11,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Opacity = 0.6,
        });

        // null ist erlaubt: So kann eine Seite einen Teil weglassen, ohne
        // die Liste drumherum umzubauen.
        foreach (var item in content)
            if (item is not null) panel.Children.Add(item);

        return new Border
        {
            Padding = new Thickness(12, 10, 12, 11),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
            Child = panel,
        };
    }

    /// <summary>Ein Haken, der sofort schreibt.</summary>
    public static CheckBox Tick(string label, bool on, Action<bool> apply)
    {
        var box = new CheckBox { Content = Strings.T(label), IsChecked = on, MinHeight = 30 };
        box.Checked += (_, _) => apply(true);
        box.Unchecked += (_, _) => apply(false);
        return box;
    }

    /// <summary>
    /// Eine Auswahl aus festen Möglichkeiten. Die Beschriftungen werden
    /// übersetzt, der gespeicherte Wert bleibt englisch — sonst hinge der
    /// Inhalt der Einstellungsdatei an der Sprache.
    /// </summary>
    public static ComboBox Choice(
        (string Label, string Value)[] options, string current, Action<string> apply)
    {
        var box = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = options.Select(o => Strings.T(o.Label)).ToList(),
            SelectedIndex = Math.Max(0, Array.FindIndex(options, o => o.Value == current)),
        };

        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0) apply(options[box.SelectedIndex].Value);
        };
        return box;
    }

    /// <summary>Eine Auswahl aus einer Liste gleichartiger Werte, etwa Formaten.</summary>
    public static ComboBox Pick(
        IEnumerable<string> items, string? current, Action<int> apply)
    {
        var list = items.ToList();
        var box = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = list,
        };

        var at = list.FindIndex(x => string.Equals(x, current, StringComparison.OrdinalIgnoreCase));
        box.SelectedIndex = at >= 0 ? at : 0;
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0) apply(box.SelectedIndex);
        };
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

    public static StackPanel Buttons(params FrameworkElement[] items)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        foreach (var item in items) panel.Children.Add(item);
        return panel;
    }
}
