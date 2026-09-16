using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalPrep.Core.Settings;

/// <summary>
/// Die gespeicherten Einstellungen. Liegen als JSON unter
/// %APPDATA%\LocalPrep\settings.json — nicht in der Musikbibliothek.
/// </summary>
public sealed class AppSettings
{
    // ── Standardprofil ───────────────────────────────────────────
    // Greift nur, wenn ein Ordner leer oder uneinheitlich ist. Einheitliche
    // Ordner bestimmen ihr Ziel selbst.
    public string DefaultFormat { get; set; } = "FLAC";
    public int DefaultSampleRate { get; set; } = 44100;

    /// <summary>
    /// Nur relevant, wenn eine Konvertierung ohnehin verlustbehaftet kodiert.
    /// Vorhandene Dateien werden nie wegen ihrer Bitrate angefasst.
    /// </summary>
    public int DefaultKbps { get; set; } = 256;

    // ── Namensschema ─────────────────────────────────────────────
    /// <summary>Muster, nach dem aus einem Dateinamen Titel und Track gelesen werden.</summary>
    public string ParsePattern { get; set; } = "{track} - {title}";

    /// <summary>Muster für „Umbenennen".</summary>
    public string RenamePattern { get; set; } = "{track} - {title}";

    // ── Sicherheit ───────────────────────────────────────────────
    public string? BackupFolder { get; set; }

    /// <summary>„never", „7" oder „30" Tage.</summary>
    public string BackupRetention { get; set; } = "7";

    /// <summary>Ablegen ohne Rückfrage angleichen.</summary>
    public bool SkipConformDialog { get; set; }

    // ── Oberfläche ───────────────────────────────────────────────
    public string Language { get; set; } = "de";
    public string? LastFolder { get; set; }
    public double WindowWidth { get; set; } = 1360;
    public double WindowHeight { get; set; } = 820;

    /// <summary>Eigener ffmpeg-Pfad; leer heißt „mitgeliefertes benutzen".</summary>
    public string? FfmpegPath { get; set; }

    // ── Laden und Speichern ──────────────────────────────────────

    [JsonIgnore]
    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LocalPrep");

    [JsonIgnore]
    public static string FilePath => Path.Combine(Directory, "settings.json");

    [JsonIgnore]
    public string ResolvedBackupFolder =>
        string.IsNullOrWhiteSpace(BackupFolder) ? Path.Combine(Directory, "Backups") : BackupFolder;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Lädt die Einstellungen. Eine kaputte Datei führt zu Vorgaben, nicht zum Absturz.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
                if (loaded is not null) return loaded;
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch { /* Einstellungen sind Nebensache — ein Schreibfehler darf nichts abbrechen */ }
    }
}
