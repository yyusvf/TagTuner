using System.Text.Json.Serialization;

namespace TagTuner.Core.Settings;

/// <summary>
/// Was TagTuner in einem bestimmten Ordner von sich aus tun darf.
///
/// Der Album-Modus ist die Aussage „dieser Ordner ist eine eigene
/// Veröffentlichung": ein Album, eine EP, eine Single. Er ist nicht für
/// Sammelordner gedacht, in denen Zusammengesuchtes liegt, denn er vereinheitlicht
/// bewusst. Was er vereinheitlicht, entscheiden die drei Unterpunkte.
/// </summary>
public sealed class FolderRule
{
    /// <summary>
    /// Der Ordner ist eine eigene Veröffentlichung. Ohne diesen Schalter
    /// bleiben Tags, Cover und Nummern unangetastet, egal was die
    /// Unterpunkte sagen.
    /// </summary>
    public bool AlbumMode { get; set; } = true;

    /// <summary>
    /// Album, Interpret, Album-Interpret, Jahr und Genre vom Ordner
    /// übernehmen. Ein Interpret mit Gast („X feat. Y") bleibt stehen,
    /// solange der Ordner-Interpret darin vorkommt.
    /// </summary>
    public bool BaseTags { get; set; } = true;

    /// <summary>Das Cover des Ordners auf hineingelegte Dateien übertragen.</summary>
    public bool Cover { get; set; } = true;

    /// <summary>
    /// Track-Nummern folgen der Reihenfolge in der Liste, lückenlos von 1 an.
    /// Greift beim Hineinziehen und sofort beim Umsortieren.
    /// </summary>
    public bool Numbering { get; set; } = true;

    /// <summary>Hineingezogene Dateien werden auf Format und Samplerate des Ordners gebracht.</summary>
    public bool AutoConform { get; set; } = true;

    /// <summary>
    /// Der alte Name des Album-Modus. Nur zum Einlesen älterer
    /// Einstellungsdateien: Ohne das stünden alle gemerkten Ordner nach einem
    /// Update wieder auf der Vorgabe.
    /// </summary>
    /// <remarks>
    /// Die Bedingung steht an der Eigenschaft, nicht in den globalen
    /// Optionen: Sonst hinge es davon ab, wer gerade serialisiert, und der
    /// tote Schlüssel landete doch wieder in der Datei.
    /// </remarks>
    [JsonPropertyName("InheritTags")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyInheritTags
    {
        get => null;                       // nie wieder schreiben
        set { if (value is { } on) AlbumMode = on; }
    }

    public bool Matches(FolderRule other) =>
        AlbumMode == other.AlbumMode
        && BaseTags == other.BaseTags
        && Cover == other.Cover
        && Numbering == other.Numbering
        && AutoConform == other.AutoConform;

    public FolderRule Copy() => new()
    {
        AlbumMode = AlbumMode,
        BaseTags = BaseTags,
        Cover = Cover,
        Numbering = Numbering,
        AutoConform = AutoConform,
    };

    // ── Was daraus folgt ─────────────────────────────────────────
    // Ein Unterpunkt allein bewirkt nichts; er beschreibt nur, was der
    // Album-Modus tut, wenn er an ist.

    [JsonIgnore] public bool WritesBaseTags => AlbumMode && BaseTags;
    [JsonIgnore] public bool WritesCover => AlbumMode && Cover;
    [JsonIgnore] public bool WritesNumbers => AlbumMode && Numbering;
}
