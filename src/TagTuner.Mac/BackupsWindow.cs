using TagTuner.Core.Audio;
using TagTuner.Core.Safety;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>
/// Die gespeicherten Sicherungen, wie die Seite in den Windows-Einstellungen:
/// jede mit Datei, Vorgang, Zeitpunkt, Größe und dem, was sich seitdem an der
/// Datei geändert hat. Wiederherstellen schreibt die Sicherung zurück und ist
/// selbst wieder rückgängig zu machen; Löschen ist endgültig.
/// </summary>
internal static class BackupsWindow
{
    private static NSWindow? _window;

    /// <summary>Nach dem Wiederherstellen: offene Fenster lesen ihren Ordner neu.</summary>
    public static event Action<IReadOnlyList<string>>? Restored;

    public static void Show()
    {
        if (_window is null)
        {
            _window = new NSWindow(new CGRect(0, 0, 900, 560),
                NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable | NSWindowStyle.Miniaturizable,
                NSBackingStore.Buffered, false)
            {
                Title = Strings.T("Stored backups"),
                ReleasedWhenClosed = false,
                MinSize = new CGSize(640, 360),
            };
            _window.ContentViewController = new Page();
            _window.Center();
        }
        _window.MakeKeyAndOrderFront(null);
    }

    private sealed class Row(BackupItem item) : NSObject
    {
        public BackupItem Item { get; } = item;
        public bool Chosen;
        public string? Changes;
    }

    private sealed class Page : NSViewController
    {
        private readonly NSTableView _table = new();
        private readonly NSTextField _info = Theme.Small("");
        private readonly NSTextField _status = Theme.Small("");
        private readonly NSButton _restore = NSButton.CreateButton(Strings.T("Restore"), () => { });
        private readonly NSButton _delete = NSButton.CreateButton(Strings.T("Delete"), () => { });
        private List<Row> _rows = [];

        private static BackupStore Store => new(AppDelegate.Settings.ResolvedBackupFolder);

