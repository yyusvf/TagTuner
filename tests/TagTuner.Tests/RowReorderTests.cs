using TagTuner.Core.Folders;

namespace TagTuner.Tests;

public class RowReorderTests
{
    [Fact]
    public void Gap_follows_the_block_top_like_on_the_website()
    {
        double[] rows = [56, 56, 56, 56];
        Assert.Equal(0, RowReorder.NearestGap(rows, -40));
        Assert.Equal(0, RowReorder.NearestGap(rows, 27));
        Assert.Equal(1, RowReorder.NearestGap(rows, 29));
        Assert.Equal(2, RowReorder.NearestGap(rows, 120));
        Assert.Equal(4, RowReorder.NearestGap(rows, 900));
    }

    [Fact]
    public void A_short_disc_row_moves_the_boundary()
    {
        // Lied, Disc-Zeile (46), Lied: Die Grenze hinter der Disc-Zeile liegt
        // bei 56 + 23, nicht bei 56 + 28.
        double[] rows = [56, 46, 56];
        Assert.Equal(1, RowReorder.NearestGap(rows, 78));
        Assert.Equal(2, RowReorder.NearestGap(rows, 80));
    }

    [Fact]
    public void Scattered_selection_lands_together()
    {
        // a b c d e f, b und e ans Ende der übrigen (a c d f).
        Assert.Equal([0, 2, 3, 5, 1, 4], RowReorder.Order(6, [4, 1], 4));
        Assert.Equal([1, 4, 0, 2, 3, 5], RowReorder.Order(6, [1, 4], 0));
        Assert.Equal([0, 2, 1, 4, 3, 5], RowReorder.Order(6, [1, 4], 2));
    }

    [Fact]
    public void Gap_is_clamped()
    {
        Assert.Equal([1, 2, 0], RowReorder.Order(3, [0], 99));
        Assert.Equal([0, 1, 2], RowReorder.Order(3, [0], -3));
    }

    [Fact]
    public void Shifts_move_neighbours_by_the_block_height()
    {
        double[] heights = [56, 56, 56, 56];
        // Zeile 0 hinter Zeile 2: 1 und 2 rücken hoch, 0 rückt zwei Zeilen runter.
        var shifts = RowReorder.Shifts(heights, RowReorder.Order(4, [0], 2));
        Assert.Equal([112, -56, -56, 0], shifts);
    }

    [Fact]
    public void Shifts_respect_mixed_heights()
    {
        // Disc 1, a, Disc 2, b; a unter Disc 2 geschoben.
        double[] heights = [46, 56, 46, 56];
        var shifts = RowReorder.Shifts(heights, RowReorder.Order(4, [1], 2));
        Assert.Equal([0, 46, -56, 0], shifts);
    }

    [Fact]
    public void Sections_follow_the_disc_rows()
    {
        // a über der ersten Disc-Zeile zählt zur ersten Disc.
        uint?[] rows = [null, 1, null, null, 2, null];
        Assert.Equal([1u, 1u, 1u, 2u], RowReorder.Sections(rows));
        Assert.Equal([0u, 0u], RowReorder.Sections([null, null]));
    }

    [Fact]
    public void Moving_across_a_disc_changes_disc_and_numbers()
    {
        // Disc 1: a b c, Disc 2: d e. a ans Ende von Disc 2.
        uint?[] before = [1, null, null, null, 2, null, null];
        var order = RowReorder.Order(before.Length, [1], 5);
        var after = order.Select(i => before[i]).ToArray();

        var discs = RowReorder.Sections(after);
        Assert.Equal([1u, 1u, 2u, 2u, 2u], discs);
        Assert.Equal([1u, 2u, 1u, 2u, 3u], RowReorder.Numbers(discs));
    }

    [Fact]
    public void Only_renumbered_songs_light_up()
    {
        string[] was = ["a", "b", "c", "d", "e"];
        (uint, uint)[] wasNumbers = [(1, 1), (1, 2), (1, 3), (2, 1), (2, 2)];
        string[] now = ["b", "c", "d", "a", "e"];
        (uint, uint)[] nowNumbers = [(1, 1), (1, 2), (2, 1), (2, 2), (2, 3)];

        var lit = RowReorder.Renumbered(was, wasNumbers, now, nowNumbers);
        Assert.Equal(["a", "b", "c", "e"], lit.Order());
    }

    [Fact]
    public void Auto_scroll_only_at_the_edges()
    {
        Assert.Equal(0, RowReorder.AutoScrollStep(200, 400, 40, 20));
        Assert.True(RowReorder.AutoScrollStep(10, 400, 40, 20) < 0);
        Assert.True(RowReorder.AutoScrollStep(395, 400, 40, 20) > 0);
        Assert.Equal(-20, RowReorder.AutoScrollStep(-50, 400, 40, 20));
        Assert.Equal(20, RowReorder.AutoScrollStep(900, 400, 40, 20));
    }
}
