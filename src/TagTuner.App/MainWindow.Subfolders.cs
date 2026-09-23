using TagTuner.Core.Audio;
using TagTuner.Core.Folders;
using TagTuner.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TagTuner.App;

// ══ Unterordner im Hauptfenster ══════════════════════════════════
//
// Jede Hälfte hat ihren eigenen Bereich für die Unterordner ihres Ordners.
// So geht die geteilte Ansicht mit Unterordnern zusammen: Oben ein Interpret
// mit seinen Alben, unten ein anderer, und zwischen allen offenen Listen
// lässt sich ziehen.

public sealed partial class MainWindow
{
    /// <summary>Alle aufgeklappten Unterordner-Listen, aus beiden Hälften.</summary>
    private readonly List<TrackPane> _subPanes = [];

    /// <summary>Zu welcher Hälfte eine aufgeklappte Liste gehört.</summary>
    private readonly Dictionary<TrackPane, SubfolderArea> _subOwner = [];

    private SubfolderArea _subA = null!;
    private SubfolderArea _subB = null!;

    /// <summary>
    /// Ab so vielen Unterordnern ist es kein Album und kein Interpret mehr,
    /// sondern eine Bibliothek. Dann bleibt der Bereich weg.
    /// </summary>
    private const int MaxSubfolders = 80;

    private void InitSubfolders()
    {
        _subA = new SubfolderArea(this, PaneA, HalfA, SubHostA);
        _subB = new SubfolderArea(this, PaneB, HalfB, SubHostB);
    }

    /// <summary>
    /// Wechselt von einer Unterordner-Liste zurück in die Liste ihrer Hälfte.
    /// Vor dem Navigieren nötig: Sonst navigierte man in der
    /// Unterordner-Liste, die dabei samt Abschnitt verschwindet.
    /// </summary>
    private void LeaveSubfolder()
    {
        if (_activePane is null || !_subOwner.TryGetValue(_activePane, out var area)) return;
        _activePane = area.Main;
        MarkActivePane();
    }

    private void MarkActivePane()
    {
        PaneA.SetActive(_activePane == PaneA);
        PaneB.SetActive(_activePane == PaneB);
        foreach (var pane in _subPanes) pane.SetActive(_activePane == pane);
    }

    /// <summary>
    /// Hält die Zahl im Kopf eines Abschnitts aktuell, nachdem Dateien
    /// hinzukamen oder gingen, und die Aufteilung der Hälfte, deren Ordner
    /// es ist.
    /// </summary>
    private void UpdateSection(FolderTab tab)
    {
        _subA.Update(tab);
        _subB.Update(tab);
    }

    /// <summary>Baut die Unterordner der Hälfte neu, die diesen Tab zeigt.</summary>
    private void RebuildSubfoldersFor(FolderTab tab)
    {
        if (ReferenceEquals(tab, PaneA.Tab)) _subA.Rebuild();
        if (_split is not null && ReferenceEquals(tab, PaneB.Tab)) _subB.Rebuild();
    }

    /// <summary>
    /// Die Unterordner eines Ordners, in denen irgendwo Musik liegt, mit der
    /// Zahl der Lieder direkt darin. Läuft im Hintergrund.
    /// </summary>
    private static List<(string Path, string Name, int Count)> FindSubfolders(string folder)
    {
        try
        {
            // Erst zählen, dann in die Tiefe schauen: Das Nachsehen, ob
            // unter einem Ordner Musik liegt, kostet je Ordner einen
            // Plattenzugriff. Bei hunderten Unterordnern ist das hier die
            // Bibliothek selbst, und für die gibt es den Baum links.
            var all = FolderScanner.Subfolders(folder);
            if (all.Count > MaxSubfolders) return [];

            return all
                .Where(f => FolderScanner.HasAudioBelow(f.Path))
                .Select(f => (f.Path, f.Name, Count: CountAudio(f.Path)))
                .ToList();
        }
        catch { return []; }

        static int CountAudio(string path)
        {
            try
            {
                return Directory.EnumerateFiles(path)
                    .Count(f => AudioFormats.IsAudioFile(Path.GetFileName(f)));
            }
            catch { return 0; }
        }
    }

