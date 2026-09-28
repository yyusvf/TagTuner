using TagTuner.Core.Metadata;
using TagTuner.Core.Model;

namespace TagTuner.Tests;

public class RenamePlanTests
{
    private static AudioTrack At(string folder, string file, uint track, string title) => new()
    {
        Path = Path.Combine(folder, file), FileName = file, Track = track, Title = title,
    };

    [Fact]
    public void Right_names_stay_and_two_equal_names_do_not_collide()
    {
        using var ws = new Workspace();
        var dir = ws.PathTo("album");
        Directory.CreateDirectory(dir);

        var plan = FileNaming.Plan([
            At(dir, "01 - Ufer.mp3", 1, "Ufer"),
            At(dir, "b.mp3", 2, "Nacht"),
            At(dir, "c.mp3", 2, "Nacht"),
        ], "{track} - {title}");

        Assert.Equal(2, plan.Count);
        Assert.Equal("02 - Nacht.mp3", Path.GetFileName(plan[0].Target));
        Assert.Equal("02 - Nacht (2).mp3", Path.GetFileName(plan[1].Target));
    }
}
