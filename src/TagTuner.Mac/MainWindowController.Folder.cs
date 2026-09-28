using TagTuner.Core.Audio;
using TagTuner.Core.Folders;
using TagTuner.Core.Metadata;
using TagTuner.Core.Model;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

// Was einem Ordner als Ganzem gilt: seine Album-Regel, der Album-Modus auf
// vorhandene Dateien und die Track-Nummern nach dem Umsortieren.

public sealed partial class MainWindowController
{
    /// <summary>Die Schalter der Ordner-Regel, als Tag der Menüeinträge.</summary>
    internal enum RuleSwitch { AlbumMode = 1, BaseTags, Cover, Numbering, RenameFiles, Reset }

    private FolderRule? CurrentRule => _tracks.Folder is { } f ? Settings.RuleFor(f) : null;

    [Export("toggleRule:")]
    public void ToggleRule(NSMenuItem item)
    {
        if (_tracks.Folder is not { } folder) return;
        if ((RuleSwitch)(int)item.Tag == RuleSwitch.Reset)
        {
            Settings.ClearRule(folder);
            Window.Subtitle = Strings.T("\"{0}\" follows the global setting again", Path.GetFileName(folder));
            return;
        }

        var rule = Settings.RuleFor(folder).Copy();
        switch ((RuleSwitch)(int)item.Tag)
        {
            case RuleSwitch.AlbumMode: rule.AlbumMode = !rule.AlbumMode; break;
            case RuleSwitch.BaseTags: rule.BaseTags = !rule.BaseTags; break;
            case RuleSwitch.Cover: rule.Cover = !rule.Cover; break;
            case RuleSwitch.Numbering: rule.Numbering = !rule.Numbering; break;
            case RuleSwitch.RenameFiles: rule.RenameFiles = !rule.RenameFiles; break;
        }
        Settings.SetRule(folder, rule);
    }

    /// <summary>Häkchen und Ausgrauen der Regel-Einträge, aus ValidateMenuItem.</summary>
    private bool ValidateRuleItem(NSMenuItem item)
    {
        if (CurrentRule is not { } rule) { item.State = NSCellStateValue.Off; return false; }
        var sw = (RuleSwitch)(int)item.Tag;
        var on = sw switch
        {
            RuleSwitch.AlbumMode => rule.AlbumMode,
            RuleSwitch.BaseTags => rule.BaseTags,
            RuleSwitch.Cover => rule.Cover,
            RuleSwitch.Numbering => rule.Numbering,
            RuleSwitch.RenameFiles => rule.RenameFiles,
            _ => false,
        };
        item.State = on ? NSCellStateValue.On : NSCellStateValue.Off;
        return sw switch
        {
            RuleSwitch.AlbumMode => true,
            RuleSwitch.RenameFiles => rule.AlbumMode && rule.Numbering,
            RuleSwitch.Reset => Settings.HasOwnRule(_tracks.Folder!),
            _ => rule.AlbumMode,
        };
    }

    // ── Album-Modus auf vorhandene Dateien ───────────────────────

    [Export("applyAlbumMode:")]
    public async void ApplyAlbumMode(NSObject sender)
    {
        if (_tracks.Folder is not { } folder || _tracks.Tracks.Count == 0) return;
        var rule = Settings.RuleFor(folder);
        if (!rule.AlbumMode) return;

        // Nummeriert wird nach der Playlist-Reihenfolge, auch wenn die Liste
        // gerade anders sortiert ist.
        var ordered = TrackSorting.FolderSort(_tracks.Tracks, Settings.SortByDiscThenTrack);
        var inherited = FolderAnalysis.Of(ordered).Inherited(byMajority: true);
        var (cover, needsCover) = rule.Cover
            ? await Task.Run(() => AlbumCover.Majority(ordered))
            : (null, new HashSet<string>());
        var plan = AlbumPlanner.Plan(ordered, rule, inherited, needsCover, cover is not null);

        if (plan.Count == 0)
        {
            new NSAlert
            {
                MessageText = Strings.T("Album mode"),
                InformativeText = Strings.T("Every file already matches the album mode. Nothing to change."),
            }.BeginSheet(Window);
            return;
        }

        if (!await AlbumSheet.ConfirmAsync(Window, Path.GetFileName(folder), plan, cover)) return;

        var jobs = plan.Select(c =>
        {
            var edit = c.Edit;
            if (cover is not null && c.Changes.Any(x => x.Field == "Cover"))
                edit = edit with { Cover = cover.Data, CoverMimeType = cover.MimeType };
            return (c.Track, edit);
        }).ToList();

        await WriteAsync(jobs, "album", Strings.T("Album mode applied to \"{0}\"", Path.GetFileName(folder)),
                         renameAfter: rule.WritesFileNames);
    }

    // ── Umsortieren ──────────────────────────────────────────────

    /// <summary>
    /// Nach dem Ziehen die Track-Nummern der neuen Reihenfolge schreiben.
    /// Ohne Rückfrage, wie unter Windows: Wer eine Zeile zieht, meint die
    /// neue Reihenfolge, und der Verlauf holt es zurück.
    /// </summary>
    private async void OnReordered(List<AudioTrack> order)
    {
        if (_tracks.Folder is not { } folder) return;
        if (!Settings.RuleFor(folder).WritesNumbers)
        {
            Window.Subtitle = Strings.T("Order changed. Track numbers unchanged "
                + "(track numbering is off for this folder)");
            return;
        }

        var numbers = AlbumPlanner.Numbers(order);
        var jobs = order.Select((t, i) => (t, numbers[i]))
                        .Where(x => x.t.Track != x.Item2)
                        .Select(x => (x.t, new TagEdit { Track = x.Item2 }))
                        .ToList();
        if (jobs.Count == 0) return;

        await WriteAsync(jobs, "tracknumbers", Strings.T("Order changed"),
                         renameAfter: Settings.RuleFor(folder).WritesFileNames);
    }
}

