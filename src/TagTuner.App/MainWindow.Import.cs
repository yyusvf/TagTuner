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

// Dateien ablegen, verschieben und von Hand umsortieren.

public sealed partial class MainWindow
{
    // ══ Ablegen und Verschieben ══════════════════════════════════

    private async void OnPaneFilesDropped(object? sender, FilesDroppedArgs args)
    {
        if (args.Pane.Tab is not { Analysis: not null, Target: not null } tab)
        {
            // Der Ordner wird gerade noch gelesen. Vorher weiß niemand, auf
            // welches Format angeglichen und welche Tags übernommen werden
            // sollen. Früher fiel das Ablegen hier wortlos unter den Tisch.
            StatusText.Text = Strings.T("The folder is still being read, try again in a moment.");
            return;
        }

        await ImportAsync(tab, args.Paths, args.Index, null);
    }

    /// <summary>Tracks aus der anderen Hälfte — verschieben, mit Strg kopieren.</summary>
    private async void OnPaneTracksMoved(object? sender, TracksMovedArgs args)
    {
        var to = args.To.Tab;
        var from = args.From.Tab;
        if (from is null) return;

        // Innerhalb desselben Ordners ist das Umsortieren, nicht Verschieben.
        if (to is not null && string.Equals(to.Path, from.Path, StringComparison.OrdinalIgnoreCase))
            return;

        if (to is not { Analysis: not null, Target: not null })
        {
            StatusText.Text = Strings.T("The folder is still being read, try again in a moment.");
            return;
        }

        await ImportAsync(to, args.Tracks.Select(t => t.Path).ToList(), args.Index,
                          args.Copy ? null : from);
    }

    /// <summary>
    /// Bringt Dateien in einen Ordner: kopieren, an dessen Ziel angleichen,
    /// die Tags übernehmen, auf die sich der Ordner einig ist, und an der
    /// gewünschten Stelle einsortieren.
    ///
    /// <paramref name="removeFrom"/> gesetzt heißt verschieben: die Quellen
    /// werden danach gesichert und entfernt, sodass der Verlauf sie
    /// zurückholen kann.
    /// </summary>
    private async Task ImportAsync(
        FolderTab tab, IReadOnlyList<string> paths, int index, FolderTab? removeFrom)
    {
        var analysis = tab.Analysis!;
        var target = tab.Target!;
        // Album-Modus: Wer die Übernahme anhat, will einen Ordner, der sich
        // wie ein Album verhält — auch wenn er gerade noch uneinheitlich ist.
        var inherited = analysis.Inherited(byMajority: _settings.RuleFor(tab.Path).AlbumMode);
        var move = removeFrom is not null;

        // Was dieser Ordner an Automatik erlaubt. Ist beides aus, werden die
        // Dateien nur hereinkopiert und sonst nicht angefasst.
        var rule = _settings.RuleFor(tab.Path);

        var incoming = paths.Select(AudioProbe.Read).OfType<AudioTrack>().ToList();
        if (incoming.Count == 0) return;

        var needConvert = rule.AutoConform
            ? incoming.Where(t => !FolderAnalysis.Matches(t, target)).ToList()
            : [];

        var existing = tab.Tracks.ToList();
        var at = Math.Clamp(index, 0, existing.Count);

        // Die Track-Nummer ist ein Tag. Wer die Übernahme abgeschaltet hat,
        // will auch nicht, dass Nummern vergeben oder verschoben werden.
        //
        // Ist sie an, landet die Datei dort, wo sie abgelegt wurde, und der
        // Ordner wird anschließend von 1 an durchnummeriert. Vorher hing das
        // Einsortieren daran, dass der Ordner schon lückenlos nummeriert war;
        // andernfalls bekam die Datei die nächste freie Nummer und sprang beim
        // nächsten Einlesen ans Ende — sichtbar woanders hin, als man sie
        // abgelegt hatte.
        var renumber = rule.WritesNumbers;

        if (!_settings.SkipConformDialog)
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine(Strings.T(move ? "Move {0} file(s) to \"{1}\"."
                                           : "Take {0} file(s) into \"{1}\".",
                                      incoming.Count, tab.Name)).AppendLine();
            text.AppendLine(!rule.AutoConform
                ? Strings.T("Aligning is off for this folder. Format and sample rate stay.")
                : needConvert.Count > 0
                    ? Strings.T("Conversion: {0} file(s) → {1} · {2}",
                                needConvert.Count, target.Format, FormatRate(target.SampleRate))
                    : Strings.T("No conversion needed."));

            if (!rule.WritesBaseTags)
            {
                text.AppendLine(Strings.T(
                    "Tags are left alone, switched off for this folder."));
            }
            else
            {
                var erben = new List<string>();
                if (inherited.Album is not null) erben.Add(Strings.T("Album"));
                if (inherited.Artist is not null) erben.Add(Strings.T("Artist"));
                if (inherited.AlbumArtist is not null) erben.Add(Strings.T("Album artist"));
                if (inherited.Year is not null) erben.Add(Strings.T("Year"));
                if (inherited.Genre is not null) erben.Add(Strings.T("Genre"));
                text.AppendLine(erben.Count > 0
                    ? Strings.T("Inherited from the folder: {0}", string.Join(", ", erben))
                    : existing.Count == 0
                        ? Strings.T("The folder is empty, so there is nothing to inherit from.")
                        : Strings.T("The folder agrees on no tag, so nothing is inherited."));
            }

            if (rule.WritesNumbers)
            {
                text.AppendLine(existing.Count == 0
                    ? Strings.T("The folder is empty, numbering starts at 1.")
                    : at >= existing.Count
                        ? Strings.T("Appended at the end as track {0}.", existing.Count + 1)
                        : Strings.T("Inserted from track {0} on; the ones after it move up.",
                                    at + 1));
            }

            if (target.FromDefaultProfile)
                text.AppendLine().AppendLine(Strings.T(
                    "⚠ The target folder is mixed, the target comes from the default profile."));

            var down = needConvert.Count(t => t.SampleRate > target.SampleRate);
            if (down > 0)
                text.AppendLine().AppendLine(
                    Strings.T("⚠ {0} file(s) are downsampled, which cannot be undone "
                              + "without loss.", down));

            text.AppendLine().AppendLine(move
                ? Strings.T("The originals in \"{0}\" are removed; the history can bring "
                            + "them back.", removeFrom!.Name)
                : Strings.T("The source files are kept (copy)."));

            if (!await Confirm(Strings.T(move ? "Move files" : "Align files"),
                               text.ToString().TrimEnd(),
                               Strings.T(move ? "Move" : "Apply"))) return;
        }

