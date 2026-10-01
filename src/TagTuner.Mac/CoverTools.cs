using TagTuner.Core.Audio;
using TagTuner.Core.Model;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>
/// Die Cover-Werkzeuge wie unter Windows: ein nicht quadratisches Bild
/// zuschneiden, ein Cover verkleinern, eines der Cover aus dem Ordner wählen.
/// Jedes ist ein Blatt am Fenster und liefert die fertigen Bytes, oder null
/// bei Abbrechen.
/// </summary>
internal static class CoverTools
{
    private static readonly int[] Edges = [500, 600, 800, 1000, 1200, 1400, 1600, 3000];

    // ── Zuschneiden ──────────────────────────────────────────────

    /// <summary>Ein Quadrat aus einem Bild wählen, das keines ist.</summary>
    public static Task<(byte[] Data, string Mime)?> CropAsync(NSWindow parent, byte[] data)
    {
        var done = new TaskCompletionSource<(byte[], string)?>();
        if (Covers.Decode(data) is not { } image) { done.SetResult(null); return done.Task; }

        var view = new CropView(image);
        var size = Math.Min(image.Width, image.Height);
        var info = Theme.Small("");
        var slider = new NSSlider { MinValue = Math.Min(100, size), MaxValue = size, DoubleValue = size };
        void Info() => info.StringValue = Strings.T("Selection: {0} × {0} pixels", (int)view.Edge)
                                         + "   ·   " + Strings.T("Original {0} × {1} · {2}", image.Width, image.Height,
                                             $"{data.Length / 1024.0:0.#} KB");
        slider.Activated += (_, _) => { view.Edge = slider.DoubleValue; Info(); };
        view.Changed = Info;
        Info();

#if DEBUG
        if (AlbumSheet.AutoConfirm) return Task.FromResult(Covers.Render(image, view.Selection, 3000, Covers.IsPng(data)));
#endif
        var sheet = Sheet(Strings.T("Choose the cover area"), 620, 620, out var body, out var ok, out var cancel, parent, done);
        ok.Activated += (_, _) =>
        {
            parent.EndSheet(sheet);
            done.TrySetResult(Covers.Render(image, view.Selection, 3000, Covers.IsPng(data)));
        };

        foreach (var v in new NSView[] { view, slider, info })
        {
            v.TranslatesAutoresizingMaskIntoConstraints = false;
            body.AddSubview(v);
        }
        NSLayoutConstraint.ActivateConstraints([
            view.TopAnchor.ConstraintEqualTo(body.TopAnchor),
            view.LeadingAnchor.ConstraintEqualTo(body.LeadingAnchor),
            view.TrailingAnchor.ConstraintEqualTo(body.TrailingAnchor),
            slider.TopAnchor.ConstraintEqualTo(view.BottomAnchor, 12),
            slider.LeadingAnchor.ConstraintEqualTo(body.LeadingAnchor),
            slider.TrailingAnchor.ConstraintEqualTo(body.TrailingAnchor),
            info.TopAnchor.ConstraintEqualTo(slider.BottomAnchor, 6),
            info.LeadingAnchor.ConstraintEqualTo(body.LeadingAnchor),
            info.TrailingAnchor.ConstraintEqualTo(body.TrailingAnchor),
            info.BottomAnchor.ConstraintEqualTo(body.BottomAnchor),
        ]);
        parent.BeginSheet(sheet, _ => done.TrySetResult(null));
        return done.Task;
    }

    /// <summary>
    /// Das Bild mit einem verschiebbaren Quadrat. Außerhalb ist abgedunkelt,
    /// damit man sieht, was wegfällt. Die Auswahl rechnet in Bildpixeln,
    /// Ursprung oben links, wie CGImage beim Ausschneiden.
    /// </summary>
    private sealed class CropView(CGImage image) : NSView
    {
        private readonly NSImage _shown = new(image, new CGSize(image.Width, image.Height));
        private CGPoint _origin = new((image.Width - Math.Min(image.Width, image.Height)) / 2.0,
                                      (image.Height - Math.Min(image.Width, image.Height)) / 2.0);
        private double _edge = Math.Min(image.Width, image.Height);
        private CGPoint? _grab;
        public Action? Changed;

        public override bool IsFlipped => true;

