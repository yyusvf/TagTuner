namespace TagTuner.Core.Model;

/// <summary>Wie eine Spalte ausgerichtet und gesetzt wird.</summary>
public enum ColumnLook
{
    /// <summary>Normaler Text, linksbündig.</summary>
    Text,

    /// <summary>Zahlen und Kürzel: schmal, feste Zeichenbreite.</summary>
    Mono,

    /// <summary>Rechtsbündig, feste Zeichenbreite — Nummern und Dauer.</summary>
    MonoRight,
}

/// <summary>
/// Eine Spalte der Trackliste.
///
/// Alles, was eine Spalte ausmacht, steht hier an einer Stelle: wie sie
/// heißt, woher ihr Wert kommt, wonach sie sortiert und wie breit sie
/// anfängt. Eine Spalte hinzuzufügen heißt, diese Liste zu erweitern —
/// Kopfzeile, Zeilenvorlage und die Auswahl in den Einstellungen bauen sich
/// daraus von selbst.
/// </summary>
public sealed record TrackColumn(
    string Id,
    string Header,
    string Property,
    ColumnLook Look,
    double Width,
    bool OnByDefault,
    TrackSort? Sort = null)
{
    /// <summary>Die Reihenfolge hier ist die Vorgabe, solange nichts anderes gespeichert ist.</summary>
    public static readonly IReadOnlyList<TrackColumn> All =
    [
        new("track",       "#",             nameof(AudioTrack.TrackLabel),      ColumnLook.MonoRight,  32, true,  TrackSort.Track),
        new("cover",       "",              "",                                 ColumnLook.Text,       52, true),
        new("title",       "TITLE",         nameof(AudioTrack.Title),           ColumnLook.Text,      240, true,  TrackSort.Title),
        new("artist",      "ARTIST",        nameof(AudioTrack.Artist),          ColumnLook.Text,      170, true,  TrackSort.Artist),
        new("album",       "ALBUM",         nameof(AudioTrack.Album),           ColumnLook.Text,      190, true,  TrackSort.Album),
        new("format",      "FORMAT",        nameof(AudioTrack.Format),          ColumnLook.Mono,       62, true,  TrackSort.Format),
        new("samplerate",  "SAMPLE RATE",   nameof(AudioTrack.SampleRateLabel), ColumnLook.Mono,       78, true,  TrackSort.SampleRate),
        new("duration",    "LENGTH",        nameof(AudioTrack.DurationLabel),   ColumnLook.MonoRight,  56, true,  TrackSort.Duration),

        // Ab hier standardmäßig aus: nützlich, aber nicht für jeden Ordner.
        new("year",        "YEAR",          nameof(AudioTrack.YearLabel),       ColumnLook.Mono,       50, false, TrackSort.Year),
        new("disc",        "DISC",          nameof(AudioTrack.DiscLabel),       ColumnLook.MonoRight,  40, false, TrackSort.Disc),
        new("genre",       "GENRE",         nameof(AudioTrack.Genre),           ColumnLook.Text,      110, false, TrackSort.Genre),
        new("albumartist", "ALBUM ARTIST",  nameof(AudioTrack.AlbumArtist),     ColumnLook.Text,      150, false, TrackSort.AlbumArtist),
        new("composer",    "COMPOSER",      nameof(AudioTrack.Composer),        ColumnLook.Text,      140, false, TrackSort.Composer),
        new("comment",     "COMMENT",       nameof(AudioTrack.Comment),         ColumnLook.Text,      160, false, TrackSort.Comment),
        new("bitrate",     "BITRATE",       nameof(AudioTrack.BitrateLabel),    ColumnLook.Mono,       74, false, TrackSort.Bitrate),
        new("tag",         "TAG",           nameof(AudioTrack.TagFormat),       ColumnLook.Mono,       74, false, TrackSort.TagFormat),
        new("codec",       "CODEC",         nameof(AudioTrack.Codec),           ColumnLook.Mono,      120, false, TrackSort.Codec),
    ];

    public static TrackColumn? ById(string id) =>
        All.FirstOrDefault(c => c.Id == id);

    /// <summary>Das Cover ist ein Bild, kein Text, und wird eigens gezeichnet.</summary>
    public bool IsCover => Id == "cover";
}