        // Beim Verschieben verschwinden die Quelldateien — die darf der
        // Player dann nicht mehr offen halten.
        if (move) ReleaseIfAffected(incoming);

        SetBusy(true, Strings.T(move ? "Move {0} file(s)" : "Take in {0} file(s)",
                                incoming.Count));

        var ffmpeg = FfmpegLocator.Find(_settings.FfmpegPath);
        var backups = new BackupStore(_settings.ResolvedBackupFolder);
        var svc = new ConversionService(new FfmpegRunner(ffmpeg ?? "ffmpeg"), backups);

        var files = new List<HistoryFile>();
        var errors = new List<string>();
        var notes = new List<string>();

        // Das Cover des Ordners, einmal gelesen statt je Datei. Die erste
        // Datei, die eines trägt, gibt es vor — in einer Veröffentlichung
        // haben ohnehin alle dasselbe.
        AudioProbe.Cover? folderCover = null;
        if (rule.WritesCover)
        {
            foreach (var candidate in existing.Where(t => t.HasCover))
            {
                try { folderCover = AudioProbe.ReadCover(candidate.Path); } catch { }
                if (folderCover is { Data.Length: > 0 }) break;
            }
        }

        // Die Nummer der ersten hereinkommenden Datei ist ihre Stelle in der
        // Liste. Ohne Nummerierung wird gar keine geschrieben.
        var number = (uint)(at + 1);
        var done = 0;

        for (var i = 0; i < incoming.Count; i++)
        {
            var src = incoming[i];
            var tags = DropTags(src, rule, inherited, folderCover, number);

            // Erst in den Zielordner holen, dann dort angleichen — die Quelle
            // bleibt bis zum Schluss unangetastet.
            var dest = UniquePath(Path.Combine(tab.Path, Path.GetFileName(src.Path)));
            try { File.Copy(src.Path, dest); }
            catch (Exception ex) { errors.Add($"{src.FileName}: {ex.Message}"); continue; }

            var copied = AudioProbe.Read(dest);
            if (copied is null)
            {
                errors.Add($"{src.FileName}: " + Strings.T("not readable"));
                continue;
            }

            ConversionOutcome outcome;
            if (!rule.AutoConform || FolderAnalysis.Matches(copied, target))
            {
                // Nichts zu konvertieren, und womöglich auch nichts zu
                // schreiben — dann ist das Hereinkopieren schon alles.
                outcome = tags is null
                    ? new ConversionOutcome(true, copied, null, null, [])
                    : await Task.Run(() => svc.WriteTagsOnly(copied, tags));
            }
            else
            {
                var slot = i;
                outcome = await Task.Run(() => svc.ConvertAsync(new ConversionRequest
                {
                    Track = copied,
                    Options = new EncodeOptions
                    {
                        Format = target.Format,
                        SampleRate = target.SampleRate,
                        },
                    Tags = tags,
                }, pct => DispatcherQueue.TryEnqueue(() =>
                    ShowProgress(true, (slot * 100 + pct) / (double)incoming.Count))));
            }

            if (outcome.Success)
            {
                if (outcome.History is not null) files.Add(outcome.History);
                foreach (var n in outcome.Notes) notes.Add($"{src.FileName}: {n}");
                number++;
                done++;

                if (move) RemoveSource(src, backups, files, errors);
            }
            else
            {
                errors.Add($"{src.FileName}: {outcome.Error}");
                try { if (File.Exists(dest)) File.Delete(dest); } catch { }
            }

            ShowProgress(true, (i + 1) * 100.0 / incoming.Count);
        }

