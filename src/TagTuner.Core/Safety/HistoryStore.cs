using System.Text.Json;
using System.Text.Json.Serialization;
using TagTuner.Core.Settings;

namespace TagTuner.Core.Safety;

/// <summary>Eine Datei innerhalb eines Vorgangs.</summary>
public sealed class HistoryFile
{
    /// <summary>Der Pfad, an den beim Rückgängigmachen zurückgeschrieben wird.</summary>
    public required string Original { get; set; }

    /// <summary>Die Sicherung, oder null wenn sie gelöscht wurde bzw. abgelaufen ist.</summary>
    public string? BackupPath { get; set; }

    /// <summary>Neu erzeugte Datei, die beim Rückgängigmachen wieder verschwinden muss.</summary>
    public string? OutputPath { get; set; }
}

public sealed class HistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n")[..10];
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Kind { get; set; } = "";
    public string Description { get; set; } = "";
    public List<HistoryFile> Files { get; set; } = [];

    /// <summary>
    /// Abgeleitet, nie gespeichert: Eine Sicherung kann jederzeit verschwinden —
    /// durch Aufräumen, „alle löschen" oder von Hand. Ein gespeichertes Flag
    /// würde behaupten, ein Undo sei möglich, das dann ins Leere liefe.
    /// </summary>
    [JsonIgnore]
    public bool CanUndo => Files.Any(f => f.BackupPath is not null && File.Exists(f.BackupPath));
}

public sealed record UndoResult(int Restored, int Failed);

