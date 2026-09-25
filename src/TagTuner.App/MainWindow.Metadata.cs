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

// Die Metadatenspalte und das Schreiben ihrer Felder.

public sealed partial class MainWindow
{
    // ══ Metadatenspalte ══════════════════════════════════════════

    private void UpdateMetaPanel()
    {
        var folderScope = FolderScope;
        var sel = TargetTracks();
        _suppressSelection = true;

        var any = sel.Count > 0;
        foreach (var box in TagBoxes()) box.IsEnabled = any;
        FFormat.IsEnabled = FRate.IsEnabled = any;

        // Titel und Track sind je Datei verschieden — für mehrere Dateien
        // gleichzeitig gibt es da nichts Sinnvolles zu schreiben.
        var single = any && !folderScope && sel.Count == 1;
        FTitle.IsEnabled = FTrack.IsEnabled = single;

        MetaHead.Text = folderScope
            ? Strings.T("METADATA: {0}, {1} TRACKS", ActiveTab.Name, sel.Count)
            : sel.Count > 1 ? Strings.T("METADATA: {0} TRACKS", sel.Count)
                            : Strings.T("METADATA");
        if (Quick) UpdateQuickTitle(sel);

        if (!any)
        {
            foreach (var box in TagBoxes()) { box.Text = ""; box.PlaceholderText = ""; }
            Snapshot();
            FFormat.SelectedIndex = -1;
            FRate.SelectedIndex = -1;
            CoverImage.Source = null;
            CoverMime.Text = "";
            CoverInfo.Text = Strings.T("nothing selected");
            TechPanel.Children.Clear();
            _suppressSelection = false;
            UpdatePlan();
            return;
        }

        // Ein Feld zeigt nur dann einen Wert, wenn sich die Auswahl darin einig
        // ist — sonst „<verschieden>", das beim Anwenden unangetastet bleibt.
        Fill(FTitle, single ? sel[0].Title : Agree(sel, t => t.Title));
        Fill(FArtist, Agree(sel, t => t.Artist));
        Fill(FAlbum, Agree(sel, t => t.Album));
        Fill(FYear, Agree(sel, t => t.Year == 0 ? "" : t.Year.ToString()));
        Fill(FTrack, single ? sel[0].TrackLabel : null);
        Fill(FGenre, Agree(sel, t => t.Genre));
        Fill(FAlbumArtist, Agree(sel, t => t.AlbumArtist));
        Fill(FComposer, Agree(sel, t => t.Composer));
        Fill(FComment, Agree(sel, t => t.Comment));
        Fill(FDisc, Agree(sel, t => t.DiscLabel));

        var fmt = Agree(sel, t => AudioFormats.TargetExtension(t.Format).ToUpperInvariant());
        FFormat.SelectedItem = fmt is null ? null
            : AudioFormats.Targets.FirstOrDefault(x => x.Equals(fmt, StringComparison.OrdinalIgnoreCase));

        var rate = Agree(sel, t => t.SampleRate.ToString());
        FRate.SelectedIndex = rate is not null && int.TryParse(rate, out var hz)
            ? Array.IndexOf(Rates, hz) : -1;

        UpdateCover(sel[0]);
        UpdateTech(sel);

        Snapshot();
        _suppressSelection = false;
        UpdatePlan();

        static void Fill(TextBox box, string? value)
        {
            box.Text = value ?? "";
            box.PlaceholderText = value is null ? Strings.T("<mixed>") : "";
        }
    }

    private void Snapshot()
    {
        _loaded.Clear();
        foreach (var box in TagBoxes()) _loaded[box] = box.Text;
    }

    private bool TagsChanged() =>
        TagBoxes().Any(b => !_loaded.TryGetValue(b, out var was) || b.Text != was);

    private static string? Agree(List<AudioTrack> sel, Func<AudioTrack, string> pick)
    {
        var values = sel.Select(pick).Distinct(StringComparer.Ordinal).ToList();
        return values.Count == 1 ? values[0] : null;
    }

    /// <summary>Das aktuell angezeigte Cover — Grundlage für Kopieren und Anpassen.</summary>
    private AudioProbe.Cover? _cover;

