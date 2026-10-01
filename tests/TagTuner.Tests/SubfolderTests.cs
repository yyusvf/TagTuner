using TagTuner.Core.Folders;

namespace TagTuner.Tests;

public class SubfolderTests
{
    [Fact]
    public void Only_folders_with_music_count_and_too_many_means_none()
    {
        using var ws = new Workspace();
        ws.Wav("artist/album1/01.wav");
        ws.Wav("artist/album1/02.wav");
        ws.Wav("artist/album2/cd1/01.wav");
        Directory.CreateDirectory(ws.PathTo("artist", "scans"));

        var subs = FolderScanner.AudioSubfolders(ws.PathTo("artist"));
        Assert.Equal(["album1:2", "album2:0"], subs.Select(s => $"{s.Name}:{s.Count}"));
        Assert.Empty(FolderScanner.AudioSubfolders(ws.PathTo("artist"), max: 1));
    }
}
