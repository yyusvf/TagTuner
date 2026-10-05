using ObjCRuntime;
using TagTuner.Core.Audio;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>
/// Die Leiste unten. Inhaltlich wie unter Windows (Wiedergabe, Lautstärke,
/// Titel und Zeit, Statuszeile mit „Rückgängig"), gestaltet wie ein Player
/// in macOS: eine schwebende Glas-Kapsel, links Cover und Titel, in der Mitte
/// die Steuerung über einem dünnen Fortschritt, rechts Status und Lautstärke.
/// </summary>
internal sealed class PlayerBar : NSView
{
    public MainWindowController? Owner { get; set; }

    private readonly NSButton _play;
    private readonly NSButton _prev;
    private readonly NSButton _next;
    private readonly NSImageView _art = new() { ImageScaling = NSImageScale.ProportionallyUpOrDown, WantsLayer = true };
    private readonly NSTextField _title = NSTextField.CreateLabel("");
    private readonly NSTextField _artist = NSTextField.CreateLabel("");
    private readonly NSTextField _elapsed = NSTextField.CreateLabel("");
    private readonly NSTextField _remaining = NSTextField.CreateLabel("");
    private readonly NSSlider _position = new() { MinValue = 0, MaxValue = 1, ControlSize = NSControlSize.Mini };
    private readonly NSSlider _volume = new() { MinValue = 0, MaxValue = 1, ControlSize = NSControlSize.Mini };
    private readonly NSTextField _status = NSTextField.CreateLabel("");
    private readonly NSButton _undo;
    private readonly NSProgressIndicator _work = new()
    {
        Style = NSProgressIndicatorStyle.Bar, Indeterminate = false, MinValue = 0, MaxValue = 100,
        ControlSize = NSControlSize.Small, Hidden = true,
    };
    private string? _artFor;

    public PlayerBar()
    {
        NSButton Btn(string symbol, string selector, float size)
        {
            var b = NSButton.CreateButton(NSImage.GetSystemSymbol(symbol, null)!, () => { });
            b.Action = new Selector(selector);
            b.Bordered = false;
            b.SymbolConfiguration = NSImageSymbolConfiguration.Create(size, NSFontWeight.Semibold);
            return b;
        }
        _play = Btn("play.fill", "playPause:", 20);
        _prev = Btn("backward.fill", "previousTrack:", 13);
        _next = Btn("forward.fill", "nextTrack:", 13);

        // ── Links: Cover und Titel ───────────────────────────────
        _art.Layer!.CornerRadius = 5;
        _art.Layer.MasksToBounds = true;
        _title.Font = NSFont.SystemFontOfSize(12.5f, NSFontWeight.Medium);
        _artist.Font = NSFont.SystemFontOfSize(11);
        _artist.TextColor = NSColor.SecondaryLabel;
        _title.LineBreakMode = _artist.LineBreakMode = NSLineBreakMode.TruncatingTail;
        var now = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Alignment = NSLayoutAttribute.Leading,
            Spacing = 1,
        }.Arranged(_title, _artist);

