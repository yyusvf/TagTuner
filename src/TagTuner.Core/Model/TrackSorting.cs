namespace TagTuner.Core.Model;

/// <summary>Wonach die Trackliste sortiert ist.</summary>
public enum TrackSort
{
    /// <summary>Playlist-Reihenfolge: Disc, Track, dann Name.</summary>
    Natural,
    Track,
    Title,
    Artist,
    Album,
    Format,
    SampleRate,
    Duration,
}

public static class TrackSorting
{
    /// <summary>
    /// Sortiert für die Anzeige.
    ///
    /// Bei Gleichstand entscheidet immer der Dateiname — sonst springen Zeilen
    /// mit gleichem Interpreten bei jedem Neueinlesen umher, weil die
    /// Ausgangsreihenfolge des Dateisystems nicht garantiert ist.
    /// </summary>
    public static List<AudioTrack> Apply(IEnumerable<AudioTrack> tracks, TrackSort key, bool descending)
    {
        var list = tracks.ToList();
        if (key == TrackSort.Natural) return FolderSort(list);

        var text = StringComparer.CurrentCultureIgnoreCase;

        IOrderedEnumerable<AudioTrack> ordered = key switch
        {
            // Dateien ohne Nummer gehören ans Ende, nicht an den Anfang.
            TrackSort.Track => Order(t => t.Track == 0 ? uint.MaxValue : t.Track),
            TrackSort.Title => Order(t => Fallback(t.Title, t.FileName), text),
            TrackSort.Artist => Order(t => t.Artist, text),
            TrackSort.Album => Order(t => t.Album, text),
            TrackSort.Format => Order(t => t.Format, text),
            TrackSort.SampleRate => Order(t => t.SampleRate),
            _ => Order(t => t.Duration),
        };

        return [.. ordered.ThenBy(t => t.FileName, text)];

        IOrderedEnumerable<AudioTrack> Order<TKey>(
            Func<AudioTrack, TKey> pick, IComparer<TKey>? comparer = null) =>
            descending
                ? list.OrderByDescending(pick, comparer)
                : list.OrderBy(pick, comparer);

        static string Fallback(string value, string other) =>
            string.IsNullOrWhiteSpace(value) ? other : value;
    }

    /// <summary>Die Reihenfolge, in der ein Ordner als Playlist gemeint ist.</summary>
    public static List<AudioTrack> FolderSort(IEnumerable<AudioTrack> tracks) =>
        [.. tracks
            .OrderBy(t => t.Disc == 0 ? uint.MaxValue : t.Disc)
            .ThenBy(t => t.Track == 0 ? uint.MaxValue : t.Track)
            .ThenBy(t => t.FileName, StringComparer.CurrentCultureIgnoreCase)];
}
