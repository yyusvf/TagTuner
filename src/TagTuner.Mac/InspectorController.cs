using ObjCRuntime;
using TagTuner.Core.Audio;
using TagTuner.Core.Metadata;
using TagTuner.Core.Model;
using TagTuner.Core.Safety;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>
/// Die Metadaten der Auswahl, rechts im Fenster. Wie unter Windows zeigt ein
/// Feld nur dann einen Wert, wenn sich alle gewählten Dateien einig sind,
/// sonst „&lt;verschieden&gt;". Geschrieben wird nur, was man angefasst hat.
/// </summary>
internal sealed partial class InspectorController : NSViewController
{
    /// <summary>„Anwenden": was in welche Datei soll, und wie es im Verlauf heißt.</summary>
    public event Action<IReadOnlyList<Job>, string>? ApplyRequested;

    /// <summary>Die Sampleraten zur Auswahl, wie unter Windows.</summary>
    private static readonly int[] Rates = [44100, 48000, 88200, 96000, 176400, 192000];

    private readonly NSPopUpButton _format = new();
    private readonly NSPopUpButton _rate = new();

    /// <summary>Was die Auswahl beim Laden hatte; null bei uneinigen Dateien.</summary>
    private string? _loadedFormat;
    private int? _loadedRate;

    private List<AudioTrack> _sel = [];
    private readonly Dictionary<NSTextField, string> _loaded = [];

    private NSTextField _title = null!, _artist = null!, _album = null!, _albumArtist = null!,
        _genre = null!, _year = null!, _track = null!, _disc = null!, _composer = null!, _comment = null!;

    private readonly CoverWell _cover = new();
    private readonly NSTextField _coverInfo = NSTextField.CreateLabel("");
    private readonly NSTextField _head = NSTextField.CreateLabel("");
    private readonly NSStackView _tech = new() { Orientation = NSUserInterfaceLayoutOrientation.Vertical, Alignment = NSLayoutAttribute.Leading, Spacing = 3 };
    private readonly NSTextField _plan = NSTextField.CreateWrappingLabel("");
    private readonly NSButton _apply = NSButton.CreateButton(Strings.T("Apply"), () => { });
    private readonly NSButton _reset = NSButton.CreateButton(Strings.T("Reset"), () => { });
    private readonly NSProgressIndicator _spinner = new() { Style = NSProgressIndicatorStyle.Spinning, ControlSize = NSControlSize.Small, IsDisplayedWhenStopped = false };

    /// <summary>Das Cover, wie es in der Datei steht.</summary>
    private AudioProbe.Cover? _fileCover;

    /// <summary>Neues Cover: null unverändert, leer „entfernen".</summary>
    private (byte[] Data, string Mime)? _pendingCover;

    private bool _busy;

    public override void LoadView()
    {
        _title = Field(); _artist = Field(); _album = Field(); _albumArtist = Field();
        _genre = Field(); _year = Field(numeric: true); _track = Field(numeric: true);
        _disc = Field(numeric: true); _composer = Field(); _comment = Field();

        _head.Font = NSFont.BoldSystemFontOfSize(NSFont.SmallSystemFontSize);
        _head.TextColor = NSColor.SecondaryLabel;

        // ── Cover ────────────────────────────────────────────────
        _cover.Owner = this;
        _cover.TranslatesAutoresizingMaskIntoConstraints = false;
        _coverInfo.Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize);
        _coverInfo.TextColor = NSColor.SecondaryLabel;
        _coverInfo.Alignment = NSTextAlignment.Center;

