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

        if (plan.Count == 0)
        {
            if (!offered)
                await Inform(Strings.T("Album mode"),
                             Strings.T("Every file already matches the album mode. Nothing to change."));
            return;
        }

        if (!await ConfirmAlbumPlan(tab, plan, ordered.Count)) return;

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

    /// <summary>Die Vorschau: welche Datei was bekommt. Erst nach „Anwenden" wird geschrieben.</summary>
    private async Task<bool> ConfirmAlbumPlan(FolderTab tab, List<AlbumChange> plan, int total)
    {
        var list = new StackPanel { Spacing = 10 };

        foreach (var change in plan)
        {
            var lines = new StackPanel { Spacing = 1, Margin = new Thickness(12, 2, 0, 0) };
            foreach (var c in change.Changes)
            {
                lines.Children.Add(new TextBlock
                {
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Res("TextFillColorSecondaryBrush"),
                    Text = c.Field == "Cover"
                        ? Strings.T("Cover: taken from the folder")
                        : Strings.T("{0}: {1} → {2}", Strings.T(c.Field),
                                    c.From.Length == 0 ? "–" : c.From, c.To),
                });
            }

            list.Children.Add(new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = change.Track.FileName,
                        FontSize = 13,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                    lines,
                },
            });
        }

        var dialog = new ContentDialog
        {
            Title = Strings.T("Apply album mode to \"{0}\"", tab.Name),
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = Strings.T("{0} of {1} files change. Every file is backed up "
                                         + "first; the history can undo it.", plan.Count, total),
                    },
                    new ScrollViewer
                    {
                        MaxHeight = 380,
                        Padding = new Thickness(0, 0, 14, 0),
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Content = list,
                    },
                },
            },
            PrimaryButtonText = Strings.T("Apply"),
            CloseButtonText = Strings.T("Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Root.XamlRoot,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 640.0;

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
