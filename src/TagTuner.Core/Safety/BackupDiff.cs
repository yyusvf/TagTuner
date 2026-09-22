using TagTuner.Core.Folders;
using TagTuner.Core.Model;

namespace TagTuner.Core.Safety;

/// <summary>
/// Was sich an einer Datei geändert hat, seit sie gesichert wurde: der Wert in
/// der Sicherung, dann der jetzige. Genau das würde das Wiederherstellen
/// zurückdrehen.
/// </summary>
public static class BackupDiff
{
    public static List<FieldChange> Compare(AudioTrack then, AudioTrack now, byte[]? coverThen, byte[]? coverNow)
    {
        var changes = new List<FieldChange>();

        void Text(string field, string a, string b)
        {
            if (!string.Equals(a.Trim(), b.Trim(), StringComparison.Ordinal))
                changes.Add(new FieldChange(field, a, b));
        }

        void Number(string field, uint a, uint b)
        {
            if (a != b) changes.Add(new FieldChange(field, a == 0 ? "" : a.ToString(), b == 0 ? "" : b.ToString()));
        }

        Text("Title", then.Title, now.Title);
        Text("Artist", then.Artist, now.Artist);
        Text("Album", then.Album, now.Album);
        Text("Album artist", then.AlbumArtist, now.AlbumArtist);
        Text("Genre", then.Genre, now.Genre);
        Number("Year", then.Year, now.Year);
        Number("Track", then.Track, now.Track);
        Number("Disc", then.Disc, now.Disc);
        Text("Composer", then.Composer, now.Composer);
        Text("Comment", then.Comment, now.Comment);
        Text("Format", then.Format, now.Format);
        if (then.SampleRate != now.SampleRate)
            changes.Add(new FieldChange("Sample rate", Rate(then.SampleRate), Rate(now.SampleRate)));

        var coverA = coverThen is { Length: > 0 };
        var coverB = coverNow is { Length: > 0 };
        if (coverA != coverB || (coverA && !coverThen!.AsSpan().SequenceEqual(coverNow)))
            changes.Add(new FieldChange("Cover", coverA ? "yes" : "", coverB ? "yes" : ""));

        return changes;
    }

    private static string Rate(int hz) =>
        hz <= 0 ? "" : hz % 1000 == 0 ? $"{hz / 1000} kHz" : $"{hz / 1000.0:0.0} kHz";
}
