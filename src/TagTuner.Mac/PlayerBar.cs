using ObjCRuntime;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>
/// Die Leiste unten, wie unter Windows: links Wiedergabe, Lautstärke, Titel
/// und Zeit, in der Mitte der Fortschritt, rechts die Statuszeile mit
/// „Rückgängig" nach jedem Vorgang und einem schmalen Balken, solange
/// geschrieben wird.
/// </summary>
internal sealed class PlayerBar : NSView
{
    public MainWindowController? Owner { get; set; }

    private readonly NSButton _play;
    private readonly NSButton _prev;
    private readonly NSButton _next;
    private readonly NSSlider _volume = new() { MinValue = 0, MaxValue = 1, ControlSize = NSControlSize.Small };
    private readonly NSTextField _title = NSTextField.CreateLabel("");
    private readonly NSTextField _time = NSTextField.CreateLabel("");
    private readonly NSSlider _position = new() { MinValue = 0, MaxValue = 1, ControlSize = NSControlSize.Mini };
    private readonly NSTextField _status = NSTextField.CreateLabel("");
    private readonly NSButton _undo;
    private readonly NSProgressIndicator _work = new()
    {
        Style = NSProgressIndicatorStyle.Bar, Indeterminate = false, MinValue = 0, MaxValue = 100,
        ControlSize = NSControlSize.Small, Hidden = true,
    };

    public PlayerBar()
    {
        NSButton Btn(string symbol, string selector, float size)
        {
            var b = NSButton.CreateButton(NSImage.GetSystemSymbol(symbol, null)!, () => { });
            b.Action = new Selector(selector);
            b.Bordered = false;
            b.SymbolConfiguration = NSImageSymbolConfiguration.Create(size, NSFontWeight.Medium);
            return b;
        }
        _play = Btn("play.fill", "playPause:", 17);
        _prev = Btn("backward.fill", "previousTrack:", 12);
        _next = Btn("forward.fill", "nextTrack:", 12);

        var speaker = NSImageView.FromImage(NSImage.GetSystemSymbol("speaker.wave.2.fill", null)!);
        speaker.ContentTintColor = NSColor.SecondaryLabel;
        _volume.DoubleValue = AppDelegate.Player.Volume;
        _volume.TrackFillColor = Theme.Accent;
        _volume.Activated += (_, _) => AppDelegate.Player.Volume = (float)_volume.DoubleValue;

        _title.Font = NSFont.SystemFontOfSize(12, NSFontWeight.Medium);
        _title.LineBreakMode = NSLineBreakMode.TruncatingTail;
        _time.Font = NSFont.MonospacedDigitSystemFontOfSize(10.5f, NSFontWeight.Regular);
        _time.TextColor = NSColor.SecondaryLabel;
        var now = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Alignment = NSLayoutAttribute.Leading,
            Spacing = 0,
        }.Arranged(_title, _time);

        _position.TrackFillColor = Theme.Accent;
        _position.Activated += (_, _) => AppDelegate.Player.Seek(_position.DoubleValue * AppDelegate.Player.Duration);

        _status.Font = NSFont.SystemFontOfSize(11.5f);
        _status.TextColor = NSColor.SecondaryLabel;
        _status.LineBreakMode = NSLineBreakMode.TruncatingTail;
        _status.SetContentCompressionResistancePriority(100, NSLayoutConstraintOrientation.Horizontal);
        _undo = NSButton.CreateButton(Strings.T("Undo"), () => { });
        _undo.Action = new Selector("undo:");
        _undo.Bordered = false;
        _undo.AttributedTitle = new NSAttributedString(Strings.T("Undo"), new NSStringAttributes
        {
            ForegroundColor = Theme.Accent,
            Font = NSFont.SystemFontOfSize(11.5f),
        });
        _undo.Hidden = true;

        foreach (var v in new NSView[] { _prev, _play, _next, speaker, _volume, now, _position, _work, _status, _undo })
        {
            v.TranslatesAutoresizingMaskIntoConstraints = false;
            AddSubview(v);
        }
        NSLayoutConstraint.ActivateConstraints([
            _prev.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 14),
            _prev.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            _play.LeadingAnchor.ConstraintEqualTo(_prev.TrailingAnchor, 10),
            _play.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            _next.LeadingAnchor.ConstraintEqualTo(_play.TrailingAnchor, 10),
            _next.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            speaker.LeadingAnchor.ConstraintEqualTo(_next.TrailingAnchor, 18),
            speaker.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            _volume.LeadingAnchor.ConstraintEqualTo(speaker.TrailingAnchor, 6),
            _volume.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            _volume.WidthAnchor.ConstraintEqualTo(80),
            now.LeadingAnchor.ConstraintEqualTo(_volume.TrailingAnchor, 16),
            now.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            now.WidthAnchor.ConstraintEqualTo(210),
            _position.LeadingAnchor.ConstraintEqualTo(now.TrailingAnchor, 12),
            _position.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            _position.WidthAnchor.ConstraintGreaterThanOrEqualTo(120),
            _position.WidthAnchor.ConstraintLessThanOrEqualTo(420),
            _work.LeadingAnchor.ConstraintGreaterThanOrEqualTo(_position.TrailingAnchor, 20),
            _work.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            _work.WidthAnchor.ConstraintEqualTo(90),
            _status.LeadingAnchor.ConstraintEqualTo(_work.TrailingAnchor, 10),
            _status.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            _undo.LeadingAnchor.ConstraintEqualTo(_status.TrailingAnchor, 8),
            _undo.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            _undo.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -14),
        ]);
    }

    public void Update(Player p)
    {
        _play.Image = NSImage.GetSystemSymbol(p.IsPlaying ? "pause.fill" : "play.fill", null);
        _title.StringValue = p.Track is { } t
            ? (string.IsNullOrWhiteSpace(t.Title) ? Path.GetFileNameWithoutExtension(t.FileName) : t.Title)
            : "";
        _title.ToolTip = p.Track?.Artist;
        _position.Enabled = _prev.Enabled = _next.Enabled = p.Track is not null;
        UpdateProgress(p);
    }

    public void UpdateProgress(Player p)
    {
        _position.DoubleValue = p.Duration > 0 ? p.Position / p.Duration : 0;
        _time.StringValue = p.Track is null ? "" : $"{Fmt(p.Position)} / {Fmt(p.Duration)}";
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
