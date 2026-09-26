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

// Suchen im offenen Ordner und das Kontextmenü der Suchtreffer.

public sealed partial class MainWindow
{
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
        pane.NumbersFollowOrder = tab => !tab.Recursive && _settings.RuleFor(tab.Path).WritesNumbers;
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
