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
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)_settings.WindowWidth, (int)_settings.WindowHeight));

        _activePane = PaneA;
        foreach (var pane in new[] { PaneA, PaneB }) WirePane(pane);
        InitSubfolders();

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

    // ══ Baum ═════════════════════════════════════════════════════

    private void BuildTreeRoots()
    {
        FolderTree.RootNodes.Clear();
        foreach (var root in FolderScanner.Roots(_settings.LibraryPaths, _settings.HiddenRoots))
            FolderTree.RootNodes.Add(NodeFor(root, isRoot: true));
    }

    /// <summary>Eine Baumzeile samt Cover, das im Hintergrund nachgeladen wird.</summary>
    private TreeViewNode NodeFor(FolderEntry entry, bool isRoot = false)
    {
        var folder = new LibraryFolder(entry)
        {
            IsCustomRoot = entry.IsCustomRoot,
            IsRoot = isRoot,
        };
        folder.BeginLoad(DispatcherQueue);

        return new TreeViewNode
        {
            Content = folder,
            HasUnrealizedChildren = FolderScanner.HasSubfolders(entry.Path, _settings.OnlyAudioFolders),
        };
    }

    /// <summary>Zweige erst beim Aufklappen lesen — „C:\" darf nicht die Platte durchlaufen.</summary>
    private void OnTreeExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        var node = args.Node;
        _justExpanding = node;
        _justExpandingAt = DateTime.UtcNow;

        if (!node.HasUnrealizedChildren) return;

        node.Children.Clear();
        if (node.Content is not LibraryFolder folder) return;

        foreach (var child in FolderScanner.Subfolders(folder.Path, _settings.OnlyAudioFolders))
            node.Children.Add(NodeFor(child));

        node.HasUnrealizedChildren = false;
    }

    private void OnTreeItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TreeViewNode { Content: LibraryFolder folder })
        {
            NavigateActive(folder.Path);
        }
    }

    /// <summary>
    /// Linksklick öffnet den Ordner, Mausradklick in einem weiteren Tab.
    ///
    /// Zwei Dinge haben sich unterwegs als untauglich erwiesen und sind
    /// deshalb nicht mehr drin:
    ///
    /// Die Handler in der Zeilenvorlage bekamen nie ein Zeigerereignis, und
    /// <c>Tapped</c> entsteht erst beim Loslassen — bis dahin hatte die Liste
    /// den ersten Klick schon verbraucht. Darum hängt das hier am Baum und an
    /// PointerPressed.
    ///
    /// Und der Versuch, Pfeil von Inhalt zu unterscheiden, ist gescheitert:
    /// Das angeklickte Element ist immer der Pfeil-Grid der Vorlage, der
    /// ScrollContentPresenter darüber erbt von ContentPresenter, und die
    /// gemessene Klickposition lag selbst mitten auf dem Namen in der
    /// Pfeilzone. Also wird nicht mehr unterschieden: Ein Klick öffnet den
    /// Ordner, und ob dabei auf- oder zugeklappt wird, entscheidet die
    /// TreeView allein. Zuklappen funktioniert damit wieder.
    /// </summary>
    private void OnTreePressedAnywhere(object sender, PointerRoutedEventArgs e)
    {
        var button = e.GetCurrentPoint(FolderTree).Properties;
        if (!button.IsLeftButtonPressed && !button.IsMiddleButtonPressed)
        {
            return;
        }

        if (Ancestor<TreeViewItem>(e.OriginalSource as DependencyObject) is not { } item)
        {
            return;
        }

        if (FolderTree.NodeFromContainer(item) is not { } node ||
            node.Content is not LibraryFolder folder)
        {
            return;
        }


        if (button.IsMiddleButtonPressed)
        {
            OpenTab(folder.Path);
            e.Handled = true;
            return;
        }

        OpenFolderNode(node, folder);
    }

    /// <summary>
    /// Ordner öffnen und dabei auf- oder zuklappen.
    ///
    /// Das Aufklappen beim Klick ist der Teil, der den Ordner zuverlässig mit
    /// einem Klick öffnet — ohne ihn brauchte es wieder zwei. Damit sich
    /// trotzdem etwas zuklappen lässt, ohne den schmalen Pfeil treffen zu
    /// müssen, klappt ein erneuter Klick auf den bereits geöffneten Ordner
    /// ihn wieder zu.
    /// </summary>
    /// <summary>
    /// Der zuletzt von der TreeView selbst zugeklappte Knoten.
    ///
    /// Reihenfolge laut Protokoll: Klickt man einen offenen Ordner an, meldet
    /// die TreeView <c>Collapsed</c> noch bevor unser Zeiger-Handler läuft.
    /// Bei einem geschlossenen Ordner kommt stattdessen später ein
    /// <c>Expanding</c>. Daran lässt sich ablesen, was der Klick gerade
    /// bewirkt hat — und das ist verlässlicher als <c>IsExpanded</c>, das
    /// während der Klickverarbeitung etwas anderes meldet als das, was auf
    /// dem Schirm steht.
    /// </summary>
    private TreeViewNode? _justCollapsed;
    private DateTime _justCollapsedAt;

    /// <summary>
    /// Der zuletzt aufgeklappte Knoten, um ein Paar aus Expanding und sofort
    /// folgendem Collapsed zu erkennen.
    ///
    /// Beim ersten Klick auf einen frischen Ordner meldet die TreeView beides
    /// im selben Moment, ohne dass sich etwas geaendert haette. Ohne diese
    /// Unterscheidung galt jeder erste Klick als Zuklapp-Klick, und der
    /// Ordner blieb zu.
    /// </summary>
    private TreeViewNode? _justExpanding;
    private DateTime _justExpandingAt;

    /// <summary>
    /// Öffnet den Ordner und sorgt dafür, dass ein Klick reicht.
    ///
    /// Ohne das Aufklappen braucht es zwei Klicks: Der erste wählt den
    /// Eintrag nur aus, erst der zweite klappt auf. Erzwingt man es dagegen
    /// bei jedem Klick, lässt sich nichts mehr zuklappen — die TreeView
    /// klappt zu, und das Erzwingen macht es sofort wieder auf.
    ///
    /// Deshalb: aufklappen, außer die TreeView hat gerade eben von sich aus
    /// zugeklappt. Dann war es ein Zuklapp-Klick und bleibt zu.
    /// </summary>
    private void OpenFolderNode(TreeViewNode node, LibraryFolder folder)
    {
        var closedByThisClick = ReferenceEquals(_justCollapsed, node)
            && (DateTime.UtcNow - _justCollapsedAt).TotalMilliseconds < 250;

        _justCollapsed = null;

        NavigateActive(folder.Path);

        if (closedByThisClick)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            node.IsExpanded = true;
        });
    }

    private void OnTreeCollapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        var churn = ReferenceEquals(_justExpanding, args.Node)
            && (DateTime.UtcNow - _justExpandingAt).TotalMilliseconds < 40;

        if (!churn)
        {
            _justCollapsed = args.Node;
            _justCollapsedAt = DateTime.UtcNow;
        }
    }

    /// <summary>Der nächste Vorfahre dieses Typs im sichtbaren Baum.</summary>
    private static T? Ancestor<T>(DependencyObject? from) where T : class
    {
        for (var node = from; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is T match) return match;
        return null;
    }

    private static LibraryFolder? FolderOf(object source) =>
        (source as FrameworkElement)?.DataContext is TreeViewNode { Content: LibraryFolder f } ? f : null;

    private void OnTreeRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (FolderOf(sender) is not { } folder) return;

        var menu = new MenuFlyout();
        menu.Items.Add(Item("\uE8A7", Strings.T("Open in a new tab"), () => OpenTab(folder.Path)));
        menu.Items.Add(Item("\uE721", Strings.T("Search subfolders"), () => OpenRecursive(folder.Path)));
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("\uE8DA", Strings.T("Open in Explorer"), () => Reveal(folder.Path)));

        if (folder.IsRoot)
            menu.Items.Add(Item("\uE738", Strings.T("Remove from library"),
                () => RemoveRoot(folder)));

        menu.ShowAt((UIElement)sender, e.GetPosition((UIElement)sender));
        e.Handled = true;

        static MenuFlyoutItem Item(string glyph, string text, Action run)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            item.Click += (_, _) => run();
            return item;
        }
    }

    /// <summary>Öffnet einen Ordner samt allem darunter — in einem eigenen Tab.</summary>
    /// <summary>
    /// Öffnet einen Ordner mit allem, was darunter liegt.
    ///
    /// Bei vielen Dateien vorher fragen: Ein rekursiver Scan über eine ganze
    /// Bibliothek dauert, und niemand rechnet damit nach einem Klick im
    /// Kontextmenü.
    /// </summary>
    private async void OpenRecursive(string path)
    {
        StatusText.Text = Strings.T("Counting subfolders…");
        var n = await Task.Run(() => FolderScanner.CountRecursive(path));
        StatusText.Text = "";

        if (n == 0)
        {
            await Inform(Strings.T("Nothing found"),
                         Strings.T("There are no audio files below this folder."));
            return;
        }

        if (n > 400 && !await Confirm(Strings.T("Include subfolders"),
                Strings.T("{0} files will be read. That takes a moment.",
                          n >= 5000 ? n + "+" : n.ToString())
                + "\n\n"
                + Strings.T("In this view, dropping and reordering are off. It is meant "
                            + "for looking, for aligning and for editing by hand."),
                Strings.T("Read")))
            return;

        var existing = _tabs.FindIndex(t =>
            string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));

        if (existing >= 0)
        {
            _tabs[existing].Recursive = true;
            ActivateTab(existing);
            Fire(LoadTabAsync(_tabs[existing]), Strings.T("Reading…"));
            return;
        }

        // Schon beim Anlegen rekursiv, damit ActivateTab nicht erst flach
        // einliest und gleich darauf noch einmal.
        _tabs.Add(new FolderTab(path) { Recursive = true });
        ActivateTab(_tabs.Count - 1);
    }

    private static void Reveal(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch { }
    }

    // ══ Bibliothek ═══════════════════════════════════════════════

    /// <summary>
    /// Der Plus-Knopf: Ordner hinzufügen, und — falls welche ausgeblendet
    /// sind — sie wieder zurückholen. Ohne diesen zweiten Eintrag gäbe es
    /// keinen Weg zurück, wenn man Musik oder ein Laufwerk entfernt hat.
    /// </summary>
    private void OnLibraryMenu(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout();

        var add = new MenuFlyoutItem
        {
            Text = Strings.T("Add folder…"),
            Icon = new FontIcon { Glyph = "\uE8F4" },
        };
        add.Click += OnAddLibraryPath;
        menu.Items.Add(add);

        if (_settings.HiddenRoots.Count > 0)
        {
            var back = new MenuFlyoutItem
            {
                Text = Strings.T("Show hidden again ({0})", _settings.HiddenRoots.Count),
                Icon = new FontIcon { Glyph = "\uE7A7" },
            };
            back.Click += (_, _) =>
            {
                _settings.HiddenRoots.Clear();
                _settings.Save();
                BuildTreeRoots();
                StatusText.Text = Strings.T("The hidden folders are back");
            };
            menu.Items.Add(back);
        }

        menu.ShowAt((FrameworkElement)sender);
    }

    private async void OnAddLibraryPath(object sender, RoutedEventArgs e)
    {
        var picker = new Windows.Storage.Pickers.FolderPicker();
        picker.FileTypeFilter.Add("*");

        // Unverpackt weiss der Picker nicht, zu welchem Fenster er gehört —
        // ohne das Handle wirft er beim Öffnen.
        WinRT.Interop.InitializeWithWindow.Initialize(
            picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        var path = folder.Path;
        if (_settings.LibraryPaths.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText.Text = Strings.T("\"{0}\" is already in the library", folder.Name);
            return;
        }

        _settings.LibraryPaths.Add(path);
        _settings.Save();
        InvalidateIndex();
        BuildTreeRoots();
        StatusText.Text = Strings.T("\"{0}\" added to the library", folder.Name);
    }

    /// <summary>
    /// Selbst hinzugefügte Wurzeln werden aus der Liste gestrichen. Musik,
    /// Downloads und die Laufwerke stammen nicht aus einer Liste, sondern
    /// werden bei jedem Start ermittelt — die lassen sich nur ausblenden.
    /// </summary>
    private void RemoveRoot(LibraryFolder folder)
    {
        if (folder.IsCustomRoot)
            _settings.LibraryPaths.RemoveAll(p =>
                string.Equals(p, folder.Path, StringComparison.OrdinalIgnoreCase));
        else
            _settings.HiddenRoots.Add(folder.Path);

        _settings.Save();
        InvalidateIndex();
        BuildTreeRoots();
        StatusText.Text = Strings.T(
            "\"{0}\" removed from the library. The folder itself stays.", folder.Name);
    }

    /// <summary>Alles, worin gesucht wird: eigene Pfade plus die Systemwurzeln ohne Laufwerke.</summary>
    private List<string> SearchRoots()
    {
        var roots = new List<string>(_settings.LibraryPaths);

        // Laufwerkswurzeln bleiben draussen — „C:\" abzusuchen dauert ewig
        // und liefert vor allem Windows-Eigenes.
        foreach (var entry in FolderScanner.Roots())
            if (!entry.Path.TrimEnd(Path.DirectorySeparatorChar).EndsWith(':'))
                roots.Add(entry.Path);

        return [.. roots.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private Task<LibraryIndex> IndexAsync()
    {
        var roots = SearchRoots();
        var onlyAudio = _settings.OnlyAudioFolders;
        return _index ??= Task.Run(() => LibraryIndex.Build(roots, onlyAudio));
    }

    /// <summary>Nach Änderungen an Dateien stimmt der Index nicht mehr.</summary>
    private void InvalidateIndex() => _index = null;

    private async void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _search?.Cancel();
        var query = SearchBox.Text.Trim();

        if (query.Length < 2)
        {
            SearchPanel.Visibility = Visibility.Collapsed;
            SearchList.ItemsSource = null;
            return;
        }

        SearchPanel.Visibility = Visibility.Visible;

        var cts = new CancellationTokenSource();
        _search = cts;

        try
        {
            // Nur so lange warten, dass ein zügiges Tippen nicht jeden
            // Buchstaben einzeln durchreicht. Das Filtern selbst kostet nichts
            // mehr, seit es über den Index läuft.
            await Task.Delay(120, cts.Token);

            var building = _index is null;
            if (building) SearchInfo.Text = Strings.T("Reading the library once…");

            var index = await IndexAsync();
            if (cts.IsCancellationRequested) return;

            var hits = LibrarySearch.Find(index, query);
            SearchList.ItemsSource = hits;

            var folders = hits.Count(h => h.Kind == HitKind.Folder);
            var tracks = hits.Count - folders;
            SearchInfo.Text = hits.Count == 0
                ? Strings.T("Nothing found for \"{0}\".", query)
                : Strings.T("{0} folders, {1} songs", folders, tracks)
                  + (hits.Count >= LibrarySearch.DefaultLimit ? Strings.T(" (more available)") : "");
        }
        catch (OperationCanceledException) { }
        finally { if (_search == cts) _search = null; }
    }

    /// <summary>
    /// Ein Ordnertreffer wird geöffnet, ein Liedtreffer öffnet seinen Ordner
    /// und wählt das Lied darin aus.
    /// </summary>
    private async void OnSearchHitClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not SearchHit hit) return;

        if (hit.Kind == HitKind.Folder)
        {
            NavigateActive(hit.Path);
            return;
        }

        var folder = Path.GetDirectoryName(hit.Path);
        if (folder is null) return;

        LeaveSubfolder();
        TopTab.Navigate(folder);
        TopTab.SelectedPaths.Add(hit.Path);
        RebuildChrome();
        await LoadTabAsync(TopTab);
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

    // ══ Metadatenspalte ══════════════════════════════════════════

    private void UpdateMetaPanel()
    {
        var folderScope = FolderScope;
        var sel = TargetTracks();
        _suppressSelection = true;

        var any = sel.Count > 0;
        foreach (var box in TagBoxes()) box.IsEnabled = any;
        FFormat.IsEnabled = FRate.IsEnabled = any;

        // Titel und Track sind je Datei verschieden — für mehrere Dateien
        // gleichzeitig gibt es da nichts Sinnvolles zu schreiben.
        var single = any && !folderScope && sel.Count == 1;
        FTitle.IsEnabled = FTrack.IsEnabled = single;

        MetaHead.Text = folderScope
            ? Strings.T("METADATA: {0}, {1} TRACKS", ActiveTab.Name, sel.Count)
            : sel.Count > 1 ? Strings.T("METADATA: {0} TRACKS", sel.Count)
                            : Strings.T("METADATA");

        if (!any)
        {
            foreach (var box in TagBoxes()) { box.Text = ""; box.PlaceholderText = ""; }
            Snapshot();
            FFormat.SelectedIndex = -1;
            FRate.SelectedIndex = -1;
            CoverImage.Source = null;
            CoverMime.Text = "";
            CoverInfo.Text = Strings.T("nothing selected");
            TechPanel.Children.Clear();
            _suppressSelection = false;
            UpdatePlan();
            return;
        }

        // Ein Feld zeigt nur dann einen Wert, wenn sich die Auswahl darin einig
        // ist — sonst „<verschieden>", das beim Anwenden unangetastet bleibt.
        Fill(FTitle, single ? sel[0].Title : Agree(sel, t => t.Title));
        Fill(FArtist, Agree(sel, t => t.Artist));
        Fill(FAlbum, Agree(sel, t => t.Album));
        Fill(FYear, Agree(sel, t => t.Year == 0 ? "" : t.Year.ToString()));
        Fill(FTrack, single ? sel[0].TrackLabel : null);
        Fill(FGenre, Agree(sel, t => t.Genre));
        Fill(FAlbumArtist, Agree(sel, t => t.AlbumArtist));
        Fill(FComposer, Agree(sel, t => t.Composer));
        Fill(FComment, Agree(sel, t => t.Comment));
        Fill(FDisc, Agree(sel, t => t.DiscLabel));

        var fmt = Agree(sel, t => AudioFormats.TargetExtension(t.Format).ToUpperInvariant());
        FFormat.SelectedItem = fmt is null ? null
            : AudioFormats.Targets.FirstOrDefault(x => x.Equals(fmt, StringComparison.OrdinalIgnoreCase));

        var rate = Agree(sel, t => t.SampleRate.ToString());
        FRate.SelectedIndex = rate is not null && int.TryParse(rate, out var hz)
            ? Array.IndexOf(Rates, hz) : -1;

        UpdateCover(sel[0]);
        UpdateTech(sel);

        Snapshot();
        _suppressSelection = false;
        UpdatePlan();

        static void Fill(TextBox box, string? value)
        {
            box.Text = value ?? "";
            box.PlaceholderText = value is null ? "<verschieden>" : "";
        }
    }

    private void Snapshot()
    {
        _loaded.Clear();
        foreach (var box in TagBoxes()) _loaded[box] = box.Text;
    }

    private bool TagsChanged() =>
        TagBoxes().Any(b => !_loaded.TryGetValue(b, out var was) || b.Text != was);

    private static string? Agree(List<AudioTrack> sel, Func<AudioTrack, string> pick)
    {
        var values = sel.Select(pick).Distinct(StringComparer.Ordinal).ToList();
        return values.Count == 1 ? values[0] : null;
    }

    /// <summary>Das aktuell angezeigte Cover — Grundlage für Kopieren und Anpassen.</summary>
    private AudioProbe.Cover? _cover;

    private void UpdateCover(AudioTrack track)
    {
        _cover = AudioProbe.ReadCover(track.Path);

        if (_cover is null)
        {
            CoverImage.Source = null;
            CoverMime.Text = "";
            CoverInfo.Text = Strings.T("no cover");
            return;
        }

        try
        {
            var bmp = new BitmapImage();
            using var ms = new MemoryStream(_cover.Data);
            using var ras = ms.AsRandomAccessStream();
            bmp.SetSource(ras);
            CoverImage.Source = bmp;

            CoverMime.Text = ImageInfo.ShortName(_cover.MimeType).ToLowerInvariant();
            CoverInfo.Text = $"{_cover.Data.Length / 1024.0:0.#} KB";

            // Die Maße kommen aus dem Dekoder, nicht aus BitmapImage.ImageOpened:
            // Bei einem Bild aus dem Speicher ist das Ereignis oft schon gelaufen,
            // bevor man sich daran hängen kann — dann blieben die Maße leer.
            ShowDimensions(_cover);
        }
        catch
        {
            CoverImage.Source = null;
            CoverMime.Text = "";
            CoverInfo.Text = Strings.T("cover not readable");
        }
    }

    private async void ShowDimensions(AudioProbe.Cover cover)
    {
        var info = await CoverImaging.MeasureAsync(cover.Data);

        // Zwischenzeitlich eine andere Datei gewählt — dann gehört das Ergebnis
        // nicht mehr zu dem, was gerade zu sehen ist.
        if (info is null || !ReferenceEquals(_cover, cover)) return;

        CoverMime.Text = info.Format.ToLowerInvariant();
        CoverInfo.Text = $"{info.Width} × {info.Height}{Environment.NewLine}" +
                         $"{cover.Data.Length / 1024.0:0.#} KB";
    }

    private void UpdateTech(List<AudioTrack> sel)
    {
        TechPanel.Children.Clear();
        void Row(string k, string v) => TechPanel.Children.Add(new TextBlock
        {
            Text = $"{k}: {v}",
            FontSize = 11.5,
            Foreground = Res("TextFillColorTertiaryBrush"),
        });

        if (sel.Count > 1)
        {
            Row(Strings.T(FolderScope ? "Folder" : "Selection"),
                Strings.T("{0} files", sel.Count));

            var total = TimeSpan.FromTicks(sel.Sum(t => t.Duration.Ticks));
            if (total > TimeSpan.Zero)
                Row(Strings.T("Total duration"), total.TotalHours >= 1
                    ? Strings.T("{0}:{1:00}:{2:00} hours",
                                (int)total.TotalHours, total.Minutes, total.Seconds)
                    : Strings.T("{0}:{1:00} minutes",
                                (int)total.TotalMinutes, total.Seconds));

            var bytes = sel.Sum(t => t.Size);
            if (bytes > 0) Row(Strings.T("Total size"), Bytes(bytes));

            var formats = sel.Select(t => t.Format).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (formats.Count == 1) Row(Strings.T("Format"), formats[0]);
            return;
        }

        var t = sel[0];
        Row(Strings.T("Channels"), t.Channels.ToString());
        Row(Strings.T("Duration"), t.DurationLabel);
        Row(Strings.T("Size"), t.SizeLabel);
        if (t.Bitrate > 0) Row(Strings.T("Bitrate"), $"{t.Bitrate} kbps");
        if (ActiveTab.Target is { } tg && !FolderAnalysis.Matches(t, tg))
            Row(Strings.T("Folder target"), $"{tg.Format} · {FormatRate(tg.SampleRate)}");
    }

    private static string Bytes(long size) => size switch
    {
        < 1024 => $"{size} B",
        < 1024 * 1024 => $"{size / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{size / 1024.0 / 1024.0:0.#} MB",
        _ => $"{size / 1024.0 / 1024.0 / 1024.0:0.##} GB",
    };

    /// <summary>Sagt vorher an, was „Anwenden" tun würde.</summary>
    private void UpdatePlan()
    {
        var folderScope = FolderScope;
        var sel = TargetTracks();
        if (sel.Count == 0)
        {
            PlanText.Text = Strings.T("Nothing selected.");
            ApplyBtn.IsEnabled = false;
            return;
        }

        var jobs = new List<string>();
        if (TagsChanged()) jobs.Add(Strings.T("write tags"));

        if (FFormat.SelectedItem is string f &&
            !f.Equals(Agree(sel, t => AudioFormats.TargetExtension(t.Format).ToUpperInvariant()),
                      StringComparison.OrdinalIgnoreCase))
            jobs.Add(Strings.T("convert to {0}", f));

        if (FRate.SelectedIndex >= 0)
        {
            var hz = Rates[FRate.SelectedIndex];
            if (Agree(sel, t => t.SampleRate.ToString()) is not { } r || r != hz.ToString())
                jobs.Add(Strings.T("bring to {0}", FormatRate(hz)));
        }

        var was = folderScope
            ? Strings.T("Folder ({0} file(s))", sel.Count)
            : Strings.T("{0} file(s)", sel.Count);

        ApplyBtn.IsEnabled = jobs.Count > 0;
        PlanText.Text = jobs.Count > 0
            ? $"{was}: {string.Join(" · ", jobs)}"
            : was;
    }

    // ══ Cover ════════════════════════════════════════════════════

    /// <summary>
    /// Führt eine Menüaktion aus und zeigt Fehler an, statt sie zu verlieren.
    ///
    /// Vorher standen hier <c>_ = MachWasAsync()</c>-Aufrufe. Eine Ausnahme
    /// darin verschwand vollständig: Der Dialog erschien nicht, und es gab
    /// keinen Hinweis warum.
    /// </summary>
    private async void Run(Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex) { await Inform(Strings.T("Failed"), ex.Message); }
    }

    private void OnCoverRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var targets = TargetTracks();
        if (targets.Count == 0) return;

        var scope = FolderScope ? Strings.T("Folder ({0})", targets.Count)
                                : targets.Count.ToString();
        var many = targets.Count > 1;
        var hasCover = _cover is not null;

        var menu = new MenuFlyout();

        menu.Items.Add(Item("\uEB9F", many ? Strings.T("Set cover for {0}…", scope) : Strings.T("Set cover…"),
            true, () => Run(() => SetFromFileAsync(targets))));

        menu.Items.Add(Item("\uE8C8", Strings.T("Copy cover"),
            hasCover, () => Run(CopyCoverAsync)));

        menu.Items.Add(Item("\uE77F", many ? Strings.T("Paste cover into {0}", scope) : Strings.T("Paste cover"),
            true, () => Run(() => PasteCoverAsync(targets))));

        menu.Items.Add(new MenuFlyoutSeparator());

        menu.Items.Add(Item("\uE740", Strings.T("Resize…"),
            hasCover, () => Run(() => ResizeCoverAsync(targets))));

        menu.Items.Add(Item("\uE74E", Strings.T("Extract cover…"),
            hasCover, () => Run(ExtractCoverAsync)));

        menu.Items.Add(Item("\uE74D", many ? Strings.T("Remove cover from {0}", scope) : Strings.T("Remove cover"),
            targets.Any(t => t.HasCover), () => Run(() => ClearCoverAsync(targets))));

        menu.ShowAt((FrameworkElement)sender, e.GetPosition((UIElement)sender));
        e.Handled = true;

        static MenuFlyoutItem Item(string glyph, string text, bool enabled, Action run)
        {
            var item = new MenuFlyoutItem
            {
                Text = text,
                Icon = new FontIcon { Glyph = glyph },
                IsEnabled = enabled,
            };
            item.Click += (_, _) => run();
            return item;
        }
    }

    // ── Setzen ───────────────────────────────────────────────────

    private async Task SetFromFileAsync(List<AudioTrack> targets)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".webp", ".bmp" })
            picker.FileTypeFilter.Add(ext);

        WinRT.Interop.InitializeWithWindow.Initialize(
            picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        byte[] data;
        try { data = await File.ReadAllBytesAsync(file.Path); }
        catch (Exception ex) { await Inform(Strings.T("Image not readable"), ex.Message); return; }

        // Eine gewählte Datei liegt im Original vor — sie soll so bleiben,
        // solange sie quadratisch ist.
        await ApplyImageAsync(targets, data, Strings.T("Set cover"), keepExact: true);
    }

    private async Task PasteCoverAsync(List<AudioTrack> targets)
    {
        var content = Clipboard.GetContent();

        // Innerhalb der App: die Originalbytes, ohne sie anzufassen.
        if (content.Contains(CoverToken) && _coverClip is { } clip)
        {
            var token = await content.GetDataAsync(CoverToken) as string;
            if (token == clip.Token)
            {
                await WriteCoverAsync(targets,
                    new TagEdit { Cover = clip.Data, CoverMimeType = clip.Mime },
                    Strings.T("Paste cover ({0} KB, unchanged)",
                              $"{clip.Data.Length / 1024.0:0.#}"));
                return;
            }
        }

        byte[]? data = null;
        var fromFile = false;
        try
        {
            if (content.Contains(StandardDataFormats.StorageItems))
            {
                // Eine im Explorer kopierte Bilddatei — die liegt unverändert vor.
                var items = await content.GetStorageItemsAsync();
                if (items.OfType<Windows.Storage.StorageFile>().FirstOrDefault() is { } f)
                {
                    data = await File.ReadAllBytesAsync(f.Path);
                    fromFile = true;
                }
            }

            if (data is null && content.Contains(StandardDataFormats.Bitmap))
            {
                var reference = await content.GetBitmapAsync();
                using var stream = await reference.OpenReadAsync();
                data = await ReadAllAsync(stream);
            }
        }
        catch (Exception ex)
        {
            await Inform(Strings.T("Clipboard not readable"), ex.Message);
            return;
        }

        if (data is null || data.Length == 0)
        {
            await Inform(Strings.T("No image in the clipboard"),
                Strings.T("Copy an image or an image file and try again."));
            return;
        }

        await ApplyImageAsync(targets, data, Strings.T("Paste cover"), keepExact: fromFile);
    }

    /// <summary>
    /// Der gemeinsame Weg für Datei und Zwischenablage: messen, bei nicht
    /// quadratischen Bildern zuschneiden lassen, dann schreiben.
    ///
    /// Ein Bild, das schon quadratisch und JPEG oder PNG ist, wird
    /// unverändert übernommen. Neu zu kodieren, was bereits passt, kostet
    /// nur Qualität und ändert die Datei ohne Grund.
    /// </summary>
    private async Task ApplyImageAsync(
        List<AudioTrack> targets, byte[] data, string label, bool keepExact)
    {
        var info = await CoverImaging.MeasureAsync(data);
        if (info is null)
        {
            await Inform(Strings.T("Image not readable"),
                         Strings.T("That format is not supported."));
            return;
        }

        var asPng = info.Format == "PNG";
        byte[]? final;
        string note;

        if (!info.IsSquare)
        {
            final = await CoverCropDialog.CropAsync(Root.XamlRoot, data, info, asPng);
            if (final is null) return;   // abgebrochen
            note = Strings.T("cropped");
        }
        else if (keepExact && info.Format is "JPEG" or "PNG")
        {
            final = data;
            note = Strings.T("unchanged");
        }
        else
        {
            // Was aus der Zwischenablage kommt, ist oft ein unkomprimiertes
            // Bitmap und wäre als Tag absurd groß.
            final = await CoverImaging.NormalizeAsync(data, asPng);
            note = Strings.T("re-encoded");
        }

        if (final is null)
        {
            await Inform(Strings.T("Image cannot be processed"),
                         Strings.T("Converting the image failed."));
            return;
        }

        await WriteCoverAsync(targets,
            new TagEdit { Cover = final, CoverMimeType = asPng ? "image/png" : "image/jpeg" },
            Strings.T("{0} ({1} KB, {2})", label, $"{final.Length / 1024.0:0.#}", note));
    }

    // ── Kopieren ─────────────────────────────────────────────────

    /// <summary>
    /// Das zuletzt kopierte Cover, Byte für Byte.
    ///
    /// Der Umweg über die Windows-Zwischenablage ist verlustbehaftet:
    /// <c>SetBitmap</c> legt ein Bild ab, kein JPEG, und beim Zurückholen
    /// bekommt man ein neu kodiertes Bild statt der Originaldatei. Für
    /// „kopieren und einfügen" innerhalb der App bleiben die Bytes deshalb
    /// hier liegen; auf der Zwischenablage steht nur eine Marke, an der sich
    /// erkennen lässt, ob dieser Puffer noch der ist, der dort steht.
    /// </summary>
    private static (string Token, byte[] Data, string Mime)? _coverClip;

    private const string CoverToken = "TagTuner/CoverToken";

    private async Task CopyCoverAsync()
    {
        if (_cover is null) return;

        var token = Guid.NewGuid().ToString("n");
        _coverClip = (token, _cover.Data, _cover.MimeType);

        var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(_cover.Data);
            await writer.StoreAsync();
            writer.DetachStream();
        }
        stream.Seek(0);

        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };

        // Für andere Programme als Bild, für uns selbst über die Marke.
        package.SetBitmap(
            Windows.Storage.Streams.RandomAccessStreamReference.CreateFromStream(stream));
        package.SetData(CoverToken, token);

        Clipboard.SetContent(package);
        StatusText.Text = Strings.T("Cover copied ({0} KB, unchanged)",
                                    $"{_cover.Data.Length / 1024.0:0.#}");
    }

    // ── Größe anpassen ───────────────────────────────────────────

    /// <summary>
    /// Verkleinert das Cover — im selben Fenster, in dem auch ein nicht
    /// quadratisches Bild zugeschnitten wird. Zwei getrennte Fenster für
    /// „Ausschnitt wählen" und „kleiner machen" waren eine künstliche
    /// Trennung: Wer verkleinert, will oft genug auch den Rand weghaben.
    /// </summary>
    private async Task ResizeCoverAsync(List<AudioTrack> targets)
    {
        if (_cover is null) return;

        var info = await CoverImaging.MeasureAsync(_cover.Data);
        if (info is null)
        {
            await Inform(Strings.T("Cover not readable"),
                         Strings.T("That format is not supported."));
            return;
        }

        var final = await CoverCropDialog.ResizeAsync(Root.XamlRoot, _cover.Data, info);
        if (final is null) return;   // abgebrochen oder nicht kodierbar

        await WriteCoverAsync(targets,
            new TagEdit { Cover = final, CoverMimeType = "image/jpeg" },
            Strings.T("Shrink cover ({0} KB)", $"{final.Length / 1024.0:0.#}"));
    }

    /// <summary>
    /// Schreibt das Cover als Bilddatei heraus, Byte für Byte wie es im Tag
    /// steht. Kein Umkodieren: Wer das Bild herausholt, will das Original,
    /// nicht eine zweite Generation davon.
    /// </summary>
    private async Task ExtractCoverAsync()
    {
        if (_cover is null) return;

        var png = ImageInfo.ShortName(_cover.MimeType) == "PNG";
        var extension = png ? ".png" : ".jpg";

        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
            SuggestedFileName = SuggestedCoverName(),
        };
        picker.FileTypeChoices.Add(png ? "PNG" : "JPEG", [extension]);

        WinRT.Interop.InitializeWithWindow.Initialize(
            picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        await File.WriteAllBytesAsync(file.Path, _cover.Data);
        StatusText.Text = Strings.T("Cover saved as {0}", Path.GetFileName(file.Path));
    }

    /// <summary>Ein Name, der zum Ordner passt, statt „Unbenannt".</summary>
    private string SuggestedCoverName()
    {
        var sel = TargetTracks();
        var album = sel.Select(t => t.Album).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));
        var name = string.IsNullOrWhiteSpace(album) ? ActiveTab.Name : album;

        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, ' ');
        name = name.Trim();

        return name.Length == 0 ? "cover" : name;
    }

    // ── Entfernen und Schreiben ──────────────────────────────────

    private async Task ClearCoverAsync(List<AudioTrack> targets)
    {
        // Eine Datei ohne Cover anzufassen brächte nichts und legte trotzdem
        // eine Sicherung an.
        var hits = targets.Where(t => t.HasCover).ToList();
        if (hits.Count == 0) return;

        // Leeres Array heißt „entfernen"; null hieße „unverändert".
        await WriteCoverAsync(hits, new TagEdit { Cover = [] }, Strings.T("Remove cover"));
    }

    private async Task WriteCoverAsync(List<AudioTrack> targets, TagEdit edit, string label)
    {
        // WAV, AIFF und OGG tragen kein Cover — dort wäre das Schreiben
        // entweder wirkungslos oder es verlöre sich beim nächsten Anfassen.
        var able = targets.Where(t => AudioFormats.CanCarryCover(t.Format)).ToList();
        var unable = targets.Except(able).ToList();

        if (able.Count == 0)
        {
            await Inform(Strings.T("Format carries no cover"),
                         Strings.T("{0} cannot store a cover.", Formats(unable)));
            return;
        }

        var text = Strings.T("{0} file(s) will be changed. Each one is backed up first.",
                             able.Count);
        if (unable.Count > 0)
            text += Environment.NewLine + Environment.NewLine
                  + Strings.T("⚠ {0} file(s) stay untouched: {1} carries no cover.",
                              unable.Count, Formats(unable));

        if (!await Confirm(label, text, Strings.T("Apply"))) return;

        await RunJobAsync(label, able, _ => (false, null, null, edit));

        static string Formats(List<AudioTrack> tracks) =>
            string.Join(", ", tracks.Select(t => t.Format).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static async Task<byte[]> ReadAllAsync(Windows.Storage.Streams.IRandomAccessStreamWithContentType stream)
    {
        var bytes = new byte[stream.Size];
        using var reader = new Windows.Storage.Streams.DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        return bytes;
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
                        FontFamily = new FontFamily("Consolas"),
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
        });
        UpdateRuleSwitches();
        PaneFor(ActiveTab)?.Refresh();

        // Etwas ist dazugekommen, das der Album-Modus angleicht. Die Dateien,
        // die schon hier liegen, bleiben sonst, wie sie sind; angeboten wird,
        // sie auch anzugleichen, mit Vorschau.
        var after = _settings.RuleFor(ActiveTab.Path);
        if ((!before.WritesBaseTags && after.WritesBaseTags)
            || (!before.WritesCover && after.WritesCover)
            || (!before.WritesNumbers && after.WritesNumbers))
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

    // ══ Schreiben ════════════════════════════════════════════════

    private void OnReset(object sender, RoutedEventArgs e) => UpdateMetaPanel();

    /// <summary>
    /// Nur Felder, die tatsächlich verändert wurden. Unangetastete bleiben
    /// null und damit auch in den Dateien unangetastet — bei Mehrfachauswahl
    /// entscheidend, weil „&lt;verschieden&gt;" sonst alles gleichmachen würde.
    /// </summary>
    private TagEdit BuildTagEdit(bool single)
    {
        string? Changed(TextBox box) =>
            _loaded.TryGetValue(box, out var was) && box.Text == was ? null : box.Text;

        uint? Num(TextBox box)
        {
            var v = Changed(box);
            if (v is null) return null;
            return uint.TryParse(v.Trim(), out var n) ? n : 0u;
        }

        return new TagEdit
        {
            // Titel und Track sind je Datei verschieden — bei Mehrfachauswahl
            // wären sie für alle gleich, und das ist nie gewollt.
            Title = single ? Changed(FTitle) : null,
            Track = single ? Num(FTrack) : null,
            Artist = Changed(FArtist),
            Album = Changed(FAlbum),
            AlbumArtist = Changed(FAlbumArtist),
            Genre = Changed(FGenre),
            Composer = Changed(FComposer),
            Comment = Changed(FComment),
            Year = Num(FYear),
            Disc = Num(FDisc),
        };
    }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        var folderScope = FolderScope;
        var sel = TargetTracks();
        if (sel.Count == 0) return;

        if (folderScope && !await Confirm(Strings.T("Apply to the whole folder"),
                Strings.T("Nothing is selected. The change affects all {0} files in \"{1}\".",
                          sel.Count, ActiveTab.Name)
                + "\n\n" + Strings.T("Each file is backed up first."),
                Strings.T("Apply to all")))
            return;

        var edit = BuildTagEdit(!folderScope && sel.Count == 1);
        var wantFormat = FFormat.SelectedItem as string;
        var wantRate = FRate.SelectedIndex >= 0 ? Rates[FRate.SelectedIndex] : (int?)null;

        await RunJobAsync(Strings.T("{0} file(s)", sel.Count), sel, track =>
        {
            var convert =
                (wantFormat is not null &&
                 !AudioFormats.TargetExtension(track.Format)
                     .Equals(AudioFormats.TargetExtension(wantFormat), StringComparison.OrdinalIgnoreCase))
                || (wantRate is int hz && track.SampleRate != hz);
            return (convert, wantFormat, wantRate, edit);
        });
    }

    private async void OnAlign(object sender, RoutedEventArgs e)
    {
        var analysis = ActiveTab.Analysis;
        var target = ActiveTab.Target;
        if (analysis is null || target is null) return;

        var outliers = analysis.Outliers(target).ToList();
        if (outliers.Count == 0) return;

        var ok = await Confirm(Strings.T("Align folder"),
            Strings.T("{0} file(s) will be converted to {1} · {2}.",
                      outliers.Count, target.Format, FormatRate(target.SampleRate))
            + "\n\n"
            + Strings.T("The originals are replaced. Each file is backed up first, and "
                        + "the backup can be played back from the history."),
            Strings.T("Align"));
        if (!ok) return;

        await RunJobAsync(Strings.T("Align {0} file(s)", outliers.Count), outliers,
            _ => (true, target.Format, target.SampleRate, null));
    }

    private async Task RunJobAsync(
        string label,
        IReadOnlyList<AudioTrack> tracks,
        Func<AudioTrack, (bool Convert, string? Format, int? Rate, TagEdit? Tags)> plan)
    {
        // Nur loslassen, was gleich beschrieben wird. Der Player hält seine
        // Datei offen, und ffmpeg wie TagLib kämen sonst nicht daran — aber
        // ein Lied, das gar nicht betroffen ist, soll weiterlaufen.
        ReleaseIfAffected(tracks);

        var ffmpeg = FfmpegLocator.Find(_settings.FfmpegPath);
        if (tracks.Any(t => plan(t).Convert) && ffmpeg is null)
        {
            await Inform(Strings.T("ffmpeg is missing"),
                Strings.T("Converting needs ffmpeg.exe. It is expected next to the "
                          + "application or in tools\\ffmpeg. tools\\fetch-ffmpeg.ps1 fetches it."));
            return;
        }

        SetBusy(true, label);

        var backups = new BackupStore(_settings.ResolvedBackupFolder);
        var svc = new ConversionService(new FfmpegRunner(ffmpeg ?? "ffmpeg"), backups);

        var files = new List<HistoryFile>();
        var notes = new List<string>();
        var errors = new List<string>();
        var done = 0;

        foreach (var track in tracks)
        {
            var (convert, format, rate, tags) = plan(track);
            ConversionOutcome outcome;

            if (convert)
            {
                var slot = done;
                var opts = new EncodeOptions
                {
                    Format = format ?? track.Format,
                    SampleRate = rate,
                };
                outcome = await Task.Run(() => svc.ConvertAsync(
                    new ConversionRequest { Track = track, Options = opts, Tags = tags },
                    pct => DispatcherQueue.TryEnqueue(() =>
                        ShowProgress(true, (slot * 100 + pct) / (double)tracks.Count))));
            }
            else if (tags is not null && !tags.IsEmpty)
            {
                outcome = await Task.Run(() => svc.WriteTagsOnly(track, tags));
            }
            else { done++; continue; }

            done++;
            ShowProgress(true, done * 100.0 / tracks.Count);

            if (outcome.Success)
            {
                if (outcome.History is not null) files.Add(outcome.History);
                foreach (var n in outcome.Notes) notes.Add($"{track.FileName}: {n}");
            }
            else errors.Add($"{track.FileName}: {outcome.Error}");
        }

        if (files.Count > 0) _history.Add("batch", label, files);

        // Nach einem Schreibvorgang kann jedes Cover ein anderes sein.
        TrackArt.Reload();
        InvalidateIndex();

        SetBusy(false, null);
        await MergeTabAsync(ActiveTab);
        await ReportAsync(files.Count, errors, notes);
    }

    // ══ Ablegen und Verschieben ══════════════════════════════════

    private async void OnPaneFilesDropped(object? sender, FilesDroppedArgs args)
    {
        if (args.Pane.Tab is not { Analysis: not null, Target: not null } tab)
        {
            // Der Ordner wird gerade noch gelesen. Vorher weiß niemand, auf
            // welches Format angeglichen und welche Tags übernommen werden
            // sollen. Früher fiel das Ablegen hier wortlos unter den Tisch.
            StatusText.Text = Strings.T("The folder is still being read, try again in a moment.");
            return;
        }

        await ImportAsync(tab, args.Paths, args.Index, null);
    }

    /// <summary>Tracks aus der anderen Hälfte — verschieben, mit Strg kopieren.</summary>
    private async void OnPaneTracksMoved(object? sender, TracksMovedArgs args)
    {
        var to = args.To.Tab;
        var from = args.From.Tab;
        if (from is null) return;

        // Innerhalb desselben Ordners ist das Umsortieren, nicht Verschieben.
        if (to is not null && string.Equals(to.Path, from.Path, StringComparison.OrdinalIgnoreCase))
            return;

        if (to is not { Analysis: not null, Target: not null })
        {
            StatusText.Text = Strings.T("The folder is still being read, try again in a moment.");
            return;
        }

        await ImportAsync(to, args.Tracks.Select(t => t.Path).ToList(), args.Index,
                          args.Copy ? null : from);
    }

    /// <summary>
    /// Bringt Dateien in einen Ordner: kopieren, an dessen Ziel angleichen,
    /// die Tags übernehmen, auf die sich der Ordner einig ist, und an der
    /// gewünschten Stelle einsortieren.
    ///
    /// <paramref name="removeFrom"/> gesetzt heißt verschieben: die Quellen
    /// werden danach gesichert und entfernt, sodass der Verlauf sie
    /// zurückholen kann.
    /// </summary>
    private async Task ImportAsync(
        FolderTab tab, IReadOnlyList<string> paths, int index, FolderTab? removeFrom)
    {
        var analysis = tab.Analysis!;
        var target = tab.Target!;
        // Album-Modus: Wer die Übernahme anhat, will einen Ordner, der sich
        // wie ein Album verhält — auch wenn er gerade noch uneinheitlich ist.
        var inherited = analysis.Inherited(byMajority: _settings.RuleFor(tab.Path).AlbumMode);
        var move = removeFrom is not null;

        // Was dieser Ordner an Automatik erlaubt. Ist beides aus, werden die
        // Dateien nur hereinkopiert und sonst nicht angefasst.
        var rule = _settings.RuleFor(tab.Path);

        var incoming = paths.Select(AudioProbe.Read).OfType<AudioTrack>().ToList();
        if (incoming.Count == 0) return;

        var needConvert = rule.AutoConform
            ? incoming.Where(t => !FolderAnalysis.Matches(t, target)).ToList()
            : [];

        var existing = tab.Tracks.ToList();
        var at = Math.Clamp(index, 0, existing.Count);

        // Die Track-Nummer ist ein Tag. Wer die Übernahme abgeschaltet hat,
        // will auch nicht, dass Nummern vergeben oder verschoben werden.
        //
        // Ist sie an, landet die Datei dort, wo sie abgelegt wurde, und der
        // Ordner wird anschließend von 1 an durchnummeriert. Vorher hing das
        // Einsortieren daran, dass der Ordner schon lückenlos nummeriert war;
        // andernfalls bekam die Datei die nächste freie Nummer und sprang beim
        // nächsten Einlesen ans Ende — sichtbar woanders hin, als man sie
        // abgelegt hatte.
        var renumber = rule.WritesNumbers;

        if (!_settings.SkipConformDialog)
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine(Strings.T(move ? "Move {0} file(s) to \"{1}\"."
                                           : "Take {0} file(s) into \"{1}\".",
                                      incoming.Count, tab.Name)).AppendLine();
            text.AppendLine(!rule.AutoConform
                ? Strings.T("Aligning is off for this folder. Format and sample rate stay.")
                : needConvert.Count > 0
                    ? Strings.T("Conversion: {0} file(s) → {1} · {2}",
                                needConvert.Count, target.Format, FormatRate(target.SampleRate))
                    : Strings.T("No conversion needed."));

            if (!rule.WritesBaseTags)
            {
                text.AppendLine(Strings.T(
                    "Tags are left alone, switched off for this folder."));
            }
            else
            {
                var erben = new List<string>();
                if (inherited.Album is not null) erben.Add(Strings.T("Album"));
                if (inherited.Artist is not null) erben.Add(Strings.T("Artist"));
                if (inherited.AlbumArtist is not null) erben.Add(Strings.T("Album artist"));
                if (inherited.Year is not null) erben.Add(Strings.T("Year"));
                if (inherited.Genre is not null) erben.Add(Strings.T("Genre"));
                text.AppendLine(erben.Count > 0
                    ? Strings.T("Inherited from the folder: {0}", string.Join(", ", erben))
                    : existing.Count == 0
                        ? Strings.T("The folder is empty, so there is nothing to inherit from.")
                        : Strings.T("The folder agrees on no tag, so nothing is inherited."));
            }

            if (rule.WritesNumbers)
            {
                text.AppendLine(existing.Count == 0
                    ? Strings.T("The folder is empty, numbering starts at 1.")
                    : at >= existing.Count
                        ? Strings.T("Appended at the end as track {0}.", existing.Count + 1)
                        : Strings.T("Inserted from track {0} on; the ones after it move up.",
                                    at + 1));
            }

            if (target.FromDefaultProfile)
                text.AppendLine().AppendLine(Strings.T(
                    "⚠ The target folder is mixed, the target comes from the default profile."));

            var down = needConvert.Count(t => t.SampleRate > target.SampleRate);
            if (down > 0)
                text.AppendLine().AppendLine(
                    Strings.T("⚠ {0} file(s) are downsampled, which cannot be undone "
                              + "without loss.", down));

            text.AppendLine().AppendLine(move
                ? Strings.T("The originals in \"{0}\" are removed; the history can bring "
                            + "them back.", removeFrom!.Name)
                : Strings.T("The source files are kept (copy)."));

            if (!await Confirm(Strings.T(move ? "Move files" : "Align files"),
                               text.ToString().TrimEnd(),
                               Strings.T(move ? "Move" : "Apply"))) return;
        }

        // Beim Verschieben verschwinden die Quelldateien — die darf der
        // Player dann nicht mehr offen halten.
        if (move) ReleaseIfAffected(incoming);

        SetBusy(true, Strings.T(move ? "Move {0} file(s)" : "Take in {0} file(s)",
                                incoming.Count));

        var ffmpeg = FfmpegLocator.Find(_settings.FfmpegPath);
        var backups = new BackupStore(_settings.ResolvedBackupFolder);
        var svc = new ConversionService(new FfmpegRunner(ffmpeg ?? "ffmpeg"), backups);

        var files = new List<HistoryFile>();
        var errors = new List<string>();
        var notes = new List<string>();

        // Das Cover des Ordners, einmal gelesen statt je Datei. Die erste
        // Datei, die eines trägt, gibt es vor — in einer Veröffentlichung
        // haben ohnehin alle dasselbe.
        AudioProbe.Cover? folderCover = null;
        if (rule.WritesCover)
        {
            foreach (var candidate in existing.Where(t => t.HasCover))
            {
                try { folderCover = AudioProbe.ReadCover(candidate.Path); } catch { }
                if (folderCover is { Data.Length: > 0 }) break;
            }
        }

        // Die Nummer der ersten hereinkommenden Datei ist ihre Stelle in der
        // Liste. Ohne Nummerierung wird gar keine geschrieben.
        var number = (uint)(at + 1);
        var done = 0;

        for (var i = 0; i < incoming.Count; i++)
        {
            var src = incoming[i];
            var tags = DropTags(src, rule, inherited, folderCover, number);

            // Erst in den Zielordner holen, dann dort angleichen — die Quelle
            // bleibt bis zum Schluss unangetastet.
            var dest = UniquePath(Path.Combine(tab.Path, Path.GetFileName(src.Path)));
            try { File.Copy(src.Path, dest); }
            catch (Exception ex) { errors.Add($"{src.FileName}: {ex.Message}"); continue; }

            var copied = AudioProbe.Read(dest);
            if (copied is null)
            {
                errors.Add($"{src.FileName}: " + Strings.T("not readable"));
                continue;
            }

            ConversionOutcome outcome;
            if (!rule.AutoConform || FolderAnalysis.Matches(copied, target))
            {
                // Nichts zu konvertieren, und womöglich auch nichts zu
                // schreiben — dann ist das Hereinkopieren schon alles.
                outcome = tags is null
                    ? new ConversionOutcome(true, copied, null, null, [])
                    : await Task.Run(() => svc.WriteTagsOnly(copied, tags));
            }
            else
            {
                var slot = i;
                outcome = await Task.Run(() => svc.ConvertAsync(new ConversionRequest
                {
                    Track = copied,
                    Options = new EncodeOptions
                    {
                        Format = target.Format,
                        SampleRate = target.SampleRate,
                        },
                    Tags = tags,
                }, pct => DispatcherQueue.TryEnqueue(() =>
                    ShowProgress(true, (slot * 100 + pct) / (double)incoming.Count))));
            }

            if (outcome.Success)
            {
                if (outcome.History is not null) files.Add(outcome.History);
                foreach (var n in outcome.Notes) notes.Add($"{src.FileName}: {n}");
                number++;
                done++;

                if (move) RemoveSource(src, backups, files, errors);
            }
            else
            {
                errors.Add($"{src.FileName}: {outcome.Error}");
                try { if (File.Exists(dest)) File.Delete(dest); } catch { }
            }

            ShowProgress(true, (i + 1) * 100.0 / incoming.Count);
        }

        // Erst jetzt die schon vorhandenen Dateien auf ihre neue Nummer
        // bringen — um genau so viele, wie tatsächlich ankamen. Geschrieben
        // wird nur, was sich wirklich ändert: Hängt man hinten an, ist das
        // nichts, und es entsteht keine einzige überflüssige Sicherung.
        if (renumber && done > 0)
        {
            var fixes = new List<(AudioTrack Track, uint Number)>();
            for (var i = 0; i < existing.Count; i++)
            {
                var wanted = (uint)(i < at ? i + 1 : i + 1 + done);
                if (existing[i].Track != wanted) fixes.Add((existing[i], wanted));
            }

            if (fixes.Count > 0)
            {
                ReleaseIfAffected(fixes.Select(f => f.Track));
                await WriteNumbersAsync(fixes, svc, files, errors);
            }
        }

        if (files.Count > 0)
            _history.Add(move ? "move" : "import",
                         Strings.T(move ? "{0} file(s) moved to \"{1}\""
                                        : "{0} file(s) taken into \"{1}\"",
                                   done, tab.Name),
                         files);

        TrackArt.Reload();
        InvalidateIndex();

        SetBusy(false, null);
        await MergeTabAsync(tab);
        if (removeFrom is not null) await MergeTabAsync(removeFrom);
        await ReportAsync(done, errors, notes);
    }

    /// <summary>
    /// Was eine hereinkommende Datei vom Ordner übernimmt.
    ///
    /// Jeder Unterpunkt des Album-Modus schreibt genau seinen Teil. Was
    /// er nicht abdeckt, bleibt null und damit unangetastet — so kann man
    /// etwa Nummern vergeben lassen, ohne dass Tags angefasst werden.
    /// </summary>
    private static TagEdit? DropTags(
        AudioTrack src, FolderRule rule, InheritedTags inherited,
        AudioProbe.Cover? cover, uint number)
    {
        if (!rule.AlbumMode) return null;

        var edit = new TagEdit
        {
            // Ein leerer Titel ist keiner. Der Dateiname ist zwar geraten,
            // aber immer noch besser als eine Zeile ohne Beschriftung.
            Title = rule.BaseTags && string.IsNullOrWhiteSpace(src.Title)
                ? Path.GetFileNameWithoutExtension(src.Path) : null,

            Album = rule.BaseTags ? inherited.Album : null,
            Artist = rule.BaseTags ? ArtistFor(src.Artist, inherited.Artist) : null,
            AlbumArtist = rule.BaseTags ? inherited.AlbumArtist : null,
            Genre = rule.BaseTags ? inherited.Genre : null,
            Year = rule.BaseTags && uint.TryParse(inherited.Year, out var y) ? y : null,
            Disc = rule.BaseTags && uint.TryParse(inherited.Disc, out var d) ? d : null,

            Track = rule.Numbering ? number : null,

            Cover = rule.Cover && cover is { Data.Length: > 0 } ? cover.Data : null,
            CoverMimeType = rule.Cover && cover is { Data.Length: > 0 } ? cover.MimeType : null,
        };

        return edit.IsEmpty ? null : edit;
    }

    private static string? ArtistFor(string own, string? folder) =>
        AlbumPlanner.ArtistFor(own, folder);

    /// <summary>
    /// Entfernt eine verschobene Quelldatei — vorher gesichert, damit der
    /// Verlauf sie an ihren alten Platz zurücklegen kann.
    /// </summary>
    private static void RemoveSource(
        AudioTrack src, BackupStore backups, List<HistoryFile> files, List<string> errors)
    {
        try
        {
            var backup = backups.Create(src.Path);
            File.Delete(src.Path);
            files.Add(new HistoryFile { Original = src.Path, BackupPath = backup });
        }
        catch (Exception ex)
        {
            errors.Add($"{src.FileName}: Original blieb liegen ({ex.Message})");
        }
    }

    /// <summary>Schiebt die Tracks ab der Einfügestelle um <paramref name="by"/> nach hinten.</summary>
    /// <summary>
    /// Schreibt Track-Nummern. Von hinten nach vorn, wenn es aufwärts geht,
    /// damit unterwegs nie zwei Dateien dieselbe Nummer tragen.
    /// </summary>
    private static async Task WriteNumbersAsync(
        List<(AudioTrack Track, uint Number)> plan, ConversionService svc,
        List<HistoryFile> files, List<string> errors)
    {
        var rising = plan.Any(p => p.Number > p.Track.Track);
        var order = rising ? Enumerable.Reverse(plan) : plan;

        foreach (var (track, number) in order)
        {
            var outcome = await Task.Run(
                () => svc.WriteTagsOnly(track, new TagEdit { Track = number }));

            if (outcome.Success)
            {
                if (outcome.History is not null) files.Add(outcome.History);
            }
            else errors.Add($"{track.FileName}: "
                            + Strings.T("number not moved ({0})", outcome.Error));
        }
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return path;
    }

    // ══ Umsortieren ══════════════════════════════════════════════

    /// <summary>
    /// Track-Nummern nach dem Ziehen neu vergeben.
    ///
    /// Maßgeblich ist allein der Schalter „Tags vom Ordner übernehmen": Er
    /// sagt, dass dieser Ordner ein Album ist und seine Nummern der
    /// Reihenfolge folgen sollen. Früher hing es zusätzlich daran, ob der
    /// Ordner schon lückenlos von 1 an nummeriert war — bei einer
    /// Teilauswahl (Tracks 1, 2, 5, 11 …) geschah dann gar nichts, und das
    /// Ziehen blieb folgenlos.
    /// </summary>
    private async void OnPaneReordered(object? sender, TrackPane pane)
    {
        var tab = pane.Tab;
        if (tab is null) return;

        var order = tab.Tracks.ToList();

        // Mit Disc-Zeilen zählt jede Disc von 1, und ein Lied, das in eine
        // andere Disc gezogen wurde, bekommt deren Nummer.
        var sections = pane.ReorderedSections;
        if (sections is not null) order = sections.Select(x => x.Track).ToList();

        if (!_settings.RuleFor(tab.Path).WritesNumbers)
        {
            StatusText.Text = Strings.T("Order changed. Track numbers unchanged "
                + "(track numbering is off for this folder)");
            return;
        }

        // Ohne Rückfrage: Wer eine Zeile zieht, hat die neue Reihenfolge
        // gemeint. Ein Dialog nach jedem Zug wäre im Weg, und der Verlauf
        // holt es zurück, falls es doch daneben ging.
        ReleaseIfAffected(order);
        SetBusy(true, Strings.T("Writing track numbers"));
        var svc = new ConversionService(
            new FfmpegRunner("ffmpeg"), new BackupStore(_settings.ResolvedBackupFolder));
        var files = new List<HistoryFile>();

        var perDisc = new Dictionary<uint, uint>();

        // Ohne Disc-Zeilen dieselbe Zählung wie beim Anwenden auf vorhandene
        // Dateien: mit mehreren Discs jede für sich.
        var plain = AlbumPlanner.Numbers(order);

        for (var i = 0; i < order.Count; i++)
        {
            uint wanted;
            uint? disc = null;

            if (sections is not null)
            {
                var d = sections[i].Disc;
                wanted = perDisc[d] = perDisc.GetValueOrDefault(d) + 1;
                if (order[i].Disc != d) disc = d;
            }
            else
            {
                wanted = plain[i];
            }

            if (order[i].Track == wanted && disc is null) continue;
            var edit = new TagEdit { Track = wanted, Disc = disc };
            var outcome = await Task.Run(() => svc.WriteTagsOnly(order[i], edit));
            if (outcome.Success && outcome.History is not null) files.Add(outcome.History);
            ShowProgress(true, (i + 1) * 100.0 / order.Count);
        }

        if (files.Count > 0) _history.Add("tracknumbers", Strings.T("Order changed"), files);
        SetBusy(false, null);
        await MergeTabAsync(tab);
        StatusText.Text = Strings.T("{0} track number(s) written", files.Count);
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
        if (await HistoryDialog.ShowAsync(Root.XamlRoot, _history))
        {
            TrackArt.Reload();
            InvalidateIndex();
            await MergeTabAsync(ActiveTab);
        }   // nach einem Undo liegt anderes im Ordner
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
    // ══ Umbenennen ═══════════════════════════════════════════════

    /// <summary>
    /// Benennt die Dateien des Ordners nach ihren Metadaten.
    ///
    /// Vorher wird gezeigt, was herauskommt: Ein Muster, das man nicht im
    /// Kopf ausrechnen kann, ist sonst ein Sprung ins Wasser, und die Namen
    /// von hundert Dateien wieder herzustellen macht niemandem Freude.
    /// Jede Datei wird gesichert, der Verlauf holt sie zurück.
    /// </summary>
    private async void OnRenameFiles(object sender, RoutedEventArgs e)
    {
        var tracks = ActiveTab.Tracks.ToList();
        if (tracks.Count == 0) return;

        var pattern = new TextBox
        {
            Text = _settings.RenamePattern,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var preview = new TextBlock
        {
            FontSize = 11.5,
            FontFamily = new FontFamily("Consolas"),
            TextWrapping = TextWrapping.NoWrap,
            Foreground = Res("TextFillColorSecondaryBrush"),
        };

        var summary = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };

        List<(AudioTrack Track, string Target)> plan = [];

        void Recalculate()
        {
            plan = RenamePlan(tracks, pattern.Text);

            var changing = plan.Count;
            summary.Text = changing == 0
                ? Strings.T("Every file already has this name.")
                : Strings.T("{0} of {1} file(s) get a new name.", changing, tracks.Count);

            // Alle, nicht die ersten acht: Wer hundert Dateien umbenennt,
            // will genau die eine sehen können, bei der das Muster daneben
            // greift. Der ScrollViewer darum herum macht es tragbar.
            preview.Text = string.Join(Environment.NewLine,
                plan.Select(p => p.Track.FileName + "  →  " + Path.GetFileName(p.Target)));
        }

        pattern.TextChanged += (_, _) => Recalculate();
        Recalculate();

        var panel = new StackPanel { Spacing = 10, Width = 460 };
        panel.Children.Add(new TextBlock
        {
            Text = string.Join("  ", FileNaming.Placeholders),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Res("TextFillColorTertiaryBrush"),
        });
        panel.Children.Add(pattern);
        panel.Children.Add(summary);
        panel.Children.Add(new ScrollViewer
        {
            MaxHeight = 300,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = preview,
        });

        var dialog = new ContentDialog
        {
            Title = Strings.T("Rename by metadata"),
            Content = panel,
            PrimaryButtonText = Strings.T("Rename"),
            CloseButtonText = Strings.T("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Root.XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || plan.Count == 0) return;

        // Das Muster, mit dem es zuletzt gut ging, ist das bessere Standardmuster.
        _settings.RenamePattern = pattern.Text;
        _settings.Save();

        await RunRenameAsync(plan);
    }

    /// <summary>
    /// Was das Muster aus diesen Tracks macht, ohne die Dateien, die ihren
    /// Namen behalten. Zwei Tracks dürfen nicht auf demselben Namen landen,
    /// darum wandert jeder Treffer gleich in die Liste des Belegten.
    /// </summary>
    private static List<(AudioTrack Track, string Target)> RenamePlan(
        IReadOnlyList<AudioTrack> tracks, string pattern)
    {
        var plan = new List<(AudioTrack, string)>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var track in tracks)
        {
            var folder = Path.GetDirectoryName(track.Path)!;
            var wanted = Path.Combine(folder, FileNaming.Build(track, pattern));

            // Die eigene Datei zählt nicht als Hindernis, sonst bekäme jede
            // schon richtig benannte Datei ein „(2)" verpasst.
            if (string.Equals(wanted, track.Path, StringComparison.OrdinalIgnoreCase))
            {
                taken.Add(track.Path);
                continue;
            }

            var target = FileNaming.Free(wanted, taken);
            taken.Add(target);
            plan.Add((track, target));
        }

        return plan;
    }

    private async Task RunRenameAsync(List<(AudioTrack Track, string Target)> plan)
    {
        ReleaseIfAffected(plan.Select(p => p.Track));
        SetBusy(true, Strings.T("Renaming {0} file(s)", plan.Count));

        var backups = new BackupStore(_settings.ResolvedBackupFolder);
        var files = new List<HistoryFile>();
        var errors = new List<string>();

        for (var i = 0; i < plan.Count; i++)
        {
            var (track, target) = plan[i];
            try
            {
                // Erst sichern, dann umbenennen. Der Verlauf legt die Sicherung
                // an den alten Platz zurück und räumt den neuen Namen weg.
                var backup = backups.Create(track.Path);
                File.Move(track.Path, target);
                files.Add(new HistoryFile
                {
                    Original = track.Path,
                    BackupPath = backup,
                    OutputPath = target,
                });
            }
            catch (Exception ex) { errors.Add($"{track.FileName}: {ex.Message}"); }

            ShowProgress(true, (i + 1) * 100.0 / plan.Count);
        }

        if (files.Count > 0)
            _history.Add("rename", Strings.T("{0} file(s) renamed", files.Count), files);

        TrackArt.Reload();
        InvalidateIndex();

        SetBusy(false, null);
        await MergeTabAsync(ActiveTab);
        await ReportAsync(files.Count, errors, []);
    }

    /// <summary>
    /// Ein Bild als Cover für jede Datei des Ordners.
    ///
    /// Zuerst zur Auswahl, was im Ordner schon liegt: In den allermeisten
    /// Fällen trägt eine der Dateien bereits das richtige Cover, und dann ist
    /// der Weg über den Dateiauswahl-Dialog ein Umweg über eine Datei, die es
    /// vielleicht gar nicht mehr gibt.
    /// </summary>
    private void OnCoverForAll(object sender, RoutedEventArgs e)
    {
        var targets = ActiveTab.Tracks.ToList();
        if (targets.Count == 0) return;
        Run(() => CoverForAllAsync(targets));
    }

    private async Task CoverForAllAsync(List<AudioTrack> targets)
    {
        var found = await Task.Run(() => DistinctCovers(targets));

        var gallery = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        AudioProbe.Cover? picked = null;
        ContentDialog? host = null;

        foreach (var cover in found)
        {
            var picture = new Image { Stretch = Stretch.UniformToFill, Width = 96, Height = 96 };
            try
            {
                var bitmap = new BitmapImage { DecodePixelWidth = 192 };
                using var stream = new MemoryStream(cover.Data);
                bitmap.SetSource(stream.AsRandomAccessStream());
                picture.Source = bitmap;
            }
            catch { continue; }

            var choice = new Button
            {
                Padding = new Thickness(3),
                CornerRadius = new CornerRadius(5),
                Content = picture,
            };
            ToolTipService.SetToolTip(choice,
                Strings.T("{0} ({1} KB)", ImageInfo.ShortName(cover.MimeType),
                          $"{cover.Data.Length / 1024.0:0.#}"));

            choice.Click += (_, _) => { picked = cover; host?.Hide(); };
            gallery.Children.Add(choice);
        }

        var panel = new StackPanel { Spacing = 12, Width = 430 };
        panel.Children.Add(new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Res("TextFillColorSecondaryBrush"),
            Text = found.Count == 0
                ? Strings.T("No file in this folder has a cover yet.")
                : Strings.T("Pick one of the covers already in this folder, or choose a file."),
        });

        if (found.Count > 0)
            panel.Children.Add(new ScrollViewer { MaxHeight = 320, Content = gallery });

        var dialog = new ContentDialog
        {
            Title = Strings.T("Set cover for {0}…", targets.Count),
            Content = panel,
            PrimaryButtonText = Strings.T("Choose a file…"),
            CloseButtonText = Strings.T("Cancel"),
            DefaultButton = found.Count == 0
                ? ContentDialogButton.Primary : ContentDialogButton.Close,
            XamlRoot = Root.XamlRoot,
        };
        host = dialog;

        var answer = await dialog.ShowAsync();

        // Ein Klick auf ein Bild schließt über Hide(), das None liefert.
        if (picked is { Data.Length: > 0 } chosen)
        {
            await ApplyImageAsync(targets, chosen.Data, Strings.T("Set cover"), keepExact: true);
            return;
        }

        if (answer == ContentDialogResult.Primary) await SetFromFileAsync(targets);
    }

    /// <summary>
    /// Die verschiedenen Cover eines Ordners.
    ///
    /// Verglichen wird über die Bytes, nicht über das Bild: Zwei Dateien mit
    /// demselben Cover sollen einmal erscheinen, und ein Vergleich Bild für
    /// Bild wäre bei zwanzig Titeln zwanzigmal dekodieren.
    /// </summary>
    private static List<AudioProbe.Cover> DistinctCovers(IReadOnlyList<AudioTrack> tracks)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var found = new List<AudioProbe.Cover>();

        // Ein Album hat selten mehr als eine Handvoll verschiedener Bilder,
        // und ein rekursiver Ordner kann tausende Dateien haben.
        foreach (var track in tracks.Where(t => t.HasCover).Take(300))
        {
            AudioProbe.Cover? cover = null;
            try { cover = AudioProbe.ReadCover(track.Path); } catch { }
            if (cover is not { Data.Length: > 0 }) continue;

            var mark = cover.Data.Length + ":" + Convert.ToHexString(
                System.Security.Cryptography.MD5.HashData(cover.Data));

            if (!seen.Add(mark)) continue;
            found.Add(cover);

            if (found.Count >= 24) break;
        }

        return found;
    }
    // ══ Tags übertragen ══════════════════════════════════════════

    /// <summary>
    /// Die Tags der zuletzt kopierten Datei, samt Cover.
    ///
    /// Bewusst nicht über die Windows-Zwischenablage: Dort landete entweder
    /// Text, den niemand zurücklesen kann, oder ein eigenes Format, das
    /// außerhalb der App ohnehin niemand versteht.
    /// </summary>
    private (AudioTrack Track, AudioProbe.Cover? Art)? _tagClip;

    private void OnCopyTags(object? sender, AudioTrack track)
    {
        AudioProbe.Cover? art = null;
        try { art = AudioProbe.ReadCover(track.Path); } catch { }

        _tagClip = (track, art);
        PaneA.TagsCopied = PaneB.TagsCopied = true;

        StatusText.Text = Strings.T("Tags copied from \"{0}\"", track.FileName);
    }

    private async void OnPasteTags(object? sender, IReadOnlyList<AudioTrack> targets)
    {
        if (_tagClip is not { } clip || targets.Count == 0) return;

        var everything = _settings.TagPasteMode == "all";

        // Die Datei, aus der kopiert wurde, noch einmal zu beschreiben wäre
        // ein Schreibvorgang ohne Wirkung, samt Sicherung und Verlaufseintrag.
        var write = targets
            .Where(t => !string.Equals(t.Path, clip.Track.Path, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (write.Count == 0)
        {
            StatusText.Text = Strings.T("That is the file the tags came from.");
            return;
        }

        var edit = BuildTagCopy(clip.Track, clip.Art, everything);

        var fields = string.Join(", ", CopiedFields(everything));
        var text = Strings.T("From \"{0}\": {1}", clip.Track.FileName, fields)
                 + Environment.NewLine + Environment.NewLine
                 + Strings.T("{0} file(s) will be changed. Each one is backed up first.",
                             write.Count);

        if (!await Confirm(Strings.T("Paste tags"), text, Strings.T("Apply"))) return;

        await RunJobAsync(Strings.T("Paste tags"), write, _ => (false, null, null, edit));
    }

    /// <summary>
    /// Was übernommen wird. Titel und Track-Nummer sind je Datei verschieden:
    /// Sie mitzuschreiben macht aus einem Album zwölfmal dasselbe Lied, und
    /// genau deshalb ist „ohne Titel und Nummer" die Vorgabe.
    /// </summary>
    private static TagEdit BuildTagCopy(AudioTrack from, AudioProbe.Cover? art, bool everything) => new()
    {
        Artist = from.Artist,
        Album = from.Album,
        AlbumArtist = from.AlbumArtist,
        Genre = from.Genre,
        Composer = from.Composer,
        Comment = from.Comment,
        Year = from.Year,
        Disc = from.Disc,

        Title = everything ? from.Title : null,
        Track = everything ? from.Track : null,

        // Leeres Array hieße „Cover entfernen". Hat die Quelle keines, bleibt
        // das Cover des Ziels stehen, statt gelöscht zu werden.
        Cover = art is { Data.Length: > 0 } ? art.Data : null,
        CoverMimeType = art?.MimeType,
    };

    private static IEnumerable<string> CopiedFields(bool everything)
    {
        if (everything)
        {
            yield return Strings.T("Title");
            yield return Strings.T("Track");
        }
        yield return Strings.T("Artist");
        yield return Strings.T("Album");
        yield return Strings.T("Album artist");
        yield return Strings.T("Year");
        yield return Strings.T("Disc");
        yield return Strings.T("Genre");
        yield return Strings.T("Composer");
        yield return Strings.T("Comment");
        yield return Strings.T("Cover");
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

    // ══ Kontextmenü in der Suche ═════════════════════════════════

    /// <summary>
    /// Rechtsklick auf einen Treffer. Lieder und Ordner bekommen je das
    /// Menü, das man von ihnen kennt — vorher passierte hier gar nichts,
    /// und man musste den Treffer erst öffnen, um irgendetwas zu tun.
    /// </summary>
    private void OnSearchHitRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not SearchHit hit) return;

        SearchList.SelectedItem = hit;

        var menu = new MenuFlyout();

        if (hit.Kind == HitKind.Folder)
        {
            menu.Items.Add(Item("\uE8A7", Strings.T("Open in a new tab"), () => OpenTab(hit.Path)));
            menu.Items.Add(Item("\uE721", Strings.T("Search subfolders"), () => OpenRecursive(hit.Path)));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Item("\uE8DA", Strings.T("Open in Explorer"), () => Reveal(hit.Path)));
        }
        else
        {
            var folder = Path.GetDirectoryName(hit.Path);

            menu.Items.Add(Item("\uE768", Strings.T("Play"), () =>
            {
                if (AudioProbe.Read(hit.Path) is { } track) _player.Play(track);
            }));
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(Item("\uE8A7", Strings.T("Go to folder"), () =>
            {
                // Mit der Datei ausgewählt, sonst sucht man sie im Ordner
                // noch einmal.
                if (folder is null) return;
                NavigateActive(folder);
                Fire(LoadTabAsync(TopTab, hit.Path), Strings.T("Reading…"));
            }));
            menu.Items.Add(Item("\uE8DA", Strings.T("Show in Explorer"), () => Reveal(hit.Path)));
            menu.Items.Add(Item("\uE8C8", Strings.T("Copy path"), () =>
            {
                var package = new DataPackage();
                package.SetText(hit.Path);
                Clipboard.SetContent(package);
            }));
        }

        menu.ShowAt((UIElement)sender, e.GetPosition((UIElement)sender));
        e.Handled = true;

        static MenuFlyoutItem Item(string glyph, string text, Action run)
        {
            var item = new MenuFlyoutItem
            {
                Text = text,
                Icon = new FontIcon { Glyph = glyph },
            };
            item.Click += (_, _) => run();
            return item;
        }
    }
    // ══ Suchen im Ordner ═════════════════════════════════════════

    /// <summary>
    /// Die Treffer der laufenden Suche, in der Reihenfolge der Liste, und wo
    /// man gerade steht.
    /// </summary>
    private List<AudioTrack> _found = [];
    private int _at = -1;

    private void OnFindShortcut(
        KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        FindBar.Visibility = Visibility.Visible;
        FindBox.Focus(FocusState.Programmatic);
        FindBox.SelectAll();
        args.Handled = true;
    }

    private void OnFindClose(object sender, RoutedEventArgs e)
    {
        FindBar.Visibility = Visibility.Collapsed;
        FindBox.Text = "";
        _found = [];
        _at = -1;
        ActivePane.Mark([]);
    }

    private void OnFindChanged(object sender, TextChangedEventArgs e)
    {
        var needle = FindBox.Text.Trim();
        _found = needle.Length == 0 ? [] : Matches(needle);
        _at = _found.Count > 0 ? 0 : -1;

        ActivePane.Mark(_found.Select(t => t.Path));
        ShowFindCount();

        // Beim Tippen gleich zum ersten Treffer, wie im Browser.
        if (_at >= 0) ActivePane.Reveal(_found[_at]);
    }

    /// <summary>
    /// Was auf die Eingabe passt. Gesucht wird in dem, was in der Zeile
    /// steht: Titel, Interpret, Album und der Dateiname.
    /// </summary>
    private List<AudioTrack> Matches(string needle) =>
    [
        .. ActiveTab.Tracks.Where(t =>
            Has(t.Title) || Has(t.Artist) || Has(t.Album) || Has(t.FileName)),
    ];

    private bool Has(string value) =>
        value.Contains(FindBox.Text.Trim(), StringComparison.CurrentCultureIgnoreCase);

    private void ShowFindCount() =>
        FindCount.Text = FindBox.Text.Trim().Length == 0 ? ""
            : _found.Count == 0 ? Strings.T("none")
            : $"{_at + 1}/{_found.Count}";

    private void OnFindNext(object sender, RoutedEventArgs e) => Step(1);
    private void OnFindPrevious(object sender, RoutedEventArgs e) => Step(-1);

    /// <summary>Einen Treffer weiter, rundherum wie im Browser.</summary>
    private void Step(int by)
    {
        if (_found.Count == 0) return;

        _at = (_at + by + _found.Count) % _found.Count;
        ActivePane.Reveal(_found[_at]);
        ShowFindCount();
    }

    private void OnFindKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Escape:
                OnFindClose(sender, e);
                e.Handled = true;
                break;

            case Windows.System.VirtualKey.Enter:
                // Mit Umschalt rückwärts, genau wie im Browser.
                Step(Shift() ? -1 : 1);
                e.Handled = true;
                break;
        }

        static bool Shift() =>
            Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }
    /// <summary>
    /// Hängt eine Liste an das Hauptfenster. Für die beiden Hälften einmal
    /// beim Start, für jeden aufgeklappten Unterordner beim Aufklappen —
    /// derselbe Satz Ereignisse, damit sich eine Unterordner-Liste in nichts
    /// von einer Hälfte unterscheidet: ziehen, ablegen, Rechtsklick, alles.
    /// </summary>
    private void WirePane(TrackPane pane)
    {
        pane.SelectionChanged += OnPaneSelectionChanged;
        pane.Activated += OnPaneActivated;
        pane.FilesDropped += OnPaneFilesDropped;
        pane.ReorderCompleted += OnPaneReordered;
        pane.TracksMoved += OnPaneTracksMoved;
        pane.DeleteRequested += OnPaneDeleteRequested;
        pane.PlayRequested += (_, track) => _player.Play(track);
        pane.SortRequested += OnPaneSortRequested;
        pane.ScopeExitRequested += OnScopeExit;
        pane.ColumnsReordered += (_, _) => ApplyColumns();
        pane.IsAlbumFolder = folder => _settings.RuleFor(folder).AlbumMode;
        pane.CopyTagsRequested += OnCopyTags;
        pane.PasteTagsRequested += OnPasteTags;
        pane.NavigateRequested += (_, target) => NavigateActive(target);
        pane.ColumnsResized += (_, _) => RememberColumns();
    }
}
