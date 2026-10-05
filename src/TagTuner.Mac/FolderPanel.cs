using TagTuner.Core.Folders;
using TagTuner.Core.Model;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>
/// Die rechte Spalte, wie unter Windows: Ordner-Analyse mit Befund und Ziel,
/// darunter der Album-Modus des Ordners und was beim Ablegen geschieht. Die
/// Schalter schreiben die Regel des Ordners sofort.
/// </summary>
internal sealed class FolderPanel : NSViewController
{
    public event Action<IReadOnlyList<AudioTrack>, FolderTarget>? AlignRequested;
    public event Action? AlbumRequested;
    public event Action? RenameRequested;
    public event Action? CoverAllRequested;

    private readonly FlippedStack _stack = new()
    {
        Orientation = NSUserInterfaceLayoutOrientation.Vertical,
        Alignment = NSLayoutAttribute.Leading,
        Spacing = 8,
        EdgeInsets = new NSEdgeInsets(10, 12, 16, 12),
    };

    private string? _folder;
    private IReadOnlyList<AudioTrack> _tracks = [];

    private static AppSettings Settings => AppDelegate.Settings;

    public override void LoadView()
    {
        var scroll = new NSScrollView
        {
            HasVerticalScroller = true,
            DrawsBackground = false,
            AutohidesScrollers = true,
            DocumentView = _stack,
        };
        _stack.TranslatesAutoresizingMaskIntoConstraints = false;
        NSLayoutConstraint.ActivateConstraints([
            _stack.LeadingAnchor.ConstraintEqualTo(scroll.ContentView.LeadingAnchor),
            _stack.TrailingAnchor.ConstraintEqualTo(scroll.ContentView.TrailingAnchor),
            _stack.TopAnchor.ConstraintEqualTo(scroll.ContentView.TopAnchor),
        ]);
        var root = new NSView();
        scroll.TranslatesAutoresizingMaskIntoConstraints = false;
        root.AddSubview(scroll);
        NSLayoutConstraint.ActivateConstraints([
            scroll.TopAnchor.ConstraintEqualTo(root.SafeAreaLayoutGuide.TopAnchor),
            scroll.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor),
            scroll.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor),
            scroll.BottomAnchor.ConstraintEqualTo(root.BottomAnchor),
            root.WidthAnchor.ConstraintGreaterThanOrEqualTo(220),
        ]);
        View = root;
        Show(null, []);
    }

    public void Show(string? folder, IReadOnlyList<AudioTrack> tracks)
    {
        _folder = folder;
        _tracks = tracks;
        Rebuild();
    }

    private void Rebuild()
    {
        foreach (var v in _stack.ArrangedSubviews) { _stack.RemoveArrangedSubview(v); v.RemoveFromSuperview(); }
        Add(Theme.Section(Strings.T("FOLDER ANALYSIS")), after: 10);
        if (_folder is not { } folder) return;

        // ── Befund: eine Zeile mit Zeichen, grün oder gelb ───────
        var analysis = FolderAnalysis.Of(_tracks);
        var target = analysis.ResolveTarget(Settings.DefaultFormat, Settings.DefaultSampleRate);
        var ok = analysis.IsUniform;
        Add(Verdict(analysis.IsEmpty
            ? Strings.T("The folder is empty. New files follow the default profile.")
            : ok ? Strings.T("Uniform. All {0} tracks match the target.", analysis.Tracks.Count)
                 : Strings.T("Mixed. The default profile from the settings applies."), ok), after: 12);

        // ── Ziel als Zweispalter, wie im Informationsfenster ─────
        var fromDefault = target.FromDefaultProfile ? Strings.T("from the default profile") : null;
        Add(Pair(Strings.T("Target format"), target.Format), after: 4);
        Add(Pair(Strings.T("Target sample rate"), Rate(target.SampleRate)), after: fromDefault is null ? 12 : 2);
        if (fromDefault is not null) Add(Faint(fromDefault), after: 12);

        if (!analysis.IsEmpty && !analysis.IsUniform)
        {
            Add(Pills(Strings.T("Formats"), analysis.FormatCounts.OrderByDescending(p => p.Value)
                .Select(p => ($"{p.Value} {p.Key}", p.Key.Equals(target.Format, StringComparison.OrdinalIgnoreCase)))), after: 8);
            Add(Pills(Strings.T("Sample rates"), analysis.SampleRateCounts.OrderByDescending(p => p.Value)
                .Select(p => ($"{p.Value} × {Rate(p.Key)}", p.Key == target.SampleRate))), after: 12);
        }

        // ── Aktionen: normal hohe Knöpfe, das Angleichen hervorgehoben, wenn es etwas zu tun gibt ──
        var outliers = analysis.Outliers(target).ToList();
        var align = Button(outliers.Count > 0 ? Strings.T("Align folder ({0})", outliers.Count) : Strings.T("Align folder"),
            () => AlignRequested?.Invoke(outliers, target));
        align.Enabled = outliers.Count > 0;
        if (outliers.Count > 0) Theme.MakePrimary(align);
        Add(align, after: 6);
        Add(Button(Strings.T("Rename…"), () => RenameRequested?.Invoke()), after: 6);
        Add(Button(Strings.T("Set cover for all…"), () => CoverAllRequested?.Invoke()), after: 6);
        Add(Faint(outliers.Count > 0
            ? Strings.T("{0} file(s) deviate. Aligning converts them in place and backs them up first.", outliers.Count)
            : Strings.T("Files dropped in are brought to this target automatically.")), after: 18);

        // ── Album-Modus ──────────────────────────────────────────
        var rule = Settings.RuleFor(folder);
        var album = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Alignment = NSLayoutAttribute.Leading,
            Spacing = 6,
        };
        // Umbrechende Texte und Zeilen brauchen die volle Breite.
        void Wide(NSView v, float after = 6) { album.AddArrangedSubview(v); album.FillWidth(v); album.SetCustomSpacing(after, v); }

        Wide(Theme.Section(Strings.T("ALBUM MODE")), 8);
        Wide(SwitchRow(Strings.T("This folder is one release"), rule.AlbumMode, on => Change(r => r.AlbumMode = on)), 4);
        Wide(Faint(Strings.T("For an album, an EP or a single: one release with uniform metadata. "
            + "It overwrites to make the folder consistent, so leave it off for folders where you collect mixed music.")), 10);
        Wide(Check(Strings.T("Base metadata"), rule.BaseTags, rule.AlbumMode, 0, on => Change(r => r.BaseTags = on)), 4);
        Wide(Check(Strings.T("Cover"), rule.Cover, rule.AlbumMode, 0, on => Change(r => r.Cover = on)), 4);
        Wide(Check(Strings.T("Track numbering"), rule.Numbering, rule.AlbumMode, 0, on => Change(r => r.Numbering = on)), 4);
        Wide(Check(Strings.T("File names follow"), rule.RenameFiles, rule.AlbumMode && rule.Numbering, 1,
            on => Change(r => r.RenameFiles = on)), 8);
        Wide(Faint(rule.AlbumMode
            ? Strings.T("Applies when files are dropped in and when the order changes.")
            : Strings.T("Tags, cover and track numbers stay untouched here.")), 8);
        if (rule.AlbumMode && Numbering(_tracks) is { Length: > 0 } problems)
            Wide(Theme.Small(problems, Theme.Warn), 8);
        var apply = Button(Strings.T("Apply to existing files…"), () => AlbumRequested?.Invoke());
        apply.Enabled = rule.AlbumMode && _tracks.Count > 0;
        Wide(apply, 16);

        // ── Beim Ablegen ─────────────────────────────────────────
        Wide(Theme.Section(Strings.T("ON DROP")), 8);
        Wide(SwitchRow(Strings.T("Align format and sample rate"), rule.AutoConform, on => Change(r => r.AutoConform = on)), 4);
        var own = Settings.HasOwnRule(folder);
        Wide(Faint(own
            ? Strings.T("Own setting for \"{0}\". It wins over the global one.", Path.GetFileName(folder))
            : Strings.T("Follows the global setting from the settings.")), 8);
        if (own) Wide(Button(Strings.T("Reset to the global setting"), () => { Settings.ClearRule(folder); Rebuild(); }));
        Add(Theme.Card(album, NSColor.FromWhite(1, 0.035f), 12));
    }

    /// <summary>Die Regel ändern und sofort merken, dann neu zeichnen.</summary>
    private void Change(Action<FolderRule> edit)
    {
        if (_folder is null) return;
        var rule = Settings.RuleFor(_folder).Copy();
        edit(rule);
        Settings.SetRule(_folder, rule);
        Rebuild();
    }

    /// <summary>Was an der Nummerierung auffällt, als Zeilen wie „Track 7 fehlt". Leer, wenn alles stimmt.</summary>
    private static string Numbering(IReadOnlyList<AudioTrack> tracks)
    {
        var check = TrackNumberCheck.Check(tracks);
        var lines = new List<string>();
        foreach (var d in check.Discs)
        {
            var prefix = d.Disc > 0 ? Strings.T("Disc {0}", d.Disc) + ": " : "";
            var parts = new List<string>();
            if (d.Missing.Count == 1) parts.Add(Strings.T("track {0} missing", d.Missing[0]));
            else if (d.Missing.Count > 1) parts.Add(Strings.T("tracks {0} missing", TrackNumberCheck.Ranges(d.Missing)));
            parts.AddRange(d.Doubled.Select(x => Strings.T("track {0} appears {1}×", x.Track, x.Count)));
            var text = string.Join(", ", parts);
            lines.Add(prefix + (text.Length > 0 ? char.ToUpper(text[0]) + text[1..] : ""));
        }
        if (check.Unnumbered > 0) lines.Add(Strings.T("{0} file(s) without a track number", check.Unnumbered));
        return string.Join("\n", lines);
    }

    // ── Bausteine ────────────────────────────────────────────────

    private void Add(NSView v, float after = 8)
    {
        _stack.AddArrangedSubview(v);
        _stack.FillWidth(v);
        _stack.SetCustomSpacing(after, v);
    }

    private static string Rate(int hz) => hz % 1000 == 0 ? $"{hz / 1000} kHz" : $"{hz / 1000.0:0.0} kHz";

    /// <summary>Ein Wert wie im Informationsfenster: Bezeichnung links, Wert rechts.</summary>
    private static NSView Pair(string key, string value)
    {
        var k = NSTextField.CreateLabel(key);
        k.Font = NSFont.SystemFontOfSize(12);
        k.TextColor = NSColor.SecondaryLabel;
        var v = NSTextField.CreateLabel(value);
        v.Font = NSFont.SystemFontOfSize(12, NSFontWeight.Medium);
        v.Alignment = NSTextAlignment.Right;
        return new NSStackView { Spacing = 8 }.Arranged(k, new NSView(), v);
    }

    /// <summary>Erklärender Text, leiser als der Rest.</summary>
    private static NSTextField Faint(string text)
    {
        var l = Theme.Small(text);
        l.Font = NSFont.SystemFontOfSize(11);
        l.TextColor = NSColor.TertiaryLabel;
        return l;
    }

    /// <summary>Der Befund als Zeile mit Zeichen, auf leicht getönter Fläche.</summary>
    private static NSView Verdict(string text, bool ok)
    {
        var icon = NSImageView.FromImage(NSImage.GetSystemSymbol(ok ? "checkmark.circle.fill" : "exclamationmark.triangle.fill", null)!);
        icon.ContentTintColor = ok ? Theme.Accent : Theme.Warn;
        icon.SetContentHuggingPriorityForOrientation(1000, NSLayoutConstraintOrientation.Horizontal);
        var label = Theme.Small(text, ok ? Theme.Accent : Theme.Warn);
        var row = new NSStackView { Spacing = 8, Alignment = NSLayoutAttribute.Top }.Arranged(icon, label);
        return Theme.Card(row, ok ? Theme.AccentDim : Theme.WarnDim, 9);
    }

    /// <summary>
    /// Formate und Sampleraten als Pillen: grün, was schon dem Ziel entspricht,
    /// gelb, was noch angefasst werden muss.
    /// </summary>
    private static NSView Pills(string caption, IEnumerable<(string Text, bool OnTarget)> items)
    {
        var row = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Spacing = 5 };
        foreach (var (text, onTarget) in items)
        {
            var l = NSTextField.CreateLabel(text);
            l.Font = NSFont.MonospacedSystemFont(10.5f, NSFontWeight.Regular);
            l.TextColor = onTarget ? Theme.Accent : Theme.Warn;
            row.AddArrangedSubview(Theme.Card(l, onTarget ? Theme.AccentDim : Theme.WarnDim, 3));
        }
        var c = Theme.Small(caption);
        c.TextColor = NSColor.TertiaryLabel;
        return new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Alignment = NSLayoutAttribute.Leading,
            Spacing = 4,
        }.Arranged(c, row);
    }

    private static NSButton Button(string title, Action action) => NSButton.CreateButton(title, action);

    /// <summary>Titel links, Schalter rechts, wie in den Systemeinstellungen.</summary>
    private static NSView SwitchRow(string title, bool on, Action<bool> changed)
    {
        var sw = new NSSwitch { State = on ? 1 : 0, ControlSize = NSControlSize.Small };
        sw.Activated += (_, _) => changed(sw.State == 1);
        var label = NSTextField.CreateWrappingLabel(title);
        label.Font = NSFont.SystemFontOfSize(12.5f);
        label.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        return new NSStackView { Spacing = 8, Alignment = NSLayoutAttribute.CenterY }.Arranged(label, new NSView(), sw);
    }

    private static NSView Check(string title, bool value, bool enabled, int indent, Action<bool> changed)
    {
        var b = NSButton.CreateCheckbox(title, () => { });
        b.ControlSize = NSControlSize.Small;
        b.Font = NSFont.SystemFontOfSize(12);
        b.State = value ? NSCellStateValue.On : NSCellStateValue.Off;
        b.Enabled = enabled;
        b.Activated += (_, _) => changed(b.State == NSCellStateValue.On);
        return new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
            EdgeInsets = new NSEdgeInsets(0, 20 * indent, 0, 0),
        }.Arranged(b);
    }
}

/// <summary>Oben beginnend, damit der Inhalt nicht am unteren Rand klebt.</summary>
internal class FlippedStack : NSStackView
{
    public override bool IsFlipped => true;
}
