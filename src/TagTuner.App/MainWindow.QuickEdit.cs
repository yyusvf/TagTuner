using System.Runtime.InteropServices;

using TagTuner.Core.Model;
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
// Die ausgeblendete Liste gibt es weiterhin: Sie hält die Auswahl, auf die
// sich die Spalte bezieht.

public sealed partial class MainWindow
{
    /// <summary>Aus dem Explorer zum Bearbeiten geöffnet.</summary>
    private static bool Quick => App.Launch.Edit;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

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

        // AppWindow rechnet in echten Pixeln, die Maße hier sind in
        // Bildschirmpunkten gedacht.
        var scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        if (scale <= 0) scale = 1;
        var size = new Windows.Graphics.SizeInt32((int)(400 * scale), (int)(820 * scale));

        // Mittig auf dem Bildschirm, auf dem gerade gearbeitet wird, und nie
        // höher als dieser.
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        size.Height = Math.Min(size.Height, area.Height);
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            area.X + (area.Width - size.Width) / 2,
            area.Y + (area.Height - size.Height) / 2,
            size.Width, size.Height));
    }

    private void UpdateQuickTitle(List<AudioTrack> sel)
    {
        QuickTitle.Text = sel.Count == 1 && !FolderScope
            ? sel[0].FileName
            : ActiveTab.Name;
        Title = $"{QuickTitle.Text} · TagTuner";
    }

    /// <summary>
    /// Eine weitere im Explorer markierte Datei. Sie kommt zur Auswahl dazu,
    /// solange sie im selben Ordner liegt; die Liste zeigt nur diesen einen.
    /// </summary>
    private void AddQuickFile(string file)
    {
        var tab = ActiveTab;
        if (!string.Equals(Path.GetDirectoryName(file), tab.Path, StringComparison.OrdinalIgnoreCase))
            return;
        if (tab.SelectedPaths.Contains(file, StringComparer.OrdinalIgnoreCase)) return;

        tab.SelectedPaths.Add(file);

        // Ist der Ordner noch nicht gelesen, übernimmt das Laden die Auswahl.
        if (tab.Tracks.Count == 0) return;
        PaneA.Refresh();
        UpdateMetaPanel();
    }
}
