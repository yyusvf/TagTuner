using Microsoft.Win32;

namespace LocalPrep.Core.Shell;

/// <summary>
/// Der Eintrag „LocalPrep" im Rechtsklick-Menü des Explorers.
///
/// Schreibt ausschließlich nach HKCU — keine Administratorrechte nötig.
/// Ein einzelner, anklickbarer Eintrag ohne Untermenü: Welcher Bereich
/// geöffnet wird, fragt die App selbst, wo sie es in der Sprache des
/// Nutzers und mit der Datei vor Augen tun kann.
/// </summary>
public static class ContextMenuRegistration
{
    private const string Label = "LocalPrep";

    private static readonly string[] Extensions =
        [".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac", ".aiff", ".aif"];

    private static string FileKey(string ext) =>
        $@"Software\Classes\SystemFileAssociations\{ext}\shell\LocalPrep";

    private const string FolderKey = @"Software\Classes\Directory\shell\LocalPrep";

    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(FileKey(".mp3"));
            return key is not null;
        }
        catch { return false; }
    }

    /// <summary>
    /// Trägt den Eintrag für alle Audioformate und für Ordner ein.
    /// Entfernt vorher die alten Schlüssel — ein von einer früheren Fassung
    /// geschriebenes Untermenü bliebe sonst bestehen, egal was wir neu schreiben.
    /// </summary>
    public static void Register(string exePath, string? iconPath = null)
    {
        Unregister();

        var command = $"\"{exePath}\" --file=\"%1\"";
        var icon = iconPath ?? $"{exePath},0";

        foreach (var ext in Extensions)
            WriteEntry(FileKey(ext), command, icon);

        WriteEntry(FolderKey, $"\"{exePath}\" --folder=\"%1\"", icon);
    }

    public static void Unregister()
    {
        foreach (var ext in Extensions) DeleteTree(FileKey(ext));
        DeleteTree(FolderKey);
    }

    private static void WriteEntry(string path, string command, string icon)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path, writable: true)
            ?? throw new InvalidOperationException($"Registry-Schlüssel {path} nicht anlegbar.");

        // Der Standardwert ist die Beschriftung im Menü.
        key.SetValue(null, Label, RegistryValueKind.String);
        key.SetValue("Icon", icon, RegistryValueKind.String);

        using var cmd = key.CreateSubKey("command", writable: true)
            ?? throw new InvalidOperationException($"Registry-Schlüssel {path}\\command nicht anlegbar.");
        cmd.SetValue(null, command, RegistryValueKind.String);
    }

    private static void DeleteTree(string path)
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
        catch { }
    }
}
