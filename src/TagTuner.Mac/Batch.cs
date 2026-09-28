using TagTuner.Core.Audio;
using TagTuner.Core.Folders;
using TagTuner.Core.Metadata;
using TagTuner.Core.Model;
using TagTuner.Core.Safety;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>
/// Was mit einer Datei geschehen soll. Format und Samplerate null heißt: nicht
/// umwandeln, nur Tags schreiben.
/// </summary>
internal sealed record Job(AudioTrack Track, TagEdit Edit, string? Format = null, int? Rate = null)
{
    public bool Converts =>
        (Format is not null && !AudioFormats.TargetExtension(Track.Format)
            .Equals(AudioFormats.TargetExtension(Format), StringComparison.OrdinalIgnoreCase))
        || (Rate is int hz && Track.SampleRate != hz);
}

internal sealed record BatchResult(List<HistoryFile> Files, List<string> Errors, List<string> Notes)
{
    public int Written => Files.Count;
}

/// <summary>
/// Schreibt mehrere Dateien im Hintergrund, jede mit Sicherung davor, und
/// trägt das Ganze als einen Vorgang in den Verlauf ein. Inspektor,
/// Album-Modus, Umsortieren und „Tags einfügen" gehen alle hier durch.
/// </summary>
internal static class Batch
{
    private static AppSettings Settings => AppDelegate.Settings;

    /// <param name="progress">Datei (ab 0) und Prozent darin; kommt aus dem Hintergrund.</param>
    public static Task<BatchResult> RunAsync(
        IReadOnlyList<Job> jobs, string kind, string label, Action<int, int>? progress = null) =>
        Task.Run(async () =>
        {
            var svc = new ConversionService(
                new FfmpegRunner(FfmpegLocator.Find(Settings.FfmpegPath) ?? "ffmpeg"),
                new BackupStore(Settings.ResolvedBackupFolder));
            var files = new List<HistoryFile>();
            var errors = new List<string>();
            var notes = new List<string>();

            for (var i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i];
                var slot = i;
                progress?.Invoke(i, 0);
                ConversionOutcome r;
                if (job.Converts)
                {
                    r = await svc.ConvertAsync(new ConversionRequest
                    {
                        Track = job.Track,
                        Options = new EncodeOptions { Format = job.Format ?? job.Track.Format, SampleRate = job.Rate },
                        Tags = job.Edit.IsEmpty ? null : job.Edit,
                    }, pct => progress?.Invoke(slot, pct));
                }
                else if (!job.Edit.IsEmpty)
                {
                    r = svc.WriteTagsOnly(job.Track, job.Edit);
                }
                else continue;

                if (r.Success && r.History is not null) files.Add(r.History);
                else if (!r.Success) errors.Add($"{job.Track.FileName}: {r.Error}");
                notes.AddRange(r.Notes.Select(n => $"{job.Track.FileName}: {n}"));
            }
            if (files.Count > 0) AppDelegate.History.Add(kind, label, files);
            return new BatchResult(files, errors, notes);
        });

    /// <summary>Braucht einer der Aufträge ffmpeg, ist es aber nicht da?</summary>
    public static bool MissingFfmpeg(IEnumerable<Job> jobs) =>
        jobs.Any(j => j.Converts) && FfmpegLocator.Find(Settings.FfmpegPath) is null;

    /// <summary>Benennt um, je Datei mit Sicherung, als ein Vorgang im Verlauf.</summary>
    public static Task<BatchResult> RenameAsync(IReadOnlyList<(AudioTrack Track, string Target)> plan) =>
        Task.Run(() =>
        {
            var backups = new BackupStore(Settings.ResolvedBackupFolder);
            var files = new List<HistoryFile>();
            var errors = new List<string>();
            foreach (var (track, target) in plan)
            {
                try
                {
                    // Erst sichern, dann umbenennen. Der Verlauf legt die Sicherung
                    // an den alten Platz zurück und räumt den neuen Namen weg.
                    var backup = backups.Create(track.Path);
                    File.Move(track.Path, target);
                    files.Add(new HistoryFile { Original = track.Path, BackupPath = backup, OutputPath = target });
                }
                catch (Exception ex) { errors.Add($"{track.FileName}: {ex.Message}"); }
            }
            if (files.Count > 0)
                AppDelegate.History.Add("rename", Strings.T("{0} file(s) renamed", files.Count), files);
            return new BatchResult(files, errors, []);
        });

    /// <summary>
    /// Zieht die Nummer vorn im Dateinamen der Track-Nummer nach, wenn die
    /// Regel des Ordners das will. Wie unter Windows: erst sichern, dann
    /// umbenennen, damit der Verlauf es zurücknehmen kann.
    /// </summary>
    public static Task<BatchResult> RenameToNumbersAsync(string folder, IReadOnlyList<AudioTrack> tracks) =>
        Task.Run(() =>
        {
            var files = new List<HistoryFile>();
            var errors = new List<string>();
            if (!Settings.RuleFor(folder).WritesFileNames) return new BatchResult(files, errors, []);

            var existing = Directory.EnumerateFiles(folder).ToList();
            var (plan, _) = FileNumbering.Plan(tracks, existing);
            var backups = new BackupStore(Settings.ResolvedBackupFolder);
            foreach (var (track, target) in plan)
            {
                try
                {
                    var backup = backups.Create(track.Path);
                    File.Move(track.Path, target);
                    files.Add(new HistoryFile { Original = track.Path, BackupPath = backup, OutputPath = target });
                }
                catch (Exception ex) { errors.Add($"{track.FileName}: {ex.Message}"); }
            }
            if (files.Count > 0)
                AppDelegate.History.Add("rename", Strings.T("{0} file name(s) numbered in \"{1}\"",
                    files.Count, Path.GetFileName(folder)), files);
            return new BatchResult(files, errors, []);
        });
}
