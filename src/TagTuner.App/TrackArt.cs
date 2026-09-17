using TagTuner.Core.Audio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace TagTuner.App;

/// <summary>
/// Das Cover einer Zeile in der Trackliste.
///
/// Als angehängte Eigenschaft an einem gewöhnlichen <see cref="Image"/> —
/// <c>Image</c> ist versiegelt, ableiten geht nicht. Das Bild wird im
/// Hintergrund geholt und erst, wenn die Zeile sichtbar wird: So bleibt
/// <c>AudioTrack</c> ein reines Datenmodell ohne UI-Typen, und ein Ordner mit
/// tausend Liedern liest beim Öffnen nicht tausend Bilder.
/// </summary>
public static class TrackArt
{
    public static readonly DependencyProperty PathProperty =
        DependencyProperty.RegisterAttached(
            "Path", typeof(string), typeof(TrackArt),
            new PropertyMetadata(null, OnPathChanged));

    public static void SetPath(Image element, string? value) =>
        element.SetValue(PathProperty, value);

    public static string? GetPath(Image element) =>
        (string?)element.GetValue(PathProperty);

    /// <summary>
    /// Was einmal gelesen wurde. Die Liste recycelt ihre Zeilen beim Scrollen;
    /// ohne das hier würde dasselbe Album beim Hoch und Runter jedes Mal neu
    /// von der Platte gelesen.
    /// </summary>
    private static readonly Dictionary<string, ImageSource?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private const int MaxCached = 3000;

    /// <summary>Nicht mehr als eine Handvoll Dateien gleichzeitig anfassen.</summary>
    private static readonly SemaphoreSlim Gate = new(4);

    private static void OnPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Image image) return;
        var path = e.NewValue as string;

        // Sofort leeren: Sonst zeigt eine wiederverwendete Zeile kurz das
        // Cover des Liedes, das vorher an ihrer Stelle stand.
        image.Source = null;
        if (string.IsNullOrEmpty(path)) return;

        if (Cache.TryGetValue(path, out var known))
        {
            image.Source = known;
            return;
        }

        var ui = image.DispatcherQueue;
        _ = Task.Run(async () =>
        {
            byte[]? data = null;
            await Gate.WaitAsync().ConfigureAwait(false);
            try { data = AudioProbe.ReadCover(path)?.Data; }
            catch { }
            finally { Gate.Release(); }

            ui.TryEnqueue(() => Show(image, path, data));
        });
    }

    private static void Show(Image image, string path, byte[]? data)
    {
        ImageSource? picture = null;

        if (data is { Length: > 0 })
        {
            try
            {
                // In der Zeile sind 40 Pixel zu sehen, auf einem
                // hochauflösenden Schirm das Doppelte.
                var bmp = new BitmapImage { DecodePixelWidth = 88 };
                using var ms = new MemoryStream(data);
                bmp.SetSource(ms.AsRandomAccessStream());
                picture = bmp;
            }
            catch { }
        }

        if (Cache.Count >= MaxCached) Cache.Clear();
        Cache[path] = picture;

        // Zwischenzeitlich weitergescrollt — dann gehört das Bild zu einer
        // anderen Zeile und darf hier nicht landen.
        if (string.Equals(GetPath(image), path, StringComparison.OrdinalIgnoreCase))
            image.Source = picture;
    }

    /// <summary>Nach Schreibvorgängen vergessen, was gemerkt wurde.</summary>
    public static void Forget() => Cache.Clear();
}
