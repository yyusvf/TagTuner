using TagTuner.Core.Model;

using TagTuner.Core.Settings;

namespace TagTuner.Core.Audio;

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
    /// Wie die Tags in der Datei stecken, für die Anzeige.
    ///
    /// TagLib meldet ein Bitfeld, weil eine MP3 gleichzeitig ID3v1 und ID3v2
    /// tragen kann. Angezeigt wird die Art, nach der TagTuner schreibt, und
    /// bei ID3v2 die Fassung, die tatsächlich in der Datei steht: Genau
    /// daran hängt, ob der Explorer ein Cover anzeigt.
    /// </summary>
    private static string TagName(TagLib.File file)
    {
        var types = file.TagTypes;

        if (types.HasFlag(TagLib.TagTypes.Id3v2))
        {
            var version = file.GetTag(TagLib.TagTypes.Id3v2) is TagLib.Id3v2.Tag id3
                ? id3.Version : (byte)0;
            return version > 0 ? $"ID3v2.{version}" : "ID3v2";
        }

        if (types.HasFlag(TagLib.TagTypes.Apple)) return "MP4";
        if (types.HasFlag(TagLib.TagTypes.Xiph)) return "Vorbis";
        if (types.HasFlag(TagLib.TagTypes.RiffInfo)) return "RIFF INFO";
        if (types.HasFlag(TagLib.TagTypes.Ape)) return "APEv2";
        if (types.HasFlag(TagLib.TagTypes.Id3v1)) return "ID3v1";

        return "";
    }

    /// <summary>
    /// Liest eine Datei ein. Wirft nie: eine kaputte Datei soll den Ordner
    /// nicht unlesbar machen, sondern als Zeile mit fehlenden Werten erscheinen.
    /// </summary>
    /// <summary>
    /// Öffnet eine Datei für TagLib. Sicherungen enden auf .bak, und daran
    /// erkennt TagLib kein Format. Das steckt im ursprünglichen Namen, den der
    /// Name der Sicherung noch trägt.
    /// </summary>
    private static TagLib.File Open(string path)
    {
        if (!Safety.BackupStore.IsBackup(path)) return TagLib.File.Create(path);

        var ext = System.IO.Path.GetExtension(Safety.BackupStore.OriginalName(path)).TrimStart('.');
        return TagLib.File.Create(new TagLib.File.LocalFileAbstraction(path),
                                  "taglib/" + ext.ToLowerInvariant(), TagLib.ReadStyle.Average);
    }

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
            Format = AudioFormats.Normalize(Safety.BackupStore.IsBackup(path)
                ? Safety.BackupStore.OriginalName(path) : path).ToUpperInvariant(),
            Size = info.Length,
        };

        try
        {
            using var file = Open(path);

            track.SampleRate = file.Properties?.AudioSampleRate ?? 0;
            track.Channels = file.Properties?.AudioChannels ?? 0;
            track.Bitrate = file.Properties?.AudioBitrate ?? 0;
            track.Duration = file.Properties?.Duration ?? TimeSpan.Zero;

            // Der Kodierer, wie die Datei selbst ihn nennt. Mehrere Ströme
            // gibt es bei Audiodateien praktisch nie; der erste genügt.
            track.Codec = file.Properties?.Codecs?
                .Select(c => c?.Description ?? "")
                .FirstOrDefault(d => d.Length > 0) ?? "";

            track.TagFormat = TagName(file);

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
            track.Title = System.IO.Path.GetFileNameWithoutExtension(
                Safety.BackupStore.IsBackup(path) ? Safety.BackupStore.OriginalName(path) : path);

        return track;
    }

    /// <summary>Das eingebettete Cover mit seinen Eckdaten.</summary>
    public sealed record Cover(byte[] Data, string MimeType, string Kind);

    /// <summary>Holt das eingebettete Cover, oder null.</summary>
    public static Cover? ReadCover(string path)
    {
        try
        {
            using var file = Open(path);
            var pic = file.Tag?.Pictures?.FirstOrDefault();
            if (pic?.Data?.Data is not { Length: > 0 } bytes) return null;

            var kind = pic.Type switch
            {
                TagLib.PictureType.FrontCover => "Front Cover",
                TagLib.PictureType.BackCover => "Back Cover",
                TagLib.PictureType.Other => Strings.T("Image"),
                _ => pic.Type.ToString(),
            };
            return new Cover(bytes, string.IsNullOrWhiteSpace(pic.MimeType) ? Strings.T("unknown") : pic.MimeType, kind);
        }
        catch { return null; }
    }
}