        // ── Mitte: Steuerung über dem Fortschritt ────────────────
        var transport = new NSStackView { Spacing = 22, Alignment = NSLayoutAttribute.CenterY }.Arranged(_prev, _play, _next);
        foreach (var t in new[] { _elapsed, _remaining })
        {
            t.Font = NSFont.MonospacedDigitSystemFontOfSize(10, NSFontWeight.Regular);
            t.TextColor = NSColor.TertiaryLabel;
        }
        _remaining.Alignment = NSTextAlignment.Right;
        _elapsed.WidthAnchor.ConstraintEqualTo(34).Active = true;
        _remaining.WidthAnchor.ConstraintEqualTo(38).Active = true;
        _position.TrackFillColor = Theme.Accent;
        _position.Activated += (_, _) => AppDelegate.Player.Seek(_position.DoubleValue * AppDelegate.Player.Duration);
        var progress = new NSStackView { Spacing = 8, Alignment = NSLayoutAttribute.CenterY }.Arranged(_elapsed, _position, _remaining);
        var center = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Alignment = NSLayoutAttribute.CenterX,
            Spacing = 2,
        }.Arranged(transport, progress);
        progress.WidthAnchor.ConstraintEqualTo(center.WidthAnchor).Active = true;

        // ── Rechts: Status mit Rückgängig, dann Lautstärke ───────
        _status.Font = NSFont.SystemFontOfSize(11);
        _status.TextColor = NSColor.SecondaryLabel;
        _status.LineBreakMode = NSLineBreakMode.TruncatingTail;
        _status.Alignment = NSTextAlignment.Right;
        _status.SetContentCompressionResistancePriority(100, NSLayoutConstraintOrientation.Horizontal);
        _undo = NSButton.CreateButton(Strings.T("Undo"), () => { });
        _undo.Action = new Selector("undo:");
        _undo.Bordered = false;
        _undo.AttributedTitle = new NSAttributedString(Strings.T("Undo"), new NSStringAttributes
        {
            ForegroundColor = Theme.Accent,
            Font = NSFont.SystemFontOfSize(11, NSFontWeight.Medium),
        });
        _undo.Hidden = true;
        var speaker = NSImageView.FromImage(NSImage.GetSystemSymbol("speaker.wave.2.fill", null)!);
        speaker.ContentTintColor = NSColor.SecondaryLabel;
        speaker.SymbolConfiguration = NSImageSymbolConfiguration.Create(11, NSFontWeight.Regular);
        _volume.DoubleValue = AppDelegate.Player.Volume;
        _volume.TrackFillColor = Theme.Accent;
        _volume.Activated += (_, _) => AppDelegate.Player.Volume = (float)_volume.DoubleValue;

        // ── Die Kapsel aus Glas ──────────────────────────────────
        var glass = new NSGlassEffectView { CornerRadius = 22 };
        var content = new NSView();
        glass.ContentView = content;
        foreach (var v in new NSView[] { _art, now, center, _work, _status, _undo, speaker, _volume })
        {
            v.TranslatesAutoresizingMaskIntoConstraints = false;
            content.AddSubview(v);
        }
        glass.TranslatesAutoresizingMaskIntoConstraints = false;
        AddSubview(glass);
        NSLayoutConstraint.ActivateConstraints([
            glass.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 12),
            glass.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -12),
            glass.TopAnchor.ConstraintEqualTo(TopAnchor, 6),
            glass.BottomAnchor.ConstraintEqualTo(BottomAnchor, -8),

            _art.LeadingAnchor.ConstraintEqualTo(content.LeadingAnchor, 8),
            _art.CenterYAnchor.ConstraintEqualTo(content.CenterYAnchor),
            _art.WidthAnchor.ConstraintEqualTo(32),
            _art.HeightAnchor.ConstraintEqualTo(32),
            now.LeadingAnchor.ConstraintEqualTo(_art.TrailingAnchor, 10),
            now.CenterYAnchor.ConstraintEqualTo(content.CenterYAnchor),
            now.WidthAnchor.ConstraintEqualTo(220),

            center.CenterXAnchor.ConstraintEqualTo(content.CenterXAnchor),
            center.CenterYAnchor.ConstraintEqualTo(content.CenterYAnchor),
            center.WidthAnchor.ConstraintEqualTo(380),
            center.LeadingAnchor.ConstraintGreaterThanOrEqualTo(now.TrailingAnchor, 16),

            _volume.TrailingAnchor.ConstraintEqualTo(content.TrailingAnchor, -18),
            _volume.CenterYAnchor.ConstraintEqualTo(content.CenterYAnchor),
            _volume.WidthAnchor.ConstraintEqualTo(80),
            speaker.TrailingAnchor.ConstraintEqualTo(_volume.LeadingAnchor, -6),
            speaker.CenterYAnchor.ConstraintEqualTo(content.CenterYAnchor),
            _undo.TrailingAnchor.ConstraintEqualTo(speaker.LeadingAnchor, -20),
            _undo.CenterYAnchor.ConstraintEqualTo(content.CenterYAnchor),
            _status.TrailingAnchor.ConstraintEqualTo(_undo.LeadingAnchor, -6),
            _status.CenterYAnchor.ConstraintEqualTo(content.CenterYAnchor),
            _status.LeadingAnchor.ConstraintGreaterThanOrEqualTo(center.TrailingAnchor, 16),
            _work.TrailingAnchor.ConstraintEqualTo(_status.LeadingAnchor, -8),
            _work.CenterYAnchor.ConstraintEqualTo(content.CenterYAnchor),
            _work.WidthAnchor.ConstraintEqualTo(70),
        ]);
        Update(AppDelegate.Player);
    }

    public void Update(Player p)
    {
        _play.Image = NSImage.GetSystemSymbol(p.IsPlaying ? "pause.fill" : "play.fill", null);
        var t = p.Track;
        _title.StringValue = t is null ? "TagTuner"
            : string.IsNullOrWhiteSpace(t.Title) ? Path.GetFileNameWithoutExtension(t.FileName) : t.Title;
        _title.TextColor = t is null ? NSColor.TertiaryLabel : NSColor.Label;
        _artist.StringValue = t?.Artist ?? "";
        _position.Enabled = _prev.Enabled = _next.Enabled = t is not null;
        LoadArt(t);
        UpdateProgress(p);
    }

    /// <summary>Das Cover des laufenden Lieds, im Hintergrund gelesen; ohne eines eine Note.</summary>
    private void LoadArt(Core.Model.AudioTrack? t)
    {
        if (t?.Path == _artFor) return;
        _artFor = t?.Path;
        _art.Image = NSImage.GetSystemSymbol("music.note", null);
        _art.ContentTintColor = NSColor.TertiaryLabel;
        _art.Layer!.BackgroundColor = NSColor.FromWhite(1, 0.06f).CGColor;
        if (t is not { HasCover: true }) return;
        var path = t.Path;
        Task.Run(() =>
        {
            var img = Covers.Thumbnail(AudioProbe.ReadCover(path)?.Data, 96);
            InvokeOnMainThread(() => { if (_artFor == path && img is not null) _art.Image = img; });
        });
    }

    public void UpdateProgress(Player p)
    {
        _position.DoubleValue = p.Duration > 0 ? p.Position / p.Duration : 0;
        _elapsed.StringValue = p.Track is null ? "" : Fmt(p.Position);
        _remaining.StringValue = p.Track is null ? "" : "-" + Fmt(Math.Max(0, p.Duration - p.Position));
        static string Fmt(double s) => TimeSpan.FromSeconds(s).ToString(@"m\:ss");
    }

    public void Status(string text)
    {
        _status.StringValue = text;
        _status.ToolTip = text;
    }

    public void OfferUndo(bool on) => _undo.Hidden = !on;

    /// <summary>Fortschritt eines Vorgangs in Prozent; null blendet den Balken aus.</summary>
    public void Progress(double? percent)
    {
        _work.Hidden = percent is null;
        if (percent is { } p) _work.DoubleValue = p;
    }
}
