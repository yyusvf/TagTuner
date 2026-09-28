using TagTuner.Core.Audio;
using TagTuner.Core.Folders;
using TagTuner.Core.Model;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>
/// Der Ordner auf einen Blick, im Inspektor, solange nichts gewählt ist:
/// Cover, Formate, Lücken in der Nummerierung und was sich mit einem Klick
/// angleichen lässt. Entspricht der Ordner-Analyse unter Windows.
/// </summary>
internal sealed class FolderOverview : NSView
{
    public event Action<IReadOnlyList<AudioTrack>, FolderTarget>? AlignRequested;
    public event Action? AlbumRequested;

    private readonly NSStackView _stack = new()
    {
        Orientation = NSUserInterfaceLayoutOrientation.Vertical,
        Alignment = NSLayoutAttribute.Leading,
        Spacing = 6,
        EdgeInsets = new NSEdgeInsets(14, 16, 16, 16),
    };
    private readonly NSImageView _cover = new() { ImageScaling = NSImageScale.ProportionallyUpOrDown, WantsLayer = true };
    private string? _folder;

    public override bool IsFlipped => true;

    public FolderOverview()
    {
        _cover.Layer!.CornerRadius = 8;
        _cover.Layer.MasksToBounds = true;
        _stack.TranslatesAutoresizingMaskIntoConstraints = false;
        AddSubview(_stack);
        NSLayoutConstraint.ActivateConstraints([
            _stack.TopAnchor.ConstraintEqualTo(TopAnchor),
            _stack.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
            _stack.TrailingAnchor.ConstraintEqualTo(TrailingAnchor),
            _cover.WidthAnchor.ConstraintEqualTo(120),
            _cover.HeightAnchor.ConstraintEqualTo(120),
        ]);
    }

