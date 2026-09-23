using TagTuner.Core.Audio;
using TagTuner.Core.Safety;
using static TagTuner.Tests.Tracks;

namespace TagTuner.Tests;

public class BackupDiffTests
{
    [Fact]
    public void Same_file_has_no_changes() =>
        Assert.Empty(BackupDiff.Compare(Make("p", 3), Make("p", 3), null, null));

    [Fact]
    public void Reports_the_backed_up_value_then_the_current_one()
    {
        var now = Make("p", 1, album: "Nachtfahrt (Deluxe)");
        var changes = BackupDiff.Compare(Make("p", 3), now, null, null);
        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, c => c is { Field: "Track", From: "3", To: "1" });
        Assert.Contains(changes, c => c is { Field: "Album", From: "Nachtfahrt", To: "Nachtfahrt (Deluxe)" });
    }

    [Fact]
    public void Covers_are_compared_byte_for_byte()
    {
        Assert.Contains(BackupDiff.Compare(Make("p"), Make("p"), null, [1, 2]), c => c.Field == "Cover");
        Assert.Contains(BackupDiff.Compare(Make("p"), Make("p"), [1, 2], [1, 3]), c => c.Field == "Cover");
        Assert.Empty(BackupDiff.Compare(Make("p"), Make("p"), [1, 2], [1, 2]));
    }
}

public class BackupStoreTests
{
    [Fact]
    public void Original_name_drops_the_stamp() =>
        Assert.Equal("01 Nebelfeld.mp3",
            BackupStore.OriginalName(@"C:\b\01 Nebelfeld.mp3_20250101-120000-000.bak"));

    [Fact]
    public void A_name_without_a_stamp_is_left_alone() =>
        Assert.Equal("Lied_live", BackupStore.OriginalName(@"C:\b\Lied_live.bak"));

    [Fact]
    public void Tags_can_be_read_from_a_backup()
    {
        using var ws = new Workspace();
        var song = ws.Wav("album/01 Ufer.wav", title: "Ufer", artist: "Kollektiv Halle");
        var bak = new BackupStore(ws.PathTo("backups")).Create(song);

        var read = AudioProbe.Read(bak);
        Assert.Equal("Ufer", read?.Title);
        Assert.Equal("WAV", read?.Format);
    }
}

public class RestoreTests
{
    [Fact]
    public void Restore_puts_the_file_back_and_keeps_the_version_it_replaced()
    {
        using var ws = new Workspace();
        var song = ws.Wav("album/01 Ufer.wav", title: "Alt");
        var store = new BackupStore(ws.PathTo("backups"));
        var history = new HistoryStore();

        var bak = store.Create(song);
        history.Add("test", "Test-Aktion", [new HistoryFile { Original = song, BackupPath = bak }]);
        ws.Wav("album/01 Ufer.wav", title: "Neu");

        var listed = BackupCatalog.List(store, history).Single();
        Assert.Equal(song, listed.OriginalPath);
        Assert.Equal("Test-Aktion", listed.Action);

        var undo = history.Restore(bak, store);

        Assert.Equal("Alt", AudioProbe.Read(song)?.Title);
        Assert.False(File.Exists(bak));
        Assert.Equal("Neu", AudioProbe.Read(undo.Single().BackupPath!)?.Title);
        Assert.Null(history.Find(bak));
        Assert.DoesNotContain(history.Entries, e => e.Description == "Test-Aktion");

        // Und das Wiederherstellen selbst lässt sich zurücknehmen.
        var entry = history.Add("restore", "Wiederhergestellt", undo);
        history.Undo(entry.Id);
        Assert.Equal("Neu", AudioProbe.Read(song)?.Title);
    }

    [Fact]
    public void A_backup_without_history_needs_a_target()
    {
        using var ws = new Workspace();
        var song = ws.Wav("album/01 Ufer.wav", title: "Alt");
        var store = new BackupStore(ws.PathTo("backups"));
        var history = new HistoryStore();
        var orphan = store.Create(song);

        Assert.Null(BackupCatalog.List(store, history).Single().OriginalPath);
        Assert.Throws<InvalidOperationException>(() => history.Restore(orphan, store));

        var target = ws.PathTo("woanders", BackupStore.OriginalName(orphan));
        history.Restore(orphan, store, target);
        Assert.Equal("Alt", AudioProbe.Read(target)?.Title);
    }

    [Fact]
    public void Newest_backup_comes_first()
    {
        using var ws = new Workspace();
        var song = ws.Wav("a.wav");
        var store = new BackupStore(ws.PathTo("backups"));
        var older = store.Create(song);
        Thread.Sleep(20);
        var newer = store.Create(song);

        var order = BackupCatalog.List(store, new HistoryStore()).Select(i => i.BackupPath).ToList();
        Assert.True(order.IndexOf(newer) < order.IndexOf(older));
    }

    [Fact]
    public void Tests_never_touch_the_real_history()
    {
        using var ws = new Workspace();
        Assert.StartsWith(ws.Root, TagTuner.Core.Settings.AppSettings.Directory);
    }
}
