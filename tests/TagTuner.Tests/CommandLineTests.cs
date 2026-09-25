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
