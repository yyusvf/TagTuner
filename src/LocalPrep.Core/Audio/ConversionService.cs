using LocalPrep.Core.Metadata;
using LocalPrep.Core.Model;
using LocalPrep.Core.Safety;

namespace LocalPrep.Core.Audio;

public sealed record ConversionRequest
{
    public required AudioTrack Track { get; init; }
    public required EncodeOptions Options { get; init; }

    /// <summary>Tags, die nach der Konvertierung geschrieben werden sollen.</summary>
    public TagEdit? Tags { get; init; }

    /// <summary>
    /// Zielordner. null heißt „im selben Ordner bleiben" — im Playlist-Modell
    /// der Normalfall, weil das Ziel der Ordner ist, in dem man steht.
    /// </summary>
    public string? TargetFolder { get; init; }
}

public sealed record ConversionOutcome(
    bool Success,
    AudioTrack? Result,
    HistoryFile? History,
    string? Error,
    IReadOnlyList<string> Notes);

/// <summary>
/// Führt Konvertierungen aus — mit Sicherung davor und Nachkontrolle danach.
///
/// Die Nachkontrolle ist der Grund, warum in der Electron-Fassung überhaupt
/// auffiel, dass AIFF nie funktionierte und der AAC-Kodierer bei 256 kbps
/// deckelt: Ohne Rückmessung meldet ffmpeg Erfolg und liefert trotzdem etwas
/// anderes als bestellt.
/// </summary>
public sealed class ConversionService(
    FfmpegRunner runner,
    BackupStore backups)
{
    public async Task<ConversionOutcome> ConvertAsync(
        ConversionRequest req,
        Action<int>? onProgress = null,
        CancellationToken ct = default)
    {
        var notes = new List<string>();
        var src = req.Track.Path;
        var folder = req.TargetFolder ?? Path.GetDirectoryName(src)!;
        var targetExt = AudioFormats.TargetExtension(req.Options.Format);
        var outPath = Path.Combine(folder,
            Path.GetFileNameWithoutExtension(src) + "." + targetExt);

        var sameFile = string.Equals(outPath, src, StringComparison.OrdinalIgnoreCase);
        var hadCover = req.Track.HasCover;

        // ffmpeg kann nicht in die Datei schreiben, die es gerade liest.
        // Darum immer über eine temporäre Datei und erst danach tauschen.
        var tempPath = Path.Combine(folder,
            Path.GetFileNameWithoutExtension(src) + ".localprep-tmp." + targetExt);

        string? backupPath = null;
        try
        {
            backupPath = backups.Create(src);
        }
        catch (Exception ex)
        {
            return new ConversionOutcome(false, null, null, ex.Message, notes);
        }

        try
        {
            var args = FfmpegArgs.Build(src, tempPath, req.Options);
            var result = await runner.RunAsync(args, onProgress, ct);

            if (!result.Success)
            {
                Cleanup(tempPath);
                return new ConversionOutcome(false, null, null,
                    $"ffmpeg brach ab (Code {result.ExitCode}): {FfmpegRunner.Tail(result.Output)}",
                    notes);
            }

            // Erst jetzt an den endgültigen Platz — vorher ist nichts kaputt.
            if (sameFile)
            {
                File.Copy(tempPath, outPath, overwrite: true);
            }
            else
            {
                File.Move(tempPath, outPath, overwrite: true);
                // Format gewechselt: das Original wäre sonst ein Duplikat.
                // Es liegt gesichert im Backup-Ordner.
                TryDelete(src);
            }
            Cleanup(tempPath);

            if (req.Tags is not null && !req.Tags.IsEmpty)
                TagWriter.Write(outPath, req.Tags);

            // ── Nachkontrolle ────────────────────────────────────
            var after = AudioProbe.Read(outPath);
            if (after is null)
            {
                return new ConversionOutcome(false, null, null,
                    "Ergebnis ist nicht lesbar.", notes);
            }

            if (req.Options.SampleRate is int want && after.SampleRate != want)
            {
                notes.Add($"Samplerate ist {after.SampleRateLabel}, erwartet waren " +
                          $"{(want % 1000 == 0 ? $"{want / 1000} kHz" : $"{want / 1000.0:0.0} kHz")}.");
            }

            if (hadCover && !after.HasCover)
            {
                // Bei OGG, WAV und AIFF ist das kein Fehler, sondern die
                // Eigenschaft des Containers.
                notes.Add(AudioFormats.CanCarryCover(targetExt)
                    ? "Cover ist bei der Konvertierung verloren gegangen."
                    : $"{targetExt.ToUpperInvariant()} kann kein Cover speichern — es wurde verworfen.");
            }

            var history = new HistoryFile
            {
                Original = src,
                BackupPath = backupPath,
                OutputPath = sameFile ? null : outPath,
            };

            return new ConversionOutcome(true, after, history, null, notes);
        }
        catch (OperationCanceledException)
        {
            Cleanup(tempPath);
            Restore(backupPath, src);
            return new ConversionOutcome(false, null, null, "Abgebrochen.", notes);
        }
        catch (Exception ex)
        {
            Cleanup(tempPath);
            Restore(backupPath, src);
            return new ConversionOutcome(false, null, null, ex.Message, notes);
        }
    }

    /// <summary>Nur Tags schreiben, ohne neu zu kodieren.</summary>
    public ConversionOutcome WriteTagsOnly(AudioTrack track, TagEdit tags)
    {
        string? backupPath;
        try
        {
            backupPath = backups.Create(track.Path);
        }
        catch (Exception ex)
        {
            return new ConversionOutcome(false, null, null, ex.Message, []);
        }

        try
        {
            TagWriter.Write(track.Path, tags);
            var after = AudioProbe.Read(track.Path);
            return new ConversionOutcome(true, after,
                new HistoryFile { Original = track.Path, BackupPath = backupPath }, null, []);
        }
        catch (Exception ex)
        {
            Restore(backupPath, track.Path);
            return new ConversionOutcome(false, null, null, ex.Message, []);
        }
    }

    private static void Cleanup(string path) => TryDelete(path);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void Restore(string? backup, string original)
    {
        if (backup is null || !File.Exists(backup)) return;
        try { File.Copy(backup, original, overwrite: true); } catch { }
    }
}
