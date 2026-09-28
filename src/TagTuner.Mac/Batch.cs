using TagTuner.Core.Audio;
using TagTuner.Core.Folders;
using TagTuner.Core.Metadata;
using TagTuner.Core.Model;
using TagTuner.Core.Safety;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

internal sealed record BatchResult(int Written, List<string> Errors);

/// <summary>
/// Schreibt mehrere Dateien im Hintergrund, jede mit Sicherung davor, und
/// trägt das Ganze als einen Vorgang in den Verlauf ein. Inspektor,
/// Album-Modus, Umsortieren und „Tags einfügen" gehen alle hier durch.
/// </summary>
internal static class Batch
{
    private static AppSettings Settings => AppDelegate.Settings;

    public static Task<BatchResult> WriteTagsAsync(
        IReadOnlyList<(AudioTrack Track, TagEdit Edit)> jobs, string kind, string label) =>
        Task.Run(() =>
        {
            var svc = new ConversionService(
                new FfmpegRunner(FfmpegLocator.Find(Settings.FfmpegPath) ?? "ffmpeg"),
                new BackupStore(Settings.ResolvedBackupFolder));
            var files = new List<HistoryFile>();
            var errors = new List<string>();
            foreach (var (t, edit) in jobs)
            {
                if (edit.IsEmpty) continue;
                var r = svc.WriteTagsOnly(t, edit);
                if (r.Success && r.History is not null) files.Add(r.History);
                else if (!r.Success) errors.Add($"{t.FileName}: {r.Error}");
            }
            if (files.Count > 0) AppDelegate.History.Add(kind, label, files);
            return new BatchResult(files.Count, errors);
        });

    /// <summary>
    /// Zieht die Nummer vorn im Dateinamen der Track-Nummer nach, wenn die
    /// Regel des Ordners das will. Wie unter Windows: erst sichern, dann
    /// umbenennen, damit der Verlauf es zurücknehmen kann.
    /// </summary>
    public static Task<BatchResult> RenameToNumbersAsync(string folder, IReadOnlyList<AudioTrack> tracks) =>
        Task.Run(() =>
        {
            if (!Settings.RuleFor(folder).WritesFileNames) return new BatchResult(0, []);

            var existing = Directory.EnumerateFiles(folder).ToList();
            var (plan, skipped) = FileNumbering.Plan(tracks, existing);
            var backups = new BackupStore(Settings.ResolvedBackupFolder);
            var files = new List<HistoryFile>();
            var errors = new List<string>();
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
            return new BatchResult(files.Count, errors);
        });
}
