using TagTuner.Core.Settings;

namespace TagTuner.Core.Safety;

/// <summary>
/// Sicherungskopien vor jedem schreibenden Zugriff.
///
/// Sie liegen unter %APPDATA%\TagTuner\Backups — nie neben der Originaldatei.
/// Die Electron-Fassung hat sie anfangs als „.undo_backup" in den Musikordner
/// gelegt; das verstreut Fremddateien in der Bibliothek des Nutzers.
/// </summary>
public sealed class BackupStore(string folder)
{
    public string Folder { get; } = folder;

    /// <summary>
    /// Legt eine Kopie an und gibt deren Pfad zurück.
    /// Wirft mit klarer Meldung, wenn der Ordner nicht beschreibbar ist —
    /// ein still fehlgeschlagenes Backup darf nie zu einem Schreibvorgang führen.
    /// </summary>
    public string Create(string sourcePath)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Folder);
        }
        catch (Exception ex)
        {
            throw new IOException(
                Strings.T("The backup folder \"{0}\" cannot be created: {1}", Folder, ex.Message), ex);
        }

        var target = Path.Combine(Folder,
            $"{Path.GetFileName(sourcePath)}_{DateTime.Now:yyyyMMdd-HHmmss-fff}.bak");

        try
        {
            File.Copy(sourcePath, target, overwrite: false);

            // File.Copy übernimmt den Zeitstempel der Quelle. Eine Sicherung
            // einer zwei Jahre alten Datei wäre damit sofort „abgelaufen" und
            // beim nächsten Start weg gewesen.
            try { File.SetLastWriteTimeUtc(target, DateTime.UtcNow); } catch { }

            return target;
        }
        catch (Exception ex)
        {
            throw new IOException(
                Strings.T("Backing up \"{0}\" failed: {1}",
                          Path.GetFileName(sourcePath), ex.Message), ex);
        }
    }

    public IReadOnlyList<FileInfo> All()
    {
        try
        {
            return new DirectoryInfo(Folder).EnumerateFiles("*.bak").ToList();
        }
        catch { return []; }
    }

    public (int Count, long Bytes) Info()
    {
        var files = All();
        return (files.Count, files.Sum(f => f.Length));
    }

    /// <summary>Löscht alle Sicherungen und meldet, welche Pfade weg sind.</summary>
    public IReadOnlyList<string> DeleteAll()
    {
        var gone = new List<string>();
        foreach (var f in All())
        {
            try { f.Delete(); gone.Add(f.FullName); } catch { }
        }
        return gone;
    }

    /// <summary>
    /// Entfernt Sicherungen, die älter als die eingestellte Frist sind.
    /// „never" behält alles.
    /// </summary>
    public IReadOnlyList<string> DeleteExpired(string retention)
    {
        if (!int.TryParse(retention, out var days) || days <= 0) return [];

        var cutoff = DateTime.Now.AddDays(-days);
        var gone = new List<string>();
        foreach (var f in All().Where(f => AgeOf(f) < cutoff))
        {
            try { f.Delete(); gone.Add(f.FullName); } catch { }
        }
        return gone;
    }

    /// <summary>
    /// Wann diese Sicherung angelegt wurde.
    ///
    /// Zuerst aus dem Namen, der den Zeitpunkt trägt: Das ist die einzige
    /// Angabe, die weder vom Kopiervorgang noch von der Quelldatei stammt.
    /// Erst danach die Dateizeiten, und davon die jüngere.
    /// </summary>
    private static DateTime AgeOf(FileInfo file)
    {
        var name = Path.GetFileNameWithoutExtension(file.Name);
        var mark = name.LastIndexOf('_');

        if (mark >= 0 && DateTime.TryParseExact(
                name[(mark + 1)..], "yyyyMMdd-HHmmss-fff",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var stamped))
            return stamped;

        return file.CreationTime > file.LastWriteTime ? file.CreationTime : file.LastWriteTime;
    }

    /// <summary>
    /// Wirft Sicherungen weg, auf die kein Verlaufseintrag mehr zeigt.
    /// Was noch referenziert wird, bleibt — darum nach Referenz und nicht
    /// nach Alter.
    /// </summary>
    public int DeleteOrphans(IEnumerable<string> referenced)
    {
        var keep = new HashSet<string>(referenced, StringComparer.OrdinalIgnoreCase);
        var n = 0;
        foreach (var f in All().Where(f => !keep.Contains(f.FullName)))
        {
            try { f.Delete(); n++; } catch { }
        }
        return n;
    }
}
