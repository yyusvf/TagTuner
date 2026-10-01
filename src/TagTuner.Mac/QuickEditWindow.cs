using TagTuner.Core.Audio;
using TagTuner.Core.Model;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>
/// Das kleine Fenster aus dem Finder („Metadaten bearbeiten"), wie unter
/// Windows aus dem Explorer: nur die Metadaten-Spalte für die gewählten
/// Dateien. Nach erfolgreichem Anwenden schließt es sich; bei Fehlern bleibt
/// es offen, damit man es noch einmal versuchen kann.
/// </summary>
internal sealed class QuickEditWindow : NSWindowController
{
    private static readonly List<QuickEditWindow> Open = [];
    private readonly InspectorController _inspector = new();
    private List<AudioTrack> _tracks = [];

    private QuickEditWindow() : base(new NSWindow(new CGRect(0, 0, 320, 760),
        NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable | NSWindowStyle.FullSizeContentView,
        NSBackingStore.Buffered, false))
    {
        Window.ContentViewController = _inspector;
        Window.TitlebarAppearsTransparent = true;
        Window.MinSize = new CGSize(280, 520);
        Window.SetContentSize(new CGSize(320, 760));
        Window.Center();
        Window.WillClose += (_, _) => Open.Remove(this);
        _inspector.ApplyRequested += Apply;
    }

    public static async void Show(IReadOnlyList<string> files)
    {
        var tracks = await Task.Run(() => files.Where(AudioFormats.IsAudioFile).Select(AudioProbe.Read).OfType<AudioTrack>().ToList());
        if (tracks.Count == 0) return;
        var w = new QuickEditWindow { _tracks = tracks };
        Open.Add(w);
        w.Window.Title = tracks.Count == 1 ? tracks[0].FileName : Strings.T("{0} files", tracks.Count);
        w.ShowWindow(null);
        w._inspector.FolderSource = () => (Path.GetDirectoryName(tracks[0].Path), tracks);
        w._inspector.Show(tracks);
    }

    private async void Apply(IReadOnlyList<Job> jobs, string label)
    {
        if (Batch.MissingFfmpeg(jobs))
        {
            new NSAlert
            {
                MessageText = Strings.T("ffmpeg is missing"),
                InformativeText = Strings.T("Converting needs ffmpeg. Install it with Homebrew: brew install ffmpeg"),
            }.BeginSheet(Window);
            return;
        }
        _inspector.Busy = true;
        AppDelegate.Player.Release(jobs.Select(j => j.Track.Path));
        var r = await Batch.RunAsync(jobs, "batch", label);
        _inspector.Busy = false;
        if (r.Errors.Count == 0) { Close(); return; }

        new NSAlert { MessageText = Strings.T("Finished with errors"), InformativeText = string.Join("\n", r.Errors.Take(8)) }
            .BeginSheet(Window);
        var moved = r.Files.Where(f => f.OutputPath is not null).ToDictionary(f => f.Original, f => f.OutputPath!);
        _tracks = await Task.Run(() => _tracks.Select(t => moved.TryGetValue(t.Path, out var to) ? to : t.Path)
            .Select(AudioProbe.Read).OfType<AudioTrack>().ToList());
        _inspector.Show(_tracks);
    }
}
