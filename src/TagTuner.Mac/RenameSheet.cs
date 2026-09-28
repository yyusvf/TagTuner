using TagTuner.Core.Metadata;
using TagTuner.Core.Model;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>
/// Umbenennen nach Muster: oben das Muster mit Knöpfen für die Platzhalter,
/// darunter bei jedem Tastendruck die Vorschau alt → neu.
/// </summary>
internal static class RenameSheet
{
    public static Task<List<(AudioTrack Track, string Target)>?> AskAsync(NSWindow parent, IReadOnlyList<AudioTrack> tracks)
    {
        var done = new TaskCompletionSource<List<(AudioTrack, string)>?>();
        var plan = new List<(AudioTrack Track, string Target)>();

        var sheet = new NSWindow(new CGRect(0, 0, 640, 440),
            NSWindowStyle.Titled | NSWindowStyle.Resizable, NSBackingStore.Buffered, false)
        { MinSize = new CGSize(520, 320) };

        var head = NSTextField.CreateLabel(Strings.T("Rename by metadata"));
        head.Font = NSFont.BoldSystemFontOfSize(15);

        var pattern = new NSTextField { StringValue = AppDelegate.Settings.RenamePattern, BezelStyle = NSTextFieldBezelStyle.Rounded };
        pattern.Font = NSFont.MonospacedSystemFont(NSFont.SystemFontSize, NSFontWeight.Regular);

        Action update = () => { };
        var chips = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Spacing = 4 };
        foreach (var p in FileNaming.Placeholders)
        {
            var b = NSButton.CreateButton(p, () =>
            {
                // An der Schreibmarke einfügen, nicht ans Ende.
                if (pattern.CurrentEditor is { } ed) ed.Replace(ed.SelectedRange, p);
                else pattern.StringValue += p;
                update();
            });
            b.ControlSize = NSControlSize.Small;
            b.Font = NSFont.MonospacedSystemFont(NSFont.SmallSystemFontSize, NSFontWeight.Regular);
            chips.AddArrangedSubview(b);
        }

        var count = NSTextField.CreateLabel("");
        count.TextColor = NSColor.SecondaryLabel;

        var table = new NSTableView { Style = NSTableViewStyle.Inset, RowHeight = 22, UsesAlternatingRowBackgroundColors = true };
        table.AddColumn(new NSTableColumn("from") { Title = Strings.T("The file"), Width = 280 });
        table.AddColumn(new NSTableColumn("to") { Title = Strings.T("Rename"), Width = 300 });
        table.DataSource = new Source(() => plan.Count);
        table.Delegate = new Delegate(() => plan);
        var scroll = new NSScrollView { DocumentView = table, HasVerticalScroller = true, AutohidesScrollers = true };

        var cancel = NSButton.CreateButton(Strings.T("Cancel"), () => { parent.EndSheet(sheet); done.TrySetResult(null); });
        cancel.KeyEquivalent = "\u001b";
        var ok = NSButton.CreateButton(Strings.T("Rename"), () =>
        {
            AppDelegate.Settings.RenamePattern = pattern.StringValue;
            AppDelegate.Settings.Save();
            parent.EndSheet(sheet);
            done.TrySetResult(plan);
        });
        ok.KeyEquivalent = "\r";

        void Update()
        {
            plan = FileNaming.Plan(tracks, pattern.StringValue);
            count.StringValue = plan.Count == 0
                ? Strings.T("Every file already has this name.")
                : Strings.T("{0} of {1} file(s) get a new name.", plan.Count, tracks.Count);
            ok.Enabled = plan.Count > 0;
            table.ReloadData();
        }
        update = Update;
        pattern.Changed += (_, _) => Update();
        Update();

        var root = new NSView();
        foreach (var v in new NSView[] { head, pattern, chips, count, scroll, cancel, ok })
        {
            v.TranslatesAutoresizingMaskIntoConstraints = false;
            root.AddSubview(v);
        }
        NSLayoutConstraint.ActivateConstraints([
            head.TopAnchor.ConstraintEqualTo(root.TopAnchor, 20),
            head.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor, 20),
            pattern.TopAnchor.ConstraintEqualTo(head.BottomAnchor, 12),
            pattern.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor, 20),
            pattern.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor, -20),
            chips.TopAnchor.ConstraintEqualTo(pattern.BottomAnchor, 8),
            chips.LeadingAnchor.ConstraintEqualTo(pattern.LeadingAnchor),
            chips.TrailingAnchor.ConstraintLessThanOrEqualTo(root.TrailingAnchor, -20),
            count.TopAnchor.ConstraintEqualTo(chips.BottomAnchor, 12),
            count.LeadingAnchor.ConstraintEqualTo(pattern.LeadingAnchor),
            scroll.TopAnchor.ConstraintEqualTo(count.BottomAnchor, 6),
            scroll.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor),
            scroll.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor),
            scroll.BottomAnchor.ConstraintEqualTo(ok.TopAnchor, -14),
            ok.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor, -20),
            ok.BottomAnchor.ConstraintEqualTo(root.BottomAnchor, -16),
            cancel.TrailingAnchor.ConstraintEqualTo(ok.LeadingAnchor, -8),
            cancel.CenterYAnchor.ConstraintEqualTo(ok.CenterYAnchor),
        ]);
        sheet.ContentView = root;
        parent.BeginSheet(sheet, _ => done.TrySetResult(null));
        sheet.MakeFirstResponder(pattern);
        return done.Task;
    }

    private sealed class Source(Func<int> count) : NSTableViewDataSource
    {
        public override nint GetRowCount(NSTableView tableView) => count();
    }

    private sealed class Delegate(Func<List<(AudioTrack Track, string Target)>> plan) : NSTableViewDelegate
    {
        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
        {
            var (t, target) = plan()[(int)row];
            var from = tableColumn.Identifier == "from";
            var l = NSTextField.CreateLabel(from ? t.FileName : "→  " + Path.GetFileName(target));
            l.LineBreakMode = NSLineBreakMode.TruncatingMiddle;
            l.TextColor = from ? NSColor.SecondaryLabel : NSColor.Label;
            return l;
        }
    }
}
