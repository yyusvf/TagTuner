using System.Diagnostics;
using System.Text.RegularExpressions;

namespace LocalPrep.Core.Audio;

public sealed record FfmpegResult(bool Success, string CommandLine, string Output, int ExitCode);

/// <summary>Startet ffmpeg und liest den Fortschritt aus dessen Ausgabe.</summary>
public sealed partial class FfmpegRunner(string ffmpegPath)
{
    [GeneratedRegex(@"Duration:\s*(\d+):(\d+):(\d+\.?\d*)")]
    private static partial Regex DurationPattern();

    [GeneratedRegex(@"time=\s*(\d+):(\d+):(\d+\.?\d*)")]
    private static partial Regex TimePattern();

    /// <summary>
    /// Führt einen Lauf aus. <paramref name="onProgress"/> bekommt 0–100,
    /// sofern sich aus der Ausgabe eine Dauer ablesen ließ.
    /// </summary>
    public async Task<FfmpegResult> RunAsync(
        IReadOnlyList<string> args,
        Action<int>? onProgress = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        var commandLine = $"ffmpeg {FfmpegArgs.Quote(args)}";
        var log = new System.Text.StringBuilder();
        double totalSeconds = 0;

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            log.AppendLine(e.Data);

            // ffmpeg meldet erst die Gesamtdauer, danach laufend die Position.
            if (totalSeconds <= 0 && DurationPattern().Match(e.Data) is { Success: true } d)
                totalSeconds = Seconds(d);

            if (onProgress is not null && totalSeconds > 0 &&
                TimePattern().Match(e.Data) is { Success: true } t)
            {
                var pct = (int)Math.Clamp(Seconds(t) / totalSeconds * 100, 0, 99);
                onProgress(pct);
            }
        };

        proc.Start();
        proc.BeginErrorReadLine();
        proc.BeginOutputReadLine();

        try
        {
            await proc.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        onProgress?.Invoke(100);
        return new FfmpegResult(proc.ExitCode == 0, commandLine, log.ToString(), proc.ExitCode);

        static double Seconds(Match m) =>
            int.Parse(m.Groups[1].Value) * 3600 +
            int.Parse(m.Groups[2].Value) * 60 +
            double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Die letzten Zeilen der ffmpeg-Ausgabe — für eine brauchbare Fehlermeldung.</summary>
    public static string Tail(string output, int lines = 4)
    {
        var all = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" · ", all.TakeLast(lines).Select(l => l.Trim()));
    }
}
