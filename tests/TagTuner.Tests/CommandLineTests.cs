using TagTuner.Core.Safety;
using TagTuner.Core.Shell;

namespace TagTuner.Tests;

public class CommandLineTests
{
    [Fact]
    public void Edit_from_the_explorer_menu()
    {
        var target = CommandLine.Parse(["--edit", @"--file=""C:\Music\a.mp3"""]);
        Assert.True(target.Edit);
        Assert.Equal(@"C:\Music\a.mp3", target.File);
        Assert.Equal(@"C:\Music", target.FolderToOpen);
    }

    [Fact]
    public void Open_is_not_edit() =>
        Assert.False(CommandLine.Parse([@"--folder=C:\Music"]).Edit);
}

/// <summary>
/// Das kleine Fenster aus dem Explorer schreibt in denselben Verlauf wie das
/// Hauptfenster. Keiner darf dabei die Einträge des anderen überschreiben.
/// </summary>
public class HistorySharingTests
{
    [Fact]
    public void Two_stores_keep_each_others_entries()
    {
        using var ws = new Workspace();
        var main = new HistoryStore();
        var quick = new HistoryStore();

        quick.Add("batch", "from the explorer", []);
        main.Add("batch", "from the app", []);

        Assert.Equal(["from the app", "from the explorer"],
                     new HistoryStore().Entries.Select(e => e.Description));
    }
}

/// <summary>
/// Sicherungen außerhalb des Sicherungsordners, etwa noch aus der Zeit als
/// LocalPrep, kommen beim Start dorthin, wo Liste und Aufräumen sie sehen.
/// </summary>
public class AdoptBackupsTests
{
    [Fact]
    public void Moves_stray_backups_and_repoints_the_history()
    {
        using var ws = new Workspace();
        var folder = ws.PathTo("Backups");
        var old = ws.PathTo("LocalPrep", "Backups");
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(folder);

        var stray = Path.Combine(old, "a.mp3_20260917-014251-134.bak");
        var copied = Path.Combine(old, "b.mp3_20260917-014304-791.bak");
        File.WriteAllText(stray, "a");
        File.WriteAllText(copied, "b");
        File.WriteAllText(Path.Combine(folder, Path.GetFileName(copied)), "b");

        var history = new HistoryStore();
        history.Add("batch", "old", [
            new HistoryFile { Original = ws.PathTo("a.mp3"), BackupPath = stray },
            new HistoryFile { Original = ws.PathTo("b.mp3"), BackupPath = copied },
            new HistoryFile { Original = ws.PathTo("c.mp3"), BackupPath = Path.Combine(old, "gone.bak") },
        ]);

        Assert.Equal(3, history.AdoptBackups(folder));

        var files = new HistoryStore().Entries[0].Files;
        Assert.Equal(Path.Combine(folder, Path.GetFileName(stray)), files[0].BackupPath);
        Assert.Equal(Path.Combine(folder, Path.GetFileName(copied)), files[1].BackupPath);
        Assert.Null(files[2].BackupPath);
        Assert.Empty(Directory.GetFiles(old));
        Assert.Equal(2, Directory.GetFiles(folder).Length);
    }
}

public class SkippedVersionTests
{
    [Fact]
    public void A_skip_is_forgotten_once_the_app_caught_up()
    {
        using var ws = new Workspace();
        var settings = new TagTuner.Core.Settings.AppSettings { SkippedVersion = "0.8.1" };

        TagTuner.Core.Settings.UpdateService.ForgetOutdatedSkip(settings, "0.8.0");
        Assert.Equal("0.8.1", settings.SkippedVersion);

        TagTuner.Core.Settings.UpdateService.ForgetOutdatedSkip(settings, "0.8.3");
        Assert.Null(settings.SkippedVersion);
    }
}
