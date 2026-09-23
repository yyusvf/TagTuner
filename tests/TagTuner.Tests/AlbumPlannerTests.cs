using TagTuner.Core.Folders;
using TagTuner.Core.Model;
using TagTuner.Core.Settings;
using static TagTuner.Tests.Tracks;

namespace TagTuner.Tests;

public class AlbumPlannerTests
{
    private static readonly FolderRule On = new() { AlbumMode = true, BaseTags = true, Cover = true, Numbering = true };
    private static readonly HashSet<string> NoCover = [];

    private static List<AlbumChange> Plan(AudioTrack[] list, FolderRule? rule = null,
                                          ISet<string>? needsCover = null, bool hasCover = false) =>
        AlbumPlanner.Plan(list, rule ?? On, FolderAnalysis.Of(list).Inherited(byMajority: true),
                          needsCover ?? NoCover, hasCover);

    [Fact]
    public void Numbers_follow_the_order()
    {
        var plan = Plan([Make("a", 3), Make("b", 1), Make("c", 2)]);
        Assert.Equal(3, plan.Count);
        var a = plan.Single(p => p.Track.Path == "a");
        Assert.Equal(1u, a.Edit.Track);
        Assert.Contains(a.Changes, c => c is { Field: "Track", From: "3", To: "1" });
    }

    [Fact]
    public void Nothing_to_do_when_already_right() =>
        Assert.Empty(Plan([Make("a", 1), Make("b", 2)]));

    [Fact]
    public void Majority_wins_but_a_guest_artist_stays()
    {
        var plan = Plan([
            Make("a", 1), Make("b", 2), Make("c", 3, album: "Nachtfahrt (Demo)"),
            Make("d", 4, artist: "Kollektiv Halle feat. Gast"), Make("e", 5, artist: "Jemand")]);

        Assert.Equal("Nachtfahrt", plan.Single(p => p.Track.Path == "c").Edit.Album);
        Assert.DoesNotContain(plan, p => p.Track.Path == "d");
        Assert.Equal("Kollektiv Halle", plan.Single(p => p.Track.Path == "e").Edit.Artist);
    }

    [Fact]
    public void Each_disc_counts_from_one_and_the_disc_is_never_written()
    {
        var plan = Plan([Make("a", 1, 1), Make("b", 5, 1), Make("c", 9, 2), Make("d", 2, 2)]);
        Assert.Equal(2u, plan.Single(p => p.Track.Path == "b").Edit.Track);
        Assert.Equal(1u, plan.Single(p => p.Track.Path == "c").Edit.Track);
        Assert.DoesNotContain(plan, p => p.Track.Path == "d");
        Assert.All(plan, p => Assert.Null(p.Edit.Disc));
    }

    [Fact]
    public void Sub_switches_limit_what_is_written()
    {
        var list = new[] { Make("a", 2, album: "X"), Make("b", 1), Make("c", 3) };
        var numbersOnly = new FolderRule { AlbumMode = true, BaseTags = false, Cover = false, Numbering = true };
        Assert.All(Plan(list, numbersOnly), p => Assert.Null(p.Edit.Album));
        Assert.Empty(Plan(list, new FolderRule { AlbumMode = false }, hasCover: true));
    }

    [Fact]
    public void Cover_only_where_it_is_missing_and_only_if_the_folder_has_one()
    {
        var list = new[] { Make("a", 1), Make("b", 2) };
        var plan = Plan(list, needsCover: new HashSet<string> { "b" }, hasCover: true);
        Assert.Single(plan);
        Assert.Contains(plan[0].Changes, c => c.Field == "Cover");
        Assert.Empty(Plan(list, needsCover: new HashSet<string> { "b" }, hasCover: false));
    }

    [Fact]
    public void Numbers_without_several_discs_run_through()
    {
        var numbers = AlbumPlanner.Numbers([Make("a", disc: 1), Make("b", disc: 1), Make("c", disc: 0)]);
        Assert.Equal([1u, 2u, 3u], numbers);
    }
}