        var coverMenu = NSButton.CreateButton(NSImage.GetSystemSymbol("ellipsis.circle", null)!, () => { });
        coverMenu.Bordered = false;
        coverMenu.ToolTip = Strings.T("Cover");
        coverMenu.Activated += (_, _) =>
        {
            var m = new NSMenu();
            m.AddItem(new NSMenuItem(Strings.T("Choose a file…"), (_, _) => ChooseCover()));
            m.AddItem(new NSMenuItem(Strings.T("Paste cover"), (_, _) => PasteCover()));
            if (_fileCover is not null)
                m.AddItem(new NSMenuItem(Strings.T("Copy cover"), (_, _) => CopyCover()));
            m.AddItem(NSMenuItem.SeparatorItem);
            m.AddItem(new NSMenuItem(Strings.T("Remove cover"), (_, _) => RemoveCover()));
            m.PopUpMenu(null, new CGPoint(0, coverMenu.Bounds.Height + 4), coverMenu);
        };

        var coverRow = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
            Spacing = 6,
        }.Arranged(_coverInfo, coverMenu);

        // ── Felder ───────────────────────────────────────────────
        var trackDisc = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
            Spacing = 8,
        }.Arranged(_track, Label(Strings.T("Disc")), _disc);
        _track.WidthAnchor.ConstraintEqualTo(_disc.WidthAnchor).Active = true;

        var grid = NSGridView.Create(new NSView[][]
        {
            [Label(Strings.T("Title")), _title],
            [Label(Strings.T("Artist")), _artist],
            [Label(Strings.T("Album")), _album],
            [Label(Strings.T("Album artist")), _albumArtist],
            [Label(Strings.T("Genre")), _genre],
            [Label(Strings.T("Year")), _year],
            [Label(Strings.T("Track")), trackDisc],
            [Label(Strings.T("Composer")), _composer],
            [Label(Strings.T("Comment")), _comment],
            [Label(Strings.T("Format")), _format],
            [Label(Strings.T("Sample rate")), _rate],
        });
        grid.GetRow(9).TopPadding = 14;
        _format.Activated += (_, _) => UpdatePlan();
        _rate.Activated += (_, _) => UpdatePlan();
        grid.RowSpacing = 7;
        grid.ColumnSpacing = 8;
        grid.GetColumn(0).X = NSGridCellPlacement.Trailing;
        grid.RowAlignment = NSGridRowAlignment.FirstBaseline;
        grid.GetRow(5).GetCell(1).X = NSGridCellPlacement.Leading;
        _year.WidthAnchor.ConstraintEqualTo(70).Active = true;

        var techHead = NSTextField.CreateLabel(Strings.T("AUDIO"));
        techHead.Font = NSFont.BoldSystemFontOfSize(NSFont.SmallSystemFontSize);
        techHead.TextColor = NSColor.SecondaryLabel;

        var content = new FlippedStack
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Alignment = NSLayoutAttribute.CenterX,
            Spacing = 10,
            EdgeInsets = new NSEdgeInsets(14, 16, 16, 16),
        }.Arranged(_head, _cover, coverRow, grid, techHead, _tech);
        content.SetCustomSpacing(18, coverRow);
        content.SetCustomSpacing(20, grid);
        content.TranslatesAutoresizingMaskIntoConstraints = false;

        var scroll = new NSScrollView
        {
            HasVerticalScroller = true,
            DrawsBackground = false,
            AutohidesScrollers = true,
            DocumentView = content,
        };
        var clip = scroll.ContentView;
        NSLayoutConstraint.ActivateConstraints([
            content.LeadingAnchor.ConstraintEqualTo(clip.LeadingAnchor),
            content.TrailingAnchor.ConstraintEqualTo(clip.TrailingAnchor),
            content.TopAnchor.ConstraintEqualTo(clip.TopAnchor),
            _head.LeadingAnchor.ConstraintEqualTo(content.LeadingAnchor, 16),
            _cover.WidthAnchor.ConstraintEqualTo(content.WidthAnchor, 1, -64),
            _cover.HeightAnchor.ConstraintEqualTo(_cover.WidthAnchor),
            grid.LeadingAnchor.ConstraintEqualTo(content.LeadingAnchor, 16),
            grid.TrailingAnchor.ConstraintEqualTo(content.TrailingAnchor, -16),
            techHead.LeadingAnchor.ConstraintEqualTo(content.LeadingAnchor, 16),
            _tech.LeadingAnchor.ConstraintEqualTo(content.LeadingAnchor, 16),
            _tech.TrailingAnchor.ConstraintLessThanOrEqualTo(content.TrailingAnchor, -16),
        ]);

        // ── Leiste unten ─────────────────────────────────────────
        _plan.Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize);
        _plan.TextColor = NSColor.SecondaryLabel;
        _plan.MaximumNumberOfLines = 3;

        _apply.KeyEquivalent = "\r";
        _apply.BezelColor = NSColor.ControlAccent;
        _apply.Activated += (_, _) => Apply();
        _reset.Activated += (_, _) => Show(_sel);

        var buttons = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Horizontal,
            Spacing = 8,
        }.Arranged(_spinner, new NSView(), _reset, _apply);
        var bottom = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Alignment = NSLayoutAttribute.Leading,
            Spacing = 8,
            EdgeInsets = new NSEdgeInsets(10, 16, 14, 16),
        }.Arranged(_plan, buttons);
        var line = new NSBox { BoxType = NSBoxType.NSBoxSeparator };

        var root = new NSView();
        foreach (var v in new NSView[] { scroll, line, bottom })
        {
            v.TranslatesAutoresizingMaskIntoConstraints = false;
            root.AddSubview(v);
        }
        NSLayoutConstraint.ActivateConstraints([
            scroll.TopAnchor.ConstraintEqualTo(root.SafeAreaLayoutGuide.TopAnchor),
            scroll.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor),
            scroll.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor),
            scroll.BottomAnchor.ConstraintEqualTo(line.TopAnchor),
            line.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor),
            line.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor),
            line.BottomAnchor.ConstraintEqualTo(bottom.TopAnchor),
            bottom.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor),
            bottom.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor),
            bottom.BottomAnchor.ConstraintEqualTo(root.BottomAnchor),
            buttons.TrailingAnchor.ConstraintEqualTo(bottom.TrailingAnchor, -16),
            _plan.TrailingAnchor.ConstraintEqualTo(bottom.TrailingAnchor, -16),
            root.WidthAnchor.ConstraintGreaterThanOrEqualTo(260),
        ]);
        View = root;
        Show([]);
    }

    private NSTextField Field(bool numeric = false)
    {
        var f = new NSTextField
        {
            BezelStyle = NSTextFieldBezelStyle.Rounded,
            LineBreakMode = NSLineBreakMode.TruncatingTail,
            UsesSingleLineMode = true,
        };
        f.Cell.Scrollable = true;
        f.Cell.Wraps = false;
        f.SetContentHuggingPriorityForOrientation(1, NSLayoutConstraintOrientation.Horizontal);
        if (numeric) f.Alignment = NSTextAlignment.Right;
        f.Changed += (_, _) => UpdatePlan();
        return f;
    }

    private static NSTextField Label(string text)
    {
        var l = NSTextField.CreateLabel(text);
        l.TextColor = NSColor.SecondaryLabel;
        l.Alignment = NSTextAlignment.Right;
        return l;
    }

    private IEnumerable<NSTextField> Fields() =>
        [_title, _artist, _album, _albumArtist, _genre, _year, _track, _disc, _composer, _comment];

    // ── Anzeigen ─────────────────────────────────────────────────

    public bool HasChanges => Fields().Any(f => _loaded.TryGetValue(f, out var was) && f.StringValue != was)
                              || _pendingCover is not null || WantedFormat is not null || WantedRate is not null;

    public void Show(List<AudioTrack> sel)
    {
        _sel = sel;
        _pendingCover = null;
        var any = sel.Count > 0;
        var single = sel.Count == 1;

        foreach (var f in Fields()) f.Enabled = any && !_busy;
        // Titel und Track sind je Datei verschieden, für mehrere gleichzeitig
        // gibt es da nichts Sinnvolles zu schreiben.
        _title.Enabled = _track.Enabled = single && !_busy;

        _head.StringValue = sel.Count > 1
            ? Strings.T("METADATA: {0} TRACKS", sel.Count)
            : Strings.T("METADATA");

        Fill(_title, single ? sel[0].Title : null);
        Fill(_artist, Agree(t => t.Artist));
        Fill(_album, Agree(t => t.Album));
        Fill(_albumArtist, Agree(t => t.AlbumArtist));
        Fill(_genre, Agree(t => t.Genre));
        Fill(_year, Agree(t => t.YearLabel));
        Fill(_track, single ? sel[0].TrackLabel : null);
        Fill(_disc, Agree(t => t.DiscLabel));
        Fill(_composer, Agree(t => t.Composer));
        Fill(_comment, Agree(t => t.Comment));

        _loaded.Clear();
        foreach (var f in Fields()) _loaded[f] = f.StringValue;

        _loadedFormat = any ? Agree(t => AudioFormats.TargetExtension(t.Format).ToUpperInvariant()) : null;
        var rate = any ? Agree(t => t.SampleRate.ToString()) : null;
        _loadedRate = int.TryParse(rate, out var hz) ? hz : null;
        FillPopup(_format, [.. AudioFormats.Targets], _loadedFormat, any);
        FillPopup(_rate, [.. Rates.Select(RateLabel)], _loadedRate is { } r ? RateLabel(r) : null, any);

        ShowCover();
        ShowTech();
        UpdatePlan();

        void Fill(NSTextField f, string? value)
        {
            f.StringValue = any ? value ?? "" : "";
            f.PlaceholderString = any && value is null && f.Enabled ? Strings.T("<mixed>") : "";
        }
    }

    private static string RateLabel(int hz) =>
        hz % 1000 == 0 ? $"{hz / 1000} kHz" : $"{hz / 1000.0:0.0} kHz";

    /// <summary>
    /// Füllt eine Auswahl. Sind sich die Dateien uneinig oder ist der Wert
    /// keiner der Einträge, steht oben „&lt;verschieden&gt;" bzw. der Wert
    /// selbst und ist gewählt; so ändert sich nichts, solange man nichts wählt.
    /// </summary>
    private void FillPopup(NSPopUpButton p, string[] items, string? current, bool enabled)
    {
        p.RemoveAllItems();
        if (!enabled) { p.Enabled = false; return; }
        if (current is null || !items.Contains(current))
        {
            p.AddItem(current ?? Strings.T("<mixed>"));
            p.Menu!.AddItem(NSMenuItem.SeparatorItem);
        }
        p.AddItems(items);
        p.SelectItem(current ?? Strings.T("<mixed>"));
        p.Enabled = !_busy;
    }

    /// <summary>Gewähltes Zielformat, oder null wenn es beim Alten bleibt.</summary>
    private string? WantedFormat =>
        _format.TitleOfSelectedItem is { } f && AudioFormats.Targets.Contains(f) && f != _loadedFormat ? f : null;

    private int? WantedRate
    {
        get
        {
            var i = Array.FindIndex(Rates, r => RateLabel(r) == _rate.TitleOfSelectedItem);
            return i >= 0 && Rates[i] != _loadedRate ? Rates[i] : null;
        }
    }

    private string? Agree(Func<AudioTrack, string> pick)
    {
        var values = _sel.Select(pick).Distinct(StringComparer.Ordinal).Take(2).ToList();
        return values.Count == 1 ? values[0] : null;
    }

    private void ShowCover()
    {
        _fileCover = null;
        if (_sel.Count == 0)
        {
            _cover.Image = null;
            _cover.Mixed = false;
            _coverInfo.StringValue = Strings.T("nothing selected");
            return;
        }

        // Bei Mehrfachauswahl das Cover der ersten zeigen, aber sagen, wenn
        // die anderen ein anderes haben (grob: gleiche Größe gilt als gleich).
        var first = _sel[0];
        var path = first.Path;
        _cover.Image = null;
        _coverInfo.StringValue = "";
        Task.Run(() =>
        {
            var c = AudioProbe.ReadCover(path);
            var mixed = _sel.Count > 1 && _sel.Skip(1).Take(200).Any(t =>
                AudioProbe.ReadCover(t.Path)?.Data.Length != c?.Data.Length);
            var img = Covers.Thumbnail(c?.Data, 800);
            var size = Covers.Size(c?.Data);
            InvokeOnMainThread(() =>
            {
                if (_sel.Count == 0 || _sel[0].Path != path || _pendingCover is not null) return;
                _fileCover = c;
                _cover.Image = img;
                _cover.Mixed = mixed;
                _coverInfo.StringValue = c is null
                    ? (mixed ? Strings.T("<mixed>") : Strings.T("no cover"))
                    : Describe(c.Data, c.MimeType) + (mixed ? " · " + Strings.T("<mixed>") : "");
            });
        });
    }

    private static string Describe(byte[] data, string mime)
    {
        var kind = mime.Replace("image/", "").ToUpperInvariant().Replace("JPEG", "JPG");
        var kb = $"{data.Length / 1024.0:0.#} KB";
        return Covers.Size(data) is { } s ? $"{kind} · {s.Width} × {s.Height} · {kb}" : $"{kind} · {kb}";
    }

    private void ShowTech()
    {
        foreach (var v in _tech.ArrangedSubviews) { _tech.RemoveArrangedSubview(v); v.RemoveFromSuperview(); }
        void Row(string k, string v)
        {
            var l = NSTextField.CreateLabel($"{k}: {v}");
            l.Font = NSFont.SystemFontOfSize(NSFont.SmallSystemFontSize);
            l.TextColor = NSColor.SecondaryLabel;
            l.LineBreakMode = NSLineBreakMode.TruncatingMiddle;
            _tech.AddArrangedSubview(l);
        }

        if (_sel.Count == 0) return;
        if (_sel.Count > 1)
        {
            Row(Strings.T("Selection"), Strings.T("{0} files", _sel.Count));
            var total = TimeSpan.FromTicks(_sel.Sum(t => t.Duration.Ticks));
            if (total > TimeSpan.Zero)
                Row(Strings.T("Total duration"), total.TotalHours >= 1
                    ? Strings.T("{0}:{1:00}:{2:00} hours", (int)total.TotalHours, total.Minutes, total.Seconds)
                    : Strings.T("{0}:{1:00} minutes", (int)total.TotalMinutes, total.Seconds));
            var formats = _sel.Select(t => t.Format).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Row(Strings.T("Formats"), string.Join(", ", formats));
            return;
        }

        var t = _sel[0];
        Row(Strings.T("Format"), string.IsNullOrEmpty(t.Codec) ? t.Format : $"{t.Format} ({t.Codec})");
        Row(Strings.T("Sample rate"), t.SampleRateLabel);
        Row(Strings.T("Channels"), t.Channels.ToString());
        Row(Strings.T("Duration"), t.DurationLabel);
        if (t.Bitrate > 0) Row(Strings.T("Bitrate"), t.BitrateLabel);
        Row(Strings.T("Size"), t.SizeLabel);
        if (t.TagFormat.Length > 0) Row(Strings.T("Tag format"), t.TagFormat);
        Row(Strings.T("File"), t.FileName);
    }

    /// <summary>Sagt vorher an, was „Anwenden" tun würde.</summary>
    private void UpdatePlan()
    {
        if (_sel.Count == 0)
        {
            _plan.StringValue = Strings.T("Nothing selected.");
            _apply.Enabled = _reset.Enabled = false;
            return;
        }

        var jobs = new List<string>();
        if (Fields().Any(f => _loaded.TryGetValue(f, out var was) && f.StringValue != was))
            jobs.Add(Strings.T("write tags"));
        if (_pendingCover is { } pc)
            jobs.Add(pc.Data.Length == 0 ? Strings.T("Remove cover") : Strings.T("Set cover"));
        if (WantedFormat is { } wf) jobs.Add(Strings.T("convert to {0}", wf));
        if (WantedRate is { } wr) jobs.Add(Strings.T("bring to {0}", RateLabel(wr)));

        var files = Strings.T("{0} file(s)", _sel.Count);
        _plan.StringValue = jobs.Count > 0 ? $"{files}: {string.Join(" · ", jobs)}" : files;
        _apply.Enabled = _reset.Enabled = jobs.Count > 0 && !_busy;
    }

    // ── Cover ────────────────────────────────────────────────────

    public void SetPendingCover(NSData data)
    {
        if (_sel.Count == 0) return;
        var prepared = Covers.Prepare(data);
        if (prepared is null) { NSBeep(); return; }
        var cannot = _sel.FirstOrDefault(t => !AudioFormats.CanCarryCover(t.Path));
        if (cannot is not null)
        {
            Alert(Strings.T("{0} cannot store a cover.", cannot.Format), "");
            return;
        }
        _pendingCover = prepared;
        _cover.Image = Covers.Thumbnail(prepared.Value.Data, 800);
        _cover.Mixed = false;
        _coverInfo.StringValue = Describe(prepared.Value.Data, prepared.Value.Mime);
        UpdatePlan();
    }

    public void ChooseCover()
    {
        if (_sel.Count == 0) return;
        var panel = NSOpenPanel.OpenPanel;
        panel.CanChooseFiles = true;
        panel.CanChooseDirectories = false;
        panel.AllowedContentTypes = [UniformTypeIdentifiers.UTTypes.Image];
        if (_sel[0].Path is { } p) panel.DirectoryUrl = NSUrl.FromFilename(Path.GetDirectoryName(p)!);
        panel.BeginSheet(View.Window, r =>
        {
            if (r == (nint)(long)NSModalResponse.OK && panel.Url is { } url && NSData.FromUrl(url) is { } d)
                SetPendingCover(d);
        });
    }

    public void PasteCover()
    {
        var pb = NSPasteboard.GeneralPasteboard;
        var data = pb.GetDataForType(NSPasteboard.NSPasteboardTypePNG)
                   ?? pb.GetDataForType(NSPasteboard.NSPasteboardTypeTIFF);
        if (data is null && pb.ReadObjectsForClasses([new Class(typeof(NSUrl))], null)?.OfType<NSUrl>().FirstOrDefault() is { } url)
            data = NSData.FromUrl(url);
        if (data is null) { Alert(Strings.T("No image in the clipboard"), ""); return; }
        SetPendingCover(data);
    }

    public void CopyCover()
    {
        if (_fileCover is null) return;
        var pb = NSPasteboard.GeneralPasteboard;
        pb.ClearContents();
        var img = Covers.Full(_fileCover.Data);
        if (img is not null) pb.WriteObjects([img]);
    }

    public void RemoveCover()
    {
        if (_sel.Count == 0) return;
        _pendingCover = ([], "");
        _cover.Image = null;
        _cover.Mixed = false;
        _coverInfo.StringValue = Strings.T("no cover");
        UpdatePlan();
    }

    // ── Schreiben ────────────────────────────────────────────────

    public void Revert() => Show(_sel);

    /// <summary>Nur Felder, die tatsächlich verändert wurden. Der Rest bleibt null.</summary>
    private TagEdit BuildEdit()
    {
        string? Changed(NSTextField f) =>
            _loaded.TryGetValue(f, out var was) && f.StringValue == was ? null : f.StringValue;
        uint? Num(NSTextField f)
        {
            var v = Changed(f);
            if (v is null) return null;
            return uint.TryParse(v.Trim(), out var n) ? n : 0u;
        }
        var single = _sel.Count == 1;
        return new TagEdit
        {
            Title = single ? Changed(_title) : null,
            Track = single ? Num(_track) : null,
            Artist = Changed(_artist),
            Album = Changed(_album),
            AlbumArtist = Changed(_albumArtist),
            Genre = Changed(_genre),
            Composer = Changed(_composer),
            Comment = Changed(_comment),
            Year = Num(_year),
            Disc = Num(_disc),
            Cover = _pendingCover?.Data,
            CoverMimeType = _pendingCover?.Mime,
        };
    }

    public void Apply()
    {
        if (_busy || _sel.Count == 0) return;
        View.Window?.MakeFirstResponder(null);   // laufende Eingabe übernehmen
        var edit = BuildEdit();
        var format = WantedFormat;
        var rate = WantedRate;
        var jobs = _sel.Select(t => new Job(t, edit, format, rate)).Where(j => j.Converts || !edit.IsEmpty).ToList();
        if (jobs.Count == 0) return;

        var label = Describe(edit);
        if (format is not null || rate is not null)
        {
            var conv = string.Join(" · ", new[] { format, rate is { } r ? RateLabel(r) : null }.OfType<string>());
            label = label.Length > 0 ? $"{label}, {conv}" : conv;
        }
        ApplyRequested?.Invoke(jobs, label);
    }

    /// <summary>Für den Verlauf: welche Felder, nicht nur wie viele Dateien.</summary>
    private static string Describe(TagEdit e)
    {
        var parts = new List<string>();
        void Add(object? value, string name) { if (value is not null) parts.Add(Strings.T(name)); }
        Add(e.Title, "Title"); Add(e.Artist, "Artist"); Add(e.Album, "Album");
        Add(e.AlbumArtist, "Album artist"); Add(e.Genre, "Genre"); Add(e.Year, "Year");
        Add(e.Track, "Track"); Add(e.Disc, "Disc"); Add(e.Composer, "Composer"); Add(e.Comment, "Comment");
        if (e.Cover is not null) parts.Add(e.Cover.Length == 0 ? Strings.T("Remove cover") : Strings.T("Cover"));
        return string.Join(", ", parts);
    }

    /// <summary>Während geschrieben wird, lässt sich nichts bearbeiten.</summary>
    public bool Busy { get => _busy; set => SetBusy(value); }

    private void SetBusy(bool on)
    {
        _busy = on;
        if (on) _spinner.StartAnimation(this); else _spinner.StopAnimation(this);
        foreach (var f in Fields()) f.Enabled = !on && _sel.Count > 0;
        _format.Enabled = _rate.Enabled = !on && _sel.Count > 0;
        if (!on) _title.Enabled = _track.Enabled = _sel.Count == 1;
        UpdatePlan();
    }

    private void Alert(string message, string info)
    {
        var a = new NSAlert { MessageText = message, InformativeText = info };
        if (View.Window is { } w) a.BeginSheet(w); else a.RunModal();
    }

    private static void NSBeep() => AppKitFramework.NSBeep();

    // ── Hilfsklassen ─────────────────────────────────────────────

    /// <summary>Oben beginnend, damit der Inhalt nicht am unteren Rand klebt.</summary>
    private sealed class FlippedStack : NSStackView
    {
        public override bool IsFlipped => true;
    }

    /// <summary>
    /// Das Cover-Feld: zeigt das Bild und nimmt Bilder oder Bilddateien per
    /// Ziehen an. Ohne Cover ein gestrichelter Rahmen mit Hinweis.
    /// </summary>
    private sealed class CoverWell : NSView
    {
        public InspectorController? Owner;
        private NSImage? _image;
        private bool _mixed, _hover;

        public NSImage? Image { get => _image; set { _image = value; NeedsDisplay = true; } }
        public bool Mixed { get => _mixed; set { _mixed = value; NeedsDisplay = true; } }

        public CoverWell()
        {
            RegisterForDraggedTypes([NSPasteboard.NSPasteboardTypeFileUrl, NSPasteboard.NSPasteboardTypePNG,
                                     NSPasteboard.NSPasteboardTypeTIFF]);
            ToolTip = Strings.T("Right click: set or remove cover");
        }

        public override void DrawRect(CGRect dirtyRect)
        {
            var r = Bounds.Inset(1, 1);
            var path = NSBezierPath.FromRoundedRect(r, 10, 10);
            if (_image is not null)
            {
                // Seitenverhältnis halten: ein Querformat liegt mittig im Quadrat.
                var s = _image.Size;
                var k = (nfloat)Math.Min(r.Width / Math.Max(1, s.Width), r.Height / Math.Max(1, s.Height));
                var fit = new CGRect(r.GetMidX() - s.Width * k / 2, r.GetMidY() - s.Height * k / 2, s.Width * k, s.Height * k);
                if (fit.Width < r.Width - 1 || fit.Height < r.Height - 1)
                {
                    NSColor.QuaternarySystemFill.SetFill();
                    path.Fill();
                }
                NSGraphicsContext.GlobalSaveGraphicsState();
                path.AddClip();
                _image.Draw(fit, CGRect.Empty, NSCompositingOperation.SourceOver, 1);
                NSGraphicsContext.GlobalRestoreGraphicsState();
                NSColor.Separator.SetStroke();
                path.LineWidth = 1;
                path.Stroke();
            }
            else
            {
                NSColor.QuaternarySystemFill.SetFill();
                path.Fill();
                var sym = NSImage.GetSystemSymbol("photo", null);
                if (sym is not null)
                {
                    var cfg = NSImageSymbolConfiguration.Create(36, NSFontWeight.Light);
                    var img = sym.GetImage(cfg) ?? sym;
                    var tinted = (NSImage)img.Copy();
                    tinted.LockFocus();
                    NSColor.TertiaryLabel.Set();
                    NSGraphics.RectFill(new CGRect(CGPoint.Empty, tinted.Size), NSCompositingOperation.SourceAtop);
                    tinted.UnlockFocus();
                    var s = tinted.Size;
                    tinted.Draw(new CGRect(r.GetMidX() - s.Width / 2, r.GetMidY() - s.Height / 2, s.Width, s.Height));
                }
            }
            if (_hover)
            {
                NSColor.ControlAccent.SetStroke();
                path.LineWidth = 3;
                path.Stroke();
            }
        }

        public override void RightMouseDown(NSEvent theEvent)
        {
            if (Owner is null) return;
            var m = new NSMenu();
            m.AddItem(new NSMenuItem(Strings.T("Choose a file…"), (_, _) => Owner.ChooseCover()));
            m.AddItem(new NSMenuItem(Strings.T("Paste cover"), (_, _) => Owner.PasteCover()));
            if (Owner._fileCover is not null)
                m.AddItem(new NSMenuItem(Strings.T("Copy cover"), (_, _) => Owner.CopyCover()));
            m.AddItem(NSMenuItem.SeparatorItem);
            m.AddItem(new NSMenuItem(Strings.T("Remove cover"), (_, _) => Owner.RemoveCover()));
            NSMenu.PopUpContextMenu(m, theEvent, this);
        }

        public override void MouseDown(NSEvent theEvent)
        {
            if (theEvent.ClickCount == 2) Owner?.ChooseCover();
        }

        public override NSDragOperation DraggingEntered(INSDraggingInfo sender)
        {
            if (Owner is null || Owner._sel.Count == 0) return NSDragOperation.None;
            _hover = true; NeedsDisplay = true;
            return NSDragOperation.Copy;
        }

        public override void DraggingExited(INSDraggingInfo? sender) { _hover = false; NeedsDisplay = true; }

        public override bool PerformDragOperation(INSDraggingInfo sender)
        {
            _hover = false; NeedsDisplay = true;
            var pb = sender.DraggingPasteboard;
            var data = pb.GetDataForType(NSPasteboard.NSPasteboardTypePNG)
                       ?? pb.GetDataForType(NSPasteboard.NSPasteboardTypeTIFF);
            if (data is null && pb.ReadObjectsForClasses([new Class(typeof(NSUrl))], null)?.OfType<NSUrl>().FirstOrDefault() is { } url)
                data = NSData.FromUrl(url);
            if (data is null) return false;
            Owner?.SetPendingCover(data);
            return true;
        }
    }
}
