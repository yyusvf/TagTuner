using Microsoft.UI.Dispatching;
using TagTuner.Core.Shell;

namespace TagTuner.App;

/// <summary>
/// „Metadaten bearbeiten" aus dem Explorer: ein kleines Fenster mit nur der
/// Metadatenspalte, das nach dem Anwenden wieder zugeht.
///
/// Markiert man im Explorer zehn Lieder und wählt den Eintrag, startet der
/// Explorer TagTuner zehnmal, je einmal pro Datei. Gewollt ist aber ein
/// Fenster für alle zehn. Darum sammelt der erste dieser Starts ein paar
/// Sekunden lang ein, was die übrigen ihm über eine eigene Pipe schicken;
/// die übrigen beenden sich dann gleich wieder.
///
/// Das laufende Hauptfenster bleibt davon unberührt: Das kleine Fenster ist
/// ein eigener Prozess und hört nicht auf dessen Pipe.
/// </summary>
internal static class QuickEdit
{
    /// <summary>Wie lange der erste Start weitere Dateien annimmt.</summary>
    private static readonly TimeSpan Collecting = TimeSpan.FromSeconds(4);

    private static string Name => InstanceChannel.Name + ".edit";

    /// <summary>Hält fest, dass gerade ein Fenster einsammelt.</summary>
    private static Mutex? _collector;

    /// <summary>Kommt eine weitere Datei an, meldet sie sich hier.</summary>
    public static event Action<string>? FileAdded;

    /// <summary>
    /// Gibt die Befehlszeile an ein Fenster, das gerade einsammelt. Wahr
    /// heißt: angenommen, dieser Prozess hat nichts mehr zu tun.
    /// </summary>
    public static bool HandOff(IReadOnlyList<string> args)
    {
        _collector = new Mutex(false, Name, out var first);
        if (first) return false;

        _collector.Dispose();
        _collector = null;

        // Der Sammler startet gleichzeitig mit uns und braucht einen Moment,
        // bis er zuhört. Darum hier länger warten als beim Hauptfenster.
        return InstanceChannel.Send(args, timeoutMs: 3000, name: Name);
    }

    /// <summary>
    /// Nimmt eine Weile weitere Dateien an und gibt danach den Platz frei:
    /// Ein späterer Aufruf aus dem Explorer bekommt dann sein eigenes Fenster.
    /// </summary>
    public static void Collect(DispatcherQueue ui)
    {
        if (_collector is null) return;

        var stop = new CancellationTokenSource(Collecting);
        InstanceChannel.Listen(target =>
        {
            if (target.File is { Length: > 0 } file)
                ui.TryEnqueue(() => FileAdded?.Invoke(file));
        }, stop.Token, Name);

        stop.Token.Register(() =>
        {
            _collector?.Dispose();
            _collector = null;
        });
    }
}
