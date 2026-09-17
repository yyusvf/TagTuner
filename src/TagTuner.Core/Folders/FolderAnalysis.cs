using TagTuner.Core.Audio;
using TagTuner.Core.Model;

namespace TagTuner.Core.Folders;

/// <summary>Wohin ein Ordner angeglichen wird, und woher diese Vorgabe stammt.</summary>
public sealed record FolderTarget(string Format, int SampleRate, bool FromDefaultProfile);

/// <summary>
/// Was in einem Ordner steckt.
///
/// Bewusst <em>nicht</em> gespeichert: Es gibt kein Profil auf der Platte, weder
/// im Musikordner noch in der Konfiguration. Der Ordner wird bei jedem Öffnen
/// neu gelesen. Damit kann nichts veralten und nichts muss synchron gehalten
/// werden — und in der Musikbibliothek des Nutzers liegen keine Fremddateien.
/// </summary>
public sealed class FolderAnalysis
{
    public required IReadOnlyList<AudioTrack> Tracks { get; init; }
    public required IReadOnlyDictionary<string, int> FormatCounts { get; init; }
    public required IReadOnlyDictionary<int, int> SampleRateCounts { get; init; }

    public bool IsEmpty => Tracks.Count == 0;

    /// <summary>Genau ein Format und genau eine Samplerate im ganzen Ordner.</summary>
    public bool IsUniform => !IsEmpty && FormatCounts.Count == 1 && SampleRateCounts.Count == 1;

    /// <summary>
    /// Das Ziel des Ordners: Ist er einheitlich, bestimmt er es selbst.
    /// Sonst — leer oder gemischt — greift das Standardprofil aus den Einstellungen.
    /// </summary>
    public FolderTarget ResolveTarget(string defaultFormat, int defaultSampleRate)
    {
        if (IsUniform)
            return new FolderTarget(
                FormatCounts.Keys.First(),
                SampleRateCounts.Keys.First(),
                FromDefaultProfile: false);

        return new FolderTarget(defaultFormat.ToUpperInvariant(), defaultSampleRate, FromDefaultProfile: true);
    }

    /// <summary>Die Dateien, die vom Ziel abweichen — also beim Angleichen angefasst würden.</summary>
    public IEnumerable<AudioTrack> Outliers(FolderTarget target) =>
        Tracks.Where(t => !Matches(t, target));

    public static bool Matches(AudioTrack track, FolderTarget target) =>
        string.Equals(AudioFormats.TargetExtension(track.Format),
                      AudioFormats.TargetExtension(target.Format),
                      StringComparison.OrdinalIgnoreCase)
        && track.SampleRate == target.SampleRate;

    public static FolderAnalysis Of(IReadOnlyList<AudioTrack> tracks)
    {
        var formats = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var rates = new Dictionary<int, int>();

        foreach (var t in tracks)
        {
            // Über die Zielendung gruppieren: .aac und .m4a sind derselbe
            // Container und dürfen einen Ordner nicht als gemischt gelten lassen.
            var key = AudioFormats.TargetExtension(t.Format).ToUpperInvariant();
            formats[key] = formats.GetValueOrDefault(key) + 1;
            if (t.SampleRate > 0)
                rates[t.SampleRate] = rates.GetValueOrDefault(t.SampleRate) + 1;
        }

        return new FolderAnalysis
        {
            Tracks = tracks,
            FormatCounts = formats,
            SampleRateCounts = rates,
        };
    }

    /// <summary>
    /// Werte, die ein neu hinzukommender Track vom Ordner erben kann —
    /// also alles, worin sich die vorhandenen Tracks einig sind.
    /// </summary>
    public InheritedTags Inherited()
    {
        return new InheritedTags
        {
            Album = Agreed(t => t.Album),
            Artist = Agreed(t => t.Artist),
            AlbumArtist = Agreed(t => t.AlbumArtist),
            Genre = Agreed(t => t.Genre),
            Year = Agreed(t => t.Year == 0 ? "" : t.Year.ToString()),
            Disc = Agreed(t => t.Disc == 0 ? "" : t.Disc.ToString()),
            HasCover = Tracks.Count > 0 && Tracks.All(t => t.HasCover),
        };

        string? Agreed(Func<AudioTrack, string> pick)
        {
            if (Tracks.Count == 0) return null;
            var values = Tracks.Select(pick)
                               .Where(v => !string.IsNullOrWhiteSpace(v))
                               .Distinct(StringComparer.Ordinal)
                               .ToList();
            return values.Count == 1 ? values[0] : null;
        }
    }

    /// <summary>Die nächste freie Track-Nummer.</summary>
    public uint NextTrackNumber() =>
        Tracks.Count == 0 ? 1 : (uint)(Tracks.Count + 1);
}

/// <summary>Tags, in denen sich ein Ordner einig ist. null = uneinheitlich oder leer.</summary>
public sealed record InheritedTags
{
    public string? Album { get; init; }
    public string? Artist { get; init; }
    public string? AlbumArtist { get; init; }
    public string? Genre { get; init; }
    public string? Year { get; init; }
    public string? Disc { get; init; }
    public bool HasCover { get; init; }
}
