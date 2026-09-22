namespace TagTuner.Core.Model;

/// <summary>Eine Audiodatei mit dem, was für Anzeige und Bearbeitung gebraucht wird.</summary>
public sealed class AudioTrack
{
    public required string Path { get; set; }
    public required string FileName { get; set; }

    // ── Technisch ────────────────────────────────────────────────
    /// <summary>Großgeschrieben für die Anzeige: „FLAC", „MP3".</summary>
    public string Format { get; set; } = "";
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int Bitrate { get; set; }
    public TimeSpan Duration { get; set; }
    public long Size { get; set; }
    public bool HasCover { get; set; }

    /// <summary>Eine flache Kopie, etwa um eine Änderung vorab zu zeigen, ohne das Original anzufassen.</summary>
    public AudioTrack Copy() => (AudioTrack)MemberwiseClone();

    /// <summary>Welche Tag-Art in der Datei steckt: „ID3v2.3", „Vorbis", „MP4".</summary>
    public string TagFormat { get; set; } = "";

    /// <summary>Der Kodierer, wie ihn die Datei selbst nennt: „MPEG Layer 3", „FLAC".</summary>
    public string Codec { get; set; } = "";

    public string BitrateLabel => Bitrate > 0 ? $"{Bitrate} kbps" : "";
    public string YearLabel => Year == 0 ? "" : Year.ToString();

    // ── Tags ─────────────────────────────────────────────────────
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string AlbumArtist { get; set; } = "";
    public string Genre { get; set; } = "";
    public string Composer { get; set; } = "";
    public string Comment { get; set; } = "";
    public uint Year { get; set; }
    public uint Track { get; set; }
    public uint Disc { get; set; }

    /// <summary>
    /// Die Track-Nummer für die Liste. Ohne Nummer bleibt die Spalte leer —
    /// eine 0 sieht aus wie eine echte Angabe und ist keine.
    /// </summary>
    public string TrackLabel => Track == 0 ? "" : Track.ToString();

    public string DiscLabel => Disc == 0 ? "" : Disc.ToString();

    /// <summary>„44,1 kHz" — für Tabellenspalten.</summary>
    public string SampleRateLabel =>
        SampleRate <= 0 ? ""
        : (SampleRate % 1000 == 0
            ? $"{SampleRate / 1000} kHz"
            : $"{SampleRate / 1000.0:0.0} kHz");

    public string DurationLabel =>
        Duration <= TimeSpan.Zero ? ""
        : Duration.TotalHours >= 1
            ? Duration.ToString(@"h\:mm\:ss")
            : Duration.ToString(@"m\:ss");

    public string SizeLabel => Size switch
    {
        <= 0 => "",
        < 1024 => $"{Size} B",
        < 1024 * 1024 => $"{Size / 1024.0:0.#} KB",
        _ => $"{Size / 1024.0 / 1024.0:0.#} MB",
    };

    public override string ToString() => FileName;
}
