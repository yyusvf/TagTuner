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
/// </summary>
public sealed class TrackColumnLayout : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private static readonly double[] Defaults = [280, 190, 62, 78, 56];
    private const double Min = 40;
    private const double Max = 900;

    private readonly double[] _widths = [.. Defaults];

    public GridLength Title => new(_widths[0]);
    public GridLength Album => new(_widths[1]);
    public GridLength Format => new(_widths[2]);
    public GridLength Rate => new(_widths[3]);
    public GridLength Duration => new(_widths[4]);

    // Der Interpret steht in der Zeile unter dem Titel und hat darum keine
    // eigene Spalte mehr.
    private static readonly string[] Names =
        [nameof(Title), nameof(Album), nameof(Format), nameof(Rate), nameof(Duration)];

    public int Count => _widths.Length;

    public double WidthAt(int index) => _widths[index];

    public void Resize(int index, double width)
    {
        if (index < 0 || index >= _widths.Length) return;

        var clamped = Math.Clamp(width, Min, Max);
        if (Math.Abs(_widths[index] - clamped) < 0.5) return;

        _widths[index] = clamped;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Names[index]));
    }

    public double[] ToArray() => [.. _widths];

    /// <summary>Übernimmt gespeicherte Breiten; ältere oder kaputte Stände werden ignoriert.</summary>
    public void Restore(double[]? saved)
    {
        if (saved is null || saved.Length != _widths.Length) return;
        for (var i = 0; i < saved.Length; i++)
        {
            if (double.IsFinite(saved[i])) _widths[i] = Math.Clamp(saved[i], Min, Max);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Names[i]));
        }
    }
}
