namespace TagTuner.Mac;

/// <summary>
/// Die Farben von TagTuner, wie unter Windows (App.xaml): Lindgrün als
/// Akzent, Gelb für Abweichungen. Das Grün steht auch als AccentColor im
/// Asset-Katalog, damit Auswahl und Schalter es von selbst übernehmen.
/// </summary>
internal static class Theme
{
    public static readonly NSColor Accent = NSColor.FromSrgb(0xC8 / 255f, 0xF5 / 255f, 0x42 / 255f, 1);
    public static readonly NSColor AccentDim = NSColor.FromSrgb(0xC8 / 255f, 0xF5 / 255f, 0x42 / 255f, 0.15f);
    public static readonly NSColor Warn = NSColor.FromSrgb(0xF5 / 255f, 0xC4 / 255f, 0x51 / 255f, 1);
    public static readonly NSColor WarnDim = NSColor.FromSrgb(0xF5 / 255f, 0xC4 / 255f, 0x51 / 255f, 0.13f);

    /// <summary>Schrift auf dem Grün: dunkel, Weiß wäre darauf kaum lesbar.</summary>
    public static readonly NSColor OnAccent = NSColor.FromSrgb(0.08f, 0.1f, 0.02f, 1);

    public static NSTextField Section(string text)
    {
        var l = NSTextField.CreateLabel(text.ToUpper());
        l.Font = NSFont.SystemFontOfSize(10.5f, NSFontWeight.Semibold);
        l.TextColor = NSColor.SecondaryLabel;
        return l;
    }

    public static NSTextField FieldLabel(string text)
    {
        var l = NSTextField.CreateLabel(text);
        l.Font = NSFont.SystemFontOfSize(11);
        l.TextColor = NSColor.SecondaryLabel;
        return l;
    }

    public static NSTextField Small(string text, NSColor? color = null)
    {
        var l = NSTextField.CreateWrappingLabel(text);
        l.Font = NSFont.SystemFontOfSize(11);
        l.TextColor = color ?? NSColor.SecondaryLabel;
        return l;
    }

    /// <summary>Der Hauptknopf in Grün mit dunkler Schrift, wie „Apply" unter Windows.</summary>
    public static void MakePrimary(NSButton b)
    {
        b.BezelColor = Accent;
        b.AttributedTitle = new NSAttributedString(b.Title, new NSStringAttributes
        {
            ForegroundColor = OnAccent,
            Font = NSFont.SystemFontOfSize(NSFont.SystemFontSize, NSFontWeight.Medium),
        });
    }

    /// <summary>Eine Karte mit leicht abgesetztem Grund, wie die Kästen unter Windows.</summary>
    public static NSView Card(NSView content, NSColor? background = null, float padding = 10)
    {
        var box = new NSView { WantsLayer = true };
        box.Layer!.CornerRadius = 8;
        box.Layer.BackgroundColor = (background ?? NSColor.FromWhite(1, 0.045f)).CGColor;
        content.TranslatesAutoresizingMaskIntoConstraints = false;
        box.AddSubview(content);
        NSLayoutConstraint.ActivateConstraints([
            content.TopAnchor.ConstraintEqualTo(box.TopAnchor, padding),
            content.BottomAnchor.ConstraintEqualTo(box.BottomAnchor, -padding),
            content.LeadingAnchor.ConstraintEqualTo(box.LeadingAnchor, padding),
            content.TrailingAnchor.ConstraintEqualTo(box.TrailingAnchor, -padding),
        ]);
        return box;
    }
}
