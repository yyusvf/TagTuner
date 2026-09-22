using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

using TagTuner.Core.Model;
using TagTuner.Core.Settings;

namespace TagTuner.App;

/// <summary>
/// Welche Spalten die Trackliste zeigt und wie eine Zeile daraus entsteht.
///
/// Kopfzeile und Zeilen werden im Code gebaut, nicht als XAML-Vorlage: Die
/// Auswahl und die Reihenfolge stehen erst zur Laufzeit fest, und eine
/// Vorlage, die per XamlReader aus einer Zeichenkette entsteht, müsste ihre
/// Breiten über <c>StaticResource</c> auflösen. Ob das dort an die Ressourcen
/// der Anwendung kommt, ist nicht verlässlich, und eine Spalte ohne Breite
/// wäre ein Fehler, den man erst im laufenden Fenster sieht.
/// </summary>
internal static class TrackColumns
{
    /// <summary>
    /// Die sichtbaren Spalten in der eingestellten Reihenfolge.
    ///
    /// Was gespeichert ist, gewinnt; was der Katalog danach noch kennt, hängt
    /// hinten an. So bekommt eine neue Fassung ihre neuen Spalten, ohne dass
    /// die Einstellung des Nutzers verworfen wird.
    /// </summary>
    public static List<TrackColumn> Resolve(AppSettings settings)
    {
        var chosen = new List<TrackColumn>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var state in settings.TrackColumns)
        {
            if (!seen.Add(state.Id)) continue;
            if (TrackColumn.ById(state.Id) is not { } column) continue;   // aus einer älteren Fassung
            if (state.Visible) chosen.Add(column);
        }

        foreach (var column in TrackColumn.All)
        {
            if (seen.Contains(column.Id)) continue;
            if (column.OnByDefault) chosen.Add(column);
        }

        // Im vereinten Modus steht der Interpret klein unter dem Titel und
        // braucht keine eigene Spalte.
        if (settings.CombineTitleAndArtist)
            chosen.RemoveAll(c => c.Id == "artist");

        // Ebenso die Disc: Sie steht dann in der Track-Zelle.
        if (settings.CombineDiscAndTrack)
            chosen.RemoveAll(c => c.Id == "disc");

        // Ganz ohne Spalten wäre die Liste leer und der Weg zurück führte nur
        // über die Einstellungsdatei.
        if (chosen.Count == 0)
            chosen.AddRange(TrackColumn.All.Where(c => c.OnByDefault));

