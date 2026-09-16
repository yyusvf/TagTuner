namespace LocalPrep.Core.Audio;

/// <summary>
/// Was die einzelnen Container können — und was nicht.
///
/// Die Angaben stammen nicht aus der Dokumentation, sondern aus Messungen an
/// der Electron-Fassung: jede Regel hier hat dort einen Fehler verursacht,
/// bevor sie feststand.
/// </summary>
public static class AudioFormats
{
    /// <summary>Dateiendungen, die LocalPrep als Audio behandelt.</summary>
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac", ".aiff", ".aif" };

    /// <summary>Formate, die als Ziel einer Konvertierung wählbar sind.</summary>
    public static readonly IReadOnlyList<string> Targets =
        new[] { "FLAC", "MP3", "WAV", "OGG", "M4A", "AIFF" };

    /// <summary>
    /// Container, die ein eingebettetes Cover tragen können.
    /// OGG, WAV und AIFF können es nicht — dort ist ein fehlendes Cover nach
    /// der Konvertierung kein Fehler, sondern erwartbar.
    /// </summary>
    private static readonly IReadOnlySet<string> CoverCapable =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "mp3", "flac", "m4a", "aac", "mp4" };

    /// <summary>Verlustbehaftete Formate — nur hier spielt eine Bitrate überhaupt eine Rolle.</summary>
    private static readonly IReadOnlySet<string> Lossy =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "mp3", "ogg", "m4a", "aac" };

    /// <summary>Endung → tatsächlich erzeugte Dateiendung (aac landet in einem m4a-Container).</summary>
    public static string TargetExtension(string format) => Normalize(format) switch
    {
        "mp3" => "mp3",
        "flac" => "flac",
        "wav" => "wav",
        "ogg" => "ogg",
        "m4a" or "aac" => "m4a",
        "aiff" or "aif" => "aiff",
        _ => Normalize(format),
    };

    public static bool IsAudioFile(string path) =>
        Extensions.Contains(Path.GetExtension(path));

    public static bool CanCarryCover(string formatOrPath) =>
        CoverCapable.Contains(Normalize(formatOrPath));

    public static bool IsLossy(string formatOrPath) =>
        Lossy.Contains(Normalize(formatOrPath));

    /// <summary>
    /// Der native AAC-Kodierer von ffmpeg deckelt bei etwa 256 kbps. Höhere
    /// Angaben werden still ignoriert — gemessen: 320 kbps angefordert,
    /// 263 kbps erhalten. Darum gar nicht erst mehr anbieten.
    /// </summary>
    public const int AacMaxKbps = 256;

    /// <summary>„FLAC", „.flac", „C:\x\y.FLAC" → „flac"</summary>
    public static string Normalize(string formatOrPath)
    {
        if (string.IsNullOrWhiteSpace(formatOrPath)) return string.Empty;
        var s = formatOrPath;
        var dot = s.LastIndexOf('.');
        if (dot >= 0) s = s[(dot + 1)..];
        return s.Trim().ToLowerInvariant();
    }
}