        public double Edge
        {
            get => _edge;
            set
            {
                // Um die Mitte wachsen und schrumpfen, nicht um die Ecke.
                var cx = _origin.X + _edge / 2;
                var cy = _origin.Y + _edge / 2;
                _edge = Math.Clamp(value, 50, Math.Min(image.Width, image.Height));
                Place(cx - _edge / 2, cy - _edge / 2);
            }
        }

        public CGRect Selection => new(_origin.X, _origin.Y, _edge, _edge);

        private void Place(double x, double y)
        {
            _origin = new CGPoint(Math.Clamp(x, 0, image.Width - _edge), Math.Clamp(y, 0, image.Height - _edge));
            NeedsDisplay = true;
            Changed?.Invoke();
        }

        /// <summary>Wo das Bild in der View liegt, und der Maßstab Pixel → Punkte.</summary>
        private (CGRect Rect, double Scale) Fit()
        {
            var k = Math.Min(Bounds.Width / image.Width, Bounds.Height / image.Height);
            var w = image.Width * k;
            var h = image.Height * k;
            return (new CGRect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h), k);
        }

        public override void DrawRect(CGRect dirtyRect)
        {
            var (r, k) = Fit();
            _shown.Draw(r, CGRect.Empty, NSCompositingOperation.SourceOver, 1, true, null);
            var sel = new CGRect(r.X + _origin.X * k, r.Y + _origin.Y * k, _edge * k, _edge * k);

            var dim = NSBezierPath.FromRect(r);
            dim.AppendPath(NSBezierPath.FromRect(sel));
            dim.WindingRule = NSWindingRule.EvenOdd;
            NSColor.FromWhite(0, 0.55f).SetFill();
            dim.Fill();

            Theme.Accent.SetStroke();
            var frame = NSBezierPath.FromRect(sel);
            frame.LineWidth = 2;
            frame.Stroke();
        }

        public override void MouseDown(NSEvent theEvent) => _grab = ConvertPointFromView(theEvent.LocationInWindow, null);

        public override void MouseDragged(NSEvent theEvent)
        {
            if (_grab is not { } from) return;
            var now = ConvertPointFromView(theEvent.LocationInWindow, null);
            var (_, k) = Fit();
            Place(_origin.X + (now.X - from.X) / k, _origin.Y + (now.Y - from.Y) / k);
            _grab = now;
        }

