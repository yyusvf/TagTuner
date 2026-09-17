namespace TagTuner.Core.Settings;

/// <summary>
/// Was TagTuner in einem bestimmten Ordner von sich aus tun darf.
///
/// Beides ist normalerweise an — das ist der Sinn der App. Für Ordner, in
/// denen man bewusst Gemischtes sammelt, lässt es sich einzeln abschalten.
/// </summary>
public sealed class FolderRule
{
    /// <summary>Hineingezogene Dateien erben Album, Interpret, Jahr und Genre vom Ordner.</summary>
    public bool InheritTags { get; set; } = true;

    /// <summary>Hineingezogene Dateien werden auf Format und Samplerate des Ordners gebracht.</summary>
    public bool AutoConform { get; set; } = true;

    public bool Matches(FolderRule other) =>
        InheritTags == other.InheritTags && AutoConform == other.AutoConform;

    public FolderRule Copy() => new() { InheritTags = InheritTags, AutoConform = AutoConform };
}
