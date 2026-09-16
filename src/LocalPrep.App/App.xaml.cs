using LocalPrep.Core.Audio;
using LocalPrep.Core.Safety;
using LocalPrep.Core.Settings;
using LocalPrep.Core.Shell;
using Microsoft.UI.Xaml;

namespace LocalPrep.App;

public partial class App : Application
{
    private Window? _window;

    /// <summary>Datei oder Ordner, mit dem die App gestartet wurde.</summary>
    public static LaunchTarget Launch { get; private set; } = new(null, null);

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Muss vor jedem Tag-Zugriff laufen: erzwingt ID3v2.3, ohne das der
        // Windows-Explorer keine Cover-Thumbnails anzeigt.
        AudioProbe.Configure();

        Launch = CommandLine.Parse(Environment.GetCommandLineArgs().Skip(1).ToList());

        CleanUpBackups();

        _window = new MainWindow();
        _window.Activate();
    }

    /// <summary>
    /// Abgelaufene Sicherungen beim Start entfernen — und dem Verlauf sagen,
    /// dass sie weg sind, damit er kein Rückgängig anbietet, das ins Leere läuft.
    /// </summary>
    private static void CleanUpBackups()
    {
        try
        {
            var settings = AppSettings.Load();
            var store = new BackupStore(settings.ResolvedBackupFolder);
            var expired = store.DeleteExpired(settings.BackupRetention);
            if (expired.Count > 0) new HistoryStore().ForgetBackups(expired);
        }
        catch { /* Aufräumen ist Nebensache und darf den Start nicht verhindern */ }
    }
}
