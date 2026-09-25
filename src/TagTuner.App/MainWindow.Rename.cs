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

// Umbenennen nach Tags und Tags von Datei zu Datei übertragen.

public sealed partial class MainWindow
{
    // ══ Umbenennen ═══════════════════════════════════════════════

    /// <summary>
    /// Benennt die Dateien des Ordners nach ihren Metadaten.
    ///
    /// Vorher wird gezeigt, was herauskommt: Ein Muster, das man nicht im
    /// Kopf ausrechnen kann, ist sonst ein Sprung ins Wasser, und die Namen
    /// von hundert Dateien wieder herzustellen macht niemandem Freude.
    /// Jede Datei wird gesichert, der Verlauf holt sie zurück.
    /// </summary>
    private async void OnRenameFiles(object sender, RoutedEventArgs e)
    {
        var tracks = ActiveTab.Tracks.ToList();
        if (tracks.Count == 0) return;

        var pattern = new TextBox
        {
            Text = _settings.RenamePattern,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var preview = new TextBlock
        {
            FontSize = 11.5,
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            TextWrapping = TextWrapping.NoWrap,
            Foreground = Res("TextFillColorSecondaryBrush"),
        };

        var summary = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };

        List<(AudioTrack Track, string Target)> plan = [];

        void Recalculate()
        {
            plan = RenamePlan(tracks, pattern.Text);

            var changing = plan.Count;
            summary.Text = changing == 0
                ? Strings.T("Every file already has this name.")
                : Strings.T("{0} of {1} file(s) get a new name.", changing, tracks.Count);

            // Alle, nicht die ersten acht: Wer hundert Dateien umbenennt,
            // will genau die eine sehen können, bei der das Muster daneben
            // greift. Der ScrollViewer darum herum macht es tragbar.
            preview.Text = string.Join(Environment.NewLine,
                plan.Select(p => p.Track.FileName + "  →  " + Path.GetFileName(p.Target)));
        }

        pattern.TextChanged += (_, _) => Recalculate();
        Recalculate();

        // Die Platzhalter als Knöpfe: Ein Klick setzt ihn dort ein, wo die
        // Schreibmarke steht, und ersetzt, was markiert ist.
        var chips = new WrapPanel { HorizontalSpacing = 5, VerticalSpacing = 5 };
        foreach (var placeholder in FileNaming.Placeholders)
        {
            var chip = new Button
            {
                Content = placeholder,
                FontSize = 11.5,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                Padding = new Thickness(8, 3, 8, 4),
                MinWidth = 0,
                MinHeight = 0,
            };
            chip.Click += (_, _) => InsertPlaceholder(pattern, placeholder);
            chips.Children.Add(chip);
        }

        var panel = new StackPanel { Spacing = 10, Width = 460 };
        panel.Children.Add(chips);
        panel.Children.Add(pattern);
        panel.Children.Add(summary);
        panel.Children.Add(new ScrollViewer
        {
            MaxHeight = 300,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = preview,
        });

        var dialog = new ContentDialog
        {
            Title = Strings.T("Rename by metadata"),
            Content = panel,
            PrimaryButtonText = Strings.T("Rename"),
            CloseButtonText = Strings.T("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Root.XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || plan.Count == 0) return;

        // Das Muster, mit dem es zuletzt gut ging, ist das bessere Standardmuster.
        _settings.RenamePattern = pattern.Text;
        _settings.Save();

        await RunRenameAsync(plan);
    }

    /// <summary>
    /// Setzt einen Platzhalter an der Schreibmarke ein und stellt sie dahinter,
    /// damit der nächste Klick oder Tastendruck dort weitermacht.
    /// </summary>
    private static void InsertPlaceholder(TextBox box, string placeholder)
    {
        var start = Math.Clamp(box.SelectionStart, 0, box.Text.Length);
        var length = Math.Clamp(box.SelectionLength, 0, box.Text.Length - start);

        box.Text = box.Text.Remove(start, length).Insert(start, placeholder);
        box.Focus(FocusState.Programmatic);
        box.Select(start + placeholder.Length, 0);
    }

    /// <summary>
    /// Was das Muster aus diesen Tracks macht, ohne die Dateien, die ihren
    /// Namen behalten. Zwei Tracks dürfen nicht auf demselben Namen landen,
    /// darum wandert jeder Treffer gleich in die Liste des Belegten.
    /// </summary>
    private static List<(AudioTrack Track, string Target)> RenamePlan(
        IReadOnlyList<AudioTrack> tracks, string pattern)
    {
        var plan = new List<(AudioTrack, string)>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var track in tracks)
        {
            var folder = Path.GetDirectoryName(track.Path)!;
            var wanted = Path.Combine(folder, FileNaming.Build(track, pattern));

            // Die eigene Datei zählt nicht als Hindernis, sonst bekäme jede
            // schon richtig benannte Datei ein „(2)" verpasst.
            if (string.Equals(wanted, track.Path, StringComparison.OrdinalIgnoreCase))
            {
                taken.Add(track.Path);
                continue;
            }

            var target = FileNaming.Free(wanted, taken);
            taken.Add(target);
            plan.Add((track, target));
        }

        return plan;
    }

    private async Task RunRenameAsync(List<(AudioTrack Track, string Target)> plan)
    {
        ReleaseIfAffected(plan.Select(p => p.Track));
        SetBusy(true, Strings.T("Renaming {0} file(s)", plan.Count));

        var backups = new BackupStore(_settings.ResolvedBackupFolder);
        var files = new List<HistoryFile>();
        var errors = new List<string>();

        for (var i = 0; i < plan.Count; i++)
        {
            var (track, target) = plan[i];
            ProgressStep(i, plan.Count, track.FileName);
            try
            {
                // Erst sichern, dann umbenennen. Der Verlauf legt die Sicherung
                // an den alten Platz zurück und räumt den neuen Namen weg.
                var backup = backups.Create(track.Path);
                File.Move(track.Path, target);
                files.Add(new HistoryFile
                {
                    Original = track.Path,
                    BackupPath = backup,
                    OutputPath = target,
                });
            }
            catch (Exception ex) { errors.Add($"{track.FileName}: {ex.Message}"); }

            ShowProgress(true, (i + 1) * 100.0 / plan.Count);
        }

        if (files.Count > 0)
            _history.Add("rename", Strings.T("{0} file(s) renamed", files.Count), files);

        TrackArt.Reload();
        InvalidateIndex();

        SetBusy(false, null);
        await MergeTabAsync(ActiveTab);
        await ReportAsync(files.Count, errors, []);
    }

    /// <summary>
    /// Ein Bild als Cover für jede Datei des Ordners.
    ///
    /// Zuerst zur Auswahl, was im Ordner schon liegt: In den allermeisten
    /// Fällen trägt eine der Dateien bereits das richtige Cover, und dann ist
    /// der Weg über den Dateiauswahl-Dialog ein Umweg über eine Datei, die es
    /// vielleicht gar nicht mehr gibt.
    /// </summary>
    private void OnCoverForAll(object sender, RoutedEventArgs e)
    {
        var targets = ActiveTab.Tracks.ToList();
        if (targets.Count == 0) return;
        Run(() => CoverForAllAsync(targets));
    }

    private async Task CoverForAllAsync(List<AudioTrack> targets)
    {
        var found = await Task.Run(() => DistinctCovers(targets));

        // Vier Cover je Reihe, genau so breit, dass die Reihe die Fläche
        // füllt: Bei fester Kachelgröße passten nur drei, und rechts blieb
        // ein Streifen leer. Rechts bleibt Platz für die Laufleiste.
        const double width = 460, scrollbar = 12, gap = 8, columns = 4;
        var tile = Math.Floor((width - scrollbar - gap * (columns - 1)) / columns);
        var image = tile - 8;   // Innenabstand und Rand des Knopfes

        var gallery = new WrapPanel { HorizontalSpacing = gap, VerticalSpacing = gap };
        AudioProbe.Cover? picked = null;
        ContentDialog? host = null;

        foreach (var cover in found)
        {
            var picture = new Image { Stretch = Stretch.UniformToFill, Width = image, Height = image };
            try
            {
                var bitmap = new BitmapImage { DecodePixelWidth = (int)(image * 2) };
                using var stream = new MemoryStream(cover.Data);
                bitmap.SetSource(stream.AsRandomAccessStream());
                picture.Source = bitmap;
            }
            catch { continue; }

            var choice = new Button
            {
                Padding = new Thickness(3),
                CornerRadius = new CornerRadius(5),
                Width = tile,
                Height = tile,
                Content = picture,
            };
            ToolTipService.SetToolTip(choice,
                Strings.T("{0} ({1} KB)", ImageInfo.ShortName(cover.MimeType),
                          $"{cover.Data.Length / 1024.0:0.#}"));

            choice.Click += (_, _) => { picked = cover; host?.Hide(); };
            gallery.Children.Add(choice);
        }

        var panel = new StackPanel { Spacing = 12, Width = width };
        panel.Children.Add(new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Res("TextFillColorSecondaryBrush"),
            Text = found.Count == 0
                ? Strings.T("No file in this folder has a cover yet.")
                : Strings.T("Pick one of the covers already in this folder, or choose a file."),
        });

        if (found.Count > 0)
            panel.Children.Add(new ScrollViewer
            {
                MaxHeight = 2 * tile + gap + tile / 2,   // zweieinhalb Reihen: Man sieht, dass es weitergeht
                Padding = new Thickness(0, 0, scrollbar, 0),
                Content = gallery,
            });

        var dialog = new ContentDialog
        {
            Title = Strings.T("Set cover for {0}…", targets.Count),
            Content = panel,
            PrimaryButtonText = Strings.T("Choose a file…"),
            CloseButtonText = Strings.T("Cancel"),
            DefaultButton = found.Count == 0
                ? ContentDialogButton.Primary : ContentDialogButton.Close,
            XamlRoot = Root.XamlRoot,
        };
        host = dialog;

        var answer = await dialog.ShowAsync();

        // Ein Klick auf ein Bild schließt über Hide(), das None liefert.
        if (picked is { Data.Length: > 0 } chosen)
        {
            await ApplyImageAsync(targets, chosen.Data, Strings.T("Set cover"), keepExact: true);
            return;
        }

        if (answer == ContentDialogResult.Primary) await SetFromFileAsync(targets);
    }

