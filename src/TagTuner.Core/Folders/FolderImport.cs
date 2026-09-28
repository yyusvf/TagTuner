using TagTuner.Core.Audio;
using TagTuner.Core.Metadata;
using TagTuner.Core.Model;
using TagTuner.Core.Safety;
using TagTuner.Core.Settings;

namespace TagTuner.Core.Folders;

/// <summary>Was hereinkommt und wohin.</summary>
/// <param name="Existing">Die Dateien, die schon im Ordner liegen, in Playlist-Reihenfolge.</param>
/// <param name="Index">Die Stelle in <paramref name="Existing"/>, an der abgelegt wurde.</param>
/// <param name="Move">Verschieben statt kopieren: die Quellen werden danach gesichert und entfernt.</param>
public sealed record ImportRequest(
    string Folder,
    IReadOnlyList<AudioTrack> Existing,
    IReadOnlyList<AudioTrack> Incoming,
    int Index,
    bool Move,
    FolderRule Rule,
    FolderTarget Target,
    InheritedTags Inherited)
{
    public int At => Math.Clamp(Index, 0, Existing.Count);

    /// <summary>Was angeglichen werden muss, wenn der Ordner das will.</summary>
    public List<AudioTrack> NeedConvert =>
        Rule.AutoConform ? [.. Incoming.Where(t => !FolderAnalysis.Matches(t, Target))] : [];

    /// <summary>
    /// Baut die Anfrage für einen Ordner. Mit Album-Modus wird nach Mehrheit
    /// geerbt: wer ihn anhat, will einen Ordner, der sich wie ein Album
    /// verhält, auch wenn er gerade noch uneinheitlich ist.
    /// </summary>
    public static ImportRequest For(string folder, IReadOnlyList<AudioTrack> existing,
        IReadOnlyList<AudioTrack> incoming, int index, bool move, AppSettings settings)
    {
        var rule = settings.RuleFor(folder);
        var analysis = FolderAnalysis.Of(existing);
        return new ImportRequest(folder, existing, incoming, index, move, rule,
            analysis.ResolveTarget(settings.DefaultFormat, settings.DefaultSampleRate),
            analysis.Inherited(byMajority: rule.AlbumMode));
    }
}

public sealed record ImportResult(int Done, List<HistoryFile> Files, List<string> Errors, List<string> Notes);

/// <summary>
/// Bringt Dateien in einen Ordner: kopieren, an dessen Ziel angleichen, die
/// Tags übernehmen, auf die sich der Ordner einig ist, und an der
/// gewünschten Stelle einsortieren. Stand bis zur Mac-App in der
/// Windows-Oberfläche (MainWindow.Import.cs), die es noch selbst tut.
/// </summary>
public static class FolderImport
{
    /// <summary>
    /// Was vor dem Ablegen gesagt wird: was umgewandelt, was geerbt, wohin
    /// nummeriert wird und was mit den Quellen geschieht.
    /// </summary>
    public static List<string> Describe(ImportRequest r)
    {
        var lines = new List<string>
        {
            Strings.T(r.Move ? "Move {0} file(s) to \"{1}\"." : "Take {0} file(s) into \"{1}\".",
                      r.Incoming.Count, Path.GetFileName(r.Folder.TrimEnd(Path.DirectorySeparatorChar))),
        };

        var convert = r.NeedConvert;
        lines.Add(!r.Rule.AutoConform
            ? Strings.T("Aligning is off for this folder. Format and sample rate stay.")
            : convert.Count > 0
                ? Strings.T("Conversion: {0} file(s) → {1} · {2}", convert.Count, r.Target.Format, Rate(r.Target.SampleRate))
                : Strings.T("No conversion needed."));

        if (!r.Rule.WritesBaseTags)
        {
            lines.Add(Strings.T("Tags are left alone, switched off for this folder."));
        }
        else
        {
            var inherited = new List<string>();
            if (r.Inherited.Album is not null) inherited.Add(Strings.T("Album"));
            if (r.Inherited.Artist is not null) inherited.Add(Strings.T("Artist"));
            if (r.Inherited.AlbumArtist is not null) inherited.Add(Strings.T("Album artist"));
            if (r.Inherited.Year is not null) inherited.Add(Strings.T("Year"));
            if (r.Inherited.Genre is not null) inherited.Add(Strings.T("Genre"));
            lines.Add(inherited.Count > 0
                ? Strings.T("Inherited from the folder: {0}", string.Join(", ", inherited))
                : r.Existing.Count == 0
                    ? Strings.T("The folder is empty, so there is nothing to inherit from.")
                    : Strings.T("The folder agrees on no tag, so nothing is inherited."));
        }

        if (r.Rule.WritesNumbers)
        {
            lines.Add(r.Existing.Count == 0
                ? Strings.T("The folder is empty, numbering starts at 1.")
                : r.At >= r.Existing.Count
                    ? Strings.T("Appended at the end as track {0}.", r.Existing.Count + 1)
                    : Strings.T("Inserted from track {0} on; the ones after it move up.", r.At + 1));
        }

        if (r.Rule.AutoConform && r.Target.FromDefaultProfile && convert.Count > 0)
            lines.Add(Strings.T("⚠ The target folder is mixed, the target comes from the default profile."));

        var down = convert.Count(t => t.SampleRate > r.Target.SampleRate);
        if (down > 0)
            lines.Add(Strings.T("⚠ {0} file(s) are downsampled, which cannot be undone without loss.", down));

        lines.Add(r.Move
            ? Strings.T("The originals in \"{0}\" are removed; the history can bring them back.",
                        Path.GetFileName(Path.GetDirectoryName(r.Incoming[0].Path) ?? ""))
            : Strings.T("The source files are kept (copy)."));
        return lines;
    }

