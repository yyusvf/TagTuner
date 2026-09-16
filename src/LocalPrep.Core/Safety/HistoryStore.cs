using System.Text.Json;
using System.Text.Json.Serialization;
using LocalPrep.Core.Settings;

namespace LocalPrep.Core.Safety;

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

    public IReadOnlyList<HistoryEntry> Entries => _entries;

    public HistoryStore() => Load();

    private void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                _entries = JsonSerializer.Deserialize<List<HistoryEntry>>(
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
        }
        catch { }
    }

    public HistoryEntry Add(string kind, string description, IEnumerable<HistoryFile> files)
    {
        var entry = new HistoryEntry
        {
            Kind = kind,
            Description = description,
            Files = files.ToList(),
        };
        _entries.Insert(0, entry);
        if (_entries.Count > MaxEntries) _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        Save();
        return entry;
    }

    /// <summary>Alle Sicherungspfade, auf die noch ein Eintrag zeigt.</summary>
    public IEnumerable<string> ReferencedBackups() =>
        _entries.SelectMany(e => e.Files)
                .Select(f => f.BackupPath)
                .Where(p => p is not null)!;

    /// <summary>
    /// Vergisst die genannten Sicherungen in allen Einträgen. Wird nach dem
    /// Löschen aufgerufen, damit „Rückgängig" nicht mehr angeboten wird.
    /// </summary>
    public void ForgetBackups(IEnumerable<string> paths)
    {
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

    public UndoResult Undo(string id)
    {
        var entry = _entries.FirstOrDefault(e => e.Id == id)
            ?? throw new InvalidOperationException("Eintrag nicht gefunden.");

        if (!entry.CanUndo)
            throw new InvalidOperationException(
                "Keine Sicherung mehr vorhanden — sie wurde gelöscht oder ist abgelaufen.");

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

    public void Clear()
    {
        _entries.Clear();
        Save();
    }
}
