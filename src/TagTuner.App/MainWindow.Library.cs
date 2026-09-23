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

// Baum und Bibliothek links.

public sealed partial class MainWindow
{
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
}
