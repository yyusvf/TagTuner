namespace TagTuner.Core.Audio;

/// <summary>
/// Findet ffmpeg: unter Windows ffmpeg.exe, unter macOS ffmpeg.
///
/// Es gibt kein NuGet-Gegenstück zu ffmpeg-static, das die Exe mitliefert —
/// für den Release holt sie ein Build-Schritt. Solange die liegt noch nicht
/// da, greift der Rückfall auf die Kopie im Electron-Projekt daneben, damit
/// während der Entwicklung nichts blockiert.
/// </summary>
public static class FfmpegLocator
{
    private static string? _cached;

    public static string? Find(string? configured = null)
    {
        if (_cached is not null && File.Exists(_cached)) return _cached;

        foreach (var candidate in Candidates(configured))
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                return _cached = candidate;
        }
        return null;
    }

    private static IEnumerable<string?> Candidates(string? configured)
    {
        // 1. Ausdrücklich in den Einstellungen gesetzt
        yield return configured;

        var appDir = AppContext.BaseDirectory;
        var exe = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

        // 2. Neben der Exe — so wird es ausgeliefert. Im Mac-Paket liegt
        //    es unter Contents/Resources, das Programm unter Contents/MacOS.
        yield return Path.Combine(appDir, exe);
        yield return Path.Combine(appDir, "ffmpeg", exe);
        if (OperatingSystem.IsMacOS())
            yield return Path.GetFullPath(Path.Combine(appDir, "..", "Resources", exe));

        // 3. tools\ffmpeg im Projektbaum, für Läufe aus dem Build-Ordner
        var dir = new DirectoryInfo(appDir);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            yield return Path.Combine(dir.FullName, "tools", "ffmpeg", exe);
        }

        // 4. Entwicklungs-Rückfall: die Kopie der Electron-Fassung nebenan
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        yield return Path.Combine(docs, "TagTuner", "node_modules", "ffmpeg-static", exe);

        // 5. PATH
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var p in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string? candidate = null;
            try { candidate = Path.Combine(p.Trim(), exe); } catch { }
            yield return candidate;
        }

        // 6. Homebrew. Programme aus dem Finder bekommen dessen Ordner nicht
        //    im PATH mit, obwohl ffmpeg dort meist installiert ist.
        if (OperatingSystem.IsMacOS())
        {
            yield return "/opt/homebrew/bin/ffmpeg";
            yield return "/usr/local/bin/ffmpeg";
        }
    }
}
