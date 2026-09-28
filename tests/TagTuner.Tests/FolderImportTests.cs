using TagTuner.Core.Audio;
using TagTuner.Core.Folders;
using TagTuner.Core.Safety;
using TagTuner.Core.Settings;

namespace TagTuner.Tests;

public class FolderImportTests
{
    [Fact]
    public async Task Dropped_in_the_middle_takes_the_folder_tags_and_the_rest_moves_up()
    {
        using var ws = new Workspace();
        ws.Wav("album/01.wav", "Eins", "Kollektiv", "Nachtfahrt", track: 1);
        ws.Wav("album/02.wav", "Zwei", "Kollektiv", "Nachtfahrt", track: 2);
        var incoming = ws.Wav("elsewhere/neu.wav", "Neu", "", "", track: 9);

        var settings = new AppSettings();
        var folder = ws.PathTo("album");
        var existing = FolderScanner.Tracks(folder);
        var req = ImportRequest.For(folder, existing, [AudioProbe.Read(incoming)!], index: 1, move: false, settings);

        var svc = new ConversionService(new FfmpegRunner("ffmpeg"), new BackupStore(ws.PathTo("backups")));
        var r = await FolderImport.RunAsync(req, svc, new BackupStore(ws.PathTo("backups")));

        Assert.Equal(1, r.Done);
        Assert.Empty(r.Errors);
        var after = FolderScanner.Tracks(folder).Select(t => $"{t.Track}.{t.Title}/{t.Album}").ToList();
        Assert.Equal(["1.Eins/Nachtfahrt", "2.Neu/Nachtfahrt", "3.Zwei/Nachtfahrt"], after);
        Assert.True(File.Exists(incoming));
    }

    [Fact]
    public void Describe_names_what_is_inherited()
    {
        using var ws = new Workspace();
        ws.Wav("album/01.wav", "Eins", "Kollektiv", "Nachtfahrt", track: 1);
        var incoming = ws.Wav("elsewhere/neu.wav", "Neu");
        var folder = ws.PathTo("album");
        var req = ImportRequest.For(folder, FolderScanner.Tracks(folder), [AudioProbe.Read(incoming)!], 5, false, new AppSettings());

        var lines = FolderImport.Describe(req);
        Assert.Contains(lines, l => l.Contains("Album") && l.Contains("Artist"));
        Assert.Contains(lines, l => l.Contains("track 2"));
    }
}