        // Erst jetzt die schon vorhandenen Dateien auf ihre neue Nummer
        // bringen — um genau so viele, wie tatsächlich ankamen. Geschrieben
        // wird nur, was sich wirklich ändert: Hängt man hinten an, ist das
        // nichts, und es entsteht keine einzige überflüssige Sicherung.
        if (renumber && done > 0)
        {
            var fixes = new List<(AudioTrack Track, uint Number)>();
            for (var i = 0; i < existing.Count; i++)
            {
                var wanted = (uint)(i < at ? i + 1 : i + 1 + done);
                if (existing[i].Track != wanted) fixes.Add((existing[i], wanted));
            }

            if (fixes.Count > 0)
            {
                ReleaseIfAffected(fixes.Select(f => f.Track));
                await WriteNumbersAsync(fixes, svc, files, errors);
            }
        }

        if (files.Count > 0)
            _history.Add(move ? "move" : "import",
                         Strings.T(move ? "{0} file(s) moved to \"{1}\""
                                        : "{0} file(s) taken into \"{1}\"",
                                   done, tab.Name),
                         files);

        TrackArt.Reload();
        InvalidateIndex();

        SetBusy(false, null);
        await MergeTabAsync(tab);
        if (removeFrom is not null) await MergeTabAsync(removeFrom);
        await ReportAsync(done, errors, notes);
    }

    /// <summary>
    /// Was eine hereinkommende Datei vom Ordner übernimmt.
    ///
    /// Jeder Unterpunkt des Album-Modus schreibt genau seinen Teil. Was
    /// er nicht abdeckt, bleibt null und damit unangetastet — so kann man
    /// etwa Nummern vergeben lassen, ohne dass Tags angefasst werden.
    /// </summary>
    private static TagEdit? DropTags(
        AudioTrack src, FolderRule rule, InheritedTags inherited,
        AudioProbe.Cover? cover, uint number)
    {
        if (!rule.AlbumMode) return null;

        var edit = new TagEdit
        {
            // Ein leerer Titel ist keiner. Der Dateiname ist zwar geraten,
            // aber immer noch besser als eine Zeile ohne Beschriftung.
            Title = rule.BaseTags && string.IsNullOrWhiteSpace(src.Title)
                ? Path.GetFileNameWithoutExtension(src.Path) : null,

            Album = rule.BaseTags ? inherited.Album : null,
            Artist = rule.BaseTags ? ArtistFor(src.Artist, inherited.Artist) : null,
            AlbumArtist = rule.BaseTags ? inherited.AlbumArtist : null,
            Genre = rule.BaseTags ? inherited.Genre : null,
            Year = rule.BaseTags && uint.TryParse(inherited.Year, out var y) ? y : null,
            Disc = rule.BaseTags && uint.TryParse(inherited.Disc, out var d) ? d : null,

            Track = rule.Numbering ? number : null,

            Cover = rule.Cover && cover is { Data.Length: > 0 } ? cover.Data : null,
            CoverMimeType = rule.Cover && cover is { Data.Length: > 0 } ? cover.MimeType : null,
        };

        return edit.IsEmpty ? null : edit;
    }

    private static string? ArtistFor(string own, string? folder) =>
        AlbumPlanner.ArtistFor(own, folder);

    /// <summary>
    /// Entfernt eine verschobene Quelldatei — vorher gesichert, damit der
    /// Verlauf sie an ihren alten Platz zurücklegen kann.
    /// </summary>
    private static void RemoveSource(
        AudioTrack src, BackupStore backups, List<HistoryFile> files, List<string> errors)
    {
        try
        {
            var backup = backups.Create(src.Path);
            File.Delete(src.Path);
            files.Add(new HistoryFile { Original = src.Path, BackupPath = backup });
        }
        catch (Exception ex)
        {
            errors.Add($"{src.FileName}: Original blieb liegen ({ex.Message})");
        }
    }

    /// <summary>Schiebt die Tracks ab der Einfügestelle um <paramref name="by"/> nach hinten.</summary>
    /// <summary>
    /// Schreibt Track-Nummern. Von hinten nach vorn, wenn es aufwärts geht,
    /// damit unterwegs nie zwei Dateien dieselbe Nummer tragen.
    /// </summary>
    private static async Task WriteNumbersAsync(
        List<(AudioTrack Track, uint Number)> plan, ConversionService svc,
        List<HistoryFile> files, List<string> errors)
    {
        var rising = plan.Any(p => p.Number > p.Track.Track);
        var order = rising ? Enumerable.Reverse(plan) : plan;

        foreach (var (track, number) in order)
        {
            var outcome = await Task.Run(
                () => svc.WriteTagsOnly(track, new TagEdit { Track = number }));

            if (outcome.Success)
            {
                if (outcome.History is not null) files.Add(outcome.History);
            }
            else errors.Add($"{track.FileName}: "
                            + Strings.T("number not moved ({0})", outcome.Error));
        }
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
        return path;
    }

    // ══ Umsortieren ══════════════════════════════════════════════

    /// <summary>
    /// Track-Nummern nach dem Ziehen neu vergeben.
    ///
    /// Maßgeblich ist allein der Schalter „Tags vom Ordner übernehmen": Er
    /// sagt, dass dieser Ordner ein Album ist und seine Nummern der
    /// Reihenfolge folgen sollen. Früher hing es zusätzlich daran, ob der
    /// Ordner schon lückenlos von 1 an nummeriert war — bei einer
    /// Teilauswahl (Tracks 1, 2, 5, 11 …) geschah dann gar nichts, und das
    /// Ziehen blieb folgenlos.
    /// </summary>
    private async void OnPaneReordered(object? sender, TrackPane pane)
    {
        var tab = pane.Tab;
        if (tab is null) return;

        var order = tab.Tracks.ToList();

        // Mit Disc-Zeilen zählt jede Disc von 1, und ein Lied, das in eine
        // andere Disc gezogen wurde, bekommt deren Nummer.
        var sections = pane.ReorderedSections;
        if (sections is not null) order = sections.Select(x => x.Track).ToList();

        if (!_settings.RuleFor(tab.Path).WritesNumbers)
        {
            StatusText.Text = Strings.T("Order changed. Track numbers unchanged "
                + "(track numbering is off for this folder)");
            return;
        }

        // Ohne Rückfrage: Wer eine Zeile zieht, hat die neue Reihenfolge
        // gemeint. Ein Dialog nach jedem Zug wäre im Weg, und der Verlauf
        // holt es zurück, falls es doch daneben ging.
        ReleaseIfAffected(order);
        SetBusy(true, Strings.T("Writing track numbers"));
        var svc = new ConversionService(
            new FfmpegRunner("ffmpeg"), new BackupStore(_settings.ResolvedBackupFolder));
        var files = new List<HistoryFile>();

        var perDisc = new Dictionary<uint, uint>();

        // Ohne Disc-Zeilen dieselbe Zählung wie beim Anwenden auf vorhandene
        // Dateien: mit mehreren Discs jede für sich.
        var plain = AlbumPlanner.Numbers(order);

        for (var i = 0; i < order.Count; i++)
        {
            uint wanted;
            uint? disc = null;

            if (sections is not null)
            {
                var d = sections[i].Disc;
                wanted = perDisc[d] = perDisc.GetValueOrDefault(d) + 1;
                if (order[i].Disc != d) disc = d;
            }
            else
            {
                wanted = plain[i];
            }

            if (order[i].Track == wanted && disc is null) continue;
            var edit = new TagEdit { Track = wanted, Disc = disc };
            var outcome = await Task.Run(() => svc.WriteTagsOnly(order[i], edit));
            if (outcome.Success && outcome.History is not null) files.Add(outcome.History);
            ShowProgress(true, (i + 1) * 100.0 / order.Count);
        }

        if (files.Count > 0) _history.Add("tracknumbers", Strings.T("Order changed"), files);
        SetBusy(false, null);
        await MergeTabAsync(tab);
        StatusText.Text = Strings.T("{0} track number(s) written", files.Count);
    }
}