    /// <summary>
    /// Was eine hereinkommende Datei vom Ordner übernimmt. Jeder Unterpunkt
    /// des Album-Modus schreibt genau seinen Teil; was er nicht abdeckt,
    /// bleibt null und damit unangetastet.
    /// </summary>
    public static TagEdit? DropTags(AudioTrack src, FolderRule rule, InheritedTags inherited,
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
            Artist = rule.BaseTags ? AlbumPlanner.ArtistFor(src.Artist, inherited.Artist) : null,
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

    /// <param name="progress">Datei (ab 0) und Prozent darin.</param>
    public static async Task<ImportResult> RunAsync(
        ImportRequest r, ConversionService svc, BackupStore backups,
        Action<int, int>? progress = null, CancellationToken ct = default)
    {
        var files = new List<HistoryFile>();
        var errors = new List<string>();
        var notes = new List<string>();
        var rule = r.Rule;

        // Das Cover des Ordners, einmal gelesen statt je Datei. Die erste
        // Datei, die eines trägt, gibt es vor.
        AudioProbe.Cover? folderCover = null;
        if (rule.WritesCover)
        {
            foreach (var candidate in r.Existing.Where(t => t.HasCover))
            {
                try { folderCover = AudioProbe.ReadCover(candidate.Path); } catch { }
                if (folderCover is { Data.Length: > 0 }) break;
            }
        }

        // Die Nummer der ersten hereinkommenden Datei ist ihre Stelle in der Liste.
        var number = (uint)(r.At + 1);
        var done = 0;

        for (var i = 0; i < r.Incoming.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var src = r.Incoming[i];
            var slot = i;
            progress?.Invoke(i, 0);
            var tags = DropTags(src, rule, r.Inherited, folderCover, number);

            // Erst in den Zielordner holen, dann dort angleichen: die Quelle
            // bleibt bis zum Schluss unangetastet.
            var dest = FileNaming.Free(Path.Combine(r.Folder, Path.GetFileName(src.Path)));
            try { File.Copy(src.Path, dest); }
            catch (Exception ex) { errors.Add($"{src.FileName}: {ex.Message}"); continue; }

            var copied = AudioProbe.Read(dest);
            if (copied is null)
            {
                errors.Add($"{src.FileName}: " + Strings.T("not readable"));
                continue;
            }

            ConversionOutcome outcome;
            if (!rule.AutoConform || FolderAnalysis.Matches(copied, r.Target))
            {
                outcome = tags is null
                    ? new ConversionOutcome(true, copied, null, null, [])
                    : svc.WriteTagsOnly(copied, tags);
            }
            else
            {
                outcome = await svc.ConvertAsync(new ConversionRequest
                {
                    Track = copied,
                    Options = new EncodeOptions { Format = r.Target.Format, SampleRate = r.Target.SampleRate },
                    Tags = tags,
                }, pct => progress?.Invoke(slot, pct), ct);
            }

            if (outcome.Success)
            {
                if (outcome.History is not null) files.Add(outcome.History);
                foreach (var n in outcome.Notes) notes.Add($"{src.FileName}: {n}");
                number++;
                done++;

                if (r.Move) RemoveSource(src, backups, files, errors);
            }
            else
            {
                errors.Add($"{src.FileName}: {outcome.Error}");
                try { if (File.Exists(dest)) File.Delete(dest); } catch { }
            }
        }

        // Erst jetzt die vorhandenen Dateien auf ihre neue Nummer bringen, um
        // genau so viele, wie tatsächlich ankamen. Hängt man hinten an, ist
        // das nichts.
        if (rule.WritesNumbers && done > 0)
        {
            var fixes = new List<(AudioTrack Track, uint Number)>();
            for (var i = 0; i < r.Existing.Count; i++)
            {
                var wanted = (uint)(i < r.At ? i + 1 : i + 1 + done);
                if (r.Existing[i].Track != wanted) fixes.Add((r.Existing[i], wanted));
            }

            // Von hinten nach vorn, wenn es aufwärts geht, damit unterwegs nie
            // zwei Dateien dieselbe Nummer tragen.
            var rising = fixes.Any(p => p.Number > p.Track.Track);
            foreach (var (track, n) in rising ? Enumerable.Reverse(fixes) : fixes)
            {
                var outcome = svc.WriteTagsOnly(track, new TagEdit { Track = n });
                if (outcome.Success) { if (outcome.History is not null) files.Add(outcome.History); }
                else errors.Add($"{track.FileName}: " + Strings.T("number not moved ({0})", outcome.Error));
            }
        }

        return new ImportResult(done, files, errors, notes);
    }

    /// <summary>
    /// Entfernt eine verschobene Quelldatei, vorher gesichert, damit der
    /// Verlauf sie an ihren alten Platz zurücklegen kann.
    /// </summary>
    private static void RemoveSource(AudioTrack src, BackupStore backups, List<HistoryFile> files, List<string> errors)
    {
        try
        {
            var backup = backups.Create(src.Path);
            File.Delete(src.Path);
            files.Add(new HistoryFile { Original = src.Path, BackupPath = backup });
        }
        catch (Exception ex)
        {
            errors.Add($"{src.FileName}: {ex.Message}");
        }
    }

    private static string Rate(int hz) => hz % 1000 == 0 ? $"{hz / 1000} kHz" : $"{hz / 1000.0:0.0} kHz";
}