    /// <summary>
    /// Der Unterordner-Bereich einer Hälfte: eine Leiste „UNTERORDNER", darunter
    /// die Abschnitte.
    ///
    /// Hat der Ordner eigene Lieder, bleibt der Bereich eine schmale Leiste,
    /// bis man sie aufklappt; die Liste soll nicht für etwas schrumpfen, das
    /// man gerade nicht braucht. Hat er keine, etwa der Ordner eines
    /// Interpreten, bleibt von der Liste nur ihre Kopfzeile, und der Platz
    /// gehört den Unterordnern.
    /// </summary>
    private sealed class SubfolderArea
    {
        private readonly MainWindow _w;
        public TrackPane Main { get; }

        private readonly Grid _half;
        private readonly Grid _host;
        private readonly Grid _bar;
        private readonly FontIcon _chevron;
        private readonly TextBlock _count;
        private readonly ScrollViewer _scroll;
        private readonly StackPanel _list;

        private readonly Dictionary<string, TextBlock> _counts = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<TrackPane> _panes = [];

        /// <summary>Hochgezählt bei jedem Neuaufbau, damit ein verspätetes Einlesen nichts überschreibt.</summary>
        private int _round;

        /// <summary>
        /// Ob die Leiste aufgeklappt ist. Gilt über das Navigieren hinweg:
        /// Wer sie einmal offen haben will, will das beim nächsten Ordner auch.
        /// </summary>
        private bool _open;

