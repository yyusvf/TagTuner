namespace TagTuner.Core.Safety;

/// <summary>Eine Sicherung, wie die Liste in den Einstellungen sie zeigt.</summary>
public sealed record BackupItem(
    string BackupPath,
    string OriginalName,
    string? OriginalPath,
    DateTime Created,
    long Size,
    string? Action);

/// <summary>
/// Die Sicherungen mit dem, was der Verlauf über sie weiß: woher sie stammen
/// und bei welcher Aktion sie entstanden sind. Die jüngste zuerst.
/// </summary>
public static class BackupCatalog
{
    public static List<BackupItem> List(BackupStore store, HistoryStore history)
    {
        var known = new Dictionary<string, (string Original, string Action)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in history.Entries)
            foreach (var file in entry.Files)
                if (file.BackupPath is { } path)
                    known.TryAdd(path, (file.Original, entry.Description));

        return store.All()
            .Select(f =>
            {
                var hit = known.TryGetValue(f.FullName, out var h) ? h : default;
                return new BackupItem(
                    f.FullName,
                    BackupStore.OriginalName(f.FullName),
                    hit.Original,
                    BackupStore.CreatedAt(f),
                    f.Length,
                    hit.Action);
            })
            .OrderByDescending(i => i.Created)
            .ToList();
    }
}
