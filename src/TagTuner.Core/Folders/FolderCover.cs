using TagTuner.Core.Audio;

namespace TagTuner.Core.Folders;

/// <summary>
/// Das Bild, das einen Ordner in der Bibliothek vertritt.
///
/// Zuerst wird nach einer Bilddatei im Ordner gesucht — das kostet nur einen
/// Verzeichnis-Eintrag. Erst wenn keine da ist, wird in Audiodateien
/// hineingelesen, und auch dann nur in die ersten paar: ein Ordner mit
/// hundert Tracks darf den Baum nicht ausbremsen, und wenn die ersten drei
/// kein Cover tragen, tut es der Rest fast nie.
/// </summary>
public static class FolderCover
{
    private static readonly string[] Names =
        ["cover", "folder", "front", "album", "albumart", "albumartsmall", "artwork"];

    private static readonly string[] Extensions =
        [".jpg", ".jpeg", ".png", ".webp", ".bmp"];

    private const int MaxProbedTracks = 3;

    /// <summary>Die Rohdaten eines Covers, oder null wenn der Ordner keins hat.</summary>
    public static byte[]? Read(string path, CancellationToken ct = default)
    {
        try
        {
            if (FromImageFile(path) is { } image) return image;

            ct.ThrowIfCancellationRequested();
            return FromTracks(path, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
    }

    private static byte[]? FromImageFile(string path)
    {
        List<FileInfo> images;
        try
        {
            images = new DirectoryInfo(path)
                .EnumerateFiles()
                .Where(f => Extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase))
                .Where(f => (f.Attributes & FileAttributes.Hidden) == 0)
                // Riesige Scans gehören nicht als Vorschaubild in eine Baumzeile.
                .Where(f => f.Length is > 0 and < 12 * 1024 * 1024)
                .ToList();
        }
        catch { return null; }

        if (images.Count == 0) return null;

        // „cover.jpg" schlägt „irgendein-foto.jpg"; sonst das erste Bild.
        var pick = images
            .OrderBy(f => Rank(Path.GetFileNameWithoutExtension(f.Name)))
            .ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
            .First();

        try { return File.ReadAllBytes(pick.FullName); }
        catch { return null; }

        static int Rank(string name)
        {
            var i = Array.FindIndex(Names,
                n => name.Equals(n, StringComparison.OrdinalIgnoreCase));
            return i >= 0 ? i : Names.Length;
        }
    }

    private static byte[]? FromTracks(string path, CancellationToken ct)
    {
        List<string> files;
        try
        {
            files = new DirectoryInfo(path)
                .EnumerateFiles()
                .Where(f => AudioFormats.IsAudioFile(f.Name))
                .Where(f => (f.Attributes & FileAttributes.Hidden) == 0)
                .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(MaxProbedTracks)
                .Select(f => f.FullName)
                .ToList();
        }
        catch { return null; }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (AudioProbe.ReadCover(file) is { Data.Length: > 0 } cover) return cover.Data;
            }
            catch { }
        }

        return null;
    }
}