        public SubfolderArea(MainWindow window, TrackPane main, Grid half, Grid host)
        {
            _w = window;
            Main = main;
            _half = half;
            _host = host;

            _host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _host.BorderThickness = new Thickness(0, 1, 0, 0);
            _host.BorderBrush = Res("DividerStrokeColorDefaultBrush");
            _host.Visibility = Visibility.Collapsed;

            _chevron = new FontIcon
            {
                Glyph = "\uE76C",
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Res("TextFillColorTertiaryBrush"),
            };
            _count = new TextBlock
            {
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Res("TextFillColorTertiaryBrush"),
            };

            _bar = new Grid
            {
                Padding = new Thickness(18, 8, 18, 7),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            _chevron,
                            new TextBlock
                            {
                                Text = Strings.T("SUBFOLDERS"),
                                Style = (Style)Application.Current.Resources["SectionLabel"],
                                VerticalAlignment = VerticalAlignment.Center,
                            },
                            _count,
                        },
                    },
                },
            };
            _bar.Tapped += (_, e) =>
            {
                if (!OwnTracks) return;
                _open = !_open;
                UpdateLayout();
                e.Handled = true;
            };

            _list = new StackPanel { Spacing = 6, Padding = new Thickness(10, 0, 10, 12) };
            _scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _list,
            };

            Grid.SetRow(_scroll, 1);
            _host.Children.Add(_bar);
            _host.Children.Add(_scroll);
        }

        private bool OwnTracks => Main.Tab is { Tracks.Count: > 0 };

        /// <summary>
        /// Baut die Abschnitte neu. Das Einlesen läuft im Hintergrund; bis es
        /// fertig ist, bleibt der Bereich wie er war, statt kurz zu verschwinden.
        /// </summary>
        public async void Rebuild()
        {
            var round = ++_round;
            var top = Main.Tab;

            if (top is null || top.Recursive || _half.Visibility != Visibility.Visible)
            {
                Clear();
                UpdateLayout();
                return;
            }

            var folder = top.Path;

            // Ein Laufwerk ist keine Sammlung von Alben. Unter C:\ läge sonst
            // „Windows" in der Liste, weil dort Systemklänge als WAV liegen.
            var root = Path.GetPathRoot(folder);
            if (root is not null && string.Equals(
                    root.TrimEnd(Path.DirectorySeparatorChar),
                    folder.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                Clear();
                UpdateLayout();
                return;
            }

            var found = await Task.Run(() => FindSubfolders(folder));

            // Zwischenzeitlich woandershin gewechselt.
            if (round != _round || !ReferenceEquals(Main.Tab, top)) return;

            Clear();
            foreach (var (subPath, name, count) in found)
                _list.Children.Add(Section(subPath, name, count));

            _count.Text = found.Count.ToString();
            UpdateLayout();
        }

        /// <summary>Räumt den Bereich leer, etwa wenn die Hälfte verschwindet.</summary>
        public void Clear()
        {
            _round++;
            if (_panes.Contains(_w._activePane)) _w.LeaveSubfolder();

            foreach (var pane in _panes)
            {
                _w._subPanes.Remove(pane);
                _w._subOwner.Remove(pane);
            }
            _panes.Clear();
            _counts.Clear();
            _list.Children.Clear();
            _count.Text = "";
        }

        public void Update(FolderTab tab)
        {
            if (_counts.TryGetValue(tab.Path, out var label))
                label.Text = Strings.T("{0} files", tab.Tracks.Count);

            // Ob der Ordner noch eigene Lieder hat, entscheidet über die
            // Aufteilung.
            if (ReferenceEquals(tab, Main.Tab)) UpdateLayout();
        }

        /// <summary>Verteilt den Platz der Hälfte zwischen ihrer Liste und den Unterordnern.</summary>
        public void UpdateLayout()
        {
            var rows = _half.RowDefinitions;
            var hasSubs = _list.Children.Count > 0;

            if (!hasSubs)
            {
                _host.Visibility = Visibility.Collapsed;
                Main.SetCompact(false);
                rows[0].Height = new GridLength(1, GridUnitType.Star);
                rows[1].Height = GridLength.Auto;
                return;
            }

            _host.Visibility = Visibility.Visible;

            if (OwnTracks)
            {
                Main.SetCompact(false);
                rows[0].Height = new GridLength(1, GridUnitType.Star);
                rows[1].Height = GridLength.Auto;

                _chevron.Visibility = Visibility.Visible;
                _chevron.Glyph = _open ? "\uE70D" : "\uE76C";
                _scroll.Visibility = _open ? Visibility.Visible : Visibility.Collapsed;

                // Höchstens gut die Hälfte: Die Lieder des Ordners selbst
                // sollen nicht zu einem Schlitz zusammengedrückt werden.
                _host.MaxHeight = _open ? Math.Max(220, _half.ActualHeight * 0.55) : double.PositiveInfinity;
            }
            else
            {
                // Die leere Liste zeigte nur „0 Tracks" und nähme Platz weg.
                // Ihre Kopfzeile bleibt, damit man in der geteilten Ansicht
                // sieht, welche Hälfte welcher Ordner ist.
                Main.SetCompact(true);
                rows[0].Height = GridLength.Auto;
                rows[1].Height = new GridLength(1, GridUnitType.Star);

                _chevron.Visibility = Visibility.Collapsed;
                _scroll.Visibility = Visibility.Visible;
                _host.MaxHeight = double.PositiveInfinity;
            }
        }

        /// <summary>Öffnet einen Ordner in dieser Hälfte.</summary>
        private void OpenHere(string path)
        {
            _w.OnPaneActivated(null, Main);
            _w.NavigateActive(path);
        }

        private void Register(TrackPane pane)
        {
            _panes.Add(pane);
            _w._subPanes.Add(pane);
            _w._subOwner[pane] = this;
        }

        private void Unregister(TrackPane pane)
        {
            if (_w._activePane == pane) _w.LeaveSubfolder();
            _panes.Remove(pane);
            _w._subPanes.Remove(pane);
            _w._subOwner.Remove(pane);
        }

        /// <summary>
        /// Ein Abschnitt: Kopf zum Auf- und Zuklappen, darunter bei Bedarf die
        /// Liste und die Unterordner dieses Ordners, eingerückt und selbst
        /// wieder aufklappbar. <paramref name="parent"/> sammelt das Zuklappen,
        /// damit ein zugeklappter Ordner auch alles darunter schließt.
        /// </summary>
        private FrameworkElement Section(string path, string name, int count, List<Action>? parent = null)
        {
            var body = new Grid { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };

            var chevron = new FontIcon
            {
                Glyph = "\uE76C",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Res("TextFillColorTertiaryBrush"),
            };

            var art = new Image { Stretch = Stretch.UniformToFill };
            TrackArt.SetPath(art, path);

            var countText = new TextBlock
            {
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Res("TextFillColorTertiaryBrush"),
                Text = Strings.T("{0} files", count),
            };
            _counts[path] = countText;

            var openBtn = new Button
            {
                Content = "\uE8A7",
                FontFamily = new FontFamily("Segoe Fluent Icons"),
                FontSize = 11,
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(openBtn, Strings.T("Open this folder"));
            openBtn.Click += (_, _) => OpenHere(path);

            var head = new Grid
            {
                ColumnSpacing = 10,
                Padding = new Thickness(10, 6, 6, 6),
                CornerRadius = new CornerRadius(6),
                Background = Res("CardBackgroundFillColorDefaultBrush"),
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
            };

            var cover = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(4),
                Background = Res("ControlFillColorDefaultBrush"),
                Child = new Grid
                {
                    Children =
                    {
                        new FontIcon { Glyph = "\uE8B7", FontSize = 13,
                                       Foreground = Res("TextFillColorTertiaryBrush") },
                        art,
                    },
                },
            };

            var title = new TextBlock
            {
                Text = name,
                FontSize = 13.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            Grid.SetColumn(chevron, 0);
            Grid.SetColumn(cover, 1);
            Grid.SetColumn(title, 2);
            Grid.SetColumn(countText, 3);
            Grid.SetColumn(openBtn, 4);
            head.Children.Add(chevron);
            head.Children.Add(cover);
            head.Children.Add(title);
            head.Children.Add(countText);
            head.Children.Add(openBtn);

            TrackPane? pane = null;
            var expanded = false;
            var expandRound = 0;
            var children = new List<Action>();
            parent?.Add(Collapse);

            head.Tapped += (_, e) =>
            {
                // Der Knopf zum Öffnen liegt auf dieser Fläche. Sein Klick soll
                // nur navigieren, nicht nebenbei einen Abschnitt aufklappen, der
                // im nächsten Moment verschwindet.
                for (var node = e.OriginalSource as DependencyObject; node is not null && node != head;
                     node = VisualTreeHelper.GetParent(node))
                {
                    if (node == openBtn) return;
                }

                if (!expanded) Expand();
                else Collapse();
                e.Handled = true;
            };

            head.RightTapped += (_, e) =>
            {
                var menu = new MenuFlyout();
                menu.Items.Add(Item(!expanded ? Strings.T("Expand") : Strings.T("Collapse"),
                    () => { if (!expanded) Expand(); else Collapse(); }));
                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(Item(Strings.T("Open this folder"), () => OpenHere(path)));
                menu.Items.Add(Item(Strings.T("Open in a new tab"), () => _w.OpenTab(path)));
                menu.Items.Add(Item(Strings.T("Open in Explorer"), () => Reveal(path)));
                menu.ShowAt(head, e.GetPosition(head));
                e.Handled = true;
            };

            async void Expand()
            {
                var round = ++expandRound;
                expanded = true;
                body.Visibility = Visibility.Visible;
                chevron.Glyph = "\uE70D";

                var list = new StackPanel { Spacing = 6 };
                body.Children.Add(list);

                // Ein Ordner ohne eigene Lieder, etwa ein Album aus mehreren CDs,
                // bekommt keine leere Liste, nur seine Unterordner.
                if (count > 0)
                {
                    var tab = new FolderTab(path);

                    // Eine feste Höhe: Mehrere offene Abschnitte sollen nebeneinander
                    // Platz haben, und die Liste darin scrollt selbst.
                    pane = new TrackPane { Height = 380 };
                    _w.WirePane(pane);
                    pane.ApplyColumns(_w._settings);
                    pane.Bind(tab);

                    Register(pane);
                    list.Children.Add(pane);

                    _w.Fire(_w.LoadTabAsync(tab), Strings.T("Reading…"));
                }

                var found = await Task.Run(() => FindSubfolders(path));

                // Inzwischen zugeklappt, oder der ganze Bereich ist neu aufgebaut.
                if (round != expandRound || !expanded || found.Count == 0) return;

                var nested = new StackPanel { Spacing = 6, Margin = new Thickness(22, 0, 0, 0) };
                foreach (var (subPath, subName, subCount) in found)
                    nested.Children.Add(Section(subPath, subName, subCount, children));
                list.Children.Add(nested);
            }

            void Collapse()
            {
                if (!expanded) return;
                expanded = false;
                expandRound++;

                // Erst alles darunter schließen, damit keine Liste übrig bleibt,
                // die noch als aktiv oder als Ziel gilt.
                foreach (var close in children.ToList()) close();
                children.Clear();

                if (pane is not null)
                {
                    Unregister(pane);
                    pane = null;
                }

                body.Children.Clear();
                body.Visibility = Visibility.Collapsed;
                chevron.Glyph = "\uE76C";

                if (parent is null)
                {
                    _w.RebuildChrome();
                    _w.UpdateAnalysisPanel();
                    _w.UpdateMetaPanel();
                }
            }

            return new StackPanel { Children = { head, body } };

            static MenuFlyoutItem Item(string text, Action run)
            {
                var item = new MenuFlyoutItem { Text = text };
                item.Click += (_, _) => run();
                return item;
            }
        }
    }
}
