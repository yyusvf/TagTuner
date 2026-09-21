using System.Text.Json;
using System.Text.Json.Serialization;

namespace TagTuner.Core.Settings;

/// <summary>
/// Die gespeicherten Einstellungen. Liegen als JSON unter
/// %APPDATA%\TagTuner\settings.json — nicht in der Musikbibliothek.
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

    /// <summary>
    /// Was „Tags einfügen" überträgt: „all" nimmt alles mit, „format" lässt
    /// Titel und Track-Nummer stehen. Die beiden sind je Datei verschieden;
    /// wer ein Album angleicht, will sie fast nie überschreiben.
    /// </summary>
    public string TagPasteMode { get; set; } = "format";

    // ── Sicherheit ───────────────────────────────────────────────
    public string? BackupFolder { get; set; }

    /// <summary>„never", „7" oder „30" Tage.</summary>
    public string BackupRetention { get; set; } = "7";

    /// <summary>Ablegen ohne Rückfrage angleichen.</summary>
    public bool SkipConformDialog { get; set; }

    // ── Oberfläche ───────────────────────────────────────────────
    /// <summary>
    /// „de" oder „en". Leer heißt: noch nie gewählt, dann entscheidet beim
    /// ersten Start die Sprache des Systems.
    /// </summary>
    public string Language { get; set; } = "";
    public string? LastFolder { get; set; }
    public double WindowWidth { get; set; } = 2000;
    public double WindowHeight { get; set; } = 1125;

    /// <summary>Eigener ffmpeg-Pfad; leer heißt „mitgeliefertes benutzen".</summary>
    public string? FfmpegPath { get; set; }

    // ── Spaltenbreiten ───────────────────────────────────────────
    public double MetaWidth { get; set; } = 300;
    public double TreeWidth { get; set; } = 214;
    public double SideWidth { get; set; } = 248;

    /// <summary>Breiten der Listenspalten: Titel, Interpret, Album, Format, Samplerate, Dauer.</summary>
    public double[]? TrackColumnWidths { get; set; }

    // ── Bibliothek ───────────────────────────────────────────────

    /// <summary>
    /// Selbst hinzugefügte Wurzeln im Baum, zusätzlich zu Musik, Downloads,
    /// Benutzerordner und den Laufwerken.
    /// </summary>
    public List<string> LibraryPaths { get; set; } = [];

    /// <summary>
    /// Standardwurzeln, die ausgeblendet wurden. Ausgeblendet statt gelöscht,
    /// weil Musik, Downloads und die Laufwerke nicht aus einer Liste stammen,
    /// die sich bearbeiten ließe — sie werden bei jedem Start neu ermittelt.
    /// </summary>
    public List<string> HiddenRoots { get; set; } = [];

    /// <summary>
    /// Im Baum nur Ordner zeigen, unter denen irgendwo Audiodateien liegen.
    /// Kostet beim Aufklappen etwas Zeit, weil dafür in jeden Unterordner
    /// geschaut werden muss.
    /// </summary>
    public bool OnlyAudioFolders { get; set; }

    // ── Aktualisierung ───────────────────────────────────────────

    /// <summary>„never", „ask" oder „auto".</summary>
    public string UpdateBehavior { get; set; } = "ask";

    /// <summary>Wann zuletzt im Hintergrund gesucht wurde; höchstens einmal am Tag.</summary>
    public string? LastUpdateCheck { get; set; }

    /// <summary>Diese Version wurde abgelehnt und wird nicht wieder gemeldet.</summary>
    public string? SkippedVersion { get; set; }

    // ── Wiedergabe ───────────────────────────────────────────────
    public double Volume { get; set; } = 0.7;

    // ── Regeln je Ordner ─────────────────────────────────────────

    /// <summary>
    /// Nur die Ordner, in denen von der Vorgabe abgewichen wurde. Der
    /// Schlüssel ist der kleingeschriebene Pfad — ein Dictionary aus JSON
    /// bringt seinen Vergleicher nicht mit, und Windows-Pfade unterscheiden
    /// keine Groß- und Kleinschreibung.
    /// </summary>
    public Dictionary<string, FolderRule> FolderRules { get; set; } = [];

    // ── Globale Vorgabe für alle Ordner ohne eigene Regel ────────
    public bool DefaultAutoConform { get; set; } = true;
    public bool DefaultInheritTags { get; set; } = true;

    [JsonIgnore]
    public FolderRule GlobalRule => new()
    {
        AutoConform = DefaultAutoConform,
        InheritTags = DefaultInheritTags,
    };

    private static string RuleKey(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();

    /// <summary>
    /// Die Regel eines Ordners. Eine eigene Regel gewinnt immer; sonst gilt
    /// die globale Vorgabe, und zwar in ihrem jeweils aktuellen Stand.
    /// </summary>
    public FolderRule RuleFor(string path) =>
        FolderRules.TryGetValue(RuleKey(path), out var rule) ? rule : GlobalRule;

    /// <summary>Hat dieser Ordner eine eigene Regel, die von der globalen abweicht?</summary>
    public bool HasOwnRule(string path) => FolderRules.ContainsKey(RuleKey(path));

    /// <summary>
    /// Merkt sich eine Regel. Entspricht sie der globalen Vorgabe, wird der
    /// Eintrag entfernt: Der Ordner folgt dann wieder der globalen Regel,
    /// auch wenn diese sich später ändert.
    /// </summary>
    public void SetRule(string path, FolderRule rule)
    {
        var key = RuleKey(path);
        if (rule.Matches(GlobalRule)) FolderRules.Remove(key);
        else FolderRules[key] = rule;
        Save();
    }

    /// <summary>Nimmt die eigene Regel zurück, sodass wieder die globale gilt.</summary>
    public void ClearRule(string path)
    {
        if (FolderRules.Remove(RuleKey(path))) Save();
    }

    // ── Laden und Speichern ──────────────────────────────────────

    /// <summary>
    /// Verlegt Einstellungen, Verlauf und Sicherungen woandershin.
    ///
    /// Nur für Testläufe. <see cref="Save"/> schreibt sonst in die echte
    /// Einstellungsdatei des Nutzers, und ein Test, der ein frisches
    /// <see cref="AppSettings"/> anlegt und darauf <see cref="SetRule"/>
    /// aufruft, überschreibt damit alles, was dort stand.
    /// </summary>
    [JsonIgnore]
    public static string? DirectoryOverride { get; set; }

    [JsonIgnore]
    public static string Directory =>
        DirectoryOverride
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TagTuner");

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

    /// <summary>
    /// Übernimmt Einstellungen, Verlauf und Sicherungen der Vorgängerversion.
    ///
    /// Die App hieß bis Version 0.4 LocalPrep und legte alles unter diesem
    /// Namen ab. Ohne diesen Schritt stünde nach dem Update eine leere
    /// Bibliothek da, und der Verlauf wäre weg.
    /// </summary>
    private static void MigrateFromLocalPrep()
    {
        try
        {
            if (File.Exists(FilePath)) return;

            var old = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LocalPrep");
            if (!System.IO.Directory.Exists(old)) return;

            System.IO.Directory.CreateDirectory(Directory);

            foreach (var name in new[] { "settings.json", "history.json" })
            {
                var from = Path.Combine(old, name);
                if (File.Exists(from)) File.Copy(from, Path.Combine(Directory, name), overwrite: false);
            }

            var oldBackups = Path.Combine(old, "Backups");
            if (System.IO.Directory.Exists(oldBackups))
            {
                var target = Path.Combine(Directory, "Backups");
                System.IO.Directory.CreateDirectory(target);
                foreach (var file in System.IO.Directory.EnumerateFiles(oldBackups, "*.bak"))
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
            }
        }
        catch { }
    }

    /// <summary>Lädt die Einstellungen. Eine kaputte Datei führt zu Vorgaben, nicht zum Absturz.</summary>
    public static AppSettings Load()
    {
        MigrateFromLocalPrep();

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
