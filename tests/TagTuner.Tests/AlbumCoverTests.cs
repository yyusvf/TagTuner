using TagTuner.Core.Audio;
using TagTuner.Core.Folders;
using static TagTuner.Tests.Tracks;

namespace TagTuner.Tests;

public class AlbumCoverTests
{
    private static readonly AudioProbe.Cover Blue = new([1, 2, 3], "image/jpeg", "Front");
    private static readonly AudioProbe.Cover Red = new([9, 9], "image/png", "Front");

    [Fact]
    public void Most_common_cover_wins_and_the_rest_need_it()
    {
        var a = Make("a"); var b = Make("b"); var c = Make("c"); var d = Make("d");
        foreach (var t in new[] { a, b, c }) t.HasCover = true;
        var covers = new Dictionary<string, AudioProbe.Cover> { ["a"] = Blue, ["b"] = Blue, ["c"] = Red };

        var (cover, needs) = AlbumCover.Majority([a, b, c, d], t => covers.GetValueOrDefault(t.Path));

        Assert.Same(Blue, cover);
        Assert.Equal(["c", "d"], needs.Order());
    }

    [Fact]
    public void Without_any_cover_nothing_is_needed()
    {
        var (cover, needs) = AlbumCover.Majority([Make("a"), Make("b")], _ => null);
        Assert.Null(cover);
        Assert.Empty(needs);
    }
}
