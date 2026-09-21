using System.Runtime.InteropServices;

using Microsoft.UI.Dispatching;
using TagTuner.Core.Shell;

namespace TagTuner.App;

/// <summary>
/// Sorgt dafür, dass ein Doppelklick im Explorer kein zweites Fenster öffnet,
/// sondern im bestehenden einen Tab. Der Draht selbst liegt in
/// <see cref="InstanceChannel"/>; hier kommt nur dazu, was mit Fenstern zu
/// tun hat.
/// </summary>
internal static class SingleInstance
{
    private const int AllowAnyProcess = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>
    /// Versucht, die Befehlszeile an ein laufendes TagTuner zu übergeben.
    /// Gelingt das, hat dieser Prozess nichts mehr zu tun.
    /// </summary>
    public static bool HandOff(IReadOnlyList<string> args)
    {
        // Das andere Fenster darf sich in den Vordergrund holen. Ohne diese
        // Erlaubnis lässt Windows das nicht zu, und der Nutzer klickt ins
        // Leere, während sein Tab unsichtbar aufgeht. Vor dem Senden, denn
        // danach ist dieser Prozess womöglich schon weg.
        try { AllowSetForegroundWindow(AllowAnyProcess); } catch { }

        return InstanceChannel.Send(args);
    }

    /// <summary>Nimmt weitere Starts entgegen und meldet sie auf dem Oberflächenstrang.</summary>
    public static void Listen(DispatcherQueue ui, Action<LaunchTarget> handle) =>
        InstanceChannel.Listen(target => ui.TryEnqueue(() => handle(target)));
}
