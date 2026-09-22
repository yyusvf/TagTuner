using Microsoft.UI.Xaml;

using TagTuner.App.Settings;
using TagTuner.Core.Safety;
using TagTuner.Core.Settings;

namespace TagTuner.App;

/// <summary>
/// Der Einstieg in die Einstellungen.
///
/// Die Seiten selbst stehen in <see cref="SettingsCatalog"/>, das Gerüst in
/// <see cref="SettingsShell"/>. Diese Klasse bleibt, damit das Hauptfenster
/// nur einen Namen kennen muss.
/// </summary>
public static class SettingsDialog
{
    /// <summary>
    /// Zeigt die Einstellungen. Zurück kommt die Aktualisierung, die der
    /// Nutzer installieren möchte, sonst null. Der Dialog kann das nicht
    /// selbst tun: Die App muss sich dafür beenden, und dazu müsste sie sich
    /// erst schließen.
    /// </summary>
    internal static Task<UpdateCheck?> ShowAsync(
        XamlRoot root, IntPtr window, AppSettings settings, HistoryStore history,
        Action<string> changed) =>
        SettingsShell.ShowAsync(root, window, settings, history, changed);
}