/// <summary>
/// Die Vorschau vor dem Album-Modus: jede Datei, die sich ändert, mit dem,
/// was sich ändert. Erst „Anwenden" schreibt.
/// </summary>
internal static class AlbumSheet
{
#if DEBUG
    /// <summary>Für das Testskript: ohne Blatt gleich bestätigen.</summary>
    public static bool AutoConfirm;
#endif

    public static Task<bool> ConfirmAsync(NSWindow parent, string folder, List<AlbumChange> plan, AudioProbe.Cover? cover)
    {
#if DEBUG
        if (AutoConfirm) return Task.FromResult(true);
#endif
        var done = new TaskCompletionSource<bool>();
        var sheet = new NSWindow(new CGRect(0, 0, 680, 460),
            NSWindowStyle.Titled | NSWindowStyle.Resizable, NSBackingStore.Buffered, false)
        {
            MinSize = new CGSize(520, 320),
        };

        var head = NSTextField.CreateLabel(Strings.T("Apply album mode to \"{0}\"", folder));
        head.Font = NSFont.BoldSystemFontOfSize(15);
        var sub = NSTextField.CreateWrappingLabel(
            Strings.T("{0} file(s)", plan.Count) + " · " + Strings.T("Each file is backed up first."));
        sub.TextColor = NSColor.SecondaryLabel;

        var art = new NSImageView { Image = Covers.Thumbnail(cover?.Data, 200), ImageScaling = NSImageScale.ProportionallyUpOrDown };
        art.Hidden = cover is null || !plan.Any(c => c.Changes.Any(x => x.Field == "Cover"));
        art.WantsLayer = true;
        art.Layer!.CornerRadius = 6;
        art.Layer.MasksToBounds = true;

        var table = new NSTableView { Style = NSTableViewStyle.Inset, RowHeight = 24 };
        table.AddColumn(new NSTableColumn("file") { Title = Strings.T("The file"), Width = 200 });
        table.AddColumn(new NSTableColumn("changes") { Title = Strings.T("Changes"), Width = 420 });
        table.DataSource = new Source(plan.Count);
        table.Delegate = new Delegate(plan);

        var scroll = new NSScrollView { DocumentView = table, HasVerticalScroller = true, AutohidesScrollers = true };

        var cancel = NSButton.CreateButton(Strings.T("Cancel"), () => { parent.EndSheet(sheet); done.TrySetResult(false); });
        cancel.KeyEquivalent = "\u001b";
        var ok = NSButton.CreateButton(Strings.T("Apply"), () => { parent.EndSheet(sheet); done.TrySetResult(true); });
        ok.KeyEquivalent = "\r";

        var root = new NSView();
        foreach (var v in new NSView[] { head, sub, art, scroll, cancel, ok })
        {
            v.TranslatesAutoresizingMaskIntoConstraints = false;
            root.AddSubview(v);
        }
        NSLayoutConstraint.ActivateConstraints([
            head.TopAnchor.ConstraintEqualTo(root.TopAnchor, 20),
            head.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor, 20),
            head.TrailingAnchor.ConstraintLessThanOrEqualTo(art.LeadingAnchor, -12),
            sub.TopAnchor.ConstraintEqualTo(head.BottomAnchor, 4),
            sub.LeadingAnchor.ConstraintEqualTo(head.LeadingAnchor),
            sub.TrailingAnchor.ConstraintLessThanOrEqualTo(art.LeadingAnchor, -12),
            art.TopAnchor.ConstraintEqualTo(root.TopAnchor, 16),
            art.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor, -20),
            art.WidthAnchor.ConstraintEqualTo(56),
            art.HeightAnchor.ConstraintEqualTo(56),
            scroll.TopAnchor.ConstraintEqualTo(root.TopAnchor, 84),
            scroll.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor),
            scroll.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor),
            scroll.BottomAnchor.ConstraintEqualTo(ok.TopAnchor, -14),
            ok.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor, -20),
            ok.BottomAnchor.ConstraintEqualTo(root.BottomAnchor, -16),
            cancel.TrailingAnchor.ConstraintEqualTo(ok.LeadingAnchor, -8),
            cancel.CenterYAnchor.ConstraintEqualTo(ok.CenterYAnchor),
        ]);
        sheet.ContentView = root;
        parent.BeginSheet(sheet, _ => done.TrySetResult(false));
        return done.Task;
    }

    private sealed class Source(int count) : NSTableViewDataSource
    {
        public override nint GetRowCount(NSTableView tableView) => count;
    }

    private sealed class Delegate(List<AlbumChange> plan) : NSTableViewDelegate
    {
        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
        {
            var c = plan[(int)row];
            if (tableColumn.Identifier == "file")
            {
                var f = NSTextField.CreateLabel(c.Track.FileName);
                f.LineBreakMode = NSLineBreakMode.TruncatingMiddle;
                return f;
            }
            var text = string.Join("  ·  ", c.Changes.Select(x => x.Field == "Cover"
                ? Strings.T("Cover")
                : $"{Strings.T(x.Field)}: {(x.From.Length == 0 ? "–" : x.From)} → {x.To}"));
            var l = NSTextField.CreateLabel(text);
            l.TextColor = NSColor.SecondaryLabel;
            l.LineBreakMode = NSLineBreakMode.TruncatingTail;
            l.ToolTip = text;
            return l;
        }
    }
}