        public override void MouseUp(NSEvent theEvent) => _grab = null;
    }

    // ── Verkleinern ──────────────────────────────────────────────

    /// <summary>Kantenlänge und JPEG-Qualität wählen, vorher und nachher im Blick.</summary>
    public static Task<(byte[] Data, string Mime)?> ResizeAsync(NSWindow parent, byte[] data)
    {
        var done = new TaskCompletionSource<(byte[], string)?>();
        if (Covers.Decode(data) is not { } image) { done.SetResult(null); return done.Task; }

        var edge = new NSPopUpButton();
        var options = Edges.Where(e => e < Math.Max(image.Width, image.Height)).Append(Math.Max((int)image.Width, (int)image.Height)).Distinct().ToArray();
        edge.AddItems([.. options.Select(e => Strings.T("{0} × {0} pixels", e))]);
        edge.SelectItem(Array.IndexOf(options, options.FirstOrDefault(e => e >= 1000, options[^1])));
        var quality = new NSSlider { MinValue = 50, MaxValue = 100, DoubleValue = 90 };
        var qualityLabel = Theme.Small("");
        var before = Theme.Small(Strings.T("Before {0} KB ({1} × {2}, {3})", $"{data.Length / 1024.0:0.#}",
            image.Width, image.Height, Covers.IsPng(data) ? "PNG" : "JPEG"));
        var after = Theme.Small("");
        (byte[], string)? result = null;

        void Update()
        {
            var e = options[(int)edge.IndexOfSelectedItem];
            qualityLabel.StringValue = $"{(int)quality.DoubleValue} %";
            result = Covers.Render(image, new CGRect(0, 0, image.Width, image.Height), e, false, quality.DoubleValue / 100);
            if (result is not { } r) { after.StringValue = ""; return; }
            var saved = 100 - r.Item1.Length * 100.0 / data.Length;
            after.StringValue = Strings.T("After {0} KB as JPEG", $"{r.Item1.Length / 1024.0:0.#}") + " · "
                + (saved > 0 ? Strings.T("{0} % smaller", $"{saved:0}") : Strings.T("larger than the original"));
            after.TextColor = saved > 0 ? Theme.Accent : Theme.Warn;
        }
        edge.Activated += (_, _) => Update();
        quality.Activated += (_, _) => Update();
        Update();

#if DEBUG
        if (AlbumSheet.AutoConfirm) return Task.FromResult(result);
#endif
        var sheet = Sheet(Strings.T("Resize cover"), 460, 230, out var body, out var ok, out _, parent, done);
        ok.Activated += (_, _) => { parent.EndSheet(sheet); done.TrySetResult(result); };

        var grid = NSGridView.Create(new NSView[][]
        {
            [Theme.FieldLabel(Strings.T("Maximum edge length")), edge],
            [Theme.FieldLabel(Strings.T("JPEG quality")), new NSStackView { Spacing = 8 }.Arranged(quality, qualityLabel)],
        });
        grid.RowSpacing = 10;
        grid.GetColumn(0).X = NSGridCellPlacement.Trailing;
        grid.RowAlignment = NSGridRowAlignment.FirstBaseline;
        quality.WidthAnchor.ConstraintEqualTo(180).Active = true;
        var stack = new NSStackView
        {
            Orientation = NSUserInterfaceLayoutOrientation.Vertical,
            Alignment = NSLayoutAttribute.Leading,
            Spacing = 10,
        }.Arranged(grid, before, after);
        stack.TranslatesAutoresizingMaskIntoConstraints = false;
        body.AddSubview(stack);
        NSLayoutConstraint.ActivateConstraints([
            stack.TopAnchor.ConstraintEqualTo(body.TopAnchor),
            stack.LeadingAnchor.ConstraintEqualTo(body.LeadingAnchor),
            stack.TrailingAnchor.ConstraintLessThanOrEqualTo(body.TrailingAnchor),
            stack.BottomAnchor.ConstraintLessThanOrEqualTo(body.BottomAnchor),
        ]);
        parent.BeginSheet(sheet, _ => done.TrySetResult(null));
        return done.Task;
    }

    // ── Aus dem Ordner wählen ────────────────────────────────────

    /// <summary>
    /// Alle verschiedenen Cover, die im Ordner vorkommen, dazu Bilddateien
    /// im Ordner. Ein Klick wählt.
    /// </summary>
    public static async Task<(byte[] Data, string Mime)?> FromFolderAsync(NSWindow parent, string folder, IReadOnlyList<AudioTrack> tracks)
    {
        var found = await Task.Run(() =>
        {
            var list = new List<(byte[] Data, string Mime, string From)>();
            var seen = new HashSet<string>();
            foreach (var t in tracks.Where(t => t.HasCover))
            {
                if (AudioProbe.ReadCover(t.Path) is not { Data.Length: > 0 } c) continue;
                if (seen.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(c.Data))))
                    list.Add((c.Data, c.MimeType, t.FileName));
            }
            try
            {
                foreach (var f in Directory.EnumerateFiles(folder)
                             .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png"))
                {
                    var d = File.ReadAllBytes(f);
                    if (seen.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(d))))
                        list.Add((d, Covers.IsPng(d) ? "image/png" : "image/jpeg", Path.GetFileName(f)));
                }
            }
            catch { }
            return list;
        });

        if (found.Count == 0)
        {
            new NSAlert { MessageText = Strings.T("No file in this folder has a cover yet.") }.BeginSheet(parent);
            return null;
        }

        var done = new TaskCompletionSource<(byte[], string)?>();
        var sheet = Sheet(Strings.T("Choose from this folder…").TrimEnd('…'), 560, 300, out var body, out var ok, out _, parent, done);
        ok.Hidden = true;

        var row = new NSStackView { Orientation = NSUserInterfaceLayoutOrientation.Horizontal, Spacing = 14, Alignment = NSLayoutAttribute.Top };
        foreach (var (bytes, mime, from) in found.Take(12))
        {
            var b = NSButton.CreateButton(Covers.Thumbnail(bytes, 300) ?? new NSImage(), () =>
            {
                parent.EndSheet(sheet);
                done.TrySetResult((bytes, mime));
            });
            b.Bordered = false;
            b.ImageScaling = NSImageScale.ProportionallyUpOrDown;
            b.WidthAnchor.ConstraintEqualTo(140).Active = true;
            b.HeightAnchor.ConstraintEqualTo(140).Active = true;
            var size = Covers.Size(bytes);
            var label = Theme.Small($"{from}\n{size?.Width} × {size?.Height} · {bytes.Length / 1024.0:0.#} KB");
            label.Alignment = NSTextAlignment.Center;
            label.WidthAnchor.ConstraintEqualTo(140).Active = true;
            row.AddArrangedSubview(new NSStackView
            {
                Orientation = NSUserInterfaceLayoutOrientation.Vertical,
                Spacing = 6,
            }.Arranged(b, label));
        }
        var scroll = new NSScrollView { DocumentView = row, HasHorizontalScroller = true, AutohidesScrollers = true, DrawsBackground = false };
        row.TranslatesAutoresizingMaskIntoConstraints = false;
        scroll.TranslatesAutoresizingMaskIntoConstraints = false;
        body.AddSubview(scroll);
        NSLayoutConstraint.ActivateConstraints([
            scroll.TopAnchor.ConstraintEqualTo(body.TopAnchor),
            scroll.LeadingAnchor.ConstraintEqualTo(body.LeadingAnchor),
            scroll.TrailingAnchor.ConstraintEqualTo(body.TrailingAnchor),
            scroll.BottomAnchor.ConstraintEqualTo(body.BottomAnchor),
            row.TopAnchor.ConstraintEqualTo(scroll.ContentView.TopAnchor),
            row.LeadingAnchor.ConstraintEqualTo(scroll.ContentView.LeadingAnchor),
        ]);
        parent.BeginSheet(sheet, _ => done.TrySetResult(null));
        return await done.Task;
    }

    // ── Gemeinsamer Rahmen ───────────────────────────────────────

    /// <summary>Ein Blatt mit Titel, Inhaltsfläche und Abbrechen/Anwenden unten.</summary>
    private static NSWindow Sheet<T>(string title, float width, float height, out NSView body, out NSButton ok,
                                     out NSButton cancel, NSWindow parent, TaskCompletionSource<T?> done)
    {
        var sheet = new NSWindow(new CGRect(0, 0, width, height), NSWindowStyle.Titled, NSBackingStore.Buffered, false);
        var head = NSTextField.CreateLabel(title);
        head.Font = NSFont.BoldSystemFontOfSize(15);
        body = new NSView();
        var cancelButton = NSButton.CreateButton(Strings.T("Cancel"), () => { });
        cancelButton.KeyEquivalent = "\u001b";
        cancelButton.Activated += (_, _) => { parent.EndSheet(sheet); done.TrySetResult(default); };
        cancel = cancelButton;
        ok = NSButton.CreateButton(Strings.T("Apply"), () => { });
        ok.KeyEquivalent = "\r";
        Theme.MakePrimary(ok);

        var root = new NSView();
        foreach (var v in new NSView[] { head, body, cancel, ok })
        {
            v.TranslatesAutoresizingMaskIntoConstraints = false;
            root.AddSubview(v);
        }
        NSLayoutConstraint.ActivateConstraints([
            head.TopAnchor.ConstraintEqualTo(root.TopAnchor, 18),
            head.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor, 20),
            body.TopAnchor.ConstraintEqualTo(head.BottomAnchor, 14),
            body.LeadingAnchor.ConstraintEqualTo(root.LeadingAnchor, 20),
            body.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor, -20),
            body.BottomAnchor.ConstraintEqualTo(ok.TopAnchor, -16),
            ok.TrailingAnchor.ConstraintEqualTo(root.TrailingAnchor, -20),
            ok.BottomAnchor.ConstraintEqualTo(root.BottomAnchor, -16),
            cancel.TrailingAnchor.ConstraintEqualTo(ok.LeadingAnchor, -8),
            cancel.CenterYAnchor.ConstraintEqualTo(ok.CenterYAnchor),
            root.WidthAnchor.ConstraintEqualTo(width),
            root.HeightAnchor.ConstraintEqualTo(height),
        ]);
        sheet.ContentView = root;
        return sheet;
    }
}
