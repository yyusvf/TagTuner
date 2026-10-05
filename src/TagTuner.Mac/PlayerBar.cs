using ObjCRuntime;
using TagTuner.Core.Audio;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>
/// Die Statusleiste unten, wie unter Windows (MainWindow.xaml, „Statusleiste"):
/// links die Wiedergabe, bewusst leise, ein flacher Knopf, Titel und Zeit
/// klein, darunter eine dünne Linie für die Stelle im Lied, zum Springen
/// anklickbar. Die Lautstärke ist nur ein Symbol; der Regler kommt senkrecht
/// darüber, solange der Zeiger dort ist, ein Klick schaltet stumm. In der
/// Mitte der Hinweis, wenn ffmpeg fehlt, rechts in der Ecke Status und
/// „Rückgängig".
/// </summary>
internal sealed class PlayerBar : NSView
{
    public MainWindowController? Owner { get; set; }

    private readonly NSButton _play;
    private readonly NSStackView _now;
    private readonly NSTextField _title = NSTextField.CreateLabel("");
    private readonly NSTextField _time = NSTextField.CreateLabel("");
    private readonly SeekLine _seek = new();
    private readonly HoverButton _volume;
    private readonly NSTextField _ffmpeg = NSTextField.CreateLabel("");
    private readonly NSTextField _status = NSTextField.CreateLabel("");
    private readonly NSButton _undo;
    private readonly NSProgressIndicator _work = new()
    {
        Style = NSProgressIndicatorStyle.Bar, Indeterminate = false, MinValue = 0, MaxValue = 100,
        ControlSize = NSControlSize.Small, Hidden = true,
    };
    private NSPopover? _volumePopover;
    private double _unmuted = 0.5;

    public PlayerBar()
    {
        // ── Abspielen: flach, rund, leise ────────────────────────
        _play = NSButton.CreateButton(NSImage.GetSystemSymbol("play.fill", null)!, () => { });
        _play.Action = new Selector("playPause:");
        _play.Bordered = false;
        _play.SymbolConfiguration = NSImageSymbolConfiguration.Create(13, NSFontWeight.Semibold);
        _play.ContentTintColor = NSColor.SecondaryLabel;
        _play.ToolTip = Strings.T("Play and pause, or press space in the list");

        // ── Titel und Zeit, darunter die Stelle im Lied ──────────
        _title.Font = NSFont.SystemFontOfSize(11.5f);
        _title.TextColor = NSColor.SecondaryLabel;
        _title.LineBreakMode = NSLineBreakMode.TruncatingTail;
        _title.SetContentCompressionResistancePriority(250, NSLayoutConstraintOrientation.Horizontal);
        _time.Font = NSFont.MonospacedDigitSystemFontOfSize(10.5f, NSFontWeight.Regular);
        _time.TextColor = NSColor.TertiaryLabel;
        _time.SetContentHuggingPriorityForOrientation(1000, NSLayoutConstraintOrientation.Horizontal);
        var line = new NSStackView { Spacing = 8 }.Arranged(_title, _time);
        _seek.Seek = f => AppDelegate.Player.Seek(f * AppDelegate.Player.Duration);
        _now = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Alignment = NSLayoutAttribute.Leading,
            Spacing = 3,
        }.ArrangedFill(line, _seek);
        _seek.HeightAnchor.ConstraintEqualTo(10).Active = true;
        _now.Hidden = true;

        // ── Lautstärke: Symbol, Regler beim Darüberfahren ────────
        _volume = new HoverButton();
        _volume.Image = NSImage.GetSystemSymbol("speaker.wave.2.fill", null);
        _volume.Bordered = false;
        _volume.SymbolConfiguration = NSImageSymbolConfiguration.Create(13, NSFontWeight.Regular);
        _volume.ContentTintColor = NSColor.TertiaryLabel;
        _volume.Activated += (_, _) => ToggleMute();
        _volume.Entered = ShowVolume;
        _volume.Hidden = true;

