using System.Security.Cryptography;
using TagTuner.Core.Audio;
using TagTuner.Core.Folders;
using TagTuner.Core.Metadata;
using TagTuner.Core.Model;
using TagTuner.Core.Safety;
using TagTuner.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TagTuner.App;

// ══ Album-Modus auf vorhandene Dateien ═══════════════════════════
//
// Beim Hineinziehen wirkt der Album-Modus nur auf die neuen Dateien. Hier
// wird er auf das angewandt, was schon im Ordner liegt: erst als Vorschau,
// geschrieben wird erst nach Bestätigung.

public sealed partial class MainWindow
{
    private async void OnApplyAlbumToExisting(object sender, RoutedEventArgs e) =>
        await ApplyAlbumToExistingAsync(ActiveTab, offered: false);

    /// <param name="offered">
    /// Von selbst angeboten, weil der Album-Modus gerade eingeschaltet wurde.
    /// Gibt es dann nichts zu tun, bleibt es still; auf Knopfdruck wird gesagt,
    /// dass schon alles stimmt.
    /// </param>
    private async Task ApplyAlbumToExistingAsync(FolderTab tab, bool offered)
    {
        if (tab.Recursive || tab.Tracks.Count == 0) return;

        var rule = _settings.RuleFor(tab.Path);
        if (!rule.AlbumMode) return;

        // Nummeriert wird nach der Playlist-Reihenfolge, auch wenn die Liste
        // gerade anders sortiert ist.
        var ordered = tab.Sort == TrackSort.Natural
            ? tab.Tracks.ToList()
            : TrackSorting.FolderSort(tab.Tracks, _settings.SortByDiscThenTrack);

        var inherited = FolderAnalysis.Of(ordered).Inherited(byMajority: true);

        var (cover, needsCover) = rule.Cover
            ? await Task.Run(() => FolderCover(ordered))
            : (null, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var plan = AlbumPlanner.Plan(ordered, rule, inherited, needsCover, cover is not null);

        // Die Dateinamen nach den Nummern, die nach dem Anwenden gelten.
        var renames = rule.WritesFileNames ? PlannedRenames(tab, ordered, plan) : [];

        if (plan.Count == 0 && renames.Count == 0)
        {
            if (!offered)
                await Inform(Strings.T("Album mode"),
                             Strings.T("Every file already matches the album mode. Nothing to change."));
            return;
        }

        if (!await ConfirmAlbumPlan(tab, plan, ordered, cover, renames)) return;

        ReleaseIfAffected(plan.Select(c => c.Track));
        SetBusy(true, Strings.T("Applying album mode"));

        var svc = new ConversionService(
            new FfmpegRunner("ffmpeg"), new BackupStore(_settings.ResolvedBackupFolder));
        var files = new List<HistoryFile>();
        var errors = new List<string>();
        var notes = new List<string>();
        var done = 0;

        for (var i = 0; i < plan.Count; i++)
        {
            var change = plan[i];
            var edit = change.Edit;
            if (cover is not null && change.Changes.Any(c => c.Field == "Cover"))
                edit = edit with { Cover = cover.Data, CoverMimeType = cover.MimeType };

            var outcome = await Task.Run(() => svc.WriteTagsOnly(change.Track, edit));
            if (outcome.Success)
            {
                done++;
                if (outcome.History is not null) files.Add(outcome.History);
                foreach (var n in outcome.Notes) notes.Add($"{change.Track.FileName}: {n}");
            }
            else
            {
                errors.Add($"{change.Track.FileName}: {outcome.Error}");
            }
            ShowProgress(true, (i + 1) * 100.0 / plan.Count);
        }

        if (files.Count > 0)
            _history.Add("album", Strings.T("Album mode applied to \"{0}\"", tab.Name), files);

        TrackArt.Reload();
        InvalidateIndex();
        SetBusy(false, null);
        await MergeTabAsync(tab);
        await RenameToNumbersAsync(tab);
        await ReportAsync(done, errors, notes);
    }

    /// <summary>
    /// Das Cover des Ordners ist das, was die meisten Dateien tragen. Dazu die
    /// Dateien, bei denen es fehlt oder ein anderes ist.
    /// </summary>
    private static (AudioProbe.Cover? Cover, HashSet<string> Needs) FolderCover(IReadOnlyList<AudioTrack> tracks)
    {
        var byFile = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var samples = new Dictionary<string, AudioProbe.Cover>();

        foreach (var track in tracks)
        {
            string? hash = null;
            if (track.HasCover)
            {
                try
                {
                    if (AudioProbe.ReadCover(track.Path) is { Data.Length: > 0 } c)
                    {
                        hash = Convert.ToHexString(SHA256.HashData(c.Data));
                        samples.TryAdd(hash, c);
                    }
                }
                catch { }
            }
            byFile[track.Path] = hash;
        }

        var winner = byFile.Values.OfType<string>()
                           .GroupBy(h => h)
                           .OrderByDescending(g => g.Count())
                           .Select(g => g.Key)
                           .FirstOrDefault();

        var needs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (winner is null) return (null, needs);

        foreach (var (path, hash) in byFile)
            if (hash != winner) needs.Add(path);

        return (samples[winner], needs);
    }

    /// <summary>
    /// Die Vorschau: der Ordner, wie er danach in der Liste aussieht, mit den
    /// geänderten Feldern in der Akzentfarbe. Erst nach „Anwenden" wird
    /// geschrieben.
    /// </summary>
    private async Task<bool> ConfirmAlbumPlan(
        FolderTab tab, List<AlbumChange> plan, IReadOnlyList<AudioTrack> ordered, AudioProbe.Cover? cover,
        IReadOnlyList<(string From, string To)> renames)
    {
        var byPath = plan.ToDictionary(c => c.Track.Path, StringComparer.OrdinalIgnoreCase);

        // Welche Spalte zu welchem Feld gehört. Das Cover steht als Bild da und
        // braucht keine Farbe: Man sieht, dass es ein anderes ist.
        static string Column(string field) => field switch
        {
            "Title" => "title",
            "Artist" => "artist",
            "Album" => "album",
            "Album artist" => "albumartist",
            "Genre" => "genre",
            "Year" => "year",
            "Track" => "track",
            _ => "",
        };

        var changedColumns = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var previewKeys = new List<string>();
        var preview = new FolderTab(tab.Path);

        foreach (var track in ordered)
        {
            if (!byPath.TryGetValue(track.Path, out var change))
            {
                preview.Tracks.Add(track);
                continue;
            }

            var copy = track.Copy();
            var edit = change.Edit;
            if (edit.Title is { } title) copy.Title = title;
            if (edit.Album is { } album) copy.Album = album;
            if (edit.Artist is { } artist) copy.Artist = artist;
            if (edit.AlbumArtist is { } albumArtist) copy.AlbumArtist = albumArtist;
            if (edit.Genre is { } genre) copy.Genre = genre;
            if (edit.Year is { } year) copy.Year = year;
            if (edit.Track is { } number) copy.Track = number;

            // Ein neues Cover steht noch in keiner Datei. Die Kopie bekommt
            // einen erfundenen Pfad, unter dem das Bild schon bereitliegt.
            if (cover is not null && change.Changes.Any(c => c.Field == "Cover"))
            {
                copy.Path = "preview|" + track.Path;
                copy.HasCover = true;
                TrackArt.Preview(copy.Path, cover.Data);
                previewKeys.Add(copy.Path);
            }

            changedColumns[copy.Path] = change.Changes.Select(c => Column(c.Field)).ToHashSet();
            preview.Tracks.Add(copy);
        }

        var pane = new TrackPane
        {
            ReadOnly = true,
            Height = 440,
            Width = Math.Clamp(Root.ActualWidth - 160, 560, 1100),
            IsAlbumFolder = _ => true,
            Highlight = (track, column) =>
                changedColumns.TryGetValue(track.Path, out var set) && set.Contains(column),
        };
        pane.ApplyColumns(_settings);
        pane.Bind(preview);

        var dialog = new ContentDialog
        {
            Title = Strings.T("Apply album mode to \"{0}\"", tab.Name),
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = Strings.T("{0} of {1} files change. Every file is backed up "
                                         + "first; the history can undo it.",
                                         Math.Max(plan.Count, renames.Count), ordered.Count),
                    },
                    new TextBlock
                    {
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Res("TextFillColorTertiaryBrush"),
                        Text = Strings.T("This is how the folder looks afterwards. Changed values are in colour."),
                    },
                    new TextBlock
                    {
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        Visibility = renames.Count > 0 ? Visibility.Visible : Visibility.Collapsed,
                        Text = renames.Count == 0 ? "" : Strings.T(
                            "{0} file name(s) follow the numbers, for example \"{1}\" becomes \"{2}\".",
                            renames.Count, renames[0].From, renames[0].To),
                    },
                    new Border
                    {
                        CornerRadius = new CornerRadius(6),
                        BorderThickness = new Thickness(1),
                        BorderBrush = Res("DividerStrokeColorDefaultBrush"),
                        Child = pane,
                    },
                },
            },
            PrimaryButtonText = Strings.T("Apply"),
            CloseButtonText = Strings.T("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Root.XamlRoot,
        };

