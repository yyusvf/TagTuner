using System.ComponentModel;
using Microsoft.UI.Xaml;

namespace TagTuner.App;

/// <summary>
/// Die Spaltenbreiten der Trackliste, einmal für alle Zeilen und beide
/// Hälften.
///
/// Kopfzeile und Zeilenvorlage müssen dieselben Breiten benutzen, sonst
/// stehen Überschrift und Inhalt nicht übereinander. Statt die Werte an
/// zwei Stellen zu pflegen, binden beide auf dieses eine Objekt — es liegt
/// als Ressource in App.xaml.
///
/// Die Breiten heißen W0 bis W17 und nicht „Titel" oder „Album": Welche
/// Spalte an welcher Stelle steht, entscheidet der Nutzer. Einzeln benannte
/// Eigenschaften statt eines Indexers, weil die Bindung auf <c>[3]</c> in
/// WinUI nicht verlässlich auflöst und eine Spalte, die still keine Breite
/// bekommt, schwer zu finden wäre.
/// </summary>
public sealed class TrackColumnLayout : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>So viele Spalten kann die Liste höchstens zeigen.</summary>
    public const int Slots = 18;

    private const double Min = 28;
    private const double Max = 900;

    private readonly double[] _widths = new double[Slots];

    public TrackColumnLayout()
    {
        for (var i = 0; i < Slots; i++) _widths[i] = 100;
    }

    public double WidthAt(int index) =>
        index >= 0 && index < Slots ? _widths[index] : 0;

    public void Resize(int index, double width)
    {
        if (index < 0 || index >= Slots) return;

        var clamped = Math.Clamp(width, Min, Max);
        if (Math.Abs(_widths[index] - clamped) < 0.5) return;

        _widths[index] = clamped;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("W" + index));
    }

    /// <summary>Setzt alle Breiten auf einmal, etwa nach einer Änderung der Spaltenauswahl.</summary>
    public void Set(IReadOnlyList<double> widths)
    {
        for (var i = 0; i < Slots; i++)
        {
            var wanted = i < widths.Count && double.IsFinite(widths[i])
                ? Math.Clamp(widths[i], Min, Max)
                : _widths[i];

            if (Math.Abs(_widths[i] - wanted) < 0.5) continue;
            _widths[i] = wanted;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("W" + i));
        }
    }

    public double[] ToArray() => [.. _widths];

    // ── Die Bindungsziele ────────────────────────────────────────
    // Bewusst ausgeschrieben. Ein Indexer wäre kürzer, aber die Bindung
    // darauf ist in WinUI nicht zuverlässig.

    public GridLength W0 => new(_widths[0]);
    public GridLength W1 => new(_widths[1]);
    public GridLength W2 => new(_widths[2]);
    public GridLength W3 => new(_widths[3]);
    public GridLength W4 => new(_widths[4]);
    public GridLength W5 => new(_widths[5]);
    public GridLength W6 => new(_widths[6]);
    public GridLength W7 => new(_widths[7]);
    public GridLength W8 => new(_widths[8]);
    public GridLength W9 => new(_widths[9]);
    public GridLength W10 => new(_widths[10]);
    public GridLength W11 => new(_widths[11]);
    public GridLength W12 => new(_widths[12]);
    public GridLength W13 => new(_widths[13]);
    public GridLength W14 => new(_widths[14]);
    public GridLength W15 => new(_widths[15]);
    public GridLength W16 => new(_widths[16]);
    public GridLength W17 => new(_widths[17]);
}