    private void UpdateCover(AudioTrack track)
    {
        _cover = AudioProbe.ReadCover(track.Path);

        if (_cover is null)
        {
            CoverImage.Source = null;
            CoverMime.Text = "";
            CoverInfo.Text = Strings.T("no cover");
            return;
        }

        try
        {
            var bmp = new BitmapImage();
            using var ms = new MemoryStream(_cover.Data);
            using var ras = ms.AsRandomAccessStream();
            bmp.SetSource(ras);
            CoverImage.Source = bmp;

            CoverMime.Text = ImageInfo.ShortName(_cover.MimeType).ToLowerInvariant();
            CoverInfo.Text = $"{_cover.Data.Length / 1024.0:0.#} KB";

            // Die Maße kommen aus dem Dekoder, nicht aus BitmapImage.ImageOpened:
            // Bei einem Bild aus dem Speicher ist das Ereignis oft schon gelaufen,
            // bevor man sich daran hängen kann — dann blieben die Maße leer.
            ShowDimensions(_cover);
        }
        catch
        {
            CoverImage.Source = null;
            CoverMime.Text = "";
            CoverInfo.Text = Strings.T("cover not readable");
        }
    }

    private async void ShowDimensions(AudioProbe.Cover cover)
    {
        var info = await CoverImaging.MeasureAsync(cover.Data);

        // Zwischenzeitlich eine andere Datei gewählt — dann gehört das Ergebnis
        // nicht mehr zu dem, was gerade zu sehen ist.
        if (info is null || !ReferenceEquals(_cover, cover)) return;

        CoverMime.Text = info.Format.ToLowerInvariant();
        CoverInfo.Text = $"{info.Width} × {info.Height}{Environment.NewLine}" +
                         $"{cover.Data.Length / 1024.0:0.#} KB";
    }

    private void UpdateTech(List<AudioTrack> sel)
    {
        TechPanel.Children.Clear();
        void Row(string k, string v) => TechPanel.Children.Add(new TextBlock
        {
            Text = $"{k}: {v}",
            FontSize = 11.5,
            Foreground = Res("TextFillColorTertiaryBrush"),
        });

        if (sel.Count > 1)
        {
            Row(Strings.T(FolderScope ? "Folder" : "Selection"),
                Strings.T("{0} files", sel.Count));

            var total = TimeSpan.FromTicks(sel.Sum(t => t.Duration.Ticks));
            if (total > TimeSpan.Zero)
                Row(Strings.T("Total duration"), total.TotalHours >= 1
                    ? Strings.T("{0}:{1:00}:{2:00} hours",
                                (int)total.TotalHours, total.Minutes, total.Seconds)
                    : Strings.T("{0}:{1:00} minutes",
                                (int)total.TotalMinutes, total.Seconds));

            var bytes = sel.Sum(t => t.Size);
            if (bytes > 0) Row(Strings.T("Total size"), Bytes(bytes));

            var formats = sel.Select(t => t.Format).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (formats.Count == 1) Row(Strings.T("Format"), formats[0]);
            return;
        }

        var t = sel[0];
        Row(Strings.T("Channels"), t.Channels.ToString());
        Row(Strings.T("Duration"), t.DurationLabel);
        Row(Strings.T("Size"), t.SizeLabel);
        if (t.Bitrate > 0) Row(Strings.T("Bitrate"), $"{t.Bitrate} kbps");
        if (ActiveTab.Target is { } tg && !FolderAnalysis.Matches(t, tg))
            Row(Strings.T("Folder target"), $"{tg.Format} · {FormatRate(tg.SampleRate)}");
    }

    private static string Bytes(long size) => size switch
    {
        < 1024 => $"{size} B",
        < 1024 * 1024 => $"{size / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{size / 1024.0 / 1024.0:0.#} MB",
        _ => $"{size / 1024.0 / 1024.0 / 1024.0:0.##} GB",
    };

