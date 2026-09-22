namespace TagTuner.Core.Audio;

/// <summary>Qualitätsvorgaben je Zielformat. Bitrate nur dort, wo sie zählt.</summary>
public sealed record EncodeOptions
{
    /// <summary>Zielformat, z. B. „FLAC".</summary>
    public required string Format { get; init; }

    /// <summary>Ziel-Samplerate in Hz, oder null um die vorhandene zu behalten.</summary>
    public int? SampleRate { get; init; }

    /// <summary>
    /// Bitrate in kbps für verlustbehaftete Ziele. Wird nur herangezogen, wenn
    /// ohnehin verlustbehaftet kodiert wird — vorhandene Dateien werden nie
    /// wegen ihrer Bitrate angefasst.
    /// </summary>
    /// <summary>
    /// So hoch, wie das Format es zulässt. Eine Bitrate zum Einstellen gab
    /// es einmal; sie war eine Frage, auf die niemand eine andere Antwort
    /// als „die beste" gab, und für AAC deckelt der Kodierer ohnehin.
    /// </summary>
    public int Kbps { get; init; } = 320;

    /// <summary>MP3 mit variabler Bitrate (-q:a 0) statt fester Rate.</summary>
    public bool Mp3Vbr { get; init; }

    /// <summary>FLAC-Kompressionsstufe 0–8.</summary>
    public int FlacCompression { get; init; } = 5;

    /// <summary>Vorbis-Qualität 0–10.</summary>
    public int OggQuality { get; init; } = 7;

    /// <summary>PCM-Bittiefe für WAV/AIFF: 16, 24 oder 32.</summary>
    public int BitDepth { get; init; } = 24;
}

/// <summary>
/// Baut die ffmpeg-Kommandozeile.
///
/// Diese Datei ist die wörtliche Übernahme dessen, was in der Electron-Fassung
/// über mehrere Fehlersuchen hinweg entstanden ist. Jede Zeile hier hat einen
/// Grund, der ohne Kommentar nicht erkennbar wäre — darum stehen sie dabei.
/// </summary>
public static class FfmpegArgs
{
    /// <summary>
    /// Codec-Argumente für das Zielformat.
    /// </summary>
    public static List<string> Codec(EncodeOptions o)
    {
        var fmt = AudioFormats.Normalize(o.Format);
        return fmt switch
        {
            "mp3" => o.Mp3Vbr
                ? ["-c:a", "libmp3lame", "-q:a", "0"]
                : ["-c:a", "libmp3lame", "-b:a", $"{o.Kbps}k"],

            "flac" => ["-c:a", "flac", "-compression_level", o.FlacCompression.ToString()],

            "ogg" => ["-c:a", "libvorbis", "-q:a", o.OggQuality.ToString()],

            // Der native AAC-Kodierer deckelt ohnehin; wir fragen gar nicht erst mehr an.
            "m4a" or "aac" => ["-c:a", "aac", "-b:a", $"{Math.Min(o.Kbps, AudioFormats.AacMaxKbps)}k"],

            "wav" => ["-c:a", PcmCodec(o.BitDepth, bigEndian: false)],

            // AIFF ist Big-Endian. Mit pcm_s24le bricht ffmpeg mit EINVAL ab —
            // in der Electron-Fassung war der AIFF-Export dadurch komplett kaputt,
            // ohne dass es je jemandem auffiel.
            "aiff" or "aif" => ["-c:a", PcmCodec(o.BitDepth, bigEndian: true), "-f", "aiff"],

            _ => ["-c:a", "copy"],
        };
    }

    /// <summary>
    /// Argumente, die Tags und Cover über die Konvertierung retten.
    /// Müssen <em>nach</em> den Codec-Argumenten stehen.
    /// </summary>
    public static List<string> Metadata(string targetFormatOrPath, bool withCover = true)
    {
        var fmt = AudioFormats.Normalize(targetFormatOrPath);
        var args = new List<string> { "-map", "0:a", "-map_metadata", "0" };

        if (!withCover)
        {
            // Ohne Bildspur. Manche Dateien tragen ein Cover, dessen Maße
            // ffmpeg nicht aus dem Container liest („dimensions not set"), und
            // dann scheitert nicht nur das Bild, sondern der ganze Schreib-
            // vorgang. Das Cover wird in dem Fall danach selbst angeheftet.
            args.AddRange(["-vn"]);
        }
        else if (AudioFormats.CanCarryCover(fmt))
        {
            // "0:v?" — das Cover mitnehmen, wenn eines da ist, und nicht
            // scheitern, wenn nicht. Kopieren statt neu kodieren, damit die
            // Bytes exakt erhalten bleiben. Ohne diese Zeile verwirft ffmpeg
            // das Coverbild stillschweigend.
            args.AddRange(["-map", "0:v?", "-c:v", "copy", "-disposition:v", "attached_pic"]);
        }

        if (fmt == "mp3")
        {
            // ID3v2.3 statt ffmpegs Vorgabe v2.4: Der Windows-Explorer zeigt
            // Cover-Thumbnails nur bei v2.3 zuverlässig an. v1 zusätzlich für
            // ältere Abspielgeräte.
            args.AddRange(["-id3v2_version", "3", "-write_id3v1", "1"]);
        }

        return args;
    }

    /// <summary>Vollständige Argumentliste für einen Konvertierungslauf.</summary>
    public static List<string> Build(
        string input, string output, EncodeOptions o, bool withCover = true)
    {
        var args = new List<string> { "-hide_banner", "-y", "-i", input };

        if (o.SampleRate is int hz && hz > 0)
            args.AddRange(["-ar", hz.ToString()]);

        args.AddRange(Codec(o));
        args.AddRange(Metadata(output, withCover));
        args.Add(output);
        return args;
    }

    /// <summary>
    /// Ist die Bildspur schuld, dass ffmpeg abgebrochen hat?
    ///
    /// Erkennbar an der Meldung: Ein Cover ohne Maßangabe lässt den Muxer den
    /// Dateikopf nicht schreiben, und dann kommt gar nichts heraus.
    /// </summary>
    public static bool CoverBrokeIt(string log) =>
        log.Contains("dimensions not set", StringComparison.OrdinalIgnoreCase)
        || log.Contains("Could not write header", StringComparison.OrdinalIgnoreCase);

    private static string PcmCodec(int bitDepth, bool bigEndian)
    {
        var suffix = bigEndian ? "be" : "le";
        return bitDepth switch
        {
            16 => $"pcm_s16{suffix}",
            32 => $"pcm_f32{suffix}",
            _ => $"pcm_s24{suffix}",
        };
    }

    /// <summary>Für die Protokollzeile: die Argumente so, wie man sie eintippen würde.</summary>
    public static string Quote(IEnumerable<string> args) =>
        string.Join(' ', args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
}
