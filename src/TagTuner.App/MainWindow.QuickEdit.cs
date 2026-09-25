using System.Runtime.InteropServices;

using TagTuner.Core.Model;
using TagTuner.Core.Safety;
using TagTuner.Core.Settings;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace TagTuner.App;

// Das kleine Fenster für „Metadaten bearbeiten" aus dem Explorer.
//
// Es ist dasselbe Fenster wie sonst, nur ohne alles außer der
// Metadatenspalte. So verhält sich jedes Feld, das Cover und das Anwenden
// genau wie in der App, statt in einer zweiten Fassung davon abzuweichen.
// Die Liste gibt es weiterhin: Sie hält die Auswahl, auf die sich die Spalte
// bezieht. Bei einer Datei ist sie ausgeblendet; bei mehreren geht das
// Fenster nach rechts auf und zeigt genau die markierten Dateien.

public sealed partial class MainWindow
{
    /// <summary>Aus dem Explorer zum Bearbeiten geöffnet.</summary>
    private static bool Quick => App.Launch.Edit;

    /// <summary>Mehrere Dateien: Die Liste steht rechts neben der Spalte.</summary>
    private bool _quickList;

    private const int QuickWidth = 400, QuickListWidth = 700, QuickHeight = 820;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    private double WindowScale()
    {
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        return scale > 0 ? scale : 1;
    }

    private void EnterQuickMode()
    {
        NavBar.Visibility = Visibility.Collapsed;
        CrumbSlot.Visibility = Visibility.Collapsed;
        TabBar.Visibility = Visibility.Collapsed;
        ToolBar.Visibility = Visibility.Collapsed;
        QuickTitle.Visibility = Visibility.Visible;

        // Nur die erste Spalte bleibt, und sie nimmt die ganze Breite.
        foreach (var child in Work.Children.OfType<FrameworkElement>())
            if (Grid.GetColumn(child) > 0) child.Visibility = Visibility.Collapsed;
        for (var i = 0; i < Work.ColumnDefinitions.Count; i++)
        {
            var col = Work.ColumnDefinitions[i];
            col.MinWidth = 0;
            col.Width = i == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        }

        // Die Statusleiste zeigt Wiedergabe und den letzten Schritt. Hier
        // gibt es nichts abzuspielen, und Fehler kommen als Dialog.
        StatusBar.Visibility = Visibility.Collapsed;
        Root.RowDefinitions[2].Height = new GridLength(0);

        // Escape schließt, wie bei einem Dialog.
        var escape = new KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
        escape.Invoked += (_, e) => { e.Handled = true; Close(); };
        Root.KeyboardAccelerators.Add(escape);

        // Die Liste ist zum Auswählen da: nichts ziehen, umsortieren oder
        // löschen. Mit einem Teil des Ordners würde das die Nummern der
        // übrigen verbiegen.
        PaneA.ReadOnly = true;
        PaneA.FitColumns = true;
        SubHostA.Visibility = Visibility.Collapsed;

        // AppWindow rechnet in echten Pixeln, die Maße hier sind in
        // Bildschirmpunkten gedacht.
        var scale = WindowScale();
        var size = new Windows.Graphics.SizeInt32((int)(QuickWidth * scale), (int)(QuickHeight * scale));

        // Mittig auf dem Bildschirm, auf dem gerade gearbeitet wird, und nie
        // höher als dieser.
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        size.Height = Math.Min(size.Height, area.Height);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            area.X + (area.Width - size.Width) / 2,
            area.Y + (area.Height - size.Height) / 2,
            size.Width, size.Height));
    }

    /// <summary>
    /// Der Tab zeigt nur die markierten Dateien, nie den ganzen Ordner.
    /// Aus einem Ordner geöffnet, bleibt es beim ganzen Ordner.
    /// </summary>
    private void PrepareQuickTab()
    {
        if (App.Launch.File is { Length: > 0 } file)
            TopTab.Only = new(StringComparer.OrdinalIgnoreCase) { file };
        else
            _quickSelectAll = true;
    }

    /// <summary>
    /// Aus einem Ordner geöffnet: Gemeint ist der ganze Ordner. Die Spalte
    /// bezieht sich nur auf die Auswahl, und die Liste dazu ist hier nicht
    /// zu sehen, also wird nach dem Einlesen alles ausgewählt.
    /// </summary>
    private bool _quickSelectAll;

    private void UpdateQuickTitle(List<AudioTrack> sel)
    {
        QuickTitle.Text = sel.Count == 1
            ? sel[0].FileName
            : ActiveTab.Name;
        Title = $"{QuickTitle.Text} · TagTuner";
    }

    /// <summary>
    /// Eine weitere im Explorer markierte Datei. Sie kommt in die Liste und
    /// zur Auswahl dazu, solange sie im selben Ordner liegt: Die Liste zeigt
    /// nur diesen einen.
    /// </summary>
    private void AddQuickFile(string file)
    {
        var tab = ActiveTab;
        if (tab.Only is not { } only) return;   // aus einem Ordner geöffnet
        if (!string.Equals(Path.GetDirectoryName(file), tab.Path, StringComparison.OrdinalIgnoreCase))
            return;
        if (!only.Add(file)) return;

        tab.SelectedPaths.Add(file);
        if (!_quickList) ShowQuickList();

        // Ist der Ordner noch nicht gelesen, holt das Laden sie mit.
        if (tab.Analysis is null) return;
        Fire(MergeTabAsync(tab), Strings.T("Reading…"));
    }

    /// <summary>
    /// Zieht das Fenster nach rechts auf und zeigt die Liste der Dateien.
    /// Die Metadatenspalte behält ihre Breite und bleibt, wo sie war.
    /// </summary>
    private void ShowQuickList()
    {
        _quickList = true;

        Work.ColumnDefinitions[0].Width = new GridLength(QuickWidth);
        Work.ColumnDefinitions[0].MinWidth = 300;
        Work.ColumnDefinitions[1].Width = new GridLength(1);
        Work.ColumnDefinitions[4].Width = new GridLength(1, GridUnitType.Star);
        Work.ColumnDefinitions[4].MinWidth = 300;
        foreach (var child in Work.Children.OfType<FrameworkElement>())
        {
            var col = Grid.GetColumn(child);
            if (col is 1 or 4) child.Visibility = Visibility.Visible;
        }

        var scale = WindowScale();
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var width = Math.Min((int)((QuickWidth + QuickListWidth) * scale), area.Width);

        // Nach rechts wachsen; nur wenn dort kein Platz ist, nach links rücken.
        var x = Math.Min(AppWindow.Position.X, area.X + area.Width - width);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            Math.Max(area.X, x), AppWindow.Position.Y, width, AppWindow.Size.Height));
    }

    /// <summary>
    /// Nach einer Konvertierung heißt die Datei anders, etwa .flac statt
    /// .mp3. Damit sie nicht aus der Liste fällt, kommt der neue Name dazu,
    /// und war sie ausgewählt, bleibt sie es.
    /// </summary>
    private void FollowConversions(IEnumerable<HistoryFile> files)
    {
        var tab = ActiveTab;
        foreach (var f in files)
        {
            if (f.OutputPath is not { } output ||
                string.Equals(output, f.Original, StringComparison.OrdinalIgnoreCase)) continue;

            if (tab.Only is { } only && only.Contains(f.Original)) only.Add(output);
            if (tab.SelectedPaths.Contains(f.Original, StringComparer.OrdinalIgnoreCase))
                tab.SelectedPaths.Add(output);
        }
    }
}
