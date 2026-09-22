using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using TagTuner.Core.Safety;
using TagTuner.Core.Settings;

namespace TagTuner.App.Settings;

/// <summary>
/// Eine Kategorie in der Leiste links. Der Inhalt wird erst gebaut, wenn die
/// Kategorie gewählt wird: Beim Öffnen des Fensters sonst jede Seite
/// aufzubauen kostet Zeit, und die meisten sieht man nie.
/// </summary>
internal sealed record SettingsSection(
    string Title,
    string Glyph,
    Func<SettingsContext, IEnumerable<FrameworkElement>> Build);

/// <summary>Was eine Seite zum Arbeiten braucht.</summary>
internal sealed class SettingsContext
{
    public required AppSettings Settings { get; init; }
    public required HistoryStore History { get; init; }
    public required XamlRoot Root { get; init; }

    /// <summary>Das Fensterhandle, für Dateiauswahl-Dialoge.</summary>
    public required IntPtr Window { get; init; }

    /// <summary>Die Aktualisierung, die der Nutzer holen möchte.</summary>
    public UpdateCheck? WantsInstall { get; set; }

    /// <summary>Schließt die Einstellungen, etwa um zu installieren.</summary>
    public required System.Action Close { get; init; }

    /// <summary>
    /// Etwas geändert, das das Hauptfenster angeht. Der Name sagt was, damit
    /// dort nicht vorsichtshalber alles neu aufgebaut wird.
    /// </summary>
    public required Action<string> Changed { get; init; }

    public void Save() => Settings.Save();
}

/// <summary>
/// Die Einstellungen als Seitenleiste links und Inhalt rechts.
///
/// Die Kategorien stehen in <see cref="SettingsCatalog"/>. Eine Einstellung zu
/// verschieben heißt dort, eine Zeile in eine andere Liste zu schieben; eine
/// neue Kategorie ist ein Eintrag mehr. Dieses Gerüst weiß von keiner
/// einzelnen Einstellung etwas.
/// </summary>
internal static class SettingsShell
{
    public static async Task<UpdateCheck?> ShowAsync(
        XamlRoot root, IntPtr window, AppSettings settings, HistoryStore history,
        Action<string> changed)
    {
        ContentDialog? dialog = null;

        var context = new SettingsContext
        {
            Settings = settings,
            History = history,
            Root = root,
            Window = window,
            Close = () => dialog?.Hide(),
            Changed = changed,
        };

        var sections = SettingsCatalog.Sections;

        // ── Inhalt rechts ────────────────────────────────────────
        var heading = new TextBlock
        {
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 7),
        };

        var body = new StackPanel { Spacing = 7 };

        var content = new ScrollViewer
        {
            Padding = new Thickness(16, 12, 16, 12),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel { Children = { heading, body } },
        };

        // ── Leiste links ─────────────────────────────────────────
        // Ohne eigenen Hintergrund: Leiste und Inhalt sind eine Fläche, und
        // eine Linie dazwischen genügt als Trennung.
        var rail = new ListView
        {
            Width = 172,
            SelectionMode = ListViewSelectionMode.Single,
            Padding = new Thickness(6, 8, 6, 8),
            Background = null,
            ItemContainerStyle = RailItemStyle(),
        };

        foreach (var section in sections)
        {
            rail.Items.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 11,
                Children =
                {
                    new FontIcon
                    {
                        Glyph = section.Glyph,
                        FontSize = 15,
                        Opacity = 0.85,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = Strings.T(section.Title),
                        FontSize = 13.5,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                },
            });
        }

        rail.SelectionChanged += (_, _) =>
        {
            var at = rail.SelectedIndex;
            if (at < 0 || at >= sections.Count) return;

            heading.Text = Strings.T(sections[at].Title);
            body.Children.Clear();

            // Scheitert eine Seite beim Aufbau, soll das Fenster stehen
            // bleiben und sagen was los ist, statt leer zu erscheinen.
            try
            {
                foreach (var item in sections[at].Build(context)) body.Children.Add(item);
            }
            catch (Exception ex)
            {
                body.Children.Add(SettingsUi.Hint(Strings.T("{0} failed: {1}",
                    Strings.T(sections[at].Title), ex.Message)));
            }

            content.ChangeView(null, 0, null, disableAnimation: true);
        };

        var divider = new Border
        {
            Width = 1,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
        };

        // Schließen oben rechts statt als Knopf unten: Der Knopfbalken des
        // Dialogs nimmt Höhe weg, die der Inhalt besser gebrauchen kann, und
        // ein X in der Ecke ist dort, wo man es sucht.
        var close = new Button
        {
            Content = "\uE711",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
            FontSize = 12,
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 6, 6, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Background = null,
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(close, Strings.T("Close"));
        close.Click += (_, _) => dialog?.Hide();

        var layout = new Grid
        {
            Height = 660,
            Width = 1180,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };

        Grid.SetColumn(rail, 0);
        Grid.SetColumn(divider, 1);
        Grid.SetColumn(content, 1);
        Grid.SetColumn(close, 1);
        layout.Children.Add(rail);
        layout.Children.Add(divider);
        layout.Children.Add(content);
        layout.Children.Add(close);

        dialog = new ContentDialog
        {
            Content = layout,
            XamlRoot = root,

            // Der Rahmen des Dialogs würde sonst einen Rand um die Leiste
            // legen, und die soll bis an die Kante gehen.
            Padding = new Thickness(0),
        };

        // Ein ContentDialog klemmt seinen Inhalt auf rund 548 Pixel — diese
        // Werte stehen als Ressourcen in seiner Vorlage, nicht als
        // Eigenschaften. Ohne sie zu überschreiben wird alles rechts davon
        // abgeschnitten, samt dem Schließen-Knopf in der Ecke, und man sieht
        // nur, dass etwas fehlt, nicht warum.
        dialog.Resources["ContentDialogMaxWidth"] = 1400.0;
        dialog.Resources["ContentDialogMinWidth"] = 640.0;
        dialog.Resources["ContentDialogMaxHeight"] = 1200.0;
        dialog.Resources["ContentDialogMinHeight"] = 320.0;

        // Ohne Knopfleiste schließt der Dialog nicht mehr von selbst auf
        // Escape. Das wäre eine Falle: Man drückt Escape und nichts passiert.
        layout.KeyDown += (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Escape) return;
            dialog?.Hide();
            e.Handled = true;
        };

        rail.SelectedIndex = 0;

        await dialog.ShowAsync();

        settings.Save();
        return context.WantsInstall;
    }

    /// <summary>Zeilen in der Leiste: schmal, abgerundet, ohne viel Luft.</summary>
    private static Style RailItemStyle()
    {
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty,
                                     HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,
                                     HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 0, 10, 0)));
        style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 34.0));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(5)));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 1, 0, 1)));
        return style;
    }
}
