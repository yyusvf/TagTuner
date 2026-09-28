#if DEBUG
using ObjCRuntime;
using TagTuner.Core.Model;

namespace TagTuner.Mac;

// Nur im Debug-Build: Bedienschritte als Startargumente, damit sich die App
// ohne Maus und ohne Bedienungshilfen-Freigabe durchprobieren lässt.
//
//   TagTuner <ordner> --do "select:0,1" --do "set:artist:Neu" --do apply --do undo
//
// Zwischen den Schritten liegt eine kurze Pause, damit Laden und Schreiben
// im Hintergrund fertig werden. Jeder Schritt schreibt eine Zeile nach stderr.

public sealed partial class MainWindowController
{
    public async void RunScript(IReadOnlyList<string> steps)
    {
        await Task.Delay(1200);
        foreach (var step in steps)
        {
            var parts = step.Split(':', 3);
            Console.Error.WriteLine($"SCRIPT {step}");
            try
            {
                switch (parts[0])
                {
                    case "select" when parts[1] == "all": _tracks.SelectAllTracks(); break;
                    case "select": _tracks.SelectRows(parts[1].Split(',').Select(int.Parse)); break;
                    case "set": _inspector.SetField(parts[1], parts.Length > 2 ? parts[2] : ""); break;
                    case "cover": _inspector.SetPendingCover(NSData.FromFile(parts[1])!); break;
                    case "nocover": _inspector.RemoveCover(); break;
                    case "apply": _inspector.Apply(); break;
                    case "undo": Undo(Window); break;
                    case "play": PlayPause(Window); break;
                    case "next": NextTrack(Window); break;
                    case "search": _search!.SearchField.StringValue = parts[1]; _tracks.Filter(parts[1]); break;
                    case "sort": _tracks.SortBy(parts[1], parts.Length > 2 && parts[2] == "desc"); break;
                    case "move": _tracks.MoveRows([.. parts[1].Split(',').Select(int.Parse)], int.Parse(parts[2])); break;
                    case "album": ApplyAlbumMode(Window); break;
                    case "album!": AlbumSheet.AutoConfirm = true; ApplyAlbumMode(Window); break;
                    case "rule": ToggleRule(new NSMenuItem { Tag = int.Parse(parts[1]) }); break;
                    case "rename": RenameFiles(Window); break;
                    case "newtab": ((AppDelegate)NSApplication.SharedApplication.Delegate).NewWindowForTab(null); break;
                    case "confirm": AlbumSheet.AutoConfirm = true; break;
                    case "drop": OnFilesDropped([.. parts[2].Split('|')], int.Parse(parts[1]), false); break;
                    case "copytags": CopyTags(Window); break;
                    case "pastetags": PasteTags(Window); break;
                    case "settings": SettingsWindow.Show(); break;
                    case "history": ShowHistory(Window); break;
                    case "open": OpenFolder(parts[1]); break;
                    case "wait": await Task.Delay(int.Parse(parts[1])); break;
                    case "state":
                        Console.Error.WriteLine("STATE " + string.Join(" | ", _tracks.Tracks.Select(t =>
                            $"{t.TrackLabel}.{t.Title}/{t.Artist}/{t.Album}/{(t.HasCover ? "cover" : "-")}")));
                        break;
                    case "snap": Snap(NSApplication.SharedApplication.KeyWindow ?? Window, parts[1]); break;
                    case "snapall":
                        var n = 0;
                        var list = new List<NSWindow>();
                        NSApplication.SharedApplication.EnumerateWindows(NSWindowListOptions.OrderedFrontToBack,
                            (NSWindow w, ref bool stop) => list.Add(w));
                        foreach (var w in list.Where(w => w.IsVisible && w.Frame.Height > 100)) Snap(w, $"{parts[1]}-{n++}.png");
                        break;
                    case "quit": NSApplication.SharedApplication.Terminate(Window); return;
                }
            }
            catch (Exception ex) { Console.Error.WriteLine($"SCRIPT FAILED {step}: {ex}"); }
            await Task.Delay(700);
        }
    }
}

public sealed partial class MainWindowController
{
    /// <summary>Das eigene Fenster als PNG, ohne Bildschirmaufnahme.</summary>
    private static void Snap(NSWindow w, string path)
    {
        var view = w.ContentView!.Superview ?? w.ContentView!;
        var rep = view.BitmapImageRepForCachingDisplayInRect(view.Bounds)!;
        view.CacheDisplay(view.Bounds, rep);
        var png = rep.RepresentationUsingTypeProperties(NSBitmapImageFileType.Png, new NSDictionary());
        png!.Save(path, true);
        Console.Error.WriteLine($"SNAP {path}");
    }
}

internal sealed partial class TrackListController
{
    /// <summary>Wählt Lieder nach ihrer Stelle in der Liste, Disc-Zeilen nicht mitgezählt.</summary>
    public void SelectRows(IEnumerable<int> tracks)
    {
        var tr = _rows.Select((r, i) => (r, i)).Where(x => x.r is AudioTrack).Select(x => x.i).ToList();
        var set = new NSMutableIndexSet();
        foreach (var t in tracks) if (t < tr.Count) set.Add((nuint)tr[t]);
        _table.SelectRows(set, false);
    }

    public void SortBy(string column, bool descending) =>
        _table.SortDescriptors = [new NSSortDescriptor(column, !descending)];
}

internal sealed partial class InspectorController
{
    public void SetField(string name, string value)
    {
        var f = name switch
        {
            "title" => _title, "artist" => _artist, "album" => _album, "albumartist" => _albumArtist,
            "genre" => _genre, "year" => _year, "track" => _track, "disc" => _disc,
            "composer" => _composer, _ => _comment,
        };
        if (name == "format") { _format.SelectItem(value); UpdatePlan(); return; }
        if (name == "rate") { _rate.SelectItem(value); UpdatePlan(); return; }
        f.StringValue = value;
        UpdatePlan();
    }
}
#endif