        public override void LoadView()
        {
            _table.Style = NSTableViewStyle.Inset;
            _table.RowHeight = 40;
            _table.UsesAlternatingRowBackgroundColors = true;
            _table.AllowsMultipleSelection = true;
            Column("pick", "", 26);
            Column("file", Strings.T("The file"), 260);
            Column("when", "", 120);
            Column("size", Strings.T("Size"), 70);
            Column("changes", Strings.T("Changes"), 360);
            _table.DataSource = new Source(this);
            _table.Delegate = new Delegate(this);
            var scroll = new NSScrollView { DocumentView = _table, HasVerticalScroller = true, AutohidesScrollers = true };

            var open = NSButton.CreateButton(Strings.T("Open backup folder"), () =>
            {
                Directory.CreateDirectory(Store.Folder);
                NSWorkspace.SharedWorkspace.OpenUrl(NSUrl.FromFilename(Store.Folder));
            });
            var deleteAll = NSButton.CreateButton(Strings.T("Delete all backups"), () => Confirm(
                Strings.T("Delete all backups"), () =>
                {
                    var gone = Store.DeleteAll();
                    // Der Verlauf muss es erfahren, sonst bietet er ein Rückgängig an, das ins Leere läuft.
                    AppDelegate.History.ForgetBackups(gone);
                    Reload();
                    _status.StringValue = Strings.T("{0} backup(s) deleted.", gone.Count)
                        + Strings.T(". Affected history entries can no longer be undone.");
                }));
            _restore.Activated += (_, _) => Restore(Chosen());
            Theme.MakePrimary(_restore);
            _delete.Activated += (_, _) => Confirm(Strings.T("Delete the selected backups for good?"), () => Delete(Chosen()));

            var top = new NSStackView { Spacing = 10 }.Arranged(_info, new NSView(), open, deleteAll);
            var bottom = new NSStackView { Spacing = 10 }.Arranged(_status, new NSView(), _delete, _restore);
            _status.SetContentCompressionResistancePriority(100, NSLayoutConstraintOrientation.Horizontal);

            var root = new NSView();
            foreach (var v in new NSView[] { top, scroll, bottom })
            {
                v.TranslatesAutoresizingMaskIntoConstraints = false;
                root.AddSubview(v);
            }
            NSLayoutConstraint.ActivateConstraints([
                top.TopAnchor.ConstraintEqualTo(root.TopAnchor, 14),
                top.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor, 20),
                top.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor, -20),
                scroll.TopAnchor.ConstraintEqualTo(top.BottomAnchor, 10),
                scroll.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor),
                scroll.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor),
                scroll.BottomAnchor.ConstraintEqualTo(bottom.TopAnchor, -10),
                bottom.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor, 20),
                bottom.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor, -20),
                bottom.BottomAnchor.ConstraintEqualTo(root.BottomAnchor, -14),
            ]);
            View = root;
        }

        public override void ViewWillAppear()
        {
            base.ViewWillAppear();
            Reload();
        }

        private void Column(string id, string title, float width) =>
            _table.AddColumn(new NSTableColumn(id) { Title = title, Width = width, Editable = false });

        private void Reload()
        {
            _rows = [.. BackupCatalog.List(Store, AppDelegate.History).Select(i => new Row(i))];
            var bytes = _rows.Sum(r => r.Item.Size);
            _info.StringValue = _rows.Count == 0
                ? Strings.T("No backups stored.")
                : Strings.T("{0} file(s), {1} MB", _rows.Count, $"{bytes / 1024.0 / 1024.0:0.#}");
            _table.ReloadData();
            UpdateButtons();

            // Die Unterschiede kosten je Sicherung zwei Dateien lesen: im Hintergrund.
            var rows = _rows;
            Task.Run(() =>
            {
                foreach (var r in rows.Take(300))
                {
                    var text = Inspect(r.Item);
                    InvokeOnMainThread(() =>
                    {
                        r.Changes = text;
                        var i = _rows.IndexOf(r);
                        if (i >= 0) _table.ReloadData(NSIndexSet.FromIndex(i), NSIndexSet.FromIndex(4));
                    });
                }
            });
        }

        private List<Row> Chosen() => [.. _rows.Where(r => r.Chosen)];

        private void UpdateButtons()
        {
            var n = _rows.Count(r => r.Chosen);
            _restore.Title = n > 0 ? Strings.T("Restore ({0})", n) : Strings.T("Restore");
            Theme.MakePrimary(_restore);
            _delete.Title = n > 0 ? Strings.T("Delete ({0})", n) : Strings.T("Delete");
            _restore.Enabled = _delete.Enabled = n > 0;
        }

        /// <summary>
        /// Liest die Sicherung und die Datei an ihrem Ort und beschreibt den
        /// Unterschied. Genau den würde das Wiederherstellen zurückdrehen.
        /// </summary>
        private static string Inspect(BackupItem item)
        {
            var then = AudioProbe.Read(item.BackupPath);
            if (then is null || item.OriginalPath is not { } original) return "";
            if (!File.Exists(original)) return Strings.T("The file no longer exists there.");
            var now = AudioProbe.Read(original);
            if (now is null) return "";
            var changes = BackupDiff.Compare(then, now,
                then.HasCover ? AudioProbe.ReadCover(item.BackupPath)?.Data : null,
                now.HasCover ? AudioProbe.ReadCover(original)?.Data : null);
            if (changes.Count == 0) return Strings.T("Same as the file now.");
            return string.Join("  ·  ", changes.Select(c => c.Field == "Cover"
                ? Strings.T("Cover changed")
                : Strings.T("{0}: {1} → {2}", Strings.T(c.Field), c.From.Length == 0 ? "–" : c.From,
                            c.To.Length == 0 ? "–" : c.To)));
        }

        private void Restore(List<Row> chosen)
        {
            if (chosen.Count == 0) return;
            var store = Store;
            var files = new List<HistoryFile>();
            var failed = new List<string>();
            // Nie dieselbe Datei zweimal: zurück soll die jüngste der gewählten Sicherungen.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in chosen.Where(r => r.Item.OriginalPath is not null).OrderByDescending(r => r.Item.Created))
            {
                if (!seen.Add(row.Item.OriginalPath!)) continue;
                try
                {
                    AppDelegate.Player.Release([row.Item.OriginalPath!]);
                    files.AddRange(AppDelegate.History.Restore(row.Item.BackupPath, store));
                }
                catch (Exception ex) { failed.Add($"{row.Item.OriginalName}: {ex.Message}"); }
            }
            if (files.Count > 0)
            {
                AppDelegate.History.Add("restore", Strings.T("{0} backup(s) restored", seen.Count - failed.Count), files);
                Restored?.Invoke([.. seen]);
            }
            Reload();
            _status.StringValue = failed.Count == 0
                ? Strings.T("{0} file(s) restored. The history can undo it.", seen.Count)
                : Strings.T("{0} restored, {1} failed: {2}", seen.Count - failed.Count, failed.Count, string.Join("; ", failed));
        }

        private void Delete(List<Row> chosen)
        {
            var gone = new List<string>();
            foreach (var row in chosen)
            {
                try { File.Delete(row.Item.BackupPath); gone.Add(row.Item.BackupPath); } catch { }
            }
            AppDelegate.History.ForgetBackups(gone);
            Reload();
            _status.StringValue = Strings.T("{0} backup(s) deleted.", gone.Count);
        }

        private void Confirm(string question, Action yes)
        {
            var a = new NSAlert { MessageText = question, AlertStyle = NSAlertStyle.Warning };
            a.AddButton(Strings.T("Delete"));
            a.AddButton(Strings.T("Cancel"));
            a.BeginSheetForResponse(View.Window!, r => { if (r == (nint)(long)NSAlertButtonReturn.First) yes(); });
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

        private sealed class Source(Page owner) : NSTableViewDataSource
        {
            public override nint GetRowCount(NSTableView tableView) => owner._rows.Count;
        }

        private sealed class Delegate(Page owner) : NSTableViewDelegate
        {
            public override NSView GetViewForItem(NSTableView tableView, NSTableColumn tableColumn, nint row)
            {
                var r = owner._rows[(int)row];
                switch (tableColumn.Identifier)
                {
                    case "pick":
                        var b = NSButton.CreateCheckbox("", () => { });
                        b.State = r.Chosen ? NSCellStateValue.On : NSCellStateValue.Off;
                        b.Activated += (_, _) => { r.Chosen = b.State == NSCellStateValue.On; owner.UpdateButtons(); };
                        return b;
                    case "file":
                        var name = NSTextField.CreateLabel(r.Item.OriginalName);
                        name.LineBreakMode = NSLineBreakMode.TruncatingMiddle;
                        name.ToolTip = r.Item.OriginalPath ?? Strings.T("The original location is unknown.");
                        var action = Theme.Small(r.Item.Action ?? Strings.T("Not in the history"));
                        action.MaximumNumberOfLines = 1;
                        action.LineBreakMode = NSLineBreakMode.TruncatingTail;
                        return new NSStackView
                        {
                            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
                            Alignment = NSLayoutAttribute.Leading,
                            Spacing = 1,
                        }.Arranged(name, action);
                    case "when":
                        return Theme.Small(When(r.Item.Created));
                    case "size":
                        return Theme.Small($"{r.Item.Size / 1024.0 / 1024.0:0.0} MB");
                    default:
                        var c = Theme.Small(r.Changes ?? Strings.T("calculating…"));
                        c.MaximumNumberOfLines = 2;
                        c.ToolTip = r.Changes;
                        return c;
                }
            }
        }
    }
}