    /// <summary>
    /// Die verschiedenen Cover eines Ordners.
    ///
    /// Verglichen wird über die Bytes, nicht über das Bild: Zwei Dateien mit
    /// demselben Cover sollen einmal erscheinen, und ein Vergleich Bild für
    /// Bild wäre bei zwanzig Titeln zwanzigmal dekodieren.
    /// </summary>
    private static List<AudioProbe.Cover> DistinctCovers(IReadOnlyList<AudioTrack> tracks)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var found = new List<AudioProbe.Cover>();

        // Ein Album hat selten mehr als eine Handvoll verschiedener Bilder,
        // und ein rekursiver Ordner kann tausende Dateien haben.
        foreach (var track in tracks.Where(t => t.HasCover).Take(300))
        {
            AudioProbe.Cover? cover = null;
            try { cover = AudioProbe.ReadCover(track.Path); } catch { }
            if (cover is not { Data.Length: > 0 }) continue;

            var mark = cover.Data.Length + ":" + Convert.ToHexString(
                System.Security.Cryptography.MD5.HashData(cover.Data));

            if (!seen.Add(mark)) continue;
            found.Add(cover);

            if (found.Count >= 24) break;
        }

        return found;
    }

    // ══ Tags übertragen ══════════════════════════════════════════

    /// <summary>
    /// Die Tags der zuletzt kopierten Datei, samt Cover.
    ///
    /// Bewusst nicht über die Windows-Zwischenablage: Dort landete entweder
    /// Text, den niemand zurücklesen kann, oder ein eigenes Format, das
    /// außerhalb der App ohnehin niemand versteht.
    /// </summary>
    private (AudioTrack Track, AudioProbe.Cover? Art)? _tagClip;

    private void OnCopyTags(object? sender, AudioTrack track)
    {
        AudioProbe.Cover? art = null;
        try { art = AudioProbe.ReadCover(track.Path); } catch { }

        _tagClip = (track, art);
        PaneA.TagsCopied = PaneB.TagsCopied = true;

        StatusText.Text = Strings.T("Tags copied from \"{0}\"", track.FileName);
    }

    private async void OnPasteTags(object? sender, IReadOnlyList<AudioTrack> targets)
    {
        if (_tagClip is not { } clip || targets.Count == 0) return;

        var everything = _settings.TagPasteMode == "all";

        // Die Datei, aus der kopiert wurde, noch einmal zu beschreiben wäre
        // ein Schreibvorgang ohne Wirkung, samt Sicherung und Verlaufseintrag.
        var write = targets
            .Where(t => !string.Equals(t.Path, clip.Track.Path, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (write.Count == 0)
        {
            StatusText.Text = Strings.T("That is the file the tags came from.");
            return;
        }

        var edit = BuildTagCopy(clip.Track, clip.Art, everything);

        var fields = string.Join(", ", CopiedFields(everything));
        var text = Strings.T("From \"{0}\": {1}", clip.Track.FileName, fields)
                 + Environment.NewLine + Environment.NewLine
                 + Strings.T("{0} file(s) will be changed. Each one is backed up first.",
                             write.Count);

        if (!await Confirm(Strings.T("Paste tags"), text, Strings.T("Apply"))) return;

        await RunJobAsync(Strings.T("Paste tags"), write, _ => (false, null, null, edit));
    }

    /// <summary>
    /// Was übernommen wird. Titel und Track-Nummer sind je Datei verschieden:
    /// Sie mitzuschreiben macht aus einem Album zwölfmal dasselbe Lied, und
    /// genau deshalb ist „ohne Titel und Nummer" die Vorgabe.
    /// </summary>
    private static TagEdit BuildTagCopy(AudioTrack from, AudioProbe.Cover? art, bool everything) => new()
    {
        Artist = from.Artist,
        Album = from.Album,
        AlbumArtist = from.AlbumArtist,
        Genre = from.Genre,
        Composer = from.Composer,
        Comment = from.Comment,
        Year = from.Year,
        Disc = from.Disc,

        Title = everything ? from.Title : null,
        Track = everything ? from.Track : null,

        // Leeres Array hieße „Cover entfernen". Hat die Quelle keines, bleibt
        // das Cover des Ziels stehen, statt gelöscht zu werden.
        Cover = art is { Data.Length: > 0 } ? art.Data : null,
        CoverMimeType = art?.MimeType,
    };

    private static IEnumerable<string> CopiedFields(bool everything)
    {
        if (everything)
        {
            yield return Strings.T("Title");
            yield return Strings.T("Track");
        }
        yield return Strings.T("Artist");
        yield return Strings.T("Album");
        yield return Strings.T("Album artist");
        yield return Strings.T("Year");
        yield return Strings.T("Disc");
        yield return Strings.T("Genre");
        yield return Strings.T("Composer");
        yield return Strings.T("Comment");
        yield return Strings.T("Cover");
    }
}
