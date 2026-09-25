using TagTuner.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TagTuner.App;

// Der Fortschritt oben in der Titelleiste: eine Pille mit dem, was gerade
// geschieht, der Datei, an der gearbeitet wird, einem Balken und der
// Prozentzahl. Mittig über dem Fenster, wenn zwischen Pfad und Tabs Platz
// ist; sonst direkt rechts vom Pfad.

public sealed partial class MainWindow
{
    /// <summary>Abstand, den die Pille in der Mitte zu Pfad und Tabs hält.</summary>
    private const double ProgressGap = 16;

    /// <summary>
    /// Beginnt oder beendet eine Arbeit. Die Beschriftung steht in der Pille;
    /// was danach herauskam, meldet die Statuszeile.
    /// </summary>
    private void SetBusy(bool busy, string? label)
    {
        ProgressTitle.Text = label ?? "";
        ProgressDetail.Text = "";
        ProgressDetail.Visibility = Visibility.Collapsed;
        ShowProgress(busy, 0);

        PaneA.IsEnabled = PaneB.IsEnabled = !busy;
        FolderTree.IsEnabled = !busy;
        if (busy) { ApplyBtn.IsEnabled = false; AlignBtn.IsEnabled = false; }
    }

    /// <summary>
    /// Zeigt oder versteckt die Pille. Die Prozentzahl nur, wenn eine
    /// bekannt ist; beim Einlesen läuft der Balken unbestimmt.
    /// </summary>
    private void ShowProgress(bool busy, double percent)
    {
        var appearing = busy && ProgressHost.Visibility != Visibility.Visible;
        ProgressHost.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Progress.Value = Math.Clamp(percent, 0, 100);
        ProgressLabel.Text = busy && !Progress.IsIndeterminate
            ? $"{Math.Round(percent)} %"
            : "";

        if (appearing) PlaceProgress();
    }

    /// <summary>Welche Datei gerade dran ist, als „3 von 14 · Name".</summary>
    private void ProgressStep(int index, int count, string name) =>
        ProgressNote(count > 1
            ? Strings.T("{0} of {1}", index + 1, count) + " · " + name
            : name);

    /// <summary>Die zweite, leisere Zeile der Pille.</summary>
    private void ProgressNote(string? text)
    {
        var was = ProgressDetail.Visibility;
        ProgressDetail.Text = text ?? "";
        ProgressDetail.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        if (ProgressDetail.Visibility != was) PlaceProgress();
    }

    /// <summary>
    /// Hängt die Pille in die Mitte, wenn sie dort weder den Pfad noch die
    /// Tabs verdeckt, und sonst an ihren Platz neben dem Pfad.
    ///
    /// Gemessen wird der Pfad an seinem Inhalt, nicht an seiner aktuellen
    /// Breite: Steht die Pille neben ihm, ist er deswegen schmaler, und die
    /// Entscheidung kippte sonst bei jedem Aufruf hin und her.
    /// </summary>
    private void PlaceProgress()
    {
        if (ProgressHost.Visibility != Visibility.Visible || AppTitleBar.ActualWidth <= 0) return;

        ProgressHost.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = ProgressHost.DesiredSize.Width;
        var total = AppTitleBar.ActualWidth;
        var left = (total - width) / 2;

        double X(FrameworkElement e) =>
            e.TransformToVisual(AppTitleBar).TransformPoint(new Windows.Foundation.Point(0, 0)).X;

        double freeLeft;
        if (CrumbSlot.Visibility == Visibility.Visible)
        {
            CrumbHost.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var crumbs = Math.Max(CrumbBar.MinWidth, CrumbHost.DesiredSize.Width + 22);
            freeLeft = X(CrumbSlot) + crumbs;
        }
        else if (QuickTitle.Visibility == Visibility.Visible)
            freeLeft = X(QuickTitle) + QuickTitle.ActualWidth;
        else
            freeLeft = X(NavBar) + NavBar.ActualWidth;

        var rightNeighbour = TabBar.Visibility == Visibility.Visible ? TabBar
                           : ToolBar.Visibility == Visibility.Visible ? ToolBar
                           : (FrameworkElement)CaptionSpacer;
        var freeRight = X(rightNeighbour);

        var centered = left >= freeLeft + ProgressGap && left + width <= freeRight - ProgressGap;
        var parent = centered ? ProgressCenter : ProgressDock;
        if (ReferenceEquals(ProgressHost.Parent, parent)) return;

        ((Panel)ProgressHost.Parent).Children.Remove(ProgressHost);
        parent.Children.Add(ProgressHost);
    }
}
