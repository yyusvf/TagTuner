using TagTuner.Core.Model;

namespace TagTuner.Core.Folders;

/// <summary>Was an der Nummerierung einer Disc nicht stimmt.</summary>
/// <param name="Disc">0, wenn der Ordner nur eine Disc hat.</param>
/// <param name="Missing">Nummern zwischen 1 und der höchsten, die fehlen.</param>
/// <param name="Doubled">Nummern, die mehrere Dateien tragen, mit ihrer Anzahl.</param>
public sealed record DiscProblems(uint Disc, IReadOnlyList<uint> Missing, IReadOnlyList<(uint Track, int Count)> Doubled);

/// <summary>
/// Findet Lücken und doppelte Nummern in einem Album, bevor man etwas
/// schreibt: Ein fehlender Track 7 ist oft eine vergessene Datei, zwei Mal
/// Track 3 ein Hinweis auf eine Datei aus einer anderen Ausgabe.
/// </summary>
public static class TrackNumberCheck
{
    public sealed record Result(IReadOnlyList<DiscProblems> Discs, int Unnumbered)
    {
        public bool IsClean => Unnumbered == 0 && Discs.Count == 0;
    }

    public static Result Check(IReadOnlyList<AudioTrack> tracks)
    {
        // Mit mehreren Discs zählt jede für sich; mit einer ist die Disc egal.
        var multi = tracks.Select(t => t.Disc).Where(d => d > 0).Distinct().Count() > 1;

        var problems = new List<DiscProblems>();
        foreach (var group in tracks.Where(t => t.Track > 0)
                                    .GroupBy(t => multi ? t.Disc : 0u)
                                    .OrderBy(g => g.Key))
        {
            var counts = group.GroupBy(t => t.Track).ToDictionary(g => g.Key, g => g.Count());
            var max = counts.Keys.Max();

            var missing = Enumerable.Range(1, (int)max).Select(n => (uint)n)
                                    .Where(n => !counts.ContainsKey(n)).ToList();
            var doubled = counts.Where(c => c.Value > 1).OrderBy(c => c.Key)
                                .Select(c => (c.Key, c.Value)).ToList();

            if (missing.Count > 0 || doubled.Count > 0)
                problems.Add(new DiscProblems(group.Key, missing, doubled));
        }

        return new Result(problems, tracks.Count(t => t.Track == 0));
    }

    /// <summary>
    /// Fasst Nummern zu Bereichen zusammen: 4, 5, 6, 9 wird „4–6, 9". Eine
    /// Liste von zwanzig einzelnen Zahlen liest niemand.
    /// </summary>
    public static string Ranges(IEnumerable<uint> numbers)
    {
        var sorted = numbers.Distinct().OrderBy(n => n).ToList();
        var parts = new List<string>();
        for (var i = 0; i < sorted.Count;)
        {
            var j = i;
            while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1) j++;
            parts.Add(j == i ? $"{sorted[i]}" : $"{sorted[i]}–{sorted[j]}");
            i = j + 1;
        }
        return string.Join(", ", parts);
    }
}
