using TagTuner.Core.Audio;
using TagTuner.Core.Safety;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

[Register("AppDelegate")]
public sealed class AppDelegate : NSApplicationDelegate
{
    public static AppSettings Settings { get; private set; } = null!;
    public static HistoryStore History { get; private set; } = null!;

    /// <summary>Ein Player für alle Fenster und Tabs.</summary>
    internal static Player Player { get; private set; } = null!;

    /// <summary>Die offenen Fenster. Hier gehalten, sonst räumt sie der GC weg.</summary>
    private readonly List<MainWindowController> _windows = [];
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
        Player = new Player(Settings.Volume);

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
            _main = Track(new MainWindowController());
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
        NSApplication.SharedApplication.ServicesProvider = this;
        if (_pendingOpen is { } pending) { _pendingOpen = null; Open(pending); }
        else if (args is not null) _main.OpenFolder(args);
        else _main.OpenLastFolder();
#if DEBUG
        var all = Environment.GetCommandLineArgs();
        var steps = all.Select((a, i) => a == "--do" && i + 1 < all.Length ? all[i + 1] : null).OfType<string>().ToList();
        if (steps.Count > 0) _main.RunScript(steps);
#endif
    }

    private MainWindowController Track(MainWindowController w)
    {
        _windows.Add(w);
        w.Closed += c => _windows.Remove(c);
        return w;
    }

    /// <summary>Das Fenster vorn, oder das erste.</summary>
    private MainWindowController? Front =>
        NSApplication.SharedApplication.KeyWindow?.WindowController as MainWindowController ?? _windows.FirstOrDefault();

    /// <summary>
    /// Neuer Tab (⌘T oder das Plus in der Tab-Leiste), wie im Finder mit dem
    /// Ordner, der gerade offen ist.
    /// </summary>
    [Export("newWindowForTab:")]
    public void NewWindowForTab(NSObject? sender) => NewWindowForTab(sender, null);

    /// <summary>Neuer Tab mit einem bestimmten Ordner, etwa aus dem Kontextmenü der Bibliothek.</summary>
    public void NewWindowForTab(NSObject? sender, string? folder)
    {
        var from = Front;
        var tab = Track(new MainWindowController(first: false));
        if (from is not null) from.Window.AddTabbedWindow(tab.Window, NSWindowOrderingMode.Above);
        tab.Window.MakeKeyAndOrderFront(this);
        if ((folder ?? from?.Folder) is { } f) tab.OpenFolder(f);
    }

    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => true;

    public override void WillTerminate(NSNotification notification)
    {
        Settings.Volume = Player.Volume;
        Settings.Save();
    }

    /// <summary>Ordner aus dem Finder aufs Dock-Symbol gezogen.</summary>
    /// <summary>
    /// Ordner oder Audiodateien aus dem Finder: aufs Dock-Symbol gezogen,
    /// über „Öffnen mit" oder den Dienst „In TagTuner öffnen". Ein Ordner
    /// öffnet sich, Dateien öffnen ihren Ordner und sind darin ausgewählt.
    /// Kommt das vor dem ersten Fenster, wird es danach nachgeholt.
    /// </summary>
    public override void OpenUrls(NSApplication application, NSUrl[] urls) =>
        Open([.. urls.Select(u => u.Path).OfType<string>()]);

    private List<string>? _pendingOpen;

    public void Open(IReadOnlyList<string> paths)
    {
        if (Front is not { } w) { _pendingOpen = [.. paths]; return; }
        if (paths.FirstOrDefault(Directory.Exists) is { } folder) { w.OpenFolder(folder); return; }
        var files = paths.Where(File.Exists).ToList();
        if (files.Count > 0 && Path.GetDirectoryName(files[0]) is { } dir) w.OpenFolder(dir, files);
        NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
    }

    /// <summary>Der Dienst im Finder-Kontextmenü, unter Dienste.</summary>
    [Export("openInTagTuner:userData:error:")]
    public void OpenInTagTuner(NSPasteboard pasteboard, string userData, out NSString? error)
    {
        error = null;
        var urls = pasteboard.ReadObjectsForClasses([new ObjCRuntime.Class(typeof(NSUrl))], null);
        Open([.. (urls?.OfType<NSUrl>() ?? []).Select(u => u.Path).OfType<string>()]);
    }

    public MainWindowController? Main => _main;

    // ── Menübefehle, die kein Fenster brauchen ───────────────────

    [Export("showSettings:")]
    public void ShowSettings(NSObject sender) => SettingsWindow.Show();

    [Export("showBackups:")]
    public void ShowBackups(NSObject sender) => BackupsWindow.Show();

    [Export("openProjectPage:")]
    public void OpenProjectPage(NSObject sender) =>
        NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl("https://tagtuner.app"));

    [Export("reportProblem:")]
    public void ReportProblem(NSObject sender) =>
        NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl("https://github.com/yyusvf/TagTuner/issues"));
}