        return chosen.Take(TrackColumnLayout.Slots).ToList();
    }

    /// <summary>
    /// Sorgt dafür, dass jede Spalte des Katalogs einen Eintrag hat.
    ///
    /// Die Einstellungsseite arbeitet auf dieser Liste: Sie ist die
    /// Reihenfolge, die der Nutzer sieht und verschiebt. Neue Spalten einer
    /// neueren Fassung hängen hinten an, statt die Reihenfolge zu verwerfen.
    /// </summary>
    public static void EnsureStates(AppSettings settings)
    {
        var known = settings.TrackColumns
            .Where(s => TrackColumn.ById(s.Id) is not null)
            .GroupBy(s => s.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        var seen = known.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var column in TrackColumn.All)
        {
            if (seen.Contains(column.Id)) continue;
            known.Add(new TrackColumnState
            {
                Id = column.Id,
                Visible = column.OnByDefault,
                Width = column.Width,
            });
        }

        settings.TrackColumns = known;
    }

    /// <summary>Die gespeicherten Breiten, in der Reihenfolge der sichtbaren Spalten.</summary>
    public static List<double> Widths(AppSettings settings, IReadOnlyList<TrackColumn> visible)
    {
        var saved = settings.TrackColumns
            .Where(s => s.Width > 0)
            .ToDictionary(s => s.Id, s => s.Width, StringComparer.Ordinal);

        return [.. visible.Select(c => saved.TryGetValue(c.Id, out var w) ? w : c.Width)];
    }

    /// <summary>
    /// Schreibt Auswahl, Reihenfolge und Breiten zurück. Spalten, die gerade
    /// nicht sichtbar sind, behalten ihren Eintrag — sonst verlöre man ihre
    /// Breite und ihren Platz beim einmaligen Abwählen.
    /// </summary>
    public static void Remember(
        AppSettings settings, IReadOnlyList<TrackColumn> visible, IReadOnlyList<double> widths)
    {
        var byId = settings.TrackColumns.ToDictionary(s => s.Id, StringComparer.Ordinal);

        for (var i = 0; i < visible.Count; i++)
        {
            if (byId.TryGetValue(visible[i].Id, out var state)) state.Width = widths[i];
        }
    }

    // ══ Kopfzeile ════════════════════════════════════════════════

    /// <summary>
    /// Baut die Kopfzeile neu. <paramref name="sortMarks"/> nimmt die
    /// Pfeilfelder auf, damit die Liste später zeigen kann, wonach sortiert ist.
    /// </summary>
    public static void BuildHeader(
        Grid host,
        IReadOnlyList<TrackColumn> columns,
        TrackColumnLayout widths,
        bool combined,
        Dictionary<TrackSort, TextBlock> sortMarks,
        TappedEventHandler onHeaderTapped,
        Action<int, PointerRoutedEventArgs> onColumnPressed,
        Style gripStyle,
        PointerEventHandler onGripPressed,
        PointerEventHandler onGripMoved,
        PointerEventHandler onGripReleased,
        PointerEventHandler onGripExited)
    {
        host.Children.Clear();
        host.ColumnDefinitions.Clear();
        sortMarks.Clear();

        var header = (Style)Application.Current.Resources["ColHeader"];
        var accent = (Brush)Application.Current.Resources["AccentBrush"];
        var dim = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];

        for (var i = 0; i < columns.Count; i++)
        {
            host.ColumnDefinitions.Add(new ColumnDefinition());
            Bind(host.ColumnDefinitions[i], widths, i);
        }

        // Eine letzte, dehnbare Spalte fängt den Rest der Breite auf.
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var cell = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 3,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = column.Look == ColumnLook.MonoRight
                    ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            };

            if (column.Id == "title" && combined)
            {
                // Vereint stehen beide Wörter in dieser einen Überschrift, und
                // jedes sortiert für sich. Fehlte der Interpret hier, gäbe es
                // keinen Weg mehr, nach ihm zu sortieren.
                cell.Children.Add(Part("TITLE", TrackSort.Title));
                cell.Children.Add(new TextBlock { Text = "·", Style = header });
                cell.Children.Add(Part("ARTIST", TrackSort.Artist));
            }
            else
            {
                if (column.Id == "duration")
                {
                    // Die Dauer trägt eine Uhr statt eines Wortes.
                    cell.Children.Add(new FontIcon
                    {
                        Glyph = "",
                        FontSize = 11,
                        Foreground = dim,
                    });
                }
                else if (column.Id == "track")
                {
                    // Genau so breit wie das Feld der Nummer darunter, damit
                    // das # mittig über ihr steht.
                    cell.Children.Add(new TextBlock
                    {
                        Text = Strings.T(column.Header),
                        Style = header,
                        Width = NumberWidth,
                        TextAlignment = TextAlignment.Center,
                    });
                }
                else if (column.Header.Length > 0)
                {
                    cell.Children.Add(new TextBlock { Text = Strings.T(column.Header), Style = header });
                }

                if (column.Sort is { } key)
                {
                    var mark = new TextBlock { Style = header, Foreground = accent };

                    // Der Pfeil ragt in den Abstand zur nächsten Spalte, statt
                    // das # beim Sortieren zur Seite zu schieben.
                    if (column.Id == "track")
                    {
                        mark.Width = TrackMarkWidth;
                        cell.Margin = new Thickness(0, 0, -TrackMarkWidth - cell.Spacing, 0);
                    }

                    cell.Children.Add(mark);
                    sortMarks[key] = mark;

                    cell.Tag = key.ToString();
                    cell.Tapped += onHeaderTapped;
                }
            }

            // Ziehen verschiebt die Spalte. Ein kurzer Klick bleibt Sortieren;
            // was davon gemeint war, entscheidet die Liste an der Strecke,
            // die der Zeiger zurücklegt.
            var index = i;
            cell.PointerPressed += (_, e) => onColumnPressed(index, e);

            if (column.Look == ColumnLook.MonoRight && column.Id != "track")
                cell.Margin = new Thickness(0, 0, 12, 0);

            if (column.Id == "track") cell.HorizontalAlignment = HorizontalAlignment.Right;

            Grid.SetColumn(cell, i);
            host.Children.Add(cell);

            // Der Greifer sitzt am rechten Rand seiner Spalte. Die letzte
            // bekommt keinen: Dahinter ist nur noch Leerraum.
            if (i >= columns.Count - 1) continue;

            var grip = new Border { Style = gripStyle, Tag = i.ToString() };
            grip.PointerPressed += onGripPressed;
            grip.PointerMoved += onGripMoved;
            grip.PointerReleased += onGripReleased;
            grip.PointerExited += onGripExited;

            Grid.SetColumn(grip, i);
            host.Children.Add(grip);
        }

        // Ein anklickbarer Teil der Überschrift mit eigenem Sortierpfeil.
        StackPanel Part(string text, TrackSort key)
        {
            var mark = new TextBlock { Style = header, Foreground = accent };
            sortMarks[key] = mark;

            var part = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 3,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Tag = key.ToString(),
                Children = { new TextBlock { Text = Strings.T(text), Style = header }, mark },
            };
            part.Tapped += onHeaderTapped;
            return part;
        }
    }

    /// <summary>Hängt eine Spaltenbreite an ihr Bindungsziel W0 bis W17.</summary>
    private static void Bind(ColumnDefinition definition, TrackColumnLayout widths, int slot)
    {
        // ColumnDefinition ist ein DependencyObject, aber kein
        // FrameworkElement: SetBinding gibt es darauf nicht.
        Microsoft.UI.Xaml.Data.BindingOperations.SetBinding(
            definition, ColumnDefinition.WidthProperty,
            new Microsoft.UI.Xaml.Data.Binding
            {
                Path = new PropertyPath("W" + slot),
                Source = widths,
                Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay,
            });
    }

    // ══ Zeile ════════════════════════════════════════════════════

    /// <summary>
    /// Der Inhalt einer Zeile. Wird beim Durchscrollen wiederverwendet, darum
    /// baut die Liste ihn einmal und setzt danach nur noch die Texte.
    /// </summary>
    public static void BuildRow(
        Grid grid, IReadOnlyList<TrackColumn> columns, TrackColumnLayout widths, bool combined)
    {
        grid.Children.Clear();
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnSpacing = 10;
        grid.Height = 56;

        var dim = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
        var second = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        var fill = (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"];

        for (var i = 0; i < columns.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            Bind(grid.ColumnDefinitions[i], widths, i);
        }
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            FrameworkElement cell;

            if (column.IsCover)
            {
                var picture = new Image { Stretch = Stretch.UniformToFill };
                cell = new Border
                {
                    Width = 40,
                    Height = 40,
                    CornerRadius = new CornerRadius(4),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = fill,
                    Child = new Grid
                    {
                        Children =
                        {
                            new FontIcon { Glyph = "", FontSize = 14, Foreground = dim },
                            picture,
                        },
                    },
                };
                cell.Tag = new CellTag(column, picture);
            }
            else if (column.Id == "track")
            {
                var number = new TextBlock
                {
                    FontSize = 12,
                    FontFamily = new FontFamily("Consolas"),
                    Foreground = dim,
                    VerticalAlignment = VerticalAlignment.Center,

                    // Rechts in der Spalte, also nah an dem, was danach kommt;
                    // der freie Platz liegt links als Rand. Mittig im festen
                    // Feld, damit 1 und 10 auf einer Achse stehen.
                    Width = NumberWidth,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    TextAlignment = TextAlignment.Center,
                };
                cell = number;
                cell.Tag = new CellTag(column, null, number);
            }
            else if (column.Id == "title" && combined)
            {
                var title = new TextBlock { FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis };
                var artist = new TextBlock
                {
                    FontSize = 11.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = dim,
                };
                cell = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Spacing = 1,
                    Children = { title, artist },
                };
                cell.Tag = new CellTag(column, null, title, artist);
            }
            else
            {
                var text = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };

                switch (column.Look)
                {
                    case ColumnLook.Mono:
                        text.FontFamily = new FontFamily("Consolas");
                        text.FontSize = 11;
                        text.Foreground = dim;
                        break;

                    case ColumnLook.MonoRight:
                        text.FontFamily = new FontFamily("Consolas");
                        text.FontSize = column.Id == "track" ? 12 : 11.5;
                        text.Foreground = dim;
                        text.HorizontalAlignment = HorizontalAlignment.Right;
                        text.Margin = new Thickness(0, 0, column.Id == "track" ? 0 : 12, 0);
                        break;

                    default:
                        text.FontSize = column.Id == "title" ? 13.5 : 12;
                        if (column.Id != "title") text.Foreground = second;
                        break;
                }

                cell = text;
                cell.Tag = new CellTag(column, null, text);
            }

            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
    }

    /// <summary>Woran eine wiederverwendete Zeile erkennt, dass sie eine Disc-Zeile ist.</summary>
    public const string DiscRowTag = "disc";

    /// <summary>
    /// Die Zeile einer Disc, wie bei Spotify: eine kleine Scheibe und
    /// „Disc 2", über die ganze Breite.
    /// </summary>
    public static void BuildDiscRow(Grid grid)
    {
        grid.Children.Clear();
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        grid.Height = 46;

        var second = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

        grid.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Padding = new Thickness(6, 8, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                DiscIcon(second),
                new TextBlock
                {
                    FontSize = 13,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = second,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            },
        });
        grid.Tag = DiscRowTag;
    }

    public static void FillDiscRow(Grid grid, uint disc)
    {
        if (grid.Children is [StackPanel { Children: [_, TextBlock text] }])
            text.Text = Strings.T("Disc {0}", disc);
    }

    /// <summary>Das Feld der Tracknummer, breit genug für drei Ziffern.</summary>
    private const double NumberWidth = 22;

    /// <summary>Platz für den Sortierpfeil hinter dem #.</summary>
    private const double TrackMarkWidth = 10;

    /// <summary>Eine kleine Scheibe: Ring mit Punkt in der Mitte.</summary>
    private static FrameworkElement DiscIcon(Brush brush) => new Grid
    {
        Width = 18,
        Height = 18,
        VerticalAlignment = VerticalAlignment.Center,
        Children =
        {
            new Microsoft.UI.Xaml.Shapes.Ellipse { Stroke = brush, StrokeThickness = 1.4 },
            new Microsoft.UI.Xaml.Shapes.Ellipse
            {
                Width = 6, Height = 6, Stroke = brush, StrokeThickness = 1.4,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        },
    };

    /// <summary>Setzt die Werte einer schon gebauten Zeile auf einen Track.</summary>
    /// <param name="trackText">
    /// Was in der Track-Zelle steht. Das hängt am Ordner und an seiner
    /// Reihenfolge, darum rechnet es die Liste aus und nicht diese Klasse.
    /// </param>
    public static void FillRow(
        Grid row, AudioTrack track, bool combined,
        Func<AudioTrack, string>? trackText = null)
    {
        var number = trackText?.Invoke(track) ?? track.TrackLabel;

        foreach (var child in row.Children)
        {
            if (child is not FrameworkElement { Tag: CellTag tag }) continue;

            if (tag.Picture is { } picture)
            {
                TrackArt.SetPath(picture, track.Path);
                continue;
            }

            if (tag.Column.Id == "track")
            {
                tag.First!.Text = number;
                continue;
            }

            if (tag.Column.Id == "title" && combined && tag.Second is { } artistLine)
            {
                tag.First!.Text = track.Title;
                artistLine.Text = track.Artist;
                continue;
            }

            if (tag.First is { } line) line.Text = Value(track, tag.Column);
        }
    }

    /// <summary>
    /// Der Wert einer Spalte. Ausgeschrieben statt über Reflexion: Das läuft
    /// für jede Zeile beim Scrollen, und der Name der Eigenschaft im Katalog
    /// dient nur der Übersicht.
    /// </summary>
    private static string Value(AudioTrack t, TrackColumn column) => column.Id switch
    {
        "track" => t.TrackLabel,
        "title" => t.Title,
        "artist" => t.Artist,
        "album" => t.Album,
        "format" => t.Format,
        "samplerate" => t.SampleRateLabel,
        "duration" => t.DurationLabel,
        "year" => t.YearLabel,
        "disc" => t.DiscLabel,
        "genre" => t.Genre,
        "albumartist" => t.AlbumArtist,
        "composer" => t.Composer,
        "comment" => t.Comment,
        "bitrate" => t.BitrateLabel,
        "tag" => t.TagFormat,
        "codec" => t.Codec,
        _ => "",
    };

    /// <summary>
    /// Was in einer Zelle steckt, damit das Füllen sie wiederfindet, ohne
    /// die Zeile jedes Mal zu durchsuchen.
    /// </summary>
    private sealed record CellTag(
        TrackColumn Column,
        Image? Picture = null,
        TextBlock? First = null,
        TextBlock? Second = null);
}
