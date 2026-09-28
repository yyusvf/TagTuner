using TagTuner.Core.Safety;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>
/// Der Verlauf als Blatt am Hauptfenster: jeder Vorgang eine Zeile, jede mit
/// Sicherung lässt sich zurücknehmen.
/// </summary>
internal static class HistoryWindow
{
    public static void Show(NSWindow parent, Func<HistoryEntry, UndoResult> undo)
    {
        var sheet = new NSWindow(new CGRect(0, 0, 620, 420),
            NSWindowStyle.Titled | NSWindowStyle.Resizable, NSBackingStore.Buffered, false)
        {
            Title = Strings.T("History"),
            MinSize = new CGSize(480, 280),
        };

        var table = new NSTableView
        {
            Style = NSTableViewStyle.Inset,
            UsesAlternatingRowBackgroundColors = false,
            RowHeight = 34,
        };
        table.AddColumn(new NSTableColumn("when") { Title = "", Width = 110 });
        table.AddColumn(new NSTableColumn("what") { Title = "", Width = 360 });
        table.AddColumn(new NSTableColumn("undo") { Title = "", Width = 110 });
        table.HeaderView = null;

        var entries = AppDelegate.History.Entries.ToList();
        var source = new Source(() => entries);
        table.DataSource = source;
        table.Delegate = new Delegate(() => entries, entry =>
        {
            try
            {
                undo(entry);
            }
            catch (Exception ex)
            {
                new NSAlert { MessageText = Strings.T("Undo"), InformativeText = ex.Message }.BeginSheet(sheet);
            }
            entries = AppDelegate.History.Entries.ToList();
            table.ReloadData();
        });

        var scroll = new NSScrollView { DocumentView = table, HasVerticalScroller = true, AutohidesScrollers = true };
        var empty = NSTextField.CreateLabel(Strings.T("Nothing has happened yet."));
        empty.TextColor = NSColor.SecondaryLabel;
        empty.Hidden = entries.Count > 0;

        var close = NSButton.CreateButton(Strings.T("Close"), () => parent.EndSheet(sheet));
        close.KeyEquivalent = "\r";

        var root = new NSView();
        foreach (var v in new NSView[] { scroll, empty, close })
        {
            v.TranslatesAutoresizingMaskIntoConstraints = false;
            root.AddSubview(v);
        }
        NSLayoutConstraint.ActivateConstraints([
            scroll.TopAnchor.ConstraintEqualTo(root.TopAnchor, 12),
            scroll.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor),
            scroll.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor),
            scroll.BottomAnchor.ConstraintEqualTo(close.TopAnchor, -12),
            empty.CenterXAnchor.ConstraintEqualTo(scroll.CenterXAnchor),
            empty.CenterYAnchor.ConstraintEqualTo(scroll.CenterYAnchor),
            close.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor, -20),
            close.BottomAnchor.ConstraintEqualTo(root.BottomAnchor, -16),
        ]);
        sheet.ContentView = root;
        parent.BeginSheet(sheet, _ => { });
    }

    private static string When(DateTime t)
    {
        var diff = DateTime.Now - t;
        if (diff.TotalMinutes < 1) return Strings.T("just now");
        if (diff.TotalMinutes < 60) return Strings.T("{0} min ago", (int)diff.TotalMinutes);
        if (t.Date == DateTime.Today) return Strings.T("today {0}", t.ToString("t"));
        if (t.Date == DateTime.Today.AddDays(-1)) return Strings.T("yesterday {0}", t.ToString("t"));
        return t.ToString("g");
    }

    private sealed class Source(Func<List<HistoryEntry>> entries) : NSTableViewDataSource
    {
        public override nint GetRowCount(NSTableView tableView) => entries().Count;
    }

    private sealed class Delegate(Func<List<HistoryEntry>> entries, Action<HistoryEntry> undo) : NSTableViewDelegate
    {
        public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
        {
            var e = entries()[(int)row];
            switch (tableColumn.Identifier)
            {
                case "when":
                    var w = NSTextField.CreateLabel(When(e.Timestamp));
                    w.TextColor = NSColor.SecondaryLabel;
                    return Centered(w);
                case "what":
                    var t = NSTextField.CreateLabel($"{e.Description}  ·  {Strings.T("{0} file(s)", e.Files.Count)}");
                    t.LineBreakMode = NSLineBreakMode.TruncatingTail;
                    t.ToolTip = string.Join("\n", e.Files.Select(f => f.Original));
                    return Centered(t);
                default:
                    if (!e.CanUndo)
                    {
                        var gone = NSTextField.CreateLabel(Strings.T("No backup left"));
                        gone.TextColor = NSColor.TertiaryLabel;
                        return Centered(gone);
                    }
                    var b = NSButton.CreateButton(Strings.T("Undo"), () => undo(e));
                    b.ControlSize = NSControlSize.Small;
                    return Centered(b);
            }
        }

        /// <summary>Senkrecht mittig in der Zeile, sonst kleben Texte oben.</summary>
        private static NSView Centered(NSView v)
        {
            var box = new NSView();
            v.TranslatesAutoresizingMaskIntoConstraints = false;
            box.AddSubview(v);
            NSLayoutConstraint.ActivateConstraints([
                v.LeadingAnchor.ConstraintEqualTo(box.LeadingAnchor, 2),
                v.TrailingAnchor.ConstraintLessThanOrEqualTo(box.TrailingAnchor, -2),
                v.CenterYAnchor.ConstraintEqualTo(box.CenterYAnchor),
            ]);
            return box;
        }
    }
}
