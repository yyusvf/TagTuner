using System.IO.Pipes;

namespace TagTuner.Core.Shell;

/// <summary>
/// Der Draht zwischen einem neu gestarteten TagTuner und dem, das schon läuft.
///
/// Über eine benannte Pipe statt über AppInstance aus dem App SDK: Bei einer
/// nicht paketierten App kommt die Befehlszeile dort nicht verlässlich beim
/// ersten Prozess an, und genau die wird gebraucht. Ohne den Pfad wäre die
/// Weiterleitung sinnlos.
///
/// Der Name trägt Benutzer und Sitzung. Zwei angemeldete Benutzer bekämen
/// sonst dasselbe Fenster zu sehen, und der zweite dürfte die Pipe des ersten
/// nicht einmal öffnen.
/// </summary>
public static class InstanceChannel
{
    public static string Name { get; set; } =
        $"TagTuner.{Environment.UserName}.{Environment.GetEnvironmentVariable("SESSIONNAME") ?? "0"}";

    /// <summary>
    /// Schickt die Befehlszeile an ein laufendes TagTuner. Wahr heißt: Es
    /// wurde angenommen, dieser Prozess hat nichts mehr zu tun.
    /// </summary>
    public static bool Send(IReadOnlyList<string> args, int timeoutMs = 500)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", Name, PipeDirection.Out);

            // Kurz halten: Läuft nichts, soll der Start nicht darauf warten.
            client.Connect(timeoutMs);

            using var writer = new StreamWriter(client);
            foreach (var arg in args) writer.WriteLine(arg);
            writer.Flush();

            return true;
        }
        catch
        {
            // Keine Gegenstelle, Zeitüberschreitung, Rechte: In all diesen
            // Fällen ist dieser Prozess der erste.
            return false;
        }
    }

    /// <summary>
    /// Nimmt ab jetzt die Befehlszeilen weiterer Starts entgegen. Der
    /// Rückruf kommt auf einem Hintergrundstrang; wer eine Oberfläche
    /// bedient, muss selbst auf deren Strang wechseln.
    /// </summary>
    public static void Listen(Action<LaunchTarget> handle, CancellationToken ct = default)
    {
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        Name, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(ct);

                    using var reader = new StreamReader(server);
                    var text = await reader.ReadToEndAsync(ct);

                    var args = text
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(a => a.TrimEnd('\r'))
                        .Where(a => a.Length > 0)
                        .ToList();

                    if (args.Count == 0) continue;

                    handle(CommandLine.Parse(args));
                }
                catch (OperationCanceledException) { return; }
                catch
                {
                    // Eine abgebrochene Verbindung darf das Zuhören nicht
                    // beenden, sonst öffnet ab da jeder Start wieder ein
                    // eigenes Fenster.
                    try { await Task.Delay(300, ct); } catch { return; }
                }
            }
        }, ct);
    }
}