        // Ein ContentDialog klemmt seinen Inhalt sonst auf rund 548 Pixel.
        dialog.Resources["ContentDialogMaxWidth"] = 1200.0;
        dialog.Resources["ContentDialogMaxHeight"] = 900.0;

        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            foreach (var key in previewKeys) TrackArt.ForgetPreview(key);
        }
    }

    /// <summary>
    /// Welche Dateien umbenannt würden, nachdem der Plan geschrieben ist:
    /// gerechnet mit den Nummern von danach, nicht von jetzt.
    /// </summary>
    private static List<(string From, string To)> PlannedRenames(
        FolderTab tab, IReadOnlyList<AudioTrack> ordered, List<AlbumChange> plan)
    {
        var byPath = plan.ToDictionary(c => c.Track.Path, StringComparer.OrdinalIgnoreCase);
        var after = ordered.Select(t =>
        {
            if (!byPath.TryGetValue(t.Path, out var change) || change.Edit.Track is not { } n) return t;
            var copy = t.Copy();
            copy.Track = n;
            return copy;
        }).ToList();

        var existing = SafeFiles(tab.Path);
        return FileNumbering.Plan(after, existing).Plan
            .Select(p => (p.Track.FileName, Path.GetFileName(p.Target)))
            .ToList();
    }

    private static List<string> SafeFiles(string folder)
    {
        try { return Directory.EnumerateFiles(folder).ToList(); }
        catch { return []; }
    }

    /// <summary>
    /// Bringt die Nummer vorn in den Dateinamen auf die Track-Nummer, wenn der
    /// Ordner das will. Läuft nach allem, was Nummern schreibt, und legt einen
    /// eigenen Eintrag im Verlauf an.
    /// </summary>
    private async Task RenameToNumbersAsync(FolderTab tab)
    {
        if (tab.Recursive || !_settings.RuleFor(tab.Path).WritesFileNames) return;

        var (plan, skipped) = FileNumbering.Plan(tab.Tracks.ToList(), SafeFiles(tab.Path));
        if (plan.Count == 0)
        {
            if (skipped.Count > 0)
                StatusText.Text = Strings.T("{0} file name(s) left as they are, the new name is taken.", skipped.Count);
            return;
        }

        ReleaseIfAffected(plan.Select(p => p.Track));
        var backups = new BackupStore(_settings.ResolvedBackupFolder);
        var files = new List<HistoryFile>();
        var errors = new List<string>();

        foreach (var (track, target) in plan)
        {
            try
            {
                // Wie beim Umbenennen nach Tags: erst sichern, dann umbenennen.
                // Der Verlauf legt die Sicherung zurück und räumt den neuen Namen weg.
                var backup = backups.Create(track.Path);
                File.Move(track.Path, target);
                files.Add(new HistoryFile { Original = track.Path, BackupPath = backup, OutputPath = target });
            }
            catch (Exception ex) { errors.Add($"{track.FileName}: {ex.Message}"); }
        }

        if (files.Count > 0)
            _history.Add("rename", Strings.T("{0} file name(s) numbered in \"{1}\"", files.Count, tab.Name), files);

        TrackArt.Reload();
        InvalidateIndex();
        await MergeTabAsync(tab);

        StatusText.Text = errors.Count > 0
            ? Strings.T("{0} renamed, {1} failed: {2}", files.Count, errors.Count, string.Join("; ", errors))
            : skipped.Count > 0
                ? Strings.T("{0} file name(s) numbered, {1} left because the name is taken.", files.Count, skipped.Count)
                : Strings.T("{0} file name(s) numbered in \"{1}\"", files.Count, tab.Name);
    }
}
