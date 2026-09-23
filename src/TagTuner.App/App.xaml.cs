using TagTuner.Core.Audio;
using TagTuner.Core.Safety;
using TagTuner.Core.Settings;
using TagTuner.Core.Shell;
using Microsoft.UI.Xaml;

namespace TagTuner.App;

public partial class App : Application
{
    private Window? _window;

    /// <summary>Datei oder Ordner, mit dem die App gestartet wurde.</summary>
    public static LaunchTarget Launch { get; private set; } = new(null, null);

    public App()
    {
        // TagTuner ist dunkel, unabhängig davon, was Windows eingestellt hat.
        // Für die ganze App gesetzt, nicht nur fürs Fenster, damit Dialoge
        // und die Farben, die der Code selbst holt, dazu passen.
        RequestedTheme = ApplicationTheme.Dark;

        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Muss vor jedem Tag-Zugriff laufen: erzwingt ID3v2.3, ohne das der
        // Windows-Explorer keine Cover-Thumbnails anzeigt.
        AudioProbe.Configure();

        var line = Environment.GetCommandLineArgs().Skip(1).ToList();

        // Laeuft schon ein TagTuner, bekommt es den Pfad und macht einen Tab
        // daraus. Dieser Prozess endet dann sofort: Zwei Fenster auf denselben
        // Ordner wuerden einander die Dateien unter den Haenden wegschreiben.
        if (SingleInstance.HandOff(line))
        {
            // Nicht Exit(): Die Anwendung ist noch nicht so weit aufgebaut,
            // dass sie sich geordnet beenden koennte.
            Environment.Exit(0);
            return;
        }

        Launch = CommandLine.Parse(line);

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
