using TagTuner.Core.Model;
using TagTuner.Core.Settings;

namespace TagTuner.Tests;

public class SettingsColumnsTests
{
    [Fact]
    public void Unknown_and_double_columns_go_missing_ones_come_last()
    {
        var s = new AppSettings
        {
            TrackColumns =
            [
                new() { Id = "album", Visible = true }, new() { Id = "gone" },
                new() { Id = "title", Visible = false }, new() { Id = "album", Visible = false },
            ],
        };
        s.EnsureTrackColumns();

        Assert.Equal(TrackColumn.All.Count, s.TrackColumns.Count);
        Assert.Equal(["album", "title"], s.TrackColumns.Take(2).Select(c => c.Id));
        Assert.True(s.TrackColumns[0].Visible);
        Assert.Equal(TrackColumn.ById("year")!.OnByDefault, s.TrackColumns.Single(c => c.Id == "year").Visible);
    }

    [Fact]
    public void Update_assets_are_told_apart()
    {
        Assert.True(UpdateService.IsWindowsSetup("TagTuner-0.9.0-Setup.exe"));
        Assert.False(UpdateService.IsWindowsSetup("TagTuner-0.9.0-macOS.dmg"));
        Assert.True(UpdateService.IsMacImage("TagTuner-0.9.0-macOS.dmg"));
    }
}
