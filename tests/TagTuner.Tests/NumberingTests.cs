using TagTuner.Core.Folders;
using TagTuner.Core.Model;
using TagTuner.Core.Safety;
using static TagTuner.Tests.Tracks;

namespace TagTuner.Tests;

public class TrackNumberCheckTests
{
    [Fact]
    public void A_clean_album_has_no_problems() =>
        Assert.True(TrackNumberCheck.Check([Make("a", 1), Make("b", 2), Make("c", 3)]).IsClean);

    [Fact]
    public void Finds_gaps_and_doubles()
    {
        var result = TrackNumberCheck.Check([Make("a", 1), Make("b", 3), Make("c", 3), Make("d", 6)]);
        var disc = Assert.Single(result.Discs);
        Assert.Equal([2u, 4u, 5u], disc.Missing);
        Assert.Equal([(3u, 2)], disc.Doubled);
    }

    [Fact]
    public void Each_disc_is_checked_on_its_own()
    {
        var result = TrackNumberCheck.Check([Make("a", 1, 1), Make("b", 2, 1), Make("c", 1, 2), Make("d", 3, 2)]);
        var disc = Assert.Single(result.Discs);
        Assert.Equal(2u, disc.Disc);
        Assert.Equal([2u], disc.Missing);
    }

    [Fact]
    public void Counts_files_without_a_number() =>
        Assert.Equal(2, TrackNumberCheck.Check([Make("a", 1), Make("b"), Make("c")]).Unnumbered);

    [Fact]
    public void Ranges_are_folded() =>
        Assert.Equal("2, 4–6, 9", TrackNumberCheck.Ranges([9, 4, 5, 6, 2]));
}

public class FileNumberingTests
{
    [Theory]
    [InlineData("03 - Ufer.mp3", 1u, "01 - Ufer.mp3")]
    [InlineData("3. Ufer.mp3", 12u, "12. Ufer.mp3")]
    [InlineData("Ufer.mp3", 4u, "04 Ufer.mp3")]
    [InlineData("2019 Live.mp3", 1u, "01 2019 Live.mp3")]
    [InlineData("01 Ufer.mp3", 1u, null)]
    public void Replaces_only_the_leading_number(string name, uint track, string? expected) =>
        Assert.Equal(expected, FileNumbering.NewName(name, track, 2));

    [Fact]
    public void With_several_discs_the_disc_goes_in_front() =>
        Assert.Equal("2-01 Ufer.mp3", FileNumbering.NewName("1-07 Ufer.mp3", 1, 2, disc: 2));

    [Fact]
    public void A_name_that_is_taken_is_skipped()
    {
        var a = Make(@"C:\m\03 Ufer.mp3", 1); a.FileName = "03 Ufer.mp3";
        var b = Make(@"C:\m\01 Ufer.mp3", 3); b.FileName = "01 Ufer.mp3";

        // Beide wollen den Namen des anderen: Ein Tausch ließe sich nicht
        // sicher zurücknehmen, also bleiben beide, wie sie sind.
        var (plan, skipped) = FileNumbering.Plan([a, b], [a.Path, b.Path]);
        Assert.Empty(plan);
        Assert.Equal(2, skipped.Count);
    }

    [Fact]
    public void Renaming_can_be_undone()
    {
        using var ws = new Workspace();
        var path = ws.Wav("album/03 Ufer.wav", title: "Ufer", track: 1);
        var track = TagTuner.Core.Audio.AudioProbe.Read(path)!;

        var (plan, _) = FileNumbering.Plan([track], [path]);
        var (_, target) = Assert.Single(plan);
        Assert.EndsWith("01 Ufer.wav", target);

        var store = new BackupStore(ws.PathTo("backups"));
        var backup = store.Create(path);
        File.Move(path, target);

        var history = new HistoryStore();
        var entry = history.Add("rename", "Dateinamen",
            [new HistoryFile { Original = path, BackupPath = backup, OutputPath = target }]);
        history.Undo(entry.Id);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void History_reports_new_entries()
    {
        using var ws = new Workspace();
        var history = new HistoryStore();
        HistoryEntry? seen = null;
        history.Added += e => seen = e;
        var entry = history.Add("test", "x", []);
        Assert.Same(entry, seen);
    }
}
