using System.Text.RegularExpressions;
using TagTuner.Core.Model;

namespace TagTuner.Core.Folders;

/// <summary>
/// Bringt die Nummer vorn im Dateinamen auf die Track-Nummer.
///
/// Nur die Ziffern vorn werden ersetzt, alles danach bleibt: Aus
/// „03 - Ufer.mp3" wird „01 - Ufer.mp3". Ein Name ohne Nummer bekommt eine
/// davor. Eine Jahreszahl vorn („2019 Live") zählt nicht als Nummer, weil
/// eine Track-Nummer höchstens dreistellig ist.
/// </summary>
public static partial class FileNumbering
{
    /// <summary>„1-03", „03", „3", gefolgt von Leerraum, Punkt, Unterstrich oder Strich.</summary>
    [GeneratedRegex(@"^(?:(?<disc>\d{1,2})-)?(?<track>\d{1,3})(?=[\s._-])")]
    private static partial Regex Prefix();

    /// <summary>Der neue Name, oder null wenn er schon stimmt.</summary>
    /// <param name="width">Stellen der Nummer, mindestens 2: „01" statt „1".</param>
    /// <param name="disc">Mit mehreren Discs die Disc davor, als „2-01". Sonst 0.</param>
    public static string? NewName(string fileName, uint track, int width, uint disc = 0)
    {
        if (track == 0) return null;

        var number = track.ToString().PadLeft(width, '0');
        var wanted = disc > 0 ? $"{disc}-{number}" : number;

        var match = Prefix().Match(fileName);
        var renamed = match.Success
            ? wanted + fileName[match.Length..]
            : wanted + " " + fileName;

        return renamed == fileName ? null : renamed;
    }

    /// <summary>
    /// Welche Dateien wie heißen sollen, nach der Track-Nummer in ihren Tags.
    ///
    /// Übersprungen wird jede Datei, deren neuer Name schon belegt wäre, sei
    /// es von einer anderen Datei im Ordner oder von einem zweiten Ziel. Ein
    /// Tausch zweier Namen ließe sich über den Verlauf nicht sauber
    /// zurücknehmen: Das Rückgängig der einen Datei löschte die andere.
    /// </summary>
    public static (List<(AudioTrack Track, string Target)> Plan, List<AudioTrack> Skipped) Plan(
        IReadOnlyList<AudioTrack> tracks, IEnumerable<string> existingFiles)
    {
        var multi = tracks.Select(t => t.Disc).Where(d => d > 0).Distinct().Count() > 1;
        var highest = tracks.Select(t => t.Track).DefaultIfEmpty(0u).Max();
        var width = Math.Max(2, highest.ToString().Length);

        var taken = new HashSet<string>(existingFiles, StringComparer.OrdinalIgnoreCase);
        var plan = new List<(AudioTrack, string)>();
        var skipped = new List<AudioTrack>();

        foreach (var track in tracks)
        {
            var name = NewName(track.FileName, track.Track, width, multi ? track.Disc : 0);
            if (name is null) continue;

            var target = Path.Combine(Path.GetDirectoryName(track.Path) ?? "", name);
            if (!taken.Add(target))
            {
                skipped.Add(track);
                continue;
            }
            plan.Add((track, target));
        }

        return (plan, skipped);
    }
}
