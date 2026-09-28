using TagTuner.Core.Audio;
using TagTuner.Core.Safety;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

[Register("AppDelegate")]
public sealed class AppDelegate : NSApplicationDelegate
{
    public static AppSettings Settings { get; private set; } = null!;
    public static HistoryStore History { get; private set; } = null!;

    private MainWindowController? _main;

    public override void WillFinishLaunching(NSNotification notification)
    {
        Settings = AppSettings.Load();
        if (string.IsNullOrEmpty(Settings.Language))
        {
            // .NET liest auf dem Mac die Sprache nicht aus den Systemeinstellungen,
            // CurrentUICulture bleibt dort Englisch. NSLocale weiß es.
            Settings.Language = NSLocale.PreferredLanguages.FirstOrDefault() ?? Strings.Initial();
            Settings.Save();
        }
        Strings.Use(Settings.Language);
        AudioProbe.Configure();
        History = new HistoryStore();

        // Abgelaufene Sicherungen wie unter Windows beim Start wegräumen,
        // und den Verlauf von Verweisen auf verschwundene Dateien befreien.
        Task.Run(() =>
        {
            var gone = new BackupStore(Settings.ResolvedBackupFolder).DeleteExpired(Settings.BackupRetention);
            if (gone.Count > 0) History.ForgetBackups(gone);
        });

        NSApplication.SharedApplication.MainMenu = MainMenu.Build(this);
    }

    public override void DidFinishLaunching(NSNotification notification)
    {
        try
        {
            _main = new MainWindowController();
        }
        catch (Exception ex)
        {
            // Ohne das endet ein Fehler beim Aufbau still in einer App ohne Fenster.
            Console.Error.WriteLine(ex);
            new NSAlert { MessageText = "TagTuner", InformativeText = ex.ToString() }.RunModal();
            NSApplication.SharedApplication.Terminate(this);
            return;
        }
        _main.ShowWindow(this);
        _main.Window.MakeKeyAndOrderFront(this);

        // Zum Ausprobieren: ein Ordner als Argument öffnet ihn gleich.
        var args = Environment.GetCommandLineArgs().Skip(1)
            .FirstOrDefault(a => !a.StartsWith('-') && Directory.Exists(a));
        if (args is not null) _main.OpenFolder(args);
        else _main.OpenLastFolder();
#if DEBUG
        var all = Environment.GetCommandLineArgs();
        var steps = all.Select((a, i) => a == "--do" && i + 1 < all.Length ? all[i + 1] : null).OfType<string>().ToList();
        if (steps.Count > 0) _main.RunScript(steps);
#endif
    }

    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => true;

    public override void WillTerminate(NSNotification notification) => _main?.SaveState();

    /// <summary>Ordner aus dem Finder aufs Dock-Symbol gezogen.</summary>
    public override void OpenUrls(NSApplication application, NSUrl[] urls)
    {
        var folder = urls.Select(u => u.Path).FirstOrDefault(p => p is not null && Directory.Exists(p));
        if (folder is not null) _main?.OpenFolder(folder);
    }

    public MainWindowController? Main => _main;

    // ── Menübefehle, die kein Fenster brauchen ───────────────────

    [Export("showSettings:")]
    public void ShowSettings(NSObject sender) => SettingsWindow.Show();

    [Export("openProjectPage:")]
    public void OpenProjectPage(NSObject sender) =>
        NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl("https://tagtuner.app"));

    [Export("reportProblem:")]
    public void ReportProblem(NSObject sender) =>
        NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl("https://github.com/yyusvf/TagTuner/issues"));
}