    /// <summary>Sagt vorher an, was „Anwenden" tun würde.</summary>
    private void UpdatePlan()
    {
        var folderScope = FolderScope;
        var sel = TargetTracks();
        if (sel.Count == 0)
        {
            PlanText.Text = Strings.T("Nothing selected.");
            ApplyBtn.IsEnabled = false;
            return;
        }

        var jobs = new List<string>();
        if (TagsChanged()) jobs.Add(Strings.T("write tags"));

        if (FFormat.SelectedItem is string f &&
            !f.Equals(Agree(sel, t => AudioFormats.TargetExtension(t.Format).ToUpperInvariant()),
                      StringComparison.OrdinalIgnoreCase))
            jobs.Add(Strings.T("convert to {0}", f));

        if (FRate.SelectedIndex >= 0)
        {
            var hz = Rates[FRate.SelectedIndex];
            if (Agree(sel, t => t.SampleRate.ToString()) is not { } r || r != hz.ToString())
                jobs.Add(Strings.T("bring to {0}", FormatRate(hz)));
        }

        var was = folderScope
            ? Strings.T("Folder ({0} file(s))", sel.Count)
            : Strings.T("{0} file(s)", sel.Count);

        ApplyBtn.IsEnabled = jobs.Count > 0;
        PlanText.Text = jobs.Count > 0
            ? $"{was}: {string.Join(" · ", jobs)}"
            : was;
    }

    // ══ Schreiben ════════════════════════════════════════════════

    private void OnReset(object sender, RoutedEventArgs e) => UpdateMetaPanel();

    /// <summary>
    /// Nur Felder, die tatsächlich verändert wurden. Unangetastete bleiben
    /// null und damit auch in den Dateien unangetastet — bei Mehrfachauswahl
    /// entscheidend, weil „&lt;verschieden&gt;" sonst alles gleichmachen würde.
    /// </summary>
    private TagEdit BuildTagEdit(bool single)
    {
        string? Changed(TextBox box) =>
            _loaded.TryGetValue(box, out var was) && box.Text == was ? null : box.Text;

        uint? Num(TextBox box)
        {
            var v = Changed(box);
            if (v is null) return null;
            return uint.TryParse(v.Trim(), out var n) ? n : 0u;
        }

        return new TagEdit
        {
            // Titel und Track sind je Datei verschieden — bei Mehrfachauswahl
            // wären sie für alle gleich, und das ist nie gewollt.
            Title = single ? Changed(FTitle) : null,
            Track = single ? Num(FTrack) : null,
            Artist = Changed(FArtist),
            Album = Changed(FAlbum),
            AlbumArtist = Changed(FAlbumArtist),
            Genre = Changed(FGenre),
            Composer = Changed(FComposer),
            Comment = Changed(FComment),
            Year = Num(FYear),
            Disc = Num(FDisc),
        };
    }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        var folderScope = FolderScope;
        var sel = TargetTracks();
        if (sel.Count == 0) return;

        if (folderScope && !await Confirm(Strings.T("Apply to the whole folder"),
                Strings.T("Nothing is selected. The change affects all {0} files in \"{1}\".",
                          sel.Count, ActiveTab.Name)
                + "\n\n" + Strings.T("Each file is backed up first."),
                Strings.T("Apply to all")))
            return;

        var edit = BuildTagEdit(!folderScope && sel.Count == 1);
        var wantFormat = FFormat.SelectedItem as string;
        var wantRate = FRate.SelectedIndex >= 0 ? Rates[FRate.SelectedIndex] : (int?)null;

        bool Converts(AudioTrack track) =>
            (wantFormat is not null &&
             !AudioFormats.TargetExtension(track.Format)
                 .Equals(AudioFormats.TargetExtension(wantFormat), StringComparison.OrdinalIgnoreCase))
            || (wantRate is int hz && track.SampleRate != hz);

        // Im Verlauf reicht die Zahl; oben in der Leiste soll stehen, was passiert.
        var doing = sel.Any(Converts)
            ? Strings.T("Converting {0} file(s)", sel.Count)
            : Strings.T("Writing tags to {0} file(s)", sel.Count);

        var ok = await RunJobAsync(Strings.T("{0} file(s)", sel.Count), sel,
            track => (Converts(track), wantFormat, wantRate, edit), doing);

