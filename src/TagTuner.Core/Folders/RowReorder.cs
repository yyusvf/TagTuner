namespace TagTuner.Core.Folders;

/// <summary>
/// Die Rechnung hinter dem Umsortieren per Zeiger, ohne Steuerelemente.
///
/// Die Liste besteht aus Zeilen: Lieder und, im Album mit mehreren Discs,
/// Disc-Zeilen dazwischen. Gezogen wird ein Block aus Liedern; er fällt in
/// eine Lücke zwischen den übrigen Zeilen. Alles hier arbeitet mit Indizes
/// und Höhen, damit es sich ohne Fenster prüfen lässt und die Liste nur noch
/// verschieben muss, was hier ausgerechnet wurde.
/// </summary>
public static class RowReorder
{
    /// <summary>
    /// Die Zeilen, die beim Ziehen stehen bleiben, in ihrer Reihenfolge.
    /// </summary>
    public static int[] Remaining(int count, IReadOnlyCollection<int> block)
    {
        var inBlock = new HashSet<int>(block);
        var rest = new List<int>(count);
        for (var i = 0; i < count; i++)
            if (!inBlock.Contains(i)) rest.Add(i);
        return [.. rest];
    }

    /// <summary>
    /// In welche Lücke zwischen den übrigen Zeilen der Block fällt, wenn seine
    /// Oberkante bei <paramref name="blockTop"/> steht.
    ///
    /// Gemessen an Oberkanten statt am Zeiger: Die Lücke ist die, deren Platz
    /// dem Block am nächsten liegt. Das entspricht der Mitte der
    /// Nachbarzeile als Grenze, auch wenn Disc-Zeilen niedriger sind als
    /// Lieder — mit einer festen Schrittweite wie auf der Website wäre der
    /// Wechsel über eine Disc-Zeile hinweg zu früh oder zu spät.
    /// </summary>
    /// <param name="heights">Die Höhen der übrigen Zeilen, in ihrer Reihenfolge.</param>
    /// <returns>0 = vor der ersten Zeile, <c>heights.Count</c> = ans Ende.</returns>
    public static int NearestGap(IReadOnlyList<double> heights, double blockTop)
    {
        var best = 0;
        var bestDistance = Math.Abs(blockTop);
        var top = 0.0;
        for (var g = 1; g <= heights.Count; g++)
        {
            top += heights[g - 1];
            var distance = Math.Abs(top - blockTop);

            // Bei Gleichstand bleibt es bei der oberen Lücke, damit der Block
            // nicht zwischen zwei gleich guten Stellen hin- und herspringt.
            if (distance < bestDistance) { best = g; bestDistance = distance; }
            else if (top > blockTop) break;
        }
        return best;
    }

    /// <summary>
    /// Die neue Reihenfolge als Indizes der alten Zeilen: die übrigen Zeilen
    /// bis zur Lücke, dann der Block in seiner bisherigen Reihenfolge, dann der
    /// Rest. Verstreut markierte Lieder landen so beisammen, wie beim
    /// Umsortieren der Liste von Windows.
    /// </summary>
    public static int[] Order(int count, IReadOnlyCollection<int> block, int gap)
    {
        var rest = Remaining(count, block);
        gap = Math.Clamp(gap, 0, rest.Length);
        var sorted = block.Distinct().Where(i => i >= 0 && i < count).Order().ToArray();
        return [.. rest.Take(gap), .. sorted, .. rest.Skip(gap)];
    }

    /// <summary>Oberkante jeder Zeile, gezählt vom Anfang der Liste.</summary>
    public static double[] Tops(IReadOnlyList<double> heights)
    {
        var tops = new double[heights.Count];
        var y = 0.0;
        for (var i = 0; i < heights.Count; i++) { tops[i] = y; y += heights[i]; }
        return tops;
    }

    /// <summary>
    /// Um wie viel jede Zeile verschoben werden muss, damit sie dort steht, wo
    /// sie in <paramref name="order"/> hingehört. Nach alten Indizes.
    /// </summary>
    public static double[] Shifts(IReadOnlyList<double> heights, IReadOnlyList<int> order)
    {
        var before = Tops(heights);
        var shifts = new double[heights.Count];
        var y = 0.0;
        foreach (var i in order)
        {
            shifts[i] = y - before[i];
            y += heights[i];
        }
        return shifts;
    }

    /// <summary>
    /// Die Disc jeder Liedzeile nach der Disc-Zeile, unter der sie steht.
    /// Über der ersten Disc-Zeile zählt ein Lied zur ersten Disc; ganz ohne
    /// Disc-Zeilen zu keiner (0).
    /// </summary>
    /// <param name="headers">Je Zeile die Disc einer Disc-Zeile, null für ein Lied.</param>
    /// <returns>Für jede Liedzeile in Reihenfolge ihre Disc.</returns>
    public static uint[] Sections(IReadOnlyList<uint?> headers)
    {
        var disc = headers.FirstOrDefault(h => h is not null) ?? 0;
        var result = new List<uint>(headers.Count);
        foreach (var h in headers)
        {
            if (h is { } d) disc = d;
            else result.Add(disc);
        }
        return [.. result];
    }

    /// <summary>
    /// Die Nummer jeder Stelle, wenn jede Disc für sich von 1 an zählt.
    /// </summary>
    public static uint[] Numbers(IReadOnlyList<uint> discs)
    {
        var perDisc = new Dictionary<uint, uint>();
        var result = new uint[discs.Count];
        for (var i = 0; i < discs.Count; i++)
            result[i] = perDisc[discs[i]] = perDisc.GetValueOrDefault(discs[i]) + 1;
        return result;
    }

    /// <summary>
    /// Welche Lieder nach dem Umsortieren eine andere Nummer oder Disc tragen.
    /// Die leuchten danach kurz auf; ein Lied, das nur mitgerutscht ist und
    /// seine Nummer behält, bleibt ruhig.
    /// </summary>
    public static HashSet<T> Renumbered<T>(
        IReadOnlyList<T> before, IReadOnlyList<(uint Disc, uint Number)> numbersBefore,
        IReadOnlyList<T> after, IReadOnlyList<(uint Disc, uint Number)> numbersAfter)
        where T : class
    {
        var old = new Dictionary<T, (uint, uint)>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < before.Count && i < numbersBefore.Count; i++) old[before[i]] = numbersBefore[i];

        var changed = new HashSet<T>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < after.Count && i < numbersAfter.Count; i++)
        {
            if (!old.TryGetValue(after[i], out var was) || was != numbersAfter[i]) changed.Add(after[i]);
        }
        return changed;
    }

    /// <summary>
    /// Wie schnell die Liste beim Ziehen am Rand von selbst scrollt, in Pixeln
    /// je Schritt. Je tiefer der Zeiger in den Randstreifen oder darüber
    /// hinaus geht, desto schneller; negativ heißt nach oben.
    /// </summary>
    /// <param name="y">Zeiger, gemessen von der Oberkante der Liste.</param>
    /// <param name="height">Höhe der Liste.</param>
    /// <param name="edge">Breite des Randstreifens.</param>
    /// <param name="max">Höchstgeschwindigkeit je Schritt.</param>
    public static double AutoScrollStep(double y, double height, double edge, double max)
    {
        if (height <= 0 || edge <= 0) return 0;
        edge = Math.Min(edge, height / 3);
        if (y < edge) return -max * Math.Min(1, (edge - y) / edge);
        if (y > height - edge) return max * Math.Min(1, (y - (height - edge)) / edge);
        return 0;
    }
}
