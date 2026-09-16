namespace LocalPrep.Core.Shell;

public sealed record LaunchTarget(string? File, string? Folder)
{
    public bool HasAny => File is not null || Folder is not null;

    /// <summary>Der Ordner, der geöffnet werden soll — bei einer Datei deren Verzeichnis.</summary>
    public string? FolderToOpen => Folder ?? (File is not null ? Path.GetDirectoryName(File) : null);
}

/// <summary>
/// Liest --file= und --folder= aus der Kommandozeile.
///
/// Die Werte hängen mit „=" am Schalter, nicht als eigenes Argument. In der
/// Electron-Fassung war „--file &lt;pfad&gt;" die Form, und Chromium sortiert
/// eine Kommandozeile zu „Programm, Schalter…, Argumente…" um: Der Schalter
/// rutschte nach vorn, der Pfad ans Ende, und dazwischen landete ein Schalter,
/// den Chromium selbst hinzufügte. Gelesen wurde dann dieser statt des Pfades.
///
/// Nativ tritt das Umsortieren nicht auf, aber die "="-Form kostet nichts und
/// hält bestehende Registry-Einträge gültig.
/// </summary>
public static class CommandLine
{
    public static LaunchTarget Parse(IReadOnlyList<string> args)
    {
        string? file = null, folder = null;
        var loose = new List<string>();
        var bare = new List<string>();

        // args[0] ist bei .NET bereits ohne Programmpfad
        foreach (var arg in args)
        {
            if (arg.StartsWith("--file=", StringComparison.OrdinalIgnoreCase))
            { file = Clean(arg[7..]); continue; }

            if (arg.StartsWith("--folder=", StringComparison.OrdinalIgnoreCase))
            { folder = Clean(arg[9..]); continue; }

            if (arg.Equals("--file", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--folder", StringComparison.OrdinalIgnoreCase))
            { bare.Add(arg.TrimStart('-').ToLowerInvariant()); continue; }

            if (arg.StartsWith('-')) continue;   // fremder Schalter
            loose.Add(Clean(arg));
        }

        // Altform „--file <pfad>": der Pfad ist das übrig gebliebene Argument.
        foreach (var key in bare)
        {
            if (loose.Count == 0) break;
            var value = loose[0];
            loose.RemoveAt(0);
            if (key == "file") file ??= value; else folder ??= value;
        }

        // Ganz ohne Schalter aufgerufen (z. B. „Öffnen mit"): einfach der Pfad.
        if (file is null && folder is null && loose.Count > 0)
        {
            var p = loose[0];
            if (Directory.Exists(p)) folder = p;
            else if (System.IO.File.Exists(p)) file = p;
        }

        return new LaunchTarget(file, folder);
    }

    private static string Clean(string s) => s.Trim().Trim('"');
}
