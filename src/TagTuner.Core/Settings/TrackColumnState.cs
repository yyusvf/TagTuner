namespace TagTuner.Core.Settings;

/// <summary>
/// Eine Spalte, wie der Nutzer sie eingestellt hat.
///
/// Gespeichert wird nur das Kürzel, nicht die Beschriftung: Die kommt aus
/// dem Katalog und hängt an der Sprache. Eine Spalte, die es in einer
/// neueren Fassung nicht mehr gibt, wird beim Lesen still übergangen.
/// </summary>
public sealed class TrackColumnState
{
    public required string Id { get; set; }
    public bool Visible { get; set; } = true;
    public double Width { get; set; } = 100;
}
