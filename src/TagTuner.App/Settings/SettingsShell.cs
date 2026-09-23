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
        // Die Überschrift scrollt nicht mit: Sie teilt sich die obere Zeile
        // mit dem Schließen-Knopf, der so in der Ecke sitzt und nicht über
        // der Bildlaufleiste.
        var heading = new TextBlock
        {
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20, 0, 0, 0),
        };

        var body = new StackPanel { Spacing = 7 };

        var content = new ScrollViewer
        {
            Padding = new Thickness(20, 2, 20, 16),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = body,
        };

        // ── Leiste links ─────────────────────────────────────────
        // Eigene Zeilen statt einer ListView: Deren Auswahl zeichnet die
        // Windows-Vorlage selbst, als eckigen Kasten, und lässt sich dort nur
        // über Ressourcen umstellen, die nicht verlässlich greifen. So ist
        // die Rundung garantiert und der farbige Strich an der gewählten
        // Zeile auch. Ohne eigenen Hintergrund: Leiste und Inhalt sind eine
        // Fläche, und eine Linie dazwischen genügt als Trennung.
        var rail = new StackPanel
        {
            Width = 172,
            Spacing = 3,
            Padding = new Thickness(8, 10, 8, 10),
        };

        // Die Zeilen sind keine Steuerelemente und nehmen keinen Fokus an.
        // Darum hält eine Hülle ihn, damit Pfeil hoch und runter die
        // Kategorie wechseln, wie es eine Liste tun würde.
        var railHost = new ContentControl
        {
            Content = rail,
            IsTabStop = true,
            UseSystemFocusVisuals = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };

        var rows = new List<(Grid Row, Border Mark)>();
        var selected = -1;

        void Paint(int index, bool hover)
        {
            var (row, mark) = rows[index];
            var isSelected = index == selected;
            mark.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
            row.Background = isSelected
                ? Res("SubtleFillColorSecondaryBrush")
                : hover ? Res("SubtleFillColorTertiaryBrush")
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        void Select(int at)
        {
            if (at < 0 || at >= sections.Count || at == selected) return;
            var before = selected;
            selected = at;
            if (before >= 0) Paint(before, hover: false);
            Paint(at, hover: false);

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
        }

        for (var i = 0; i < sections.Count; i++)
        {
            var index = i;
            var section = sections[i];

            var mark = new Border
            {
                Width = 3,
                Height = 16,
                CornerRadius = new CornerRadius(1.5),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Res("AccentFillColorDefaultBrush"),
                Visibility = Visibility.Collapsed,
            };

            var row = new Grid
            {
                Height = 36,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Children =
                {
                    mark,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 11,
                        Margin = new Thickness(12, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center,
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
                    },
                },
            };

            row.PointerEntered += (_, _) => Paint(index, hover: true);
            row.PointerExited += (_, _) => Paint(index, hover: false);
            row.PointerCanceled += (_, _) => Paint(index, hover: false);
            row.Tapped += (_, _) =>
            {
                Select(index);

                // Danach gehen die Pfeiltasten an die Leiste.
                railHost.Focus(FocusState.Pointer);
            };

            rows.Add((row, mark));
            rail.Children.Add(row);
        }

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
            Margin = new Thickness(0, 0, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Background = null,
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(close, Strings.T("Close"));
        close.Click += (_, _) => dialog?.Hide();

        var layout = new Grid
        {
            Height = 660,
            Width = 860,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
            },
        };

        var top = new Grid { Height = 52 };
        top.Children.Add(heading);
        top.Children.Add(close);

        var main = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) },
            },
        };
        Grid.SetRow(content, 1);
        main.Children.Add(top);
        main.Children.Add(content);

        Grid.SetColumn(railHost, 0);
        Grid.SetColumn(divider, 1);
        Grid.SetColumn(main, 1);
        layout.Children.Add(railHost);
        layout.Children.Add(divider);
        layout.Children.Add(main);

        dialog = new ContentDialog
        {
            Content = layout,
            XamlRoot = root,
        };

        // Ein Dialog liegt über dem Fenster, nicht darin, und erbt dessen
        // Thema nicht. Ohne das blieb er dunkel, wenn das Fenster hell war.
        if (root.Content is FrameworkElement host) dialog.RequestedTheme = host.ActualTheme;

        // Die Vorlage legt 24 Pixel Rand um den Inhalt und darunter einen
        // Knopfbalken in einer anderen Farbe, auch wenn es keine Knöpfe
        // gibt. Das sah aus wie ein Kasten im Fenster. Ohne diese Ränder
        // gehen Leiste und Inhalt bis an die Kanten, und das X sitzt in der
        // Ecke.
        dialog.Resources["ContentDialogPadding"] = new Thickness(0);
        dialog.Resources["ContentDialogSeparatorThickness"] = new Thickness(0);
        dialog.Resources["ContentDialogTitleMargin"] = new Thickness(0);

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

        railHost.KeyDown += (_, e) =>
        {
            var to = e.Key switch
            {
                Windows.System.VirtualKey.Up => selected - 1,
                Windows.System.VirtualKey.Down => selected + 1,
                Windows.System.VirtualKey.Home => 0,
                Windows.System.VirtualKey.End => sections.Count - 1,
                _ => -1,
            };
            if (to < 0 || to >= sections.Count) return;
            Select(to);
            e.Handled = true;
        };

        Select(0);

        await dialog.ShowAsync();

        settings.Save();
        return context.WantsInstall;
    }

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];
}
