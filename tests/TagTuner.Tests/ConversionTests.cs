using TagTuner.Core.Audio;
using TagTuner.Core.Metadata;
using TagTuner.Core.Safety;

namespace TagTuner.Tests;

/// <summary>
/// Umwandeln mit ffmpeg. Läuft nur, wo ffmpeg zu finden ist; sonst endet der
/// Test sofort, damit die übrigen Tests auch ohne ffmpeg laufen.
/// </summary>
public class ConversionTests
{
    /// <summary>
    /// Eine WAV kann kein Cover tragen. Wer sie zu MP3 macht und dabei ein
    /// Cover setzt, soll beides in einem Durchgang bekommen.
    /// </summary>
    [Fact]
    public async Task Wav_to_mp3_takes_a_cover_in_one_go()
    {
        var ffmpeg = FfmpegLocator.Find();
        if (ffmpeg is null) return;

        using var ws = new Workspace();
        var wav = ws.Wav("in/song.wav", title: "Ufer", artist: "Kollektiv Halle");
        var track = AudioProbe.Read(wav)!;
        Assert.False(AudioFormats.CanCarryCover(track.Format));

        // Ein gültiges, winziges JPEG reicht: Geprüft wird, dass es ankommt.
        var cover = Convert.FromBase64String(
            "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AKp//2Q==");

        var service = new ConversionService(new FfmpegRunner(ffmpeg!), new BackupStore(ws.PathTo("backups")));
        var outcome = await service.ConvertAsync(new ConversionRequest
        {
            Track = track,
            Options = new EncodeOptions { Format = "MP3" },
            Tags = new TagEdit { Cover = cover, CoverMimeType = "image/jpeg" },
        });

        Assert.True(outcome.Success, outcome.Error);
        var mp3 = ws.PathTo("in", "song.mp3");
        Assert.True(File.Exists(mp3));
        Assert.Equal(cover, AudioProbe.ReadCover(mp3)?.Data);
        Assert.Equal("Ufer", AudioProbe.Read(mp3)?.Title);
    }
}

/// <summary>Wer nach dem Anwenden ein Cover tragen kann, bei gemischter Auswahl.</summary>
public class CoverAfterTests
{
    [Theory]
    [InlineData("MP3", null, true)]     // bleibt MP3
    [InlineData("WAV", null, false)]    // bleibt WAV
    [InlineData("WAV", "MP3", true)]    // wird MP3
    [InlineData("MP3", "WAV", false)]   // wird WAV, verliert die Möglichkeit
    [InlineData("WAV", "WAV", false)]
    [InlineData("AIFF", "FLAC", true)]
    [InlineData("OGG", "M4A", true)]
    public void Decides_by_the_format_after_applying(string now, string? target, bool expected) =>
        Assert.Equal(expected, AudioFormats.CarriesCoverAfter(now, target));
}
