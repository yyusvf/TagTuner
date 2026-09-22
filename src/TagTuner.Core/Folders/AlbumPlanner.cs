using TagTuner.Core.Metadata;
using TagTuner.Core.Model;
using TagTuner.Core.Settings;

namespace TagTuner.Core.Folders;

/// <summary>Eine sichtbare Änderung an einer Datei, für die Vorschau.</summary>
public sealed record FieldChange(string Field, string From, string To);

/// <summary>Was an einer Datei geschrieben würde und wie es in der Vorschau aussieht.</summary>
public sealed record AlbumChange(AudioTrack Track, TagEdit Edit, IReadOnlyList<FieldChange> Changes);

/// <summary>
/// Rechnet aus, was der Album-Modus an den Dateien ändern würde, die schon im
/// Ordner liegen.
///
/// Beim Hineinziehen wirkt der Album-Modus nur auf die neuen Dateien. Wer ihn
/// für einen bestehenden Ordner einschaltet, will aber meist genau diesen
/// vereinheitlichen. Das geschieht nicht still: Hier entsteht nur der Plan,
/// geschrieben wird erst nach einem Blick auf die Vorschau.
/// </summary>
public static class AlbumPlanner
{
    /// <param name="tracks">Die Dateien in Playlist-Reihenfolge.</param>
    /// <param name="inherited">Worauf sich der Ordner einigt, nach Mehrheit.</param>
    /// <param name="needsCover">Die Pfade der Dateien, deren Cover fehlt oder vom Ordner abweicht.</param>
    /// <param name="hasFolderCover">Ob es überhaupt ein Cover des Ordners gibt.</param>
    public static List<AlbumChange> Plan(
        IReadOnlyList<AudioTrack> tracks, FolderRule rule, InheritedTags inherited,
        ISet<string> needsCover, bool hasFolderCover)
    {
        var result = new List<AlbumChange>();
        if (!rule.AlbumMode) return result;

        var numbers = Numbers(tracks);

        for (var i = 0; i < tracks.Count; i++)
        {
            var t = tracks[i];
            var changes = new List<FieldChange>();

            string? Text(string field, string current, string? wanted)
            {
                if (wanted is null || string.Equals(current, wanted, StringComparison.Ordinal)) return null;
                changes.Add(new FieldChange(field, current, wanted));
                return wanted;
            }

            uint? Number(string field, uint current, uint? wanted)
            {
                if (wanted is not { } w || w == current) return null;
                changes.Add(new FieldChange(field, current == 0 ? "" : current.ToString(), w.ToString()));
                return w;
            }

            string? title = null, album = null, artist = null, albumArtist = null, genre = null;
            uint? year = null, track = null;

            if (rule.BaseTags)
            {
                // Ein leerer Titel ist keiner. Der Dateiname ist zwar geraten,
                // aber besser als eine Zeile ohne Beschriftung.
                if (string.IsNullOrWhiteSpace(t.Title))
                    title = Text("Title", "", Path.GetFileNameWithoutExtension(t.Path));

                album = Text("Album", t.Album, inherited.Album);
                artist = Text("Artist", t.Artist, ArtistFor(t.Artist, inherited.Artist));
                albumArtist = Text("Album artist", t.AlbumArtist, inherited.AlbumArtist);
                genre = Text("Genre", t.Genre, inherited.Genre);
                year = Number("Year", t.Year, uint.TryParse(inherited.Year, out var y) ? y : null);

                // Die Disc bleibt, wie sie ist: Nach Mehrheit angeglichen würde
                // aus einem Album mit zwei Discs eines mit einer.
            }

            if (rule.Numbering) track = Number("Track", t.Track, numbers[i]);

            var cover = rule.Cover && hasFolderCover && needsCover.Contains(t.Path);
            if (cover) changes.Add(new FieldChange("Cover", "", ""));

            if (changes.Count == 0) continue;

            result.Add(new AlbumChange(t, new TagEdit
            {
                Title = title,
                Album = album,
                Artist = artist,
                AlbumArtist = albumArtist,
                Genre = genre,
                Year = year,
                Track = track,
            }, changes));
        }

        return result;
    }

    /// <summary>
    /// Die Nummer jeder Datei nach ihrer Stelle, lückenlos von 1 an. Mit
    /// mehreren Discs zählt jede Disc für sich.
    /// </summary>
    public static uint[] Numbers(IReadOnlyList<AudioTrack> tracks)
    {
        var perDisc = new Dictionary<uint, uint>();
        var multi = tracks.Select(t => t.Disc).Where(d => d > 0).Distinct().Count() > 1;
        var result = new uint[tracks.Count];

        for (var i = 0; i < tracks.Count; i++)
        {
            var disc = multi ? tracks[i].Disc : 0u;
            result[i] = perDisc[disc] = perDisc.GetValueOrDefault(disc) + 1;
        }
        return result;
    }

    /// <summary>
    /// Der Interpret des Ordners, außer die Datei nennt ihn schon und noch
    /// jemanden dazu.
    ///
    /// „Kollektiv Halle feat. Gast" bleibt stehen: Dieser Zusatz gehört zum
    /// Lied, nicht zum Ordner, und ihn zu überschreiben wäre ein Verlust, den
    /// niemand bemerkt, bis die Angabe fehlt. Steht schon genau der
    /// Ordner-Interpret dort, wird gar nicht erst geschrieben.
    /// </summary>
    public static string? ArtistFor(string own, string? folder)
    {
        if (folder is null) return null;
        if (string.IsNullOrWhiteSpace(own)) return folder;
        return own.Contains(folder, StringComparison.OrdinalIgnoreCase) ? null : folder;
    }
}