        // ── Mitte und rechts ─────────────────────────────────────
        _ffmpeg.Font = NSFont.SystemFontOfSize(11.5f);
        _ffmpeg.TextColor = Theme.Warn;
        _status.Font = NSFont.SystemFontOfSize(11.5f);
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
            Font = NSFont.SystemFontOfSize(11.5f),
        });
        _undo.Hidden = true;
        UpdateFfmpeg();

        var line2 = new NSBox { BoxType = NSBoxType.NSBoxSeparator };
        foreach (var v in new NSView[] { line2, _play, _now, _volume, _ffmpeg, _work, _status, _undo })
        {
            v.TranslatesAutoresizingMaskIntoConstraints = false;
            AddSubview(v);
        }
        NSLayoutConstraint.ActivateConstraints([
            line2.TopAnchor.ConstraintEqualTo(TopAnchor),
            line2.LeadingAnchor.ConstraintEqualTo(LeadingAnchor),
            line2.TrailingAnchor.ConstraintEqualTo(TrailingAnchor),

            _play.LeadingAnchor.ConstraintEqualTo(LeadingAnchor, 12),
            _play.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            _play.WidthAnchor.ConstraintEqualTo(30),
            _play.HeightAnchor.ConstraintEqualTo(30),
            _now.LeadingAnchor.ConstraintEqualTo(_play.TrailingAnchor, 10),
            _now.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            _now.WidthAnchor.ConstraintEqualTo(230),
            _volume.LeadingAnchor.ConstraintEqualTo(_now.TrailingAnchor, 10),
            _volume.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            _volume.WidthAnchor.ConstraintEqualTo(30),

            _ffmpeg.CenterXAnchor.ConstraintEqualTo(CenterXAnchor),
            _ffmpeg.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),

            _undo.TrailingAnchor.ConstraintEqualTo(TrailingAnchor, -12),
            _undo.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            _status.TrailingAnchor.ConstraintEqualTo(_undo.LeadingAnchor, -4),
            _status.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            _status.WidthAnchor.ConstraintLessThanOrEqualTo(620),
            _status.LeadingAnchor.ConstraintGreaterThanOrEqualTo(_volume.TrailingAnchor, 24),
            _work.TrailingAnchor.ConstraintEqualTo(_status.LeadingAnchor, -8),
            _work.CenterYAnchor.ConstraintEqualTo(CenterYAnchor),
            _work.WidthAnchor.ConstraintEqualTo(70),
        ]);
        Update(AppDelegate.Player);
    }

    /// <summary>Nur zu sehen, wenn ffmpeg fehlt; dass alles in Ordnung ist, muss nicht dastehen.</summary>
    public void UpdateFfmpeg()
    {
        var missing = FfmpegLocator.Find(AppDelegate.Settings.FfmpegPath) is null;
        _ffmpeg.StringValue = missing ? Strings.T("ffmpeg is missing") : "";
        _ffmpeg.Hidden = !missing;
    }

    public void Update(Player p)
    {
        var t = p.Track;
        _play.Image = NSImage.GetSystemSymbol(p.IsPlaying ? "pause.fill" : "play.fill", null);
        _play.Enabled = t is not null;
        _now.Hidden = _volume.Hidden = t is null;
        if (t is not null)
        {
            var title = string.IsNullOrWhiteSpace(t.Title) ? Path.GetFileNameWithoutExtension(t.FileName) : t.Title;
            _title.StringValue = string.IsNullOrWhiteSpace(t.Artist) ? title : $"{title} · {t.Artist}";
        }
        UpdateVolumeIcon(p.Volume);
        UpdateProgress(p);
    }

    public void UpdateProgress(Player p)
    {
        _seek.Fraction = p.Duration > 0 ? p.Position / p.Duration : 0;
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

    // ── Lautstärke ───────────────────────────────────────────────

    private void UpdateVolumeIcon(double v) =>
        _volume.Image = NSImage.GetSystemSymbol(v <= 0 ? "speaker.slash.fill" : v < 0.34 ? "speaker.wave.1.fill"
                                                : v < 0.67 ? "speaker.wave.2.fill" : "speaker.wave.3.fill", null);

    private void SetVolume(double v)
    {
        AppDelegate.Player.Volume = (float)v;
        UpdateVolumeIcon(v);
    }

    /// <summary>Ein Klick aufs Symbol schaltet stumm und wieder zurück, wie unter Windows.</summary>
    private void ToggleMute()
    {
        var v = AppDelegate.Player.Volume;
        if (v > 0) { _unmuted = v; SetVolume(0); }
        else SetVolume(_unmuted > 0 ? _unmuted : 0.5);
    }

    /// <summary>Der senkrechte Regler mit Prozentangabe, über dem Symbol.</summary>
    private void ShowVolume()
    {
        if (_volumePopover is { Shown: true }) return;
        var slider = new NSSlider { MinValue = 0, MaxValue = 1, DoubleValue = AppDelegate.Player.Volume, IsVertical = true };
        slider.TrackFillColor = Theme.Accent;
        var percent = NSTextField.CreateLabel($"{AppDelegate.Player.Volume * 100:0}");
        percent.Font = NSFont.MonospacedDigitSystemFontOfSize(10.5f, NSFontWeight.Regular);
        percent.TextColor = NSColor.TertiaryLabel;
        slider.Activated += (_, _) =>
        {
            SetVolume(slider.DoubleValue);
            percent.StringValue = $"{slider.DoubleValue * 100:0}";
        };
        var stack = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Alignment = NSLayoutAttribute.CenterX,
            Spacing = 6,
            EdgeInsets = new NSEdgeInsets(12, 10, 10, 10),
        }.Arranged(slider, percent);
        slider.HeightAnchor.ConstraintEqualTo(110).Active = true;

        var vc = new HoverViewController(stack);
        _volumePopover = new NSPopover { ContentViewController = vc, Behavior = NSPopoverBehavior.Semitransient, Animates = true };
        _volumePopover.Show(_volume.Bounds, _volume, NSRectEdge.MaxYEdge);

        // Schließen, sobald der Zeiger weder über dem Symbol noch über dem Regler ist.
        NSTimer.CreateRepeatingScheduledTimer(0.25, timer =>
        {
            if (_volumePopover is not { Shown: true }) { timer.Invalidate(); return; }
            if (_volume.Inside || vc.Inside) return;
            _volumePopover.Close();
            timer.Invalidate();
        });
    }

    /// <summary>Ein Knopf, der meldet, wenn der Zeiger darüber ist.</summary>
    private sealed class HoverButton : NSButton
    {
        public Action? Entered;
        public bool Inside;

        public override void UpdateTrackingAreas()
        {
            base.UpdateTrackingAreas();
            foreach (var a in TrackingAreas()) RemoveTrackingArea(a);
            AddTrackingArea(new NSTrackingArea(Bounds,
                NSTrackingAreaOptions.MouseEnteredAndExited | NSTrackingAreaOptions.ActiveInKeyWindow | NSTrackingAreaOptions.InVisibleRect,
                this, null));
        }

        public override void MouseEntered(NSEvent theEvent) { Inside = true; Entered?.Invoke(); }
        public override void MouseExited(NSEvent theEvent) => Inside = false;
    }

    private sealed class HoverViewController(NSView content) : NSViewController
    {
        public bool Inside;

        public override void LoadView()
        {
            var root = new HoverView(this);
            content.TranslatesAutoresizingMaskIntoConstraints = false;
            root.AddSubview(content);
            NSLayoutConstraint.ActivateConstraints([
                content.TopAnchor.ConstraintEqualTo(root.TopAnchor),
                content.BottomAnchor.ConstraintEqualTo(root.BottomAnchor),
                content.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor),
                content.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor),
            ]);
            View = root;
        }

        private sealed class HoverView(HoverViewController owner) : NSView
        {
            public override void UpdateTrackingAreas()
            {
                base.UpdateTrackingAreas();
                foreach (var a in TrackingAreas()) RemoveTrackingArea(a);
                AddTrackingArea(new NSTrackingArea(Bounds,
                    NSTrackingAreaOptions.MouseEnteredAndExited | NSTrackingAreaOptions.ActiveAlways | NSTrackingAreaOptions.InVisibleRect,
                    this, null));
            }

            public override void MouseEntered(NSEvent theEvent) => owner.Inside = true;
            public override void MouseExited(NSEvent theEvent) => owner.Inside = false;
        }
    }

    /// <summary>
    /// Die dünne Linie für die Stelle im Lied. Die Fläche ist höher als die
    /// Linie, damit man sie trifft; Klicken und Ziehen springt.
    /// </summary>
    private sealed class SeekLine : NSView
    {
        private double _fraction;
        private bool _hover;
        public Action<double>? Seek;

        public double Fraction { get => _fraction; set { _fraction = Math.Clamp(value, 0, 1); NeedsDisplay = true; } }

        public override void DrawRect(CGRect dirtyRect)
        {
            var h = _hover ? 4f : 3f;
            var track = new CGRect(0, (Bounds.Height - h) / 2, Bounds.Width, h);
            NSColor.FromWhite(1, 0.12f).SetFill();
            NSBezierPath.FromRoundedRect(track, 1.4f, 1.4f).Fill();
            var fill = new CGRect(track.X, track.Y, track.Width * _fraction, h);
            (_hover ? Theme.Accent : NSColor.TertiaryLabel).SetFill();
            NSBezierPath.FromRoundedRect(fill, 1.4f, 1.4f).Fill();
        }

        public override void UpdateTrackingAreas()
        {
            base.UpdateTrackingAreas();
            foreach (var a in TrackingAreas()) RemoveTrackingArea(a);
            AddTrackingArea(new NSTrackingArea(Bounds,
                NSTrackingAreaOptions.MouseEnteredAndExited | NSTrackingAreaOptions.ActiveInKeyWindow | NSTrackingAreaOptions.InVisibleRect,
                this, null));
        }

        public override void MouseEntered(NSEvent theEvent) { _hover = true; NeedsDisplay = true; }
        public override void MouseExited(NSEvent theEvent) { _hover = false; NeedsDisplay = true; }
        public override void MouseDown(NSEvent theEvent) => Jump(theEvent);
        public override void MouseDragged(NSEvent theEvent) => Jump(theEvent);

        private void Jump(NSEvent e)
        {
            var x = ConvertPointFromView(e.LocationInWindow, null).X;
            Fraction = x / Math.Max(1, Bounds.Width);
            Seek?.Invoke(Fraction);
        }
    }
}
