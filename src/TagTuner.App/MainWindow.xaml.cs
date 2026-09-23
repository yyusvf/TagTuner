using TagTuner.Core.Audio;
using TagTuner.Core.Folders;
using TagTuner.Core.Metadata;
using TagTuner.Core.Model;
using TagTuner.Core.Safety;
using TagTuner.Core.Settings;
using TagTuner.Core.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;

namespace TagTuner.App;

public sealed partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly HistoryStore _history = new();

    private readonly List<FolderTab> _tabs = [];
    private int _active;
    private int? _split;          // Index des Tabs in der unteren Hälfte

    private bool _suppressSelection;

    /// <summary>
    /// Der Stand der Felder direkt nach dem Laden einer Auswahl.
    ///
    /// „Geändert" wird daraus abgeleitet statt über ein Flag im TextChanged:
    /// WinUI löst TextChanged verzögert aus, sodass das Befüllen der Felder
    /// noch als Nutzereingabe ankam — die App meldete „Tags schreiben",
    /// obwohl niemand etwas angefasst hatte.
    /// </summary>
    private readonly Dictionary<TextBox, string> _loaded = [];

    private static readonly int[] Rates = [44100, 48000, 88200, 96000, 176400, 192000];

    private AudioPlayer _player = null!;
    private CancellationTokenSource? _search;

    /// <summary>
    /// Der Suchindex. Wird beim ersten Suchen gebaut und nach jedem eigenen
    /// Schreibvorgang verworfen.
    /// </summary>
    private Task<LibraryIndex>? _index;

    /// <summary>
    /// Der Tab oben in der Leiste. Navigieren, Zurück und Vor gelten ihm.
    /// </summary>
    private FolderTab TopTab => _tabs[_active];

    /// <summary>
    /// Der Tab der Liste, mit der gerade gearbeitet wird: Metadaten,
    /// Ordner-Analyse und Sammelaktionen beziehen sich auf ihn. Meist ist das
    /// der obere Tab; ist ein aufgeklappter Unterordner aktiv, dessen.
    /// </summary>
    private FolderTab ActiveTab => _activePane?.Tab ?? TopTab;
    private TrackPane ActivePane => _activePane;
    private TrackPane _activePane = null!;

    public MainWindow()
    {
        InitializeComponent();

        // Sprache steht vor allem anderen fest; danach einmal über das
        // Markup, damit die festen Beschriftungen stimmen.
        Strings.Use(_settings.Language);
        Localizer.Apply(Root);
        ApplyTheme();

        Title = "TagTuner";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Die Fenstertasten sitzen sonst oben am Rand und die eigenen Knoepfe
        // mittig in der 48 Pixel hohen Leiste — sie stehen dann versetzt.
        // „Tall" gibt den Systemtasten dieselbe Hoehe, dann fluchten sie.
        AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;

        // Das Symbol der .exe gilt nicht für das Fenster. Ohne das hier fehlt
        // es in der Vorschau über der Taskleiste und bei Alt+Tab.
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "TagTuner.ico");
        if (File.Exists(icon)) AppWindow.SetIcon(icon);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)_settings.WindowWidth, (int)_settings.WindowHeight));

        _activePane = PaneA;
        foreach (var pane in new[] { PaneA, PaneB }) WirePane(pane);
        InitSubfolders();
        InitUndo();

        MetaCol.Width = new GridLength(_settings.MetaWidth);
        TreeCol.Width = new GridLength(_settings.TreeWidth);
        SideCol.Width = new GridLength(_settings.SideWidth);
        ApplyColumns();

        FFormat.ItemsSource = AudioFormats.Targets;
        FRate.ItemsSource = Rates.Select(FormatRate).ToList();
        FFormat.SelectionChanged += (_, _) => UpdatePlan();
        FRate.SelectionChanged += (_, _) => UpdatePlan();
        foreach (var box in TagBoxes())
            box.TextChanged += (_, _) => { if (!_suppressSelection) UpdatePlan(); };

        UpdateFfmpegHint();

        _player = new AudioPlayer(DispatcherQueue, _settings.Volume);
        _player.Changed += UpdatePlayerBar;
        _player.Failed += message => StatusText.Text = message;
        VolumeSlider.Value = _settings.Volume * 100;
        UpdatePlayerBar();

        // Am Baum selbst lauschen statt in der Zeilenvorlage: Dort kamen die
        // Zeigerereignisse nachweislich nicht an — das Protokoll blieb leer,
        // während der Ordner sich trotzdem öffnete. handledEventsToo sorgt
        // dafür, dass wir den Druck auch dann sehen, wenn die Liste ihn schon
        // für sich verbucht hat.
        FolderTree.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(OnTreePressedAnywhere),
            handledEventsToo: true);

        BuildTreeRoots();

        var start = App.Launch.FolderToOpen ?? _settings.LastFolder;
        if (string.IsNullOrWhiteSpace(start) || !Directory.Exists(start))
            start = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

        _tabs.Add(new FolderTab(start));
        PaneA.Bind(ActiveTab);

        RebuildChrome();
        _ = LoadTabAsync(ActiveTab, App.Launch.File);

        Fire(CheckForUpdatesAsync(), Strings.T("Updates"));

        SingleInstance.Listen(DispatcherQueue, OnSecondLaunch);

        Closed += (_, _) =>
        {
            _settings.WindowWidth = AppWindow.Size.Width;
            _settings.WindowHeight = AppWindow.Size.Height;
            _settings.MetaWidth = MetaCol.ActualWidth;
            _settings.TreeWidth = TreeCol.ActualWidth;
            _settings.SideWidth = SideCol.ActualWidth;
            RememberColumns();
            _settings.LastFolder = TopTab.Path;
            _settings.Volume = _player.Volume;
            _settings.Save();
            _player.Dispose();
        };
    }

    private TextBox[] TagBoxes() =>
        [FTitle, FArtist, FAlbum, FYear, FTrack, FGenre, FAlbumArtist, FComposer, FComment, FDisc];

    private static string FormatRate(int hz) =>
        hz % 1000 == 0 ? $"{hz / 1000} kHz" : $"{hz / 1000.0:0.0} kHz";

    private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>
    /// Startet eine Aufgabe und protokolliert, wenn sie scheitert.
    /// <c>_ = MachWasAsync()</c> verschluckt jede Ausnahme spurlos.
    /// </summary>
    private void Fire(Task work, string label) => _ = Watch(work, label);

    private async Task Watch(Task work, string label)
    {
        try { await work; }
        catch (Exception ex)
        {
            StatusText.Text = Strings.T("{0} failed: {1}", label, ex.Message);
        }
    }

    private static TrackColumnLayout Columns =>
        (TrackColumnLayout)Application.Current.Resources["TrackColumns"];

    /// <summary>
    /// Worauf sich die Metadatenspalte bezieht: die Auswahl, oder — wenn
    /// nichts gewählt ist — der ganze Ordner. So zeigt ein frisch geöffneter
    /// Ordner sofort, worin seine Dateien sich einig sind.
    /// </summary>
    private List<AudioTrack> TargetTracks()
    {
        var sel = ActivePane.Selected();
        return sel.Count > 0 ? sel : [.. ActiveTab.Tracks];
    }

    private bool FolderScope => ActivePane.Selected().Count == 0 && ActiveTab.Tracks.Count > 0;

    // ══ Titelleiste ══════════════════════════════════════════════

    private void RebuildChrome()
    {
        RebuildCrumbs();
        RebuildTabs();
        BackBtn.IsEnabled = TopTab.CanGoBack;
        ForwardBtn.IsEnabled = TopTab.CanGoForward;
        SplitBtn.Background = _split is null ? new SolidColorBrush(Microsoft.UI.Colors.Transparent)
                                             : Res("AccentDimBrush");
        SplitBtn.Foreground = _split is null ? Res("TextFillColorPrimaryBrush") : Res("AccentBrush");
    }

    /// <summary>
    /// Der Pfad als Breadcrumbs, linksbündig und nur so breit wie nötig.
    /// Reicht der Platz nicht, schiebt sich der Anfang nach links heraus —
    /// der Ordner, in dem man steht, bleibt sichtbar.
    /// </summary>
    private void RebuildCrumbs()
    {
        var parts = ActiveTab.Crumbs();
        CrumbHost.Children.Clear();

        for (var i = 0; i < parts.Count; i++)
        {
            var last = i == parts.Count - 1;

            if (i > 0)
            {
                CrumbHost.Children.Add(new TextBlock
                {
                    Text = "›",
                    FontSize = 12,
                    Margin = new Thickness(6, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Res("TextFillColorTertiaryBrush"),
                });
            }

            var text = new TextBlock
            {
                Text = parts[i],
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = last ? Microsoft.UI.Text.FontWeights.SemiBold
                                  : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = last ? Res("TextFillColorPrimaryBrush") : Res("TextFillColorSecondaryBrush"),
            };

            if (!last)
            {
                // Auf einen Vorfahren klicken springt dorthin.
                var target = string.Join(Path.DirectorySeparatorChar, parts.Take(i + 1));
                if (!target.Contains(Path.DirectorySeparatorChar)) target += Path.DirectorySeparatorChar;
                text.PointerEntered += (s, _) => ((TextBlock)s).Foreground = Res("AccentBrush");
                text.PointerExited += (s, _) => ((TextBlock)s).Foreground = Res("TextFillColorSecondaryBrush");
                text.Tapped += (_, _) => { if (Directory.Exists(target)) NavigateActive(target); };
            }

            CrumbHost.Children.Add(text);
        }

        // Ans Ende scrollen, damit bei Platzmangel der aktuelle Ordner steht.
        DispatcherQueue.TryEnqueue(() =>
            CrumbScroll.ChangeView(CrumbScroll.ScrollableWidth, null, null, disableAnimation: true));
    }

    /// <summary>
    /// Deckelt die Pfadleiste auf den Platz, der neben den Tabs übrig bleibt,
    /// und hält die Werkzeugknöpfe von den Fenstertasten des Systems fern.
    /// Deren Breite hängt an der Anzeigeskalierung, darum bei jeder
    /// Größenänderung neu.
    /// </summary>
    private void OnTitleBarResized(object sender, SizeChangedEventArgs e)
    {
        var scale = Root.XamlRoot?.RasterizationScale ?? 1.0;
        var inset = AppWindow.TitleBar.RightInset / scale;

        // Meldet das System die Breite (noch) nicht, lieber die drei Tasten
        // grosszuegig freihalten als die Knoepfe darunter verschwinden lassen.
        CaptionSpacer.Width = inset > 0 ? inset : 141;
    }

    /// <summary>Die Pfadleiste darf nur den Platz nehmen, den die Tabs übrig lassen.</summary>
    private void OnCrumbSlotResized(object sender, SizeChangedEventArgs e)
    {
        CrumbBar.MaxWidth = Math.Max(70, e.NewSize.Width);
        CrumbScroll.ChangeView(CrumbScroll.ScrollableWidth, null, null, disableAnimation: true);
    }

    /// <summary>
    /// Baut die Tab-Leiste neu.
    ///
    /// Jeder Tab ist ein echter <see cref="Button"/>, kein angetippter Border:
    /// In der angepassten Titelleiste zählt alles, was kein Bedienelement ist,
    /// zur Ziehfläche des Fensters. Ein Border mit Tapped bekam den Klick
    /// deshalb nie zu sehen — das Fenster wurde stattdessen verschoben.
    /// </summary>
    private void RebuildTabs()
    {
        TabStrip.Children.Clear();

        for (var i = 0; i < _tabs.Count; i++)
        {
            var idx = i;
            var tab = _tabs[i];
            var isActive = i == _active;
            var isSplit = _split == i;

            var label = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
            };

            if (tab.IsMixed)
            {
                label.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = Res("WarnBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            label.Children.Add(new TextBlock
            {
                Text = tab.Name,
                FontSize = 12,
                MaxWidth = 130,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = isActive ? Res("TextFillColorPrimaryBrush") : Res("TextFillColorSecondaryBrush"),
            });

            var open = new Button
            {
                Content = label,
                Padding = new Thickness(9, 0, _tabs.Count > 1 ? 2 : 9, 0),
                Height = 30,
                MinWidth = 0,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            ToolTipService.SetToolTip(open, tab.Path + (tab.IsMixed ? "\n" + Strings.T("Folder is not uniform") : ""));
            open.Click += (_, _) => ActivateTab(idx);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            row.Children.Add(open);

            if (_tabs.Count > 1)
            {
                var close = new Button
                {
                    Content = "\uE711",
                    FontFamily = new FontFamily("Segoe Fluent Icons"),
                    FontSize = 9,
                    Width = 20,
                    Height = 20,
                    MinWidth = 0,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    Foreground = Res("TextFillColorTertiaryBrush"),
                };
                ToolTipService.SetToolTip(close, Strings.T("Close tab"));
                close.Click += (_, _) => CloseTab(idx);
                row.Children.Add(close);
            }

            TabStrip.Children.Add(new Border
            {
                Child = row,
                Height = 30,
                CornerRadius = new CornerRadius(5),
                Background = isActive ? Res("AccentDimBrush") : Res("ControlFillColorDefaultBrush"),
                BorderThickness = new Thickness(1),
                BorderBrush = isActive ? Res("AccentBrush")
                            : isSplit ? Res("ControlStrokeColorDefaultBrush")
                            : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            });
        }
    }

    // ══ Tabs ═════════════════════════════════════════════════════

    private void ActivateTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;
        _active = index;
        if (_split == index) _split = null;   // nicht zweimal derselbe Tab

        _activePane = PaneA;
        PaneA.Bind(TopTab);
        ApplySplitLayout();
        RebuildChrome();
        UpdateAnalysisPanel();
        UpdateMetaPanel();

        if (TopTab.Analysis is null) _ = LoadTabAsync(TopTab);
        else _subA.Rebuild();
    }

    private void CloseTab(int index)
    {
        if (_tabs.Count < 2) return;
        _tabs.RemoveAt(index);

        if (_split is int sp)
        {
            if (sp == index) _split = null;
            else if (sp > index) _split = sp - 1;
        }
        if (_active >= _tabs.Count) _active = _tabs.Count - 1;
        else if (_active > index) _active--;
        if (_split == _active) _split = null;

        ActivateTab(_active);
    }

    private void OpenTab(string path)
    {
        var existing = _tabs.FindIndex(t =>
            string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) { ActivateTab(existing); return; }

        _tabs.Add(new FolderTab(path));
        ActivateTab(_tabs.Count - 1);
    }

    private void OnAddTab(object sender, RoutedEventArgs e)
    {
        // Ein zweiter Tab auf denselben Ordner ist sinnlos — neu geöffnet
        // wird der übergeordnete, von dort navigiert man weiter.
        var parent = Path.GetDirectoryName(ActiveTab.Path.TrimEnd(Path.DirectorySeparatorChar));
        OpenTab(parent is not null && Directory.Exists(parent) ? parent : ActiveTab.Path);
    }

    private void OnToggleSplit(object sender, RoutedEventArgs e)
    {
        if (_split is not null)
        {
            _split = null;
        }
        else
        {
            if (_tabs.Count < 2)
            {
                var parent = Path.GetDirectoryName(ActiveTab.Path.TrimEnd(Path.DirectorySeparatorChar));
                _tabs.Add(new FolderTab(parent is not null && Directory.Exists(parent)
                    ? parent : ActiveTab.Path));
            }
            _split = Enumerable.Range(0, _tabs.Count).First(i => i != _active);
        }

        ApplySplitLayout();
        RebuildChrome();

        if (_split is int s && _tabs[s].Analysis is null) _ = LoadTabAsync(_tabs[s]);
    }

    private void ApplySplitLayout()
    {
        if (_split is int s && s < _tabs.Count)
        {
            PaneB.Bind(_tabs[s]);
            HalfB.Visibility = Visibility.Visible;
            Splitter.Visibility = Visibility.Visible;
            PaneHost.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);

            // Noch nicht eingelesen: Das Einlesen baut die Unterordner danach.
            if (_tabs[s].Analysis is not null) _subB.Rebuild();
        }
        else
        {
            HalfB.Visibility = Visibility.Collapsed;
            Splitter.Visibility = Visibility.Collapsed;
            PaneHost.RowDefinitions[2].Height = GridLength.Auto;
            _subB.Clear();
            _subB.UpdateLayout();
        }

        MarkActivePane();
        _subA.UpdateLayout();
    }

    // ══ Navigation ═══════════════════════════════════════════════

    private void NavigateActive(string path)
    {
        LeaveSubfolder();

        // Sonst liest ein zweiter Klick auf denselben Ordner alles noch einmal
        // ein und wirft dabei die Auswahl weg.
        if (string.Equals(TopTab.Path, path, StringComparison.OrdinalIgnoreCase)
            && TopTab.Analysis is not null)
        {
            return;
        }

        TopTab.Navigate(path);
        RebuildChrome();
        Fire(LoadTabAsync(TopTab), Strings.T("Reading…"));
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        LeaveSubfolder();
        if (TopTab.Back() is not null) { RebuildChrome(); _ = LoadTabAsync(TopTab); }
    }

    private void OnForward(object sender, RoutedEventArgs e)
    {
        LeaveSubfolder();
        if (TopTab.Forward() is not null) { RebuildChrome(); _ = LoadTabAsync(TopTab); }
    }

    // ══ Wiedergabe ═══════════════════════════════════════════════

    private void OnTogglePlay(object sender, RoutedEventArgs e)
    {
        if (_player.Current is not null) { _player.Toggle(); return; }

        if (ActivePane.Selected().FirstOrDefault() is { } track) _player.Play(track);
        else if (ActiveTab.Tracks.FirstOrDefault() is { } first) _player.Play(first);
    }

    private void OnVolumeChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_player is null) return;
        _player.Volume = e.NewValue / 100.0;
    }

    /// <summary>
    /// WinUI-Panels schneiden ihren Inhalt nicht ab. Ohne diese Maske stünde
    /// der Lautstärkeregler auch bei Breite 0 noch sichtbar da.
    /// </summary>
    private void OnVolumeHostResized(object sender, SizeChangedEventArgs e)
    {
        VolumeHost.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height),
        };
    }

    private const double VolumeWidth = 108;

    /// <summary>Fährt die Lautstärke neben dem Abspielknopf heraus oder wieder ein.</summary>
    private void ShowVolume(bool show)
    {
        var target = show ? VolumeWidth : 0;
        if (Math.Abs(VolumeHost.Width - target) < 0.5) return;

        var story = new Microsoft.UI.Xaml.Media.Animation.Storyboard();

        var width = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(180),
            // Breite ist eine Layout-Eigenschaft — ohne das laeuft nichts.
            EnableDependentAnimation = true,
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
            {
                EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut,
            },
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(width, VolumeHost);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(width, "Width");
        story.Children.Add(width);

        var fade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = show ? 1 : 0,
            Duration = TimeSpan.FromMilliseconds(show ? 220 : 120),
        };
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, VolumeHost);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");
        story.Children.Add(fade);

        story.Begin();
    }

    private void UpdatePlayerBar()
    {
        var track = _player.Current;

        PlayBtn.Content = _player.IsPlaying ? "\uE769" : "\uE768";
        // Die Leiste wird schon im Konstruktor gefüllt, bevor der erste Tab
        // existiert — ActiveTab wäre dort ein Zugriff ins Leere.
        var haveTracks = _tabs.Count > 0 && ActiveTab.Tracks.Count > 0;

        PlayBtn.IsEnabled = track is not null
            || haveTracks
            || ActivePane.Selected().Count > 0;

        ShowVolume(track is not null);

        if (track is null)
        {
            NowPlaying.Text = "";
            NowTime.Text = "";
            return;
        }

        NowPlaying.Text = string.IsNullOrWhiteSpace(track.Title) ? track.FileName : track.Title;

        NowTime.Text = $"{Clock(_player.Position)} / {Clock(_player.Duration)}";

        static string Clock(TimeSpan t) =>
            t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    // ══ Panelbreiten ═════════════════════════════════════════════

    private ColumnDefinition? _panelCol;
    private bool _panelFromRight;
    private double _panelStartX;
    private double _panelStartWidth;

    private void OnPanelGripPressed(object sender, PointerRoutedEventArgs e)
    {
        var grip = (GripArea)sender;
        (_panelCol, _panelFromRight) = (string)grip.Tag switch
        {
            "meta" => (MetaCol, false),
            "tree" => (TreeCol, false),
            _ => (SideCol, true),
        };

        _panelStartX = e.GetCurrentPoint(Work).Position.X;
        _panelStartWidth = _panelCol.ActualWidth;
        grip.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPanelGripMoved(object sender, PointerRoutedEventArgs e)
    {
        ((GripArea)sender).ShowResizeCursor(true);
        if (_panelCol is null) return;

        // Die rechte Spalte waechst nach links, darum dort das Vorzeichen drehen.
        var dx = e.GetCurrentPoint(Work).Position.X - _panelStartX;
        if (_panelFromRight) dx = -dx;

        var width = Math.Max(_panelCol.MinWidth, _panelStartWidth + dx);
        _panelCol.Width = new GridLength(width);
        e.Handled = true;
    }

    private void OnPanelGripReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_panelCol is null) return;
        _panelCol = null;
        ((GripArea)sender).ReleasePointerCapture(e.Pointer);

        _settings.MetaWidth = MetaCol.ActualWidth;
        _settings.TreeWidth = TreeCol.ActualWidth;
        _settings.SideWidth = SideCol.ActualWidth;
        _settings.Save();
        e.Handled = true;
    }

    private void OnPanelGripExited(object sender, PointerRoutedEventArgs e)
    {
        if (_panelCol is null) ((GripArea)sender).ShowResizeCursor(false);
    }

    // ══ Löschen ══════════════════════════════════════════════════

    /// <summary>
    /// Dateien aus dem Kontextmenü entfernen. Vorher gesichert, damit der
    /// Verlauf sie zurücklegen kann — ein endgültiges Löschen bietet die App
    /// bewusst nicht an.
    /// </summary>
    private async void OnPaneDeleteRequested(object? sender, IReadOnlyList<AudioTrack> tracks)
    {
        if (tracks.Count == 0) return;

        var names = string.Join("\n", tracks.Take(6).Select(t => "• " + t.FileName));
        if (tracks.Count > 6)
            names += "\n• " + Strings.T("… and {0} more", tracks.Count - 6);

        if (!await Confirm(
                tracks.Count == 1 ? Strings.T("Delete file")
                                  : Strings.T("Delete {0} files", tracks.Count),
                names + "\n\n" + Strings.T(
                    "Every file is backed up first and can be brought back from the history."),
                Strings.T("Delete")))
            return;

        ReleaseIfAffected(tracks);
        SetBusy(true, Strings.T("Delete {0} file(s)", tracks.Count));

        var backups = new BackupStore(_settings.ResolvedBackupFolder);
        var files = new List<HistoryFile>();
        var errors = new List<string>();

        for (var i = 0; i < tracks.Count; i++)
        {
            try
            {
                var backup = backups.Create(tracks[i].Path);
                File.Delete(tracks[i].Path);
                files.Add(new HistoryFile { Original = tracks[i].Path, BackupPath = backup });
            }
            catch (Exception ex) { errors.Add($"{tracks[i].FileName}: {ex.Message}"); }

            ShowProgress(true, (i + 1) * 100.0 / tracks.Count);
        }

        if (files.Count > 0) _history.Add("delete", Strings.T("{0} file(s) deleted", files.Count), files);

        InvalidateIndex();
        SetBusy(false, null);
        await MergeTabAsync(ActiveTab);
        await ReportAsync(files.Count, errors, []);
    }

    // ══ Laden ════════════════════════════════════════════════════

    private async Task LoadTabAsync(FolderTab tab, string? selectFile = null)
    {
        var path = tab.Path;
        StatusText.Text = Strings.T("Reading…");

        var recursive = tab.Recursive;

        // Eine ganze Bibliothek einzulesen dauert Sekunden. Ohne Anzeige sieht
        // das aus, als haenge die App.
        if (recursive)
        {
            Progress.IsIndeterminate = true;
            ShowProgress(true, 0);
        }

        var tracks = await Task.Run(() => recursive
            ? FolderScanner.TracksRecursive(path)
            : FolderScanner.Tracks(path));

        Progress.IsIndeterminate = false;
        ShowProgress(false, 0);

        if (!string.Equals(tab.Path, path, StringComparison.OrdinalIgnoreCase))
        if (!string.Equals(tab.Path, path, StringComparison.OrdinalIgnoreCase)) return;

        tab.Tracks.Clear();
        foreach (var t in TrackSorting.Apply(tracks, tab.Sort, tab.SortDescending, _settings.SortByDiscThenTrack))
            tab.Tracks.Add(t);
        tab.Analysis = FolderAnalysis.Of(tracks);
        tab.Target = tab.Analysis.ResolveTarget(_settings.DefaultFormat, _settings.DefaultSampleRate);

        if (selectFile is not null)
            tab.SelectedPaths.Add(selectFile);

        PaneFor(tab)?.Refresh();
        // Kein Pfad in der Statuszeile: Der steht oben in der Pfadleiste, und
        // hier soll stehen, was zuletzt getan wurde.
        StatusText.Text = "";

        if (ReferenceEquals(tab, PaneA.Tab) || ReferenceEquals(tab, PaneB.Tab)) RebuildSubfoldersFor(tab);
        else UpdateSection(tab);

        RebuildTabs();
        UpdateAnalysisPanel();
        UpdateMetaPanel();
    }

    /// <summary>
    /// Liest den Ordner neu, tauscht aber nur aus, was sich wirklich geändert
    /// hat.
    ///
    /// <see cref="LoadTabAsync"/> leert die Liste und füllt sie neu. Damit
    /// baut die Ansicht jede Zeile neu auf: Die Bildlaufposition springt an
    /// den Anfang, die Cover werden erneut geholt, und die Auswahl geht durch
    /// das Leeren verloren, bevor sie wiederhergestellt wird. Nach dem
    /// Schreiben von Tags ändern sich aber meist nur ein oder zwei Zeilen.
    /// </summary>
    private async Task MergeTabAsync(FolderTab tab)
    {
        var path = tab.Path;
        var recursive = tab.Recursive;

        var fresh = await Task.Run(() => recursive
            ? FolderScanner.TracksRecursive(path)
            : FolderScanner.Tracks(path));

        // Zwischenzeitlich woandershin navigiert.
        if (!string.Equals(tab.Path, path, StringComparison.OrdinalIgnoreCase)) return;

        var wanted = TrackSorting.Apply(fresh, tab.Sort, tab.SortDescending, _settings.SortByDiscThenTrack);
        var wantedPaths = new HashSet<string>(
            wanted.Select(t => t.Path), StringComparer.OrdinalIgnoreCase);

        // 1. Weg, was es nicht mehr gibt.
        for (var i = tab.Tracks.Count - 1; i >= 0; i--)
            if (!wantedPaths.Contains(tab.Tracks[i].Path)) tab.Tracks.RemoveAt(i);

        // 2. Reihenfolge herstellen und Neues einfügen.
        for (var i = 0; i < wanted.Count; i++)
        {
            var at = IndexOf(tab.Tracks, wanted[i].Path);

            if (at < 0) tab.Tracks.Insert(i, wanted[i]);
            else if (at != i) tab.Tracks.Move(at, i);
            else if (Changed(tab.Tracks[i], wanted[i])) tab.Tracks[i] = wanted[i];
        }

        tab.Analysis = FolderAnalysis.Of(wanted);
        tab.Target = tab.Analysis.ResolveTarget(_settings.DefaultFormat, _settings.DefaultSampleRate);

        PaneFor(tab)?.Refresh();
        RebuildTabs();
        UpdateAnalysisPanel();
        UpdateMetaPanel();
        UpdateSection(tab);

        static int IndexOf(IList<AudioTrack> list, string path)
        {
            for (var i = 0; i < list.Count; i++)
                if (string.Equals(list[i].Path, path, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        // Nur was in der Zeile oder den Feldern zu sehen ist. Wird hier zu viel
        // verglichen, wird zu viel ausgetauscht, und der Vorteil ist dahin.
        static bool Changed(AudioTrack a, AudioTrack b) =>
            a.Title != b.Title || a.Artist != b.Artist || a.Album != b.Album
            || a.AlbumArtist != b.AlbumArtist || a.Genre != b.Genre
            || a.Composer != b.Composer || a.Comment != b.Comment
            || a.Track != b.Track || a.Disc != b.Disc || a.Year != b.Year
            || a.Format != b.Format || a.SampleRate != b.SampleRate
            || a.Duration != b.Duration || a.Size != b.Size || a.HasCover != b.HasCover;
    }

    private TrackPane? PaneFor(FolderTab tab) =>
        PaneA.Tab == tab ? PaneA
        : PaneB.Tab == tab && HalfB.Visibility == Visibility.Visible ? PaneB
        : _subPanes.FirstOrDefault(p => p.Tab == tab);

    // ══ Auswahl ══════════════════════════════════════════════════

    private void OnPaneActivated(object? sender, TrackPane pane)
    {
        if (_activePane == pane) return;
        _activePane = pane;

        // Eine Unterordner-Liste gehört zu einer Hälfte. Deren Tab wird der
        // aktive, damit Navigieren und Zurück dort wirken, wo man arbeitet.
        var owner = _subOwner.TryGetValue(pane, out var area) ? area.Main.Tab : pane.Tab;
        if (owner is not null)
        {
            var idx = _tabs.IndexOf(owner);
            if (idx >= 0) _active = idx;
        }
        MarkActivePane();
        RebuildChrome();
        UpdateAnalysisPanel();
        UpdateMetaPanel();
    }

    private void OnPaneSelectionChanged(object? sender, TrackPane pane)
    {
        if (pane != _activePane) { OnPaneActivated(sender, pane); return; }
        UpdateMetaPanel();
        UpdatePlayerBar();
    }

    private List<AudioTrack> Selected() => ActivePane.Selected();

    /// <summary>Klick auf eine Spaltenüberschrift sortiert die Liste um.</summary>
    private void OnPaneSortRequested(object? sender, TrackSort key)
    {
        if (sender is not TrackPane pane || pane.Tab is not { } tab) return;

        tab.ToggleSort(key);

        var sorted = TrackSorting.Apply(tab.Tracks, tab.Sort, tab.SortDescending, _settings.SortByDiscThenTrack);
        tab.Tracks.Clear();
        foreach (var t in sorted) tab.Tracks.Add(t);

        pane.Refresh();
        StatusText.Text = tab.Sort == TrackSort.Natural
            ? Strings.T("Back in playlist order")
            : Strings.T(tab.SortDescending
                ? "Sorted by {0}, descending. Reordering by hand is off"
                : "Sorted by {0}. Reordering by hand is off", Label(tab.Sort));

        static string Label(TrackSort key) => Strings.T(key switch
        {
            TrackSort.Track => "Track number",
            TrackSort.Disc => "Disc number",
            TrackSort.Title => "Title",
            TrackSort.Artist => "Artist",
            TrackSort.Album => "Album",
            TrackSort.Format => "Format",
            TrackSort.SampleRate => "Sample rate",
            TrackSort.Year => "Year",
            TrackSort.Genre => "Genre",
            TrackSort.AlbumArtist => "Album artist",
            TrackSort.Composer => "Composer",
            TrackSort.Comment => "Comment",
            TrackSort.Bitrate => "Bitrate",
            TrackSort.TagFormat => "Tag format",
            TrackSort.Codec => "Codec",
            _ => "Duration",
        });
    }

    // ══ Ordner-Analyse ═══════════════════════════════════════════

    private void UpdateAnalysisPanel()
    {
        AnalysisPanel.Children.Clear();
        var analysis = ActiveTab.Analysis;
        var target = ActiveTab.Target;

        if (analysis is null || target is null)
        {
            VerdictBox.Background = Res("WarnDimBrush");
            VerdictText.Text = Strings.T("Reading…");
            VerdictText.Foreground = Res("WarnBrush");
            AlignBtn.IsEnabled = false;
            SideNote.Text = "";
            return;
        }

        var ok = analysis.IsUniform;
        VerdictBox.Background = Res(ok ? "OkDimBrush" : "WarnDimBrush");
        VerdictText.Foreground = Res(ok ? "OkBrush" : "WarnBrush");
        VerdictText.Text = analysis.IsEmpty
            ? Strings.T("The folder is empty. New files follow the default profile.")
            : ok
                ? Strings.T("Uniform. All {0} tracks match the target.", analysis.Tracks.Count)
                : Strings.T("Mixed. The default profile from the settings applies.");

        void Row(string key, string value, string? note = null)
        {
            var sp = new StackPanel { Spacing = 1 };
            sp.Children.Add(new TextBlock { Text = $"{key}: {value}", FontSize = 12, TextWrapping = TextWrapping.Wrap });
            if (note is not null)
                sp.Children.Add(new TextBlock { Text = note, FontSize = 10.5, Foreground = Res("TextFillColorTertiaryBrush") });
            AnalysisPanel.Children.Add(sp);
        }

        var fromDefault = target.FromDefaultProfile
            ? Strings.T("from the default profile") : null;
        Row(Strings.T("Target format"), target.Format, fromDefault);
        Row(Strings.T("Target sample rate"), FormatRate(target.SampleRate), fromDefault);

        if (!analysis.IsEmpty && !analysis.IsUniform)
        {
            Pills(Strings.T("Formats"), analysis.FormatCounts
                .OrderByDescending(p => p.Value)
                .Select(p => ($"{p.Value} {p.Key}", p.Key.Equals(target.Format, StringComparison.OrdinalIgnoreCase))));

            Pills(Strings.T("Sample rates"), analysis.SampleRateCounts
                .OrderByDescending(p => p.Value)
                .Select(p => ($"{p.Value} × {FormatRate(p.Key)}", p.Key == target.SampleRate)));
        }

        // Was schon dem Ziel entspricht, ist gruen; der Rest muss noch angefasst
        // werden. So sieht man auf einen Blick, wie weit der Ordner ist.
        void Pills(string caption, IEnumerable<(string Text, bool OnTarget)> items)
        {
            var group = new StackPanel { Spacing = 5 };
            group.Children.Add(new TextBlock
            {
                Text = caption,
                FontSize = 11,
                Foreground = Res("TextFillColorTertiaryBrush"),
            });

            var wrap = new WrapPanel { HorizontalSpacing = 5, VerticalSpacing = 5 };
            foreach (var (text, onTarget) in items)
            {
                wrap.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(8, 2, 8, 2),
                    Background = Res(onTarget ? "OkDimBrush" : "WarnDimBrush"),
                    Child = new TextBlock
                    {
                        Text = text,
                        FontSize = 11,
                        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                        Foreground = Res(onTarget ? "OkBrush" : "WarnBrush"),
                    },
                });
            }

            group.Children.Add(wrap);
            AnalysisPanel.Children.Add(group);
        }

        UpdateRuleSwitches();

        var off = analysis.Outliers(target).Count();
        AlignBtn.IsEnabled = off > 0;
        AlignBtn.Content = off > 0 ? Strings.T("Align folder ({0})", off)
                                   : Strings.T("Align folder");
        SideNote.Text = off > 0
            ? Strings.T("{0} file(s) deviate. Aligning converts them in place and backs "
                        + "them up first.", off)
            : ActiveTab.Recursive
                ? Strings.T("Aligning covers every subfolder.")
                : Strings.T("Files dropped in are brought to this target automatically.");
    }

    /// <summary>Die gemerkten Regeln dieses Ordners in die Schalter übertragen.</summary>
    private void UpdateRuleSwitches()
    {
        var rule = _settings.RuleFor(ActiveTab.Path);

        _suppressRules = true;
        ConformSwitch.IsOn = rule.AutoConform;
        AlbumSwitch.IsOn = rule.AlbumMode;
        BaseTagsBox.IsChecked = rule.BaseTags;
        CoverBox.IsChecked = rule.Cover;
        NumberingBox.IsChecked = rule.Numbering;
        FileNamesBox.IsChecked = rule.RenameFiles;
        FileNamesBox.IsEnabled = rule.AlbumMode && rule.Numbering && !ActiveTab.Recursive;

        // Die Unterpunkte beschreiben, was der Album-Modus tut. Ist er aus,
        // tun sie nichts, und ein bedienbarer Haken würde etwas anderes
        // behaupten.
        BaseTagsBox.IsEnabled = CoverBox.IsEnabled = NumberingBox.IsEnabled =
            AlbumApplyBtn.IsEnabled = rule.AlbumMode && !ActiveTab.Recursive;
        _suppressRules = false;

        // Mit Unterordnern wird nichts abgelegt, also gibt es auch nichts zu regeln.
        ConformSwitch.IsEnabled = AlbumSwitch.IsEnabled = !ActiveTab.Recursive;

        AlbumNote.Text = !rule.AlbumMode
            ? Strings.T("Tags, cover and track numbers stay untouched here.")
            : Strings.T("Applies when files are dropped in and when the order changes.");

        var problems = rule.AlbumMode && !ActiveTab.Recursive ? DescribeNumbering(ActiveTab.Tracks) : "";
        AlbumCheck.Text = problems;
        AlbumCheck.Visibility = problems.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        var own = _settings.HasOwnRule(ActiveTab.Path);
        ResetRuleBtn.Visibility = own ? Visibility.Visible : Visibility.Collapsed;

        RuleNote.Text = ActiveTab.Recursive
            ? Strings.T("With subfolders, dropping is off. Aligning and editing by hand "
                        + "still work.")
            : own
                ? Strings.T("Own setting for \"{0}\". It wins over the global one.",
                            ActiveTab.Name)
                : Strings.T("Follows the global setting from the settings.");
    }

    private bool _suppressRules;

    /// <summary>
    /// Was an der Nummerierung auffällt, als Zeilen wie „Track 7 fehlt".
    /// Leer, wenn alles stimmt.
    /// </summary>
    private static string DescribeNumbering(IReadOnlyList<AudioTrack> tracks)
    {
        var check = TrackNumberCheck.Check(tracks);
        var lines = new List<string>();

        foreach (var disc in check.Discs)
        {
            var parts = new List<string>();
            if (disc.Missing.Count == 1)
                parts.Add(Strings.T("track {0} missing", disc.Missing[0]));
            else if (disc.Missing.Count > 1)
                parts.Add(Strings.T("tracks {0} missing", TrackNumberCheck.Ranges(disc.Missing)));

            foreach (var (track, count) in disc.Doubled)
                parts.Add(Strings.T("track {0} appears {1}×", track, count));

            var text = string.Join(", ", parts);
            lines.Add(disc.Disc > 0 ? Strings.T("Disc {0}: {1}", disc.Disc, text) : text);
        }

        if (check.Unnumbered > 0)
            lines.Add(Strings.T("{0} file(s) without a track number", check.Unnumbered));

        // Der erste Buchstabe groß, auch wenn die Zeile mit „track" beginnt.
        return string.Join(Environment.NewLine, lines.Select(l => l.Length > 0 ? char.ToUpper(l[0]) + l[1..] : l));
    }

    private void OnFolderRuleChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressRules) return;

        var before = _settings.RuleFor(ActiveTab.Path);

        _settings.SetRule(ActiveTab.Path, new FolderRule
        {
            AutoConform = ConformSwitch.IsOn,
            AlbumMode = AlbumSwitch.IsOn,
            BaseTags = BaseTagsBox.IsChecked == true,
            Cover = CoverBox.IsChecked == true,
            Numbering = NumberingBox.IsChecked == true,
            RenameFiles = FileNamesBox.IsChecked == true,
        });
        UpdateRuleSwitches();
        PaneFor(ActiveTab)?.Refresh();

        // Etwas ist dazugekommen, das der Album-Modus angleicht. Die Dateien,
        // die schon hier liegen, bleiben sonst, wie sie sind; angeboten wird,
        // sie auch anzugleichen, mit Vorschau.
        var after = _settings.RuleFor(ActiveTab.Path);
        if ((!before.WritesBaseTags && after.WritesBaseTags)
            || (!before.WritesCover && after.WritesCover)
            || (!before.WritesNumbers && after.WritesNumbers)
            || (!before.WritesFileNames && after.WritesFileNames))
        {
            _ = ApplyAlbumToExistingAsync(ActiveTab, offered: true);
        }
    }

    /// <summary>Nimmt die eigene Regel zurück, sodass wieder die globale gilt.</summary>
    private void OnResetFolderRule(object sender, RoutedEventArgs e)
    {
        _settings.ClearRule(ActiveTab.Path);
        UpdateRuleSwitches();
        StatusText.Text = Strings.T("\"{0}\" follows the global setting again", ActiveTab.Name);
    }

    // ══ Rückmeldung ══════════════════════════════════════════════

    private async Task ReportAsync(int ok, List<string> errors, List<string> notes)
    {
        if (errors.Count == 0 && notes.Count == 0)
        {
            StatusText.Text = Strings.T("{0} file(s) processed, backup created", ok);
            return;
        }

        var text = new System.Text.StringBuilder();
        text.AppendLine(Strings.T("{0} file(s) processed.", ok));
        if (errors.Count > 0)
        {
            text.AppendLine().AppendLine(Strings.T("Failed:"));
            foreach (var e in errors.Take(8)) text.AppendLine("• " + e);
        }
        if (notes.Count > 0)
        {
            text.AppendLine().AppendLine(Strings.T("Notes:"));
            foreach (var n in notes.Take(8)) text.AppendLine("• " + n);
        }

        await Inform(Strings.T(errors.Count > 0 ? "Finished with errors" : "Finished"),
                     text.ToString().TrimEnd());
    }

    /// <summary>
    /// Gibt die laufende Datei frei, falls sie zu den gleich beschriebenen
    /// gehört. Alles andere spielt ungestört weiter.
    /// </summary>
    /// <summary>
    /// Der Fortschritt oben in der Leiste. Die Prozentzahl daneben nur, wenn
    /// eine bekannt ist — beim Einlesen läuft der Balken unbestimmt.
    /// </summary>
    private void ShowProgress(bool busy, double percent)
    {
        ProgressHost.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Progress.Value = Math.Clamp(percent, 0, 100);
        ProgressLabel.Text = busy && !Progress.IsIndeterminate
            ? $"{Math.Round(percent)} %"
            : "";
    }

    private void ReleaseIfAffected(IEnumerable<AudioTrack> tracks) =>
        _player.ReleaseIfPlaying(tracks.Select(t => t.Path));

    private void SetBusy(bool busy, string? label)
    {
        ShowProgress(busy, 0);
        PaneA.IsEnabled = PaneB.IsEnabled = !busy;
        FolderTree.IsEnabled = !busy;
        if (busy) { ApplyBtn.IsEnabled = false; AlignBtn.IsEnabled = false; }
        if (label is not null) StatusText.Text = label + " …";
    }

    private async Task<bool> Confirm(string title, string message, string primary)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            CloseButtonText = Strings.T("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Root.XamlRoot,
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task Inform(string title, string message)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                MaxHeight = 320,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            },
            CloseButtonText = "OK",
            XamlRoot = Root.XamlRoot,
        };
        await dlg.ShowAsync();
    }

    // ══ Aktualisierung ═══════════════════════════════════════════

    /// <summary>
    /// Sucht beim Start nach einer neueren Fassung.
    ///
    /// Die Regeln stecken in <see cref="UpdateService.CheckOnStartAsync"/>:
    /// „Nie" fragt gar nicht erst nach, im Hintergrund höchstens einmal am
    /// Tag, und eine abgelehnte Version bleibt still.
    /// </summary>
    private async Task CheckForUpdatesAsync()
    {
        // Nicht ins Startbild hineinplatzen.
        await Task.Delay(TimeSpan.FromSeconds(4));

        var found = await UpdateService.CheckOnStartAsync(_settings, AppInfo.Version);
        if (found is not { HasUpdate: true, SetupUrl: not null }) return;

        if (_settings.UpdateBehavior == "auto")
        {
            await InstallUpdateAsync(found);
            return;
        }

        var text = new System.Text.StringBuilder();
        text.AppendLine(Strings.T("Version {0} is available, {1} is installed.",
                                  found.Version ?? "?", AppInfo.Version));

        if (!string.IsNullOrWhiteSpace(found.Notes))
        {
            var notes = found.Notes.Trim();
            if (notes.Length > 600) notes = notes[..600] + "…";
            text.AppendLine().AppendLine(notes);
        }

        var dialog = new ContentDialog
        {
            Title = Strings.T("Update available"),
            Content = new ScrollViewer
            {
                MaxHeight = 320,
                Content = new TextBlock { Text = text.ToString().TrimEnd(), TextWrapping = TextWrapping.Wrap },
            },
            PrimaryButtonText = Strings.T("Install"),
            SecondaryButtonText = Strings.T("Skip this version"),
            CloseButtonText = Strings.T("Later"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Root.XamlRoot,
        };

        var answer = await dialog.ShowAsync();

        if (answer == ContentDialogResult.Secondary)
        {
            _settings.SkippedVersion = found.Version;
            _settings.Save();
            return;
        }

        if (answer == ContentDialogResult.Primary) await InstallUpdateAsync(found);
    }

    /// <summary>
    /// Lädt das Setup und startet es. Die App beendet sich danach, weil der
    /// Installer die laufende Datei sonst nicht ersetzen kann.
    /// </summary>
    private async Task InstallUpdateAsync(UpdateCheck found)
    {
        if (found.SetupUrl is null) return;

        StatusText.Text = Strings.T("Downloading the update…");
        ShowProgress(true, 0);

        try
        {
            var setup = await UpdateService.DownloadAsync(
                found.SetupUrl,
                new Progress<int>(p => ShowProgress(true, p)));

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = setup,
                UseShellExecute = true,
            });

            Application.Current.Exit();
        }
        catch (Exception ex)
        {
            ShowProgress(false, 0);
            StatusText.Text = Strings.T("Update failed: {0}", ex.Message);
        }
    }

    // ══ Dialoge ══════════════════════════════════════════════════

    private async void OnOpenHistory(object sender, RoutedEventArgs e)
    {
        // Der Player darf nichts offen halten, was gleich zurückgeschrieben wird.
        if (await HistoryDialog.ShowAsync(Root.XamlRoot, _history, paths => _player.ReleaseIfPlaying(paths)))
        {
            // Nach einem Rückgängig liegt anderes im Ordner, und womöglich in
            // mehreren: Jeder offene Tab wird abgeglichen.
            TrackArt.Reload();
            InvalidateIndex();
            foreach (var tab in _tabs.Where(t => t.Analysis is not null))
                await MergeTabAsync(tab);
        }
    }

    private async void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        // Die Einstellungen schreiben sofort. Was das Hauptfenster angeht,
        // meldet sich hier, damit nicht vorsichtshalber alles neu aufgebaut
        // wird — ein Baum mit tausend Ordnern merkt das.
        var libraryChanged = false;
        var sortingChanged = false;
        var filesChanged = false;

        var wanted = await SettingsDialog.ShowAsync(
            Root.XamlRoot,
            WinRT.Interop.WindowNative.GetWindowHandle(this),
            _settings, _history,
            what =>
            {
                if (what == "theme") ApplyTheme();
                if (what is "library") libraryChanged = true;
                if (what is "rules") UpdateRuleSwitches();
                if (what is "columns") ApplyColumns();
                if (what is "sorting") sortingChanged = true;

                // Vor dem Zurückschreiben einer Sicherung: Der Player darf
                // die Datei nicht mehr offen halten.
                if (what.StartsWith("release:", StringComparison.Ordinal))
                    _player.ReleaseIfPlaying([what["release:".Length..]]);
                if (what is "files") filesChanged = true;
            });

        // Sicherungen wurden zurückgeschrieben: Tags und Cover in den offenen
        // Ordnern können andere sein.
        if (filesChanged)
        {
            TrackArt.Reload();
            InvalidateIndex();
            foreach (var tab in _tabs.Where(t => t.Analysis is not null))
                Fire(MergeTabAsync(tab), Strings.T("Reading…"));
        }

        if (libraryChanged)
        {
            FolderScanner.ForgetAudioScan();
            InvalidateIndex();
            BuildTreeRoots();
        }

        // Das Ziel eines uneinheitlichen Ordners hängt am Standardprofil.
        foreach (var tab in _tabs.Where(t => t.Analysis is not null))
            tab.Target = tab.Analysis!.ResolveTarget(_settings.DefaultFormat, _settings.DefaultSampleRate);

        UpdateAnalysisPanel();
        UpdateMetaPanel();
        UpdateFfmpegHint();

        // Die Playlist-Reihenfolge hängt daran, ob die Disc mitzählt.
        if (sortingChanged)
            foreach (var tab in _tabs.Where(t => t.Sort == TrackSort.Natural))
                Fire(MergeTabAsync(tab), Strings.T("Reading…"));

        // Zuletzt, weil die App sich dafür beendet.
        if (wanted is not null) await InstallUpdateAsync(wanted);
    }

    /// <summary>
    /// Der Hinweis erscheint nur, wenn ffmpeg fehlt. Eine Zeile, die dauerhaft
    /// „alles in Ordnung" meldet, liest nach dem ersten Mal niemand.
    /// </summary>
    private void UpdateFfmpegHint()
    {
        var missing = FfmpegLocator.Find(_settings.FfmpegPath) is null;
        FfmpegText.Text = missing ? "ffmpeg fehlt" : "";
        FfmpegText.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
    }
    // ══ Zweiter Start ════════════════════════════════════════════

    /// <summary>
    /// Jemand hat TagTuner noch einmal gestartet, etwa über das
    /// Kontextmenü des Explorers. Statt eines zweiten Fensters gibt es einen
    /// Tab in diesem hier.
    /// </summary>
    private void OnSecondLaunch(LaunchTarget target)
    {
        var folder = target.FolderToOpen;
        if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
        {
            OpenTab(folder);

            // Auf eine Datei gezeigt: sie im neuen Tab auch auswählen.
            if (target.File is { Length: > 0 } file && File.Exists(file))
                Fire(LoadTabAsync(TopTab, file), Strings.T("Reading…"));
            else
                Fire(LoadTabAsync(TopTab), Strings.T("Reading…"));
        }

        ToForeground();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    private const int RestoreWindow = 9;

    /// <summary>
    /// Holt das Fenster nach vorn, auch wenn es minimiert war. Activate()
    /// allein genügt nicht: Ein minimiertes Fenster bleibt damit minimiert,
    /// und der Nutzer sieht von seinem neuen Tab nichts.
    /// </summary>
    private void ToForeground()
    {
        try
        {
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            ShowWindow(handle, RestoreWindow);
            SetForegroundWindow(handle);
            Activate();
        }
        catch { }
    }
    // ══ Darstellung ══════════════════════════════════════════════

    /// <summary>
    /// Hell oder dunkel, oder was Windows gerade sagt.
    ///
    /// Am Wurzelelement statt an der Anwendung: Application.RequestedTheme
    /// lässt sich nur vor dem ersten Fenster setzen, danach wirft es. So
    /// wechselt die Darstellung sofort, ohne Neustart.
    /// </summary>
    private void ApplyTheme()
    {
        Root.RequestedTheme = _settings.Theme switch
        {
            "dark" => ElementTheme.Dark,
            "light" => ElementTheme.Light,
            _ => ElementTheme.Default,
        };
    }
    // ══ Spalten ══════════════════════════════════════════════════

    /// <summary>Übernimmt die eingestellten Spalten in beide Hälften.</summary>
    private void ApplyColumns()
    {
        PaneA.ApplyColumns(_settings);
        PaneB.ApplyColumns(_settings);
        foreach (var pane in _subPanes) pane.ApplyColumns(_settings);
    }

    /// <summary>
    /// Schreibt die gezogenen Breiten zurück. Nur die Breiten: Auswahl und
    /// Reihenfolge ändert man in den Einstellungen, und die schreiben selbst.
    /// </summary>
    private void RememberColumns()
    {
        TrackColumns.Remember(_settings, TrackColumns.Resolve(_settings), Columns.ToArray());
        _settings.Save();
    }
    // ══ Unterordner verlassen ════════════════════════════════════

    /// <summary>
    /// Zurück in den einzelnen Ordner. Der Weg dorthin führt über das
    /// Abzeichen in der Kopfzeile der Hälfte; der Knopf in der Seitenspalte
    /// stand dafür im Weg, obwohl man ihn fast nie braucht.
    /// </summary>
    private void OnScopeExit(object? sender, TrackPane pane)
    {
        if (pane.Tab is not { Recursive: true } tab) return;

        tab.Recursive = false;
        Fire(LoadTabAsync(tab), Strings.T("Reading…"));
    }

}