    public void Show(string? folder, IReadOnlyList<AudioTrack> tracks)
    {
        foreach (var v in _stack.ArrangedSubviews) { _stack.RemoveArrangedSubview(v); v.RemoveFromSuperview(); }
        _folder = folder;
        if (folder is null) return;

        Add(Head(Strings.T("FOLDER ANALYSIS")), after: 10);

        var name = NSTextField.CreateWrappingLabel(Path.GetFileName(folder.TrimEnd('/')));
        name.Font = NSFont.BoldSystemFontOfSize(15);
        name.PreferredMaxLayoutWidth = 240;

        // Das Cover kommt aus dem Hintergrund: Bilddatei im Ordner oder die
        // ersten paar Lieder.
        _cover.Image = null;
        Task.Run(() =>
        {
            var img = Covers.Thumbnail(FolderCover.Read(folder), 400);
            InvokeOnMainThread(() => { if (_folder == folder) { _cover.Image = img; _cover.Hidden = img is null; } });
        });
        _cover.Hidden = true;
        Add(_cover, after: 10);
        Add(name, after: 2);

        if (tracks.Count == 0)
        {
            Add(Note(Strings.T("No audio files in this folder.")));
            return;
        }

        var total = TimeSpan.FromTicks(tracks.Sum(t => t.Duration.Ticks));
        var mb = tracks.Sum(t => t.Size) / 1024.0 / 1024.0;
        Add(Note($"{Strings.T("{0} files", tracks.Count)} · {(total.TotalHours >= 1 ? total.ToString(@"h\:mm\:ss") : total.ToString(@"m\:ss"))} · {mb:0.#} MB"), after: 16);

        // ── Formate ──────────────────────────────────────────────
        var analysis = FolderAnalysis.Of(tracks);
        Add(Head(Strings.T("AUDIO")));
        Add(Line(Strings.T("Formats"), string.Join(", ", analysis.FormatCounts
            .OrderByDescending(x => x.Value).Select(x => $"{x.Key} × {x.Value}"))));
        Add(Line(Strings.T("Sample rates"), string.Join(", ", analysis.SampleRateCounts
            .OrderByDescending(x => x.Value).Select(x => $"{Rate(x.Key)} × {x.Value}"))));

        var settings = AppDelegate.Settings;
        var target = analysis.ResolveTarget(settings.DefaultFormat, settings.DefaultSampleRate);
        var outliers = analysis.Outliers(target).ToList();
        Add(Line(Strings.T("Folder target"), $"{target.Format} · {Rate(target.SampleRate)}"
            + (target.FromDefaultProfile ? $" ({Strings.T("from the default profile")})" : "")));
        if (outliers.Count > 0)
        {
            var b = NSButton.CreateButton(Strings.T("Align folder ({0})", outliers.Count),
                () => AlignRequested?.Invoke(outliers, target));
            b.ControlSize = NSControlSize.Small;
            b.ToolTip = string.Join("\n", outliers.Select(o => $"{o.FileName}: {o.Format} · {o.SampleRateLabel}"));
            Add(b, after: 16);
        }
        else _stack.SetCustomSpacing(16, _stack.ArrangedSubviews.Last());

        // ── Nummerierung ─────────────────────────────────────────
        Add(Head(Strings.T("Track numbering")));
        var check = TrackNumberCheck.Check(tracks);
        if (check.IsClean) Add(Note("✓ " + Strings.T("{0} of {1}", tracks.Count, tracks.Count)));
        foreach (var d in check.Discs)
        {
            var prefix = d.Disc > 0 ? Strings.T("Disc {0}", d.Disc) + ": " : "";
            if (d.Missing.Count == 1) Add(Warn(prefix + Strings.T("track {0} missing", d.Missing[0])));
            else if (d.Missing.Count > 1) Add(Warn(prefix + Strings.T("tracks {0} missing", TrackNumberCheck.Ranges(d.Missing))));
            foreach (var (n, c) in d.Doubled) Add(Warn(prefix + Strings.T("track {0} appears {1}×", n, c)));
        }
        if (check.Unnumbered > 0) Add(Warn(Strings.T("{0} file(s) without a track number", check.Unnumbered)));
        _stack.SetCustomSpacing(16, _stack.ArrangedSubviews.Last());

        // ── Album-Modus ──────────────────────────────────────────
        var rule = settings.RuleFor(folder);
        Add(Head(Strings.T("ALBUM MODE")));
        Add(Note(rule.AlbumMode
            ? string.Join(" · ", new[]
              {
                  rule.BaseTags ? Strings.T("Base metadata") : null,
                  rule.Cover ? Strings.T("Cover") : null,
                  rule.Numbering ? Strings.T("Track numbering") : null,
                  rule.WritesFileNames ? Strings.T("File names follow") : null,
              }.OfType<string>())
            : Strings.T("off")));
        if (rule.AlbumMode)
        {
            var b = NSButton.CreateButton(Strings.T("Apply to existing files…"), () => AlbumRequested?.Invoke());
            b.ControlSize = NSControlSize.Small;
            Add(b);
        }
    }

    private void Add(NSView v, float after = 6)
    {
        _stack.AddArrangedSubview(v);
        _stack.SetCustomSpacing(after, v);
    }

    private static string Rate(int hz) => hz % 1000 == 0 ? $"{hz / 1000} kHz" : $"{hz / 1000.0:0.0} kHz";

    private static NSTextField Head(string text)
    {
        var l = NSTextField.CreateLabel(text.ToUpper());
        l.Font = NSFont.BoldSystemFontOfSize(NSFont.SmallSystemFontSize);
        l.TextColor = NSColor.SecondaryLabel;
        return l;
    }

    private static NSTextField Note(string text)
    {
        var l = NSTextField.CreateWrappingLabel(text);
        l.Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize);
        l.TextColor = NSColor.SecondaryLabel;
        l.PreferredMaxLayoutWidth = 240;
        return l;
    }

    private static NSTextField Warn(string text)
    {
        var l = Note("⚠︎ " + text);
        l.TextColor = NSColor.SystemOrange;
        return l;
    }

    private static NSTextField Line(string key, string value) => Note($"{key}: {value}");
}
