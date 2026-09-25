using Microsoft.Win32;

using TagTuner.Core.Settings;

namespace TagTuner.Core.Shell;

/// <summary>
/// Der Eintrag „TagTuner" im Rechtsklick-Menü des Explorers.
///
/// Schreibt ausschließlich nach HKCU — keine Administratorrechte nötig.
/// Ein Untermenü mit zwei Einträgen: „In TagTuner öffnen" zeigt den Ordner
/// in der App, mit der Datei ausgewählt. „Metadaten bearbeiten" öffnet nur
/// die Metadatenspalte in einem kleinen Fenster, das nach dem Anwenden
/// wieder zugeht.
/// </summary>
public static class ContextMenuRegistration
{
    private const string Label = "TagTuner";

    private static readonly string[] Extensions =
        [".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac", ".aiff", ".aif"];

    private static string FileKey(string ext) =>
        $@"Software\Classes\SystemFileAssociations\{ext}\shell\TagTuner";

    private const string FolderKey = @"Software\Classes\Directory\shell\TagTuner";

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
    /// Trägt die Einträge für alle Audioformate und für Ordner ein.
    /// Entfernt vorher die alten Schlüssel — ein von einer früheren Fassung
    /// geschriebener Eintrag bliebe sonst bestehen, egal was wir neu schreiben.
    /// </summary>
    public static void Register(string exePath, string? iconPath = null)
    {
        Unregister();

        var icon = iconPath ?? $"{exePath},0";

        foreach (var ext in Extensions)
            WriteMenu(FileKey(ext), icon, "--file", exePath);

        WriteMenu(FolderKey, icon, "--folder", exePath);
    }

    /// <summary>
    /// Schreibt die Einträge neu, falls sie eingeschaltet sind: nach einem
    /// Update mit anderem Aufbau, nach einem Umzug der App oder einem
    /// Sprachwechsel. Wer sie ausgeschaltet hat, bekommt sie nicht zurück.
    /// </summary>
    public static void Refresh(string exePath)
    {
        try { if (IsRegistered()) Register(exePath); }
        catch { }
    }

    public static void Unregister()
    {
        foreach (var ext in Extensions) DeleteTree(FileKey(ext));
        DeleteTree(FolderKey);
    }

    /// <summary>
    /// Das Untermenü. Die Unterschlüssel tragen eine Nummer im Namen, weil
    /// der Explorer sie alphabetisch nach Schlüssel sortiert, nicht nach
    /// Beschriftung.
    /// </summary>
    private static void WriteMenu(string path, string icon, string target, string exePath)
    {
        using var key = Create(Registry.CurrentUser, path);
        key.SetValue("MUIVerb", Label, RegistryValueKind.String);
        key.SetValue("Icon", icon, RegistryValueKind.String);
        // Leer heißt: Die Einträge stehen im Unterschlüssel „shell".
        key.SetValue("SubCommands", "", RegistryValueKind.String);

        using var shell = Create(key, "shell");
        WriteEntry(shell, "1open", Strings.T("Open in TagTuner"), icon,
                   $"\"{exePath}\" {target}=\"%1\"");
        WriteEntry(shell, "2edit", Strings.T("Edit metadata"), icon,
                   $"\"{exePath}\" --edit {target}=\"%1\"");
    }

    private static void WriteEntry(RegistryKey parent, string name, string label, string icon, string command)
    {
        using var key = Create(parent, name);
        key.SetValue("MUIVerb", label, RegistryValueKind.String);
        key.SetValue("Icon", icon, RegistryValueKind.String);

        using var cmd = Create(key, "command");
        cmd.SetValue(null, command, RegistryValueKind.String);
    }

    private static RegistryKey Create(RegistryKey parent, string path) =>
        parent.CreateSubKey(path, writable: true)
            ?? throw new InvalidOperationException(
                Strings.T("The registry key {0} cannot be created.", parent.Name + "\\" + path));

    private static void DeleteTree(string path)
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
        catch { }
    }
}
