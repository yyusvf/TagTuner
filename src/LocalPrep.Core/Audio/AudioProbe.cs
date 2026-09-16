using LocalPrep.Core.Model;

namespace LocalPrep.Core.Audio;

/// <summary>
/// Liest technische Daten und Tags über TagLib#.
///
/// Ersetzt music-metadata aus der Electron-Fassung — und nebenbei ffprobe,
/// das ffmpeg-static gar nicht mitlieferte: Dort musste die Nachkontrolle
/// einer Konvertierung ohnehin über den Tag-Leser laufen.
/// </summary>
public static class AudioProbe
{
    /// <summary>
    /// Muss einmal beim Start laufen. Erzwingt ID3v2.3 beim Schreiben —
    /// der Windows-Explorer zeigt Cover-Thumbnails nur damit zuverlässig an.
    /// </summary>
    public static void Configure()
    {
        TagLib.Id3v2.Tag.DefaultVersion = 3;
        TagLib.Id3v2.Tag.ForceDefaultVersion = true;
    }

    /// <summary>
    /// Liest eine Datei ein. Wirft nie: eine kaputte Datei soll den Ordner
    /// nicht unlesbar machen, sondern als Zeile mit fehlenden Werten erscheinen.
    /// </summary>
    public static AudioTrack? Read(string path)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists) return null;
        }
        catch { return null; }

        var track = new AudioTrack
        {
            Path = path,
            FileName = System.IO.Path.GetFileName(path),
            Format = AudioFormats.Normalize(path).ToUpperInvariant(),
            Size = info.Length,
        };

        try
        {
            using var file = TagLib.File.Create(path);

            track.SampleRate = file.Properties?.AudioSampleRate ?? 0;
            track.Channels = file.Properties?.AudioChannels ?? 0;
            track.Bitrate = file.Properties?.AudioBitrate ?? 0;
            track.Duration = file.Properties?.Duration ?? TimeSpan.Zero;

            var tag = file.Tag;
            if (tag is not null)
            {
                track.Title = tag.Title ?? "";
                track.Artist = tag.FirstPerformer ?? "";
                track.Album = tag.Album ?? "";
                track.AlbumArtist = tag.FirstAlbumArtist ?? "";
                track.Genre = tag.FirstGenre ?? "";
                track.Composer = tag.FirstComposer ?? "";
                track.Comment = tag.Comment ?? "";
                track.Year = tag.Year;
                track.Track = tag.Track;
                track.Disc = tag.Disc;
                track.HasCover = tag.Pictures is { Length: > 0 };
            }
        }
        catch
        {
            // Unlesbare oder beschädigte Datei: technische Felder bleiben leer,
            // die Datei taucht aber auf, statt still zu verschwinden.
        }

        // M4A/AAC melden über TagLib keinen Titel, wenn gar keine Tags da sind —
        // dann ist der Dateiname die einzig sinnvolle Anzeige.
        if (string.IsNullOrWhiteSpace(track.Title))
            track.Title = System.IO.Path.GetFileNameWithoutExtension(path);

        return track;
    }

    /// <summary>Das eingebettete Cover mit seinen Eckdaten.</summary>
    public sealed record Cover(byte[] Data, string MimeType, string Kind);

    /// <summary>Holt das eingebettete Cover, oder null.</summary>
    public static Cover? ReadCover(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var pic = file.Tag?.Pictures?.FirstOrDefault();
            if (pic?.Data?.Data is not { Length: > 0 } bytes) return null;

            var kind = pic.Type switch
            {
                TagLib.PictureType.FrontCover => "Front Cover",
                TagLib.PictureType.BackCover => "Back Cover",
                TagLib.PictureType.Other => "Bild",
                _ => pic.Type.ToString(),
            };
            return new Cover(bytes, string.IsNullOrWhiteSpace(pic.MimeType) ? "unbekannt" : pic.MimeType, kind);
        }
        catch { return null; }
    }
}
