using TagTuner.Core.Model;
using static TagTuner.Tests.Tracks;

namespace TagTuner.Tests;

public class TrackSortingTests
{
    private static readonly AudioTrack[] Album =
        [Make("2-2", 2, 2), Make("1-3", 3, 1), Make("2-1", 1, 2), Make("1-1", 1, 1), Make("1-2", 2, 1)];

    [Fact]
    public void Natural_order_is_disc_then_track() =>
        Assert.Equal("1-1 1-2 1-3 2-1 2-2", Order(TrackSorting.Apply(Album, TrackSort.Natural, false)));

    [Fact]
    public void Disc_descending_keeps_tracks_ascending_within_a_disc() =>
        Assert.Equal("2-1 2-2 1-1 1-2 1-3", Order(TrackSorting.Apply(Album, TrackSort.Disc, true)));

    [Fact]
    public void Track_sort_ignores_the_disc() =>
        Assert.Equal("1-1 2-1 1-2 2-2 1-3", Order(TrackSorting.Apply(Album, TrackSort.Track, false)));

    [Fact]
    public void Files_without_a_year_go_last()
    {
        var list = new[] { Make("a", year: 2015), Make("b", year: 0), Make("c", year: 1999) };
        Assert.Equal("c a b", Order(TrackSorting.Apply(list, TrackSort.Year, false)));
    }

    [Fact]
    public void Empty_text_fields_go_last()
    {
        var a = Make("a"); a.Genre = "Rap";
        var b = Make("b"); b.Genre = "";
        var c = Make("c"); c.Genre = "Jazz";
        Assert.Equal("c a b", Order(TrackSorting.Apply([a, b, c], TrackSort.Genre, false)));
    }

    [Fact]
    public void Every_column_but_the_cover_can_sort() =>
        Assert.All(TrackColumn.All.Where(c => !c.IsCover), c => Assert.NotNull(c.Sort));

    [Fact]
    public void Stored_enum_values_stay_stable() =>
        Assert.Equal(7, (int)TrackSort.Duration);
}