/// <summary>Der Verlauf, als JSON neben den Einstellungen.</summary>
public sealed class HistoryStore
{
    private const int MaxEntries = 200;
    private static string FilePath => Path.Combine(AppSettings.Directory, "history.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private List<HistoryEntry> _entries = [];

    /// <summary>Wann die Datei zuletzt gelesen oder geschrieben wurde.</summary>
    private DateTime _stamp;

    public IReadOnlyList<HistoryEntry> Entries { get { Load(); return _entries; } }

    public HistoryStore() => Load();

    /// <summary>
    /// Liest die Datei, wenn sie sich seit dem letzten Mal geändert hat.
    ///
    /// Auch ein anderer Prozess schreibt hinein: das kleine Fenster zum
    /// Bearbeiten aus dem Explorer. Ohne das Nachlesen überschriebe das
    /// Hauptfenster dessen Einträge mit seinem alten Stand.
    /// </summary>
    private void Load()
    {
        try
        {
            var stamp = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : default;
            if (stamp == _stamp) return;
            _stamp = stamp;
            _entries = stamp == default ? [] :
                JsonSerializer.Deserialize<List<HistoryEntry>>(
                    File.ReadAllText(FilePath), JsonOpts) ?? [];
        }
        catch { _entries = []; }
    }

    private void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(AppSettings.Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_entries, JsonOpts));
            _stamp = File.GetLastWriteTimeUtc(FilePath);
        }
        catch { }
    }

    /// <summary>Ein neuer Eintrag ist dazugekommen.</summary>
    public event Action<HistoryEntry>? Added;

    public HistoryEntry Add(string kind, string description, IEnumerable<HistoryFile> files)
    {
        Load();
        var entry = new HistoryEntry
        {
            Kind = kind,
            Description = description,
            Files = files.ToList(),
        };
        _entries.Insert(0, entry);
        if (_entries.Count > MaxEntries) _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        Save();
        Added?.Invoke(entry);
        return entry;
    }

    /// <summary>Alle Sicherungspfade, auf die noch ein Eintrag zeigt.</summary>
    public IEnumerable<string> ReferencedBackups() =>
        Entries.SelectMany(e => e.Files)
                .Select(f => f.BackupPath)
                .Where(p => p is not null)!;

    /// <summary>
    /// Vergisst die genannten Sicherungen in allen Einträgen. Wird nach dem
    /// Löschen aufgerufen, damit „Rückgängig" nicht mehr angeboten wird.
    /// </summary>
    public void ForgetBackups(IEnumerable<string> paths)
    {
        Load();
        var gone = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        if (gone.Count == 0) return;

        var touched = false;
        foreach (var f in _entries.SelectMany(e => e.Files))
        {
            if (f.BackupPath is not null && gone.Contains(f.BackupPath))
            {
                f.BackupPath = null;
                touched = true;
            }
        }
        if (touched) Save();
    }

    /// <summary>
    /// Holt Sicherungen, auf die der Verlauf zeigt, in den Sicherungsordner.
    ///
    /// Der Verlauf aus der Zeit, als die App noch LocalPrep hieß, zeigte nach
    /// dem Umzug weiter in den alten Ordner. Diese Sicherungen sah die Liste
    /// in den Einstellungen nie, und das Aufräumen nach Ablauf erreichte sie
    /// auch nicht; der Verlauf bot für sie aber weiter „Rückgängig" an.
    /// Liegt eine Sicherung schon im Ordner (die Übernahme hatte kopiert),
    /// wird nur der Verweis umgebogen und die Kopie am alten Ort entfernt.
    /// </summary>
    /// <returns>Wie viele Verweise sich geändert haben.</returns>
    public int AdoptBackups(string folder)
    {
        Load();
        var changed = 0;

        foreach (var f in _entries.SelectMany(e => e.Files))
        {
            if (f.BackupPath is not { } path) continue;
            if (string.Equals(Path.GetDirectoryName(path), Path.TrimEndingDirectorySeparator(folder),
                              StringComparison.OrdinalIgnoreCase)) continue;

            string? target = Path.Combine(folder, Path.GetFileName(path));
            try
            {
                if (File.Exists(target))
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                else if (File.Exists(path))
                {
                    System.IO.Directory.CreateDirectory(folder);
                    File.Move(path, target);
                }
                else target = null;   // nirgends mehr zu finden

                f.BackupPath = target;
                changed++;
            }
            catch { /* bleibt, wo es ist; beim nächsten Start wieder versucht */ }
        }

        if (changed > 0) Save();
        return changed;
    }

    public UndoResult Undo(string id)
    {
        Load();
        var entry = _entries.FirstOrDefault(e => e.Id == id)
            ?? throw new InvalidOperationException(Strings.T("Entry not found."));

        if (!entry.CanUndo)
            throw new InvalidOperationException(
                Strings.T("No backup left, it was deleted or has expired."));

        int restored = 0, failed = 0;
        foreach (var f in entry.Files)
        {
            try
            {
                if (f.BackupPath is null || !File.Exists(f.BackupPath)) { failed++; continue; }

                File.Copy(f.BackupPath, f.Original, overwrite: true);
                File.Delete(f.BackupPath);
                f.BackupPath = null;

                // Eine bei der Konvertierung entstandene Datei muss wieder weg,
                // sonst bleibt neben dem Original ein Duplikat liegen.
                if (f.OutputPath is not null &&
                    !string.Equals(f.OutputPath, f.Original, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(f.OutputPath))
                {
                    File.Delete(f.OutputPath);
                }
                restored++;
            }
            catch { failed++; }
        }

        _entries.Remove(entry);
        Save();
        return new UndoResult(restored, failed);
    }

    /// <summary>Der Eintrag und die Datei darin, zu der eine Sicherung gehört.</summary>
    public (HistoryEntry Entry, HistoryFile File)? Find(string backupPath)
    {
        foreach (var entry in Entries)
            foreach (var file in entry.Files)
                if (string.Equals(file.BackupPath, backupPath, StringComparison.OrdinalIgnoreCase))
                    return (entry, file);
        return null;
    }

    /// <summary>
    /// Schreibt eine einzelne Sicherung zurück.
    ///
    /// Anders als <see cref="Undo"/> für einen ganzen Eintrag: Hier wählt man
    /// Dateien einzeln. Die Fassung, die gerade an der Stelle liegt, wird
    /// vorher selbst gesichert; die zurückgegebenen Dateien gehören in einen
    /// neuen Verlaufseintrag, damit auch das Wiederherstellen rückgängig geht.
    /// </summary>
    /// <param name="target">
    /// Wohin. Ohne Angabe an den Ort, von dem die Sicherung stammt; den kennt
    /// nur der Verlauf. Für eine Sicherung ohne Eintrag muss er genannt werden.
    /// </param>
    public List<HistoryFile> Restore(string backupPath, BackupStore store, string? target = null)
    {
        Load();
        if (!File.Exists(backupPath))
            throw new InvalidOperationException(Strings.T("The backup no longer exists."));

        var owner = Find(backupPath);
        var destination = target ?? owner?.File.Original
            ?? throw new InvalidOperationException(Strings.T("The original location is unknown."));

        var undo = new List<HistoryFile>();

        // Was jetzt dort liegt, bleibt über den neuen Eintrag erreichbar.
        string? current = null;
        if (File.Exists(destination)) current = store.Create(destination);

        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(backupPath, destination, overwrite: true);
        undo.Add(new HistoryFile { Original = destination, BackupPath = current, OutputPath = current is null ? destination : null });

        // Eine bei der Konvertierung entstandene Datei gehört zu dem Stand,
        // der gerade zurückgenommen wird. Sie wird gesichert, dann entfernt.
        if (owner?.File.OutputPath is { } output
            && !string.Equals(output, destination, StringComparison.OrdinalIgnoreCase)
            && File.Exists(output))
        {
            var kept = store.Create(output);
            File.Delete(output);
            undo.Add(new HistoryFile { Original = output, BackupPath = kept });
        }

        File.Delete(backupPath);
        if (owner is { } found)
        {
            found.File.BackupPath = null;
            if (!found.Entry.CanUndo) _entries.Remove(found.Entry);
        }
        Save();
        return undo;
    }

    public void Clear()
    {
        Load();
        _entries.Clear();
        Save();
    }
}
