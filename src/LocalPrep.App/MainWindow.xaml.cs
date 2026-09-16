using System.Collections.ObjectModel;
using LocalPrep.Core.Audio;
using LocalPrep.Core.Folders;
using LocalPrep.Core.Metadata;
using LocalPrep.Core.Model;
using LocalPrep.Core.Safety;
using LocalPrep.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LocalPrep.App;

public sealed partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly HistoryStore _history = new();
    private readonly ObservableCollection<AudioTrack> _tracks = [];

    private string? _folder;
    private FolderAnalysis? _analysis;
    private FolderTarget? _target;
    private bool _suppressSelection;

    /// <summary>
    /// Der Stand der Felder direkt nach dem Laden einer Auswahl.
    ///
    /// „Geändert" wird daraus abgeleitet statt über ein Flag im TextChanged:
    /// WinUI löst TextChanged verzögert aus, sodass das Befüllen der Felder
    /// noch als Nutzereingabe ankam — die App meldete „Tags schreiben",
    /// obwohl niemand etwas angefasst hatte, und hätte beim Anwenden
    /// grundlos geschrieben.
    /// </summary>
    private readonly Dictionary<TextBox, string> _loaded = [];

    private static readonly int[] Rates = [44100, 48000, 88200, 96000, 176400, 192000];

    public MainWindow()
    {
        InitializeComponent();

        Title = "LocalPrep";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(
            (int)_settings.WindowWidth, (int)_settings.WindowHeight));

        TrackList.ItemsSource = _tracks;

        FFormat.ItemsSource = AudioFormats.Targets;
        FRate.ItemsSource = Rates.Select(FormatRate).ToList();
        FFormat.SelectionChanged += (_, _) => UpdatePlan();
        FRate.SelectionChanged += (_, _) => UpdatePlan();
        foreach (var box in TagBoxes())
            box.TextChanged += (_, _) => { if (!_suppressSelection) UpdatePlan(); };

        FfmpegText.Text = FfmpegLocator.Find(_settings.FfmpegPath) is { } p
            ? "ffmpeg bereit"
            : "ffmpeg fehlt";

        BuildTreeRoots();
        UpdateMetaPanel();
        UpdateAnalysisPanel();

        // Über das Kontextmenü gestartet? Dann zählt dieser Pfad, nicht der
        // zuletzt genutzte Ordner.
        var startFolder = App.Launch.FolderToOpen ?? _settings.LastFolder;
        if (!string.IsNullOrWhiteSpace(startFolder) && Directory.Exists(startFolder))
            _ = LoadFolderAsync(startFolder, App.Launch.File);
        else
            StatusText.Text = "Ordner im Baum wählen";

        Closed += (_, _) =>
        {
            _settings.WindowWidth = AppWindow.Size.Width;
            _settings.WindowHeight = AppWindow.Size.Height;
            _settings.Save();
        };
    }

    private TextBox[] TagBoxes() =>
        [FTitle, FArtist, FAlbum, FYear, FTrack, FGenre, FAlbumArtist, FComposer, FComment, FDisc];

    private static string FormatRate(int hz) =>
        hz % 1000 == 0 ? $"{hz / 1000} kHz" : $"{hz / 1000.0:0.0} kHz";

    // ── Baum ─────────────────────────────────────────────────────

    private void BuildTreeRoots()
    {
        foreach (var root in FolderScanner.Roots())
        {
            var node = new TreeViewNode
            {
                Content = root,
                HasUnrealizedChildren = FolderScanner.HasSubfolders(root.Path),
            };
            FolderTree.RootNodes.Add(node);
        }
    }

    /// <summary>
    /// Zweige werden erst beim Aufklappen gelesen. Ein Klick auf „C:\" darf
    /// nicht die halbe Platte durchlaufen.
    /// </summary>
    private void OnTreeExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        var node = args.Node;
        if (!node.HasUnrealizedChildren) return;

        node.Children.Clear();
        if (node.Content is not FolderEntry entry) return;

        foreach (var child in FolderScanner.Subfolders(entry.Path))
        {
            node.Children.Add(new TreeViewNode
            {
                Content = child,
                HasUnrealizedChildren = FolderScanner.HasSubfolders(child.Path),
            });
        }
        node.HasUnrealizedChildren = false;
    }

    private void OnTreeItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TreeViewNode { Content: FolderEntry entry })
            _ = LoadFolderAsync(entry.Path);
    }

    // ── Ordner laden ─────────────────────────────────────────────

    private async Task LoadFolderAsync(string path, string? selectFile = null)
    {
        _folder = path;
        _settings.LastFolder = path;
        FolderName.Text = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        CrumbText.Text = path;
        StatusText.Text = "Wird eingelesen…";

        var tracks = await Task.Run(() => FolderScanner.Tracks(path));

        _tracks.Clear();
        foreach (var t in tracks) _tracks.Add(t);

        _analysis = FolderAnalysis.Of(tracks);
        _target = _analysis.ResolveTarget(_settings.DefaultFormat, _settings.DefaultSampleRate);

        StatusText.Text = path;
        UpdateAnalysisPanel();
        UpdateCount();

        if (selectFile is not null)
        {
            var hit = _tracks.FirstOrDefault(t =>
                string.Equals(t.Path, selectFile, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                TrackList.SelectedItem = hit;
                TrackList.ScrollIntoView(hit);
                return;   // die Auswahl löst UpdateMetaPanel selbst aus
            }
        }
        UpdateMetaPanel();
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (_folder is not null) _ = LoadFolderAsync(_folder);
    }

    private void OnSelectAll(object sender, RoutedEventArgs e) => TrackList.SelectAll();

    // ── Auswahl und Metadaten ────────────────────────────────────

    private void OnTrackSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateMetaPanel();
        UpdateCount();
    }

    private List<AudioTrack> Selected() => TrackList.SelectedItems.OfType<AudioTrack>().ToList();

    private void UpdateMetaPanel()
    {
        var sel = Selected();
        _suppressSelection = true;

        var any = sel.Count > 0;
        foreach (var box in TagBoxes()) box.IsEnabled = any;
        FFormat.IsEnabled = FRate.IsEnabled = any;

        MetaHead.Text = sel.Count > 1 ? $"METADATEN — {sel.Count} TRACKS" : "METADATEN";

        if (!any)
        {
            foreach (var box in TagBoxes()) { box.Text = ""; box.PlaceholderText = ""; }
            Snapshot();
            FFormat.SelectedIndex = -1;
            FRate.SelectedIndex = -1;
            CoverImage.Source = null;
            CoverMime.Text = "";
            CoverInfo.Text = "keine Auswahl";
            TechPanel.Children.Clear();
            _suppressSelection = false;
            UpdatePlan();
            return;
        }

        // Ein Feld zeigt nur dann einen Wert, wenn sich die Auswahl darin
        // einig ist — sonst "<verschieden>" als Platzhalter, der beim
        // Anwenden unangetastet bleibt.
        Fill(FTitle, sel.Count == 1 ? sel[0].Title : Agree(sel, t => t.Title));
        Fill(FArtist, Agree(sel, t => t.Artist));
        Fill(FAlbum, Agree(sel, t => t.Album));
        Fill(FYear, Agree(sel, t => t.Year == 0 ? "" : t.Year.ToString()));
        Fill(FTrack, sel.Count == 1 ? (sel[0].Track == 0 ? "" : sel[0].Track.ToString()) : null);
        Fill(FGenre, Agree(sel, t => t.Genre));
        Fill(FAlbumArtist, Agree(sel, t => t.AlbumArtist));
        Fill(FComposer, Agree(sel, t => t.Composer));
        Fill(FComment, Agree(sel, t => t.Comment));
        Fill(FDisc, Agree(sel, t => t.Disc == 0 ? "" : t.Disc.ToString()));

        var fmt = Agree(sel, t => AudioFormats.TargetExtension(t.Format).ToUpperInvariant());
        FFormat.SelectedItem = fmt is null ? null
            : AudioFormats.Targets.FirstOrDefault(x => x.Equals(fmt, StringComparison.OrdinalIgnoreCase));

        var rate = Agree(sel, t => t.SampleRate.ToString());
        FRate.SelectedIndex = rate is not null && int.TryParse(rate, out var hz)
            ? Array.IndexOf(Rates, hz)
            : -1;

        UpdateCover(sel[0]);
        UpdateTech(sel);

        Snapshot();
        _suppressSelection = false;
        UpdatePlan();

        static void Fill(TextBox box, string? value)
        {
            box.Text = value ?? "";
            box.PlaceholderText = value is null ? "<verschieden>" : "";
        }
    }

    /// <summary>Hält den Stand der Felder fest, wie er geladen wurde.</summary>
    private void Snapshot()
    {
        _loaded.Clear();
        foreach (var box in TagBoxes()) _loaded[box] = box.Text;
    }

    /// <summary>Weicht mindestens ein Feld vom geladenen Stand ab?</summary>
    private bool TagsChanged() =>
        TagBoxes().Any(b => !_loaded.TryGetValue(b, out var was) || b.Text != was);

    /// <summary>Der gemeinsame Wert, oder null wenn sich die Auswahl uneinig ist.</summary>
    private static string? Agree(List<AudioTrack> sel, Func<AudioTrack, string> pick)
    {
        var values = sel.Select(pick).Distinct(StringComparer.Ordinal).ToList();
        return values.Count == 1 ? values[0] : null;
    }

    private void UpdateCover(AudioTrack track)
    {
        var cover = AudioProbe.ReadCover(track.Path);
        if (cover is null)
        {
            CoverImage.Source = null;
            CoverMime.Text = "";
            CoverInfo.Text = "kein Cover";
            return;
        }

        try
        {
            var bmp = new BitmapImage();
            using var ms = new MemoryStream(cover.Data);
            using var ras = ms.AsRandomAccessStream();
            bmp.SetSource(ras);
            CoverImage.Source = bmp;

            CoverMime.Text = cover.MimeType;
            // Die Maße kennt erst das dekodierte Bild — darum nachtragen,
            // sobald WinUI es geladen hat.
            var sizeLabel = $"{cover.Data.Length / 1024.0:0.#} KB{Environment.NewLine}{cover.Kind}";
            CoverInfo.Text = sizeLabel;
            bmp.ImageOpened += (_, _) =>
                CoverInfo.Text = $"{bmp.PixelWidth} × {bmp.PixelHeight}{Environment.NewLine}{sizeLabel}";
        }
        catch
        {
            CoverImage.Source = null;
            CoverMime.Text = "";
            CoverInfo.Text = "Cover nicht lesbar";
        }
    }

    private void UpdateTech(List<AudioTrack> sel)
    {
        TechPanel.Children.Clear();
        void Row(string k, string v) => TechPanel.Children.Add(new TextBlock
        {
            Text = $"{k}: {v}",
            FontSize = 11.5,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
        });

        if (sel.Count > 1) { Row("Auswahl", $"{sel.Count} Dateien"); return; }

        var t = sel[0];
        Row("Kanäle", t.Channels.ToString());
        Row("Dauer", t.DurationLabel);
        Row("Größe", t.SizeLabel);
        if (t.Bitrate > 0) Row("Bitrate", $"{t.Bitrate} kbps");
        if (_target is not null && !FolderAnalysis.Matches(t, _target))
            Row("Ordner-Ziel", $"{_target.Format} · {FormatRate(_target.SampleRate)}");
    }

    /// <summary>Sagt vorher an, was „Anwenden" tun würde.</summary>
    private void UpdatePlan()
    {
        var sel = Selected();
        if (sel.Count == 0)
        {
            PlanText.Text = "Keine Auswahl.";
            ApplyBtn.IsEnabled = false;
            return;
        }

        var jobs = new List<string>();
        if (TagsChanged()) jobs.Add("Tags schreiben");

        if (FFormat.SelectedItem is string f &&
            Agree(sel, t => AudioFormats.TargetExtension(t.Format).ToUpperInvariant()) is var cur &&
            !f.Equals(cur, StringComparison.OrdinalIgnoreCase))
            jobs.Add($"nach {f} konvertieren");

        if (FRate.SelectedIndex >= 0)
        {
            var hz = Rates[FRate.SelectedIndex];
            if (Agree(sel, t => t.SampleRate.ToString()) is not { } r || r != hz.ToString())
                jobs.Add($"auf {FormatRate(hz)} bringen");
        }

        ApplyBtn.IsEnabled = jobs.Count > 0;
        PlanText.Text = jobs.Count > 0
            ? $"{sel.Count} Datei{(sel.Count == 1 ? "" : "en")}: {string.Join(" · ", jobs)}"
            : $"{sel.Count} Datei{(sel.Count == 1 ? "" : "en")} gewählt — nichts geändert.";
    }

    // ── Ordner-Analyse ───────────────────────────────────────────

    private void UpdateAnalysisPanel()
    {
        AnalysisPanel.Children.Clear();

        if (_analysis is null || _target is null)
        {
            VerdictBox.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["WarnDimBrush"];
            VerdictText.Text = "Kein Ordner geladen.";
            VerdictText.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["WarnBrush"];
            AlignBtn.IsEnabled = false;
            SideNote.Text = "";
            return;
        }

        var ok = _analysis.IsUniform;
        VerdictBox.Background = (Microsoft.UI.Xaml.Media.Brush)
            Application.Current.Resources[ok ? "OkDimBrush" : "WarnDimBrush"];
        VerdictText.Foreground = (Microsoft.UI.Xaml.Media.Brush)
            Application.Current.Resources[ok ? "OkBrush" : "WarnBrush"];
        VerdictText.Text = _analysis.IsEmpty
            ? "Ordner ist leer — neue Dateien folgen dem Standardprofil."
            : ok
                ? $"Einheitlich. Alle {_analysis.Tracks.Count} Tracks entsprechen dem Ziel."
                : "Uneinheitlich — es gilt das Standardprofil aus den Einstellungen.";

        void Row(string key, string value, string? note = null)
        {
            var sp = new StackPanel { Spacing = 1 };
            sp.Children.Add(new TextBlock
            {
                Text = $"{key}: {value}",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            });
            if (note is not null)
                sp.Children.Add(new TextBlock
                {
                    Text = note,
                    FontSize = 10.5,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
                });
            AnalysisPanel.Children.Add(sp);
        }

        var fromDefault = _target.FromDefaultProfile ? "aus Standardprofil" : null;
        Row("Ziel-Format", _target.Format, fromDefault);
        Row("Ziel-Samplerate", FormatRate(_target.SampleRate), fromDefault);

        if (!_analysis.IsEmpty && !_analysis.IsUniform)
        {
            Row("Vorhanden",
                string.Join(", ", _analysis.FormatCounts.OrderByDescending(p => p.Value)
                    .Select(p => $"{p.Key} × {p.Value}")) + " / " +
                string.Join(", ", _analysis.SampleRateCounts.OrderByDescending(p => p.Value)
                    .Select(p => $"{FormatRate(p.Key)} × {p.Value}")));
        }

        var off = _analysis.Outliers(_target).Count();
        AlignBtn.IsEnabled = off > 0;
        AlignBtn.Content = off > 0 ? $"Ordner angleichen ({off})" : "Ordner angleichen";
        SideNote.Text = off > 0
            ? $"{off} Datei(en) weichen ab. Angleichen konvertiert sie im Ordner und legt vorher ein Backup an."
            : "Neue Dateien werden beim Ablegen automatisch auf dieses Ziel gebracht.";
    }

    private void UpdateCount()
    {
        var sel = TrackList.SelectedItems.Count;
        CountText.Text = $"{_tracks.Count} Track{(_tracks.Count == 1 ? "" : "s")}" +
                         (sel > 0 ? $" · {sel} gewählt" : "");
    }

    // ── Schreiben ────────────────────────────────────────────────

    private void OnReset(object sender, RoutedEventArgs e) => UpdateMetaPanel();

    /// <summary>
    /// Baut die zu schreibenden Tags — nur aus Feldern, die tatsächlich
    /// verändert wurden. Unangetastete Felder bleiben null und damit auch
    /// in den Dateien unangetastet; das ist bei Mehrfachauswahl entscheidend,
    /// weil „&lt;verschieden&gt;" sonst alles gleichmachen würde.
    /// </summary>
    private TagEdit BuildTagEdit(bool singleSelection)
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
            Title = singleSelection ? Changed(FTitle) : null,
            Track = singleSelection ? Num(FTrack) : null,
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
        var sel = Selected();
        if (sel.Count == 0) return;

        var edit = BuildTagEdit(sel.Count == 1);
        var wantFormat = FFormat.SelectedItem as string;
        var wantRate = FRate.SelectedIndex >= 0 ? Rates[FRate.SelectedIndex] : (int?)null;

        await RunJobAsync($"{sel.Count} Datei(en)", sel, track =>
        {
            var needsConvert =
                (wantFormat is not null &&
                 !AudioFormats.TargetExtension(track.Format)
                     .Equals(AudioFormats.TargetExtension(wantFormat), StringComparison.OrdinalIgnoreCase))
                || (wantRate is int hz && track.SampleRate != hz);

            return (needsConvert, wantFormat, wantRate, edit);
        });
    }

    private async void OnAlign(object sender, RoutedEventArgs e)
    {
        if (_analysis is null || _target is null) return;

        var outliers = _analysis.Outliers(_target).ToList();
        if (outliers.Count == 0) return;

        var ok = await Confirm(
            "Ordner angleichen",
            $"{outliers.Count} Datei(en) werden nach {_target.Format} · " +
            $"{FormatRate(_target.SampleRate)} konvertiert.\n\n" +
            "Die Originale werden ersetzt. Vorher wird je Datei eine Sicherung angelegt, " +
            "die sich über den Verlauf zurückspielen lässt.",
            "Angleichen");
        if (!ok) return;

        await RunJobAsync($"{outliers.Count} Datei(en) angleichen", outliers,
            _ => (true, _target.Format, _target.SampleRate, null));
    }

    /// <summary>
    /// Führt eine Stapelverarbeitung aus: Fortschritt, Sammeln der Ergebnisse,
    /// ein Verlaufseintrag für den ganzen Vorgang, Bericht am Ende.
    /// </summary>
    private async Task RunJobAsync(
        string label,
        IReadOnlyList<AudioTrack> tracks,
        Func<AudioTrack, (bool Convert, string? Format, int? Rate, TagEdit? Tags)> plan)
    {
        var ffmpeg = FfmpegLocator.Find(_settings.FfmpegPath);
        var needsFfmpeg = tracks.Any(t => plan(t).Convert);
        if (needsFfmpeg && ffmpeg is null)
        {
            await Inform("ffmpeg fehlt",
                "Für Konvertierungen wird ffmpeg.exe gebraucht. " +
                "Sie wird neben der Anwendung oder in tools\\ffmpeg erwartet.");
            return;
        }

        SetBusy(true, label);

        var backups = new BackupStore(_settings.ResolvedBackupFolder);
        var svc = new ConversionService(new FfmpegRunner(ffmpeg ?? "ffmpeg"), backups);

        var historyFiles = new List<HistoryFile>();
        var notes = new List<string>();
        var errors = new List<string>();
        var done = 0;

        foreach (var track in tracks)
        {
            var (convert, format, rate, tags) = plan(track);
            ConversionOutcome outcome;

            if (convert)
            {
                var opts = new EncodeOptions
                {
                    Format = format ?? track.Format,
                    SampleRate = rate,
                    Kbps = _settings.DefaultKbps,
                };
                var slot = done;
                outcome = await Task.Run(() => svc.ConvertAsync(new ConversionRequest
                {
                    Track = track,
                    Options = opts,
                    Tags = tags,
                }, pct => DispatcherQueue.TryEnqueue(() =>
                       Progress.Value = (slot * 100 + pct) / (double)tracks.Count)));
            }
            else if (tags is not null && !tags.IsEmpty)
            {
                outcome = await Task.Run(() => svc.WriteTagsOnly(track, tags));
            }
            else
            {
                done++;
                continue;
            }

            done++;
            Progress.Value = done * 100.0 / tracks.Count;

            if (outcome.Success)
            {
                if (outcome.History is not null) historyFiles.Add(outcome.History);
                foreach (var n in outcome.Notes) notes.Add($"{track.FileName}: {n}");
            }
            else
            {
                errors.Add($"{track.FileName}: {outcome.Error}");
            }
        }

        if (historyFiles.Count > 0)
            _history.Add("batch", label, historyFiles);

        SetBusy(false, null);
        if (_folder is not null) await LoadFolderAsync(_folder);

        await ReportAsync(historyFiles.Count, errors, notes);
    }

    private async Task ReportAsync(int ok, List<string> errors, List<string> notes)
    {
        if (errors.Count == 0 && notes.Count == 0)
        {
            StatusText.Text = $"{ok} Datei(en) verarbeitet — Sicherung angelegt";
            return;
        }

        var text = new System.Text.StringBuilder();
        text.AppendLine($"{ok} Datei(en) verarbeitet.");
        if (errors.Count > 0)
        {
            text.AppendLine().AppendLine("Fehlgeschlagen:");
            foreach (var e in errors.Take(8)) text.AppendLine("• " + e);
        }
        if (notes.Count > 0)
        {
            text.AppendLine().AppendLine("Hinweise:");
            foreach (var n in notes.Take(8)) text.AppendLine("• " + n);
        }

        await Inform(errors.Count > 0 ? "Mit Fehlern abgeschlossen" : "Abgeschlossen",
                     text.ToString().TrimEnd());
    }

    private void SetBusy(bool busy, string? label)
    {
        Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        Progress.Value = 0;
        ApplyBtn.IsEnabled = !busy && ApplyBtn.IsEnabled;
        AlignBtn.IsEnabled = !busy && AlignBtn.IsEnabled;
        TrackList.IsEnabled = !busy;
        FolderTree.IsEnabled = !busy;
        if (label is not null) StatusText.Text = label + " …";
    }

    // ── Dialoge ──────────────────────────────────────────────────

    private async Task<bool> Confirm(string title, string message, string primary)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primary,
            CloseButtonText = "Abbrechen",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Root.XamlRoot,
        };
        return await dlg.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task Inform(string title, string message)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                MaxHeight = 320,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            },
            CloseButtonText = "OK",
            XamlRoot = Root.XamlRoot,
        };
        await dlg.ShowAsync();
    }

    private async void OnOpenHistory(object sender, RoutedEventArgs e)
    {
        var undone = await HistoryDialog.ShowAsync(Root.XamlRoot, _history);
        // Nach einem Rückgängig liegen andere Dateien im Ordner als in der Liste.
        if (undone && _folder is not null) await LoadFolderAsync(_folder);
    }

    private async void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        await SettingsDialog.ShowAsync(Root.XamlRoot, _settings, _history);

        // Das Ziel eines uneinheitlichen Ordners hängt am Standardprofil —
        // nach dem Speichern also neu bewerten.
        if (_analysis is not null)
        {
            _target = _analysis.ResolveTarget(_settings.DefaultFormat, _settings.DefaultSampleRate);
            UpdateAnalysisPanel();
            UpdateMetaPanel();
        }
        FfmpegText.Text = FfmpegLocator.Find(_settings.FfmpegPath) is not null
            ? "ffmpeg bereit" : "ffmpeg fehlt";
    }

    // ── Umsortieren ──────────────────────────────────────────────

    /// <summary>
    /// Nach dem Ziehen innerhalb der Liste die Track-Nummern neu vergeben.
    ///
    /// Nur wenn der Ordner lückenlos von 1 an nummeriert war: Enthält er eine
    /// Teilauswahl eines Albums (Tracks 1,2,5,11 …), wäre Durchnummerieren
    /// eine stille Fälschung der Original-Nummern.
    /// </summary>
    private async void OnReorderCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.DropResult != Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move) return;

        var order = _tracks.ToList();
        var numbers = order.Select(t => t.Track).ToList();
        var contiguous = numbers.Count > 0
            && numbers.All(n => n > 0)
            && numbers.OrderBy(n => n).SequenceEqual(Enumerable.Range(1, numbers.Count).Select(i => (uint)i));

        if (!contiguous)
        {
            StatusText.Text = "Reihenfolge geändert — Track-Nummern unverändert " +
                              "(der Ordner ist nicht lückenlos von 1 an nummeriert)";
            return;
        }

        var ok = await Confirm("Track-Nummern neu vergeben",
            $"Die Reihenfolge wurde geändert. Sollen die {order.Count} Tracks " +
            "entsprechend neu von 1 an durchnummeriert werden?\n\n" +
            "Vorher wird je Datei eine Sicherung angelegt.",
            "Neu nummerieren");

        if (!ok) { if (_folder is not null) await LoadFolderAsync(_folder); return; }

        SetBusy(true, "Track-Nummern schreiben");
        var backups = new BackupStore(_settings.ResolvedBackupFolder);
        var svc = new ConversionService(new FfmpegRunner("ffmpeg"), backups);
        var files = new List<HistoryFile>();

        for (var i = 0; i < order.Count; i++)
        {
            var wanted = (uint)(i + 1);
            if (order[i].Track == wanted) continue;
            var outcome = await Task.Run(() =>
                svc.WriteTagsOnly(order[i], new TagEdit { Track = wanted }));
            if (outcome.Success && outcome.History is not null) files.Add(outcome.History);
            Progress.Value = (i + 1) * 100.0 / order.Count;
        }

        if (files.Count > 0) _history.Add("tracknumbers", "Reihenfolge geändert", files);
        SetBusy(false, null);
        if (_folder is not null) await LoadFolderAsync(_folder);
        StatusText.Text = $"{files.Count} Track-Nummer(n) geschrieben";
    }

    // ── Dateien ablegen ──────────────────────────────────────────

    private void OnListDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            return;

        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = _target is null
            ? "Ablegen"
            : $"Angleichen an {_target.Format} · {FormatRate(_target.SampleRate)}";
        e.DragUIOverride.IsCaptionVisible = true;
        e.Handled = true;
    }

    private async void OnListDrop(object sender, DragEventArgs e)
    {
        if (_folder is null || _target is null) return;
        if (!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            return;

        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.OfType<Windows.Storage.StorageFile>()
                             .Select(f => f.Path)
                             .Where(AudioFormats.IsAudioFile)
                             .ToList();

            if (paths.Count == 0)
            {
                StatusText.Text = "Keine unterstützte Audiodatei dabei";
                return;
            }

            await ConformDroppedAsync(paths);
        }
        finally { deferral.Complete(); }
    }

    /// <summary>
    /// Bringt abgelegte Dateien auf das Ziel des Ordners und übernimmt, worin
    /// sich die vorhandenen Tracks einig sind.
    /// </summary>
    private async Task ConformDroppedAsync(List<string> paths)
    {
        var inherited = _analysis!.Inherited();
        var tracks = paths.Select(AudioProbe.Read).OfType<AudioTrack>().ToList();
        if (tracks.Count == 0) return;

        var needConvert = tracks.Where(t => !FolderAnalysis.Matches(t, _target!)).ToList();

        if (!_settings.SkipConformDialog)
        {
            var text = new System.Text.StringBuilder();
            text.AppendLine($"{tracks.Count} Datei(en) nach „{Path.GetFileName(_folder)}“.");
            text.AppendLine();
            text.AppendLine(needConvert.Count > 0
                ? $"Konvertierung: {needConvert.Count} Datei(en) → {_target!.Format} · {FormatRate(_target.SampleRate)}"
                : "Keine Konvertierung nötig.");

            var erben = new List<string>();
            if (inherited.Album is not null) erben.Add("Album");
            if (inherited.Artist is not null) erben.Add("Interpret");
            if (inherited.AlbumArtist is not null) erben.Add("Album-Interpret");
            if (inherited.Year is not null) erben.Add("Jahr");
            if (inherited.Genre is not null) erben.Add("Genre");
            text.AppendLine(erben.Count > 0
                ? "Übernommen vom Ordner: " + string.Join(", ", erben)
                : "Der Ordner ist sich in keinem Tag einig — es wird nichts übernommen.");

            if (_target!.FromDefaultProfile)
                text.AppendLine().AppendLine(
                    "⚠ Zielordner ist uneinheitlich — das Ziel stammt aus dem Standardprofil.");

            var lossy = needConvert.Where(t => t.SampleRate > _target.SampleRate).ToList();
            if (lossy.Count > 0)
                text.AppendLine().AppendLine(
                    $"⚠ {lossy.Count} Datei(en) werden heruntergerechnet — das ist nicht " +
                    "verlustfrei umkehrbar.");

            text.AppendLine().AppendLine("Die Quelldateien bleiben erhalten (Kopie).");

            if (!await Confirm("Dateien angleichen", text.ToString().TrimEnd(), "Übernehmen"))
                return;
        }

        SetBusy(true, $"{tracks.Count} Datei(en) übernehmen");

        var ffmpeg = FfmpegLocator.Find(_settings.FfmpegPath);
        var backups = new BackupStore(_settings.ResolvedBackupFolder);
        var svc = new ConversionService(new FfmpegRunner(ffmpeg ?? "ffmpeg"), backups);

        var nextTrack = _analysis.NextTrackNumber();
        var files = new List<HistoryFile>();
        var errors = new List<string>();
        var notes = new List<string>();

        for (var i = 0; i < tracks.Count; i++)
        {
            var src = tracks[i];
            var title = string.IsNullOrWhiteSpace(src.Title)
                ? Path.GetFileNameWithoutExtension(src.Path)
                : src.Title;

            var tags = new TagEdit
            {
                Title = title,
                Album = inherited.Album,
                Artist = inherited.Artist ?? src.Artist,
                AlbumArtist = inherited.AlbumArtist,
                Genre = inherited.Genre,
                Year = uint.TryParse(inherited.Year, out var y) ? y : null,
                Disc = uint.TryParse(inherited.Disc, out var d) ? d : null,
                Track = nextTrack + (uint)i,
            };

            // Erst in den Zielordner holen, dann dort angleichen — so bleibt
            // die Quelle unangetastet.
            var dest = Path.Combine(_folder!, Path.GetFileName(src.Path));
            dest = UniquePath(dest);
            try { File.Copy(src.Path, dest); }
            catch (Exception ex) { errors.Add($"{src.FileName}: {ex.Message}"); continue; }

            var copied = AudioProbe.Read(dest);
            if (copied is null) { errors.Add($"{src.FileName}: nicht lesbar"); continue; }

            ConversionOutcome outcome;
            if (FolderAnalysis.Matches(copied, _target!))
            {
                outcome = await Task.Run(() => svc.WriteTagsOnly(copied, tags));
            }
            else
            {
                var slot = i;
                outcome = await Task.Run(() => svc.ConvertAsync(new ConversionRequest
                {
                    Track = copied,
                    Options = new EncodeOptions
                    {
                        Format = _target!.Format,
                        SampleRate = _target.SampleRate,
                        Kbps = _settings.DefaultKbps,
                    },
                    Tags = tags,
                }, pct => DispatcherQueue.TryEnqueue(() =>
                       Progress.Value = (slot * 100 + pct) / (double)tracks.Count)));
            }

            if (outcome.Success)
            {
                if (outcome.History is not null) files.Add(outcome.History);
                foreach (var n in outcome.Notes) notes.Add($"{src.FileName}: {n}");
            }
            else
            {
                errors.Add($"{src.FileName}: {outcome.Error}");
                try { if (File.Exists(dest)) File.Delete(dest); } catch { }
            }

            Progress.Value = (i + 1) * 100.0 / tracks.Count;
        }

        if (files.Count > 0)
            _history.Add("import", $"{files.Count} Datei(en) übernommen", files);

        SetBusy(false, null);
        await LoadFolderAsync(_folder!);
        await ReportAsync(files.Count, errors, notes);
    }

    /// <summary>Hängt „ (2)" an, falls der Name im Zielordner schon belegt ist.</summary>
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
}
