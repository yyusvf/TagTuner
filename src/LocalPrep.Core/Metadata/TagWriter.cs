namespace LocalPrep.Core.Metadata;

/// <summary>
/// Die zu schreibenden Tags. null heißt „nicht anfassen" — so bleibt bei einer
/// Mehrfachauswahl unangetastet, worin sich die Dateien uneinig sind.
/// </summary>
public sealed record TagEdit
{
    public string? Title { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public string? AlbumArtist { get; init; }
    public string? Genre { get; init; }
    public string? Composer { get; init; }
    public string? Comment { get; init; }
    public uint? Year { get; init; }
    public uint? Track { get; init; }
    public uint? Disc { get; init; }

    /// <summary>Neues Cover. Leeres Array heißt „entfernen", null „unverändert".</summary>
    public byte[]? Cover { get; init; }
    public string? CoverMimeType { get; init; }

    public bool IsEmpty =>
        Title is null && Artist is null && Album is null && AlbumArtist is null &&
        Genre is null && Composer is null && Comment is null &&
        Year is null && Track is null && Disc is null && Cover is null;
}

public static class TagWriter
{
    /// <summary>
    /// Schreibt die gesetzten Felder in eine Datei.
    ///
    /// ID3v2.3 wird global über <see cref="Audio.AudioProbe.Configure"/> erzwungen —
    /// der Windows-Explorer zeigt Cover-Thumbnails sonst nicht zuverlässig an.
    /// </summary>
    public static void Write(string path, TagEdit edit)
    {
        if (edit.IsEmpty) return;

        using var file = TagLib.File.Create(path);
        var tag = file.Tag;

        if (edit.Title is not null) tag.Title = Blank(edit.Title);
        if (edit.Album is not null) tag.Album = Blank(edit.Album);
        if (edit.Genre is not null) tag.Genres = Split(edit.Genre);
        if (edit.Composer is not null) tag.Composers = Split(edit.Composer);
        if (edit.Artist is not null) tag.Performers = Split(edit.Artist);
        if (edit.AlbumArtist is not null) tag.AlbumArtists = Split(edit.AlbumArtist);
        if (edit.Comment is not null) tag.Comment = Blank(edit.Comment);
        if (edit.Year is not null) tag.Year = edit.Year.Value;
        if (edit.Track is not null) tag.Track = edit.Track.Value;
        if (edit.Disc is not null) tag.Disc = edit.Disc.Value;

        if (edit.Cover is not null)
        {
            if (edit.Cover.Length == 0)
            {
                tag.Pictures = [];
            }
            else
            {
                var pic = new TagLib.Picture(new TagLib.ByteVector(edit.Cover))
                {
                    Type = TagLib.PictureType.FrontCover,
                    MimeType = string.IsNullOrWhiteSpace(edit.CoverMimeType)
                        ? "image/jpeg" : edit.CoverMimeType,
                    Description = "Cover",
                };
                tag.Pictures = [pic];
            }
        }

        file.Save();

        static string? Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

        // Mehrere Interpreten sind in Tags üblicherweise mit ; oder / getrennt.
        static string[] Split(string s) =>
            string.IsNullOrWhiteSpace(s)
                ? []
                : s.Split([';', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Überträgt Tags von einer Datei auf eine andere. Wird nach einer
    /// Konvertierung gebraucht, wenn ffmpeg sie nicht vollständig mitnehmen
    /// konnte — etwa bei einem Containerwechsel.
    /// </summary>
    public static void CopyTags(string from, string to)
    {
        try
        {
            using var src = TagLib.File.Create(from);
            using var dst = TagLib.File.Create(to);
            src.Tag.CopyTo(dst.Tag, overwrite: true);
            dst.Save();
        }
        catch { /* Tags sind Beiwerk — ein Fehler hier darf die Datei nicht verwerfen */ }
    }
}