        // Das kleine Fenster aus dem Explorer hat seine Aufgabe erledigt.
        // Bei Fehlern bleibt es offen, damit man es noch einmal versuchen kann.
        // Mit der Liste daneben geht es meist mit der nächsten Datei weiter.
        if (ok && Quick && !_quickList) Close();
    }

    private async void OnAlign(object sender, RoutedEventArgs e)
    {
        var analysis = ActiveTab.Analysis;
        var target = ActiveTab.Target;
        if (analysis is null || target is null) return;

        var outliers = analysis.Outliers(target).ToList();
        if (outliers.Count == 0) return;

        var ok = await Confirm(Strings.T("Align folder"),
            Strings.T("{0} file(s) will be converted to {1} · {2}.",
                      outliers.Count, target.Format, FormatRate(target.SampleRate))
            + "\n\n"
            + Strings.T("The originals are replaced. Each file is backed up first, and "
                        + "the backup can be played back from the history."),
            Strings.T("Align"));
        if (!ok) return;

        await RunJobAsync(Strings.T("Align {0} file(s)", outliers.Count), outliers,
            _ => (true, target.Format, target.SampleRate, null));
    }

    /// <returns>Falsch, wenn etwas nicht geklappt hat oder gar nicht erst anfing.</returns>
    private async Task<bool> RunJobAsync(
        string label,
        IReadOnlyList<AudioTrack> tracks,
        Func<AudioTrack, (bool Convert, string? Format, int? Rate, TagEdit? Tags)> plan,
        string? doing = null)
    {
        // Nur loslassen, was gleich beschrieben wird. Der Player hält seine
        // Datei offen, und ffmpeg wie TagLib kämen sonst nicht daran — aber
        // ein Lied, das gar nicht betroffen ist, soll weiterlaufen.
        ReleaseIfAffected(tracks);

        var ffmpeg = FfmpegLocator.Find(_settings.FfmpegPath);
        if (tracks.Any(t => plan(t).Convert) && ffmpeg is null)
        {
            await Inform(Strings.T("ffmpeg is missing"),
                Strings.T("Converting needs ffmpeg.exe. It is expected next to the "
                          + "application or in tools\\ffmpeg. tools\\fetch-ffmpeg.ps1 fetches it."));
            return false;
        }

        SetBusy(true, doing ?? label);

        var backups = new BackupStore(_settings.ResolvedBackupFolder);
        var svc = new ConversionService(new FfmpegRunner(ffmpeg ?? "ffmpeg"), backups);

        var files = new List<HistoryFile>();
        var notes = new List<string>();
        var errors = new List<string>();
        var done = 0;

        foreach (var track in tracks)
        {
            var (convert, format, rate, tags) = plan(track);
            ProgressStep(done, tracks.Count, track.FileName);
            ConversionOutcome outcome;

            if (convert)
            {
                var slot = done;
                var opts = new EncodeOptions
                {
                    Format = format ?? track.Format,
                    SampleRate = rate,
                };
                outcome = await Task.Run(() => svc.ConvertAsync(
                    new ConversionRequest { Track = track, Options = opts, Tags = tags },
                    pct => DispatcherQueue.TryEnqueue(() =>
                        ShowProgress(true, (slot * 100 + pct) / (double)tracks.Count))));
            }
            else if (tags is not null && !tags.IsEmpty)
            {
                outcome = await Task.Run(() => svc.WriteTagsOnly(track, tags));
            }
            else { done++; continue; }

            done++;
            ShowProgress(true, done * 100.0 / tracks.Count);

            if (outcome.Success)
            {
                if (outcome.History is not null) files.Add(outcome.History);
                foreach (var n in outcome.Notes) notes.Add($"{track.FileName}: {n}");
            }
            else errors.Add($"{track.FileName}: {outcome.Error}");
        }

        if (files.Count > 0) _history.Add("batch", label, files);
        FollowConversions(files);

        // Nach einem Schreibvorgang kann jedes Cover ein anderes sein.
        TrackArt.Reload();
        InvalidateIndex();

        SetBusy(false, null);
        await MergeTabAsync(ActiveTab);
        await ReportAsync(files.Count, errors, notes);
        return errors.Count == 0;
    }
}
