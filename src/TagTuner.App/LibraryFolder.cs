using System.ComponentModel;
using TagTuner.Core.Folders;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace TagTuner.App;

/// <summary>
/// Ein Ordner, wie ihn der Baum zeigt: Name plus Cover.
///
/// Das Cover kommt nachträglich. Es beim Aufklappen synchron zu lesen würde
/// den Baum bei jedem Klick stocken lassen — stattdessen erscheint erst der
/// Name, und das Bild schiebt sich nach, sobald es da ist.
/// </summary>
public sealed class LibraryFolder(FolderEntry entry) : INotifyPropertyChanged
{
    public string Path { get; } = entry.Path;
    public string Name { get; } = entry.Name;

    /// <summary>Selbst hinzugefügt — die werden entfernt statt ausgeblendet.</summary>
    public bool IsCustomRoot { get; init; }

    /// <summary>Oberste Ebene im Baum. Nur solche lassen sich aus der Bibliothek nehmen.</summary>
    public bool IsRoot { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private ImageSource? _cover;
    public ImageSource? Cover
    {
        get => _cover;
        private set
        {
            _cover = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Cover)));
        }
    }

    private string _artist = "";

    /// <summary>Der Interpret des Ordners, oder leer wenn es keinen eindeutigen gibt.</summary>
    public string Artist
    {
        get => _artist;
        private set
        {
            _artist = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Artist)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasArtist)));
        }
    }

    /// <summary>Damit die Zeile ohne Interpret nicht eine leere Zeile Platz verschenkt.</summary>
    public Visibility HasArtist => _artist.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Was ein Ordner an Cover hat — null heißt „nachgesehen, keins da".
    ///
    /// Gespeichert wird das fertig verkleinerte Bild, nicht die Rohdaten:
    /// Album-Cover sind gern ein bis zwei Megabyte groß, und die dauerhaft
    /// für jeden je aufgeklappten Ordner zu halten wäre ein Leck. Das
    /// dekodierte Bild ist ein paar Kilobyte.
    /// </summary>
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Der ermittelte Interpret je Ordner — das Zählen lohnt nur einmal.</summary>
    private static readonly Dictionary<string, string> Artists = new(StringComparer.OrdinalIgnoreCase);

    private const int MaxCached = 2000;

    /// <summary>
    /// Nur wenige Ordner gleichzeitig lesen. Ein Verzeichnis mit zweihundert
    /// Unterordnern würde sonst zweihundert Dateizugriffe auf einmal auslösen.
    /// </summary>
    private static readonly SemaphoreSlim Gate = new(3);

    private bool _started;

    /// <summary>
    /// Holt Cover und Interpret. Beides kostet Plattenzugriffe und läuft
    /// deshalb im selben Hintergrunddurchgang, gedrosselt wie das Cover.
    /// </summary>
    public void BeginLoad(DispatcherQueue ui)
    {
        if (_started) return;
        _started = true;

        var haveCover = Cache.TryGetValue(Path, out var knownCover);
        var haveArtist = Artists.TryGetValue(Path, out var knownArtist);

        if (haveCover) Cover = knownCover;
        if (haveArtist) Artist = knownArtist!;
        if (haveCover && haveArtist) return;

        _ = Task.Run(async () =>
        {
            byte[]? data = null;
            string? artist = null;

            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!haveCover) data = FolderCover.Read(Path);
                if (!haveArtist) artist = FolderArtist.Of(Path);
            }
            catch { }
            finally { Gate.Release(); }

            ui.TryEnqueue(() =>
            {
                if (!haveCover) Show(data);
                if (!haveArtist) ShowArtist(artist ?? "");
            });
        });
    }

    private void ShowArtist(string artist)
    {
        if (Artists.Count >= MaxCached) Artists.Clear();
        Artists[Path] = artist;
        Artist = artist;
    }

    /// <summary>
    /// Dekodieren und merken — auf dem UI-Faden, weil BitmapImage dort
    /// entsteht. Damit ist auch der Zugriff auf den Cache einfädig.
    /// </summary>
    private void Show(byte[]? data)
    {
        ImageSource? image = null;

        if (data is not null)
        {
            try
            {
                // Klein dekodieren: In einer Baumzeile sind 42 Pixel zu sehen,
                // auf einem hochaufloesenden Schirm das Doppelte. Ein 3000er
                // Scan im Speicher waere reine Verschwendung.
                var bmp = new BitmapImage { DecodePixelWidth = 96 };
                using var ms = new MemoryStream(data);
                bmp.SetSource(ms.AsRandomAccessStream());
                image = bmp;
            }
            catch { }
        }

        // Grob begrenzen statt zu verwalten: Wer so viele Ordner aufklappt,
        // verkraftet es, dass die Bilder danach einmal neu gelesen werden.
        if (Cache.Count >= MaxCached) Cache.Clear();
        Cache[Path] = image;

        Cover = image;
    }

    public override string ToString() => Name;
}
