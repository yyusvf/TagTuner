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

// Cover setzen, kopieren, einfügen, zuschneiden.

public sealed partial class MainWindow
{
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

        var scope = targets.Count.ToString();
        var many = targets.Count > 1;
        var hasCover = _cover is not null;

        var menu = new MenuFlyout();

        menu.Items.Add(Item("\uEB9F", many ? Strings.T("Set cover for {0}…", scope) : Strings.T("Set cover…"),
            true, () => Run(() => SetFromFileAsync(targets))));

        // Dieselbe Auswahl wie „Cover für alle setzen": alle Cover, die im
        // Ordner schon vorkommen. Gesetzt wird aber nur für das, worauf sich
        // die Spalte gerade bezieht.
        var folder = ActiveTab.Tracks.ToList();
        menu.Items.Add(Item("", Strings.T("Choose from this folder…"),
            folder.Any(t => t.HasCover), () => Run(() => CoverForAllAsync(targets, folder))));

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
}
