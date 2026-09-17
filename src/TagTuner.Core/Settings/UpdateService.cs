using System.Text.Json;

namespace TagTuner.Core.Settings;

/// <summary>Was eine Update-Suche ergeben hat.</summary>
public sealed record UpdateCheck(
    bool HasUpdate,
    string? Version = null,
    string? SetupUrl = null,
    string? Notes = null,
    string? Error = null)
{
    public bool Failed => Error is not null;

    public static UpdateCheck None => new(false);
    public static UpdateCheck Fail(string reason) => new(false, Error: reason);
}

/// <summary>
/// Sucht Aktualisierungen bei GitHub.
///
/// Dieselben Regeln wie in der Electron-Fassung:
///
/// „Nie" heißt nie. Es wird nicht einmal angefragt, auch nicht bei einer
/// Suche von Hand, denn die Einstellung verspricht Ruhe.
///
/// Die Suche beim Start läuft höchstens einmal am Tag. Eine Suche von Hand
/// ignoriert diese Frist, verbraucht sie aber auch nicht.
///
/// Eine abgelehnte Version bleibt im Hintergrund still, bis eine neuere
/// erscheint. Von Hand gesucht wird sie trotzdem gemeldet.
/// </summary>
public static class UpdateService
{
    public const string Owner = "yyusvf";
    public const string Repo = "TagTuner";

    private static readonly TimeSpan CheckInterval = TimeSpan.FromDays(1);

    /// <summary>Fragt GitHub nach der neuesten Veröffentlichung.</summary>
    public static async Task<UpdateCheck> CheckAsync(
        string currentVersion, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"TagTuner/{currentVersion}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            var json = await http.GetStringAsync(
                $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest", ct);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag)) return UpdateCheck.None;

            var latest = tag.TrimStart('v', 'V');
            if (!IsNewer(latest, currentVersion)) return UpdateCheck.None;

            // Das Setup, nicht das ZIP: Nur der Installer kann sich selbst
            // über die laufende Fassung legen.
            string? setup = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!name.Contains("setup", StringComparison.OrdinalIgnoreCase)) continue;

                    setup = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    break;
                }
            }

            var notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;
            return new UpdateCheck(true, latest, setup, notes);
        }
        catch (OperationCanceledException) { return UpdateCheck.None; }
        catch (Exception ex) { return UpdateCheck.Fail(ex.Message); }
    }

    /// <summary>
    /// Die Suche beim Start, mit allen Regeln der Einstellung.
    /// Liefert null, wenn nichts zu melden ist.
    /// </summary>
    public static async Task<UpdateCheck?> CheckOnStartAsync(
        AppSettings settings, string currentVersion, CancellationToken ct = default)
    {
        if (settings.UpdateBehavior == "never") return null;

        if (DateTime.TryParse(settings.LastUpdateCheck, out var last)
            && DateTime.Now - last < CheckInterval)
            return null;

        settings.LastUpdateCheck = DateTime.Now.ToString("o");
        settings.Save();

        var found = await CheckAsync(currentVersion, ct);
        if (!found.HasUpdate || found.Failed) return null;

        // Im Hintergrund nicht mit einer Version nerven, die schon abgelehnt
        // wurde.
        if (settings.SkippedVersion == found.Version) return null;

        return found;
    }

    /// <summary>
    /// Lädt das Setup herunter und startet es.
    ///
    /// Der Installer kann die laufende Datei nicht ersetzen, solange sie
    /// läuft. Darum beendet der Aufrufer die App unmittelbar danach; das
    /// Setup wartet von sich aus, bis der Platz frei ist.
    /// </summary>
    public static async Task<string> DownloadAsync(
        string url, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var target = Path.Combine(Path.GetTempPath(), $"TagTuner-Setup-{Guid.NewGuid():n}.exe");

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TagTuner");

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? 0;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var file = File.Create(target);

        var buffer = new byte[81920];
        long done = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            if (total > 0) progress?.Report((int)(done * 100 / total));
        }

        return target;
    }

    /// <summary>„1.2.10" ist neuer als „1.2.9"; verglichen wird Zahl für Zahl.</summary>
    public static bool IsNewer(string candidate, string current)
    {
        var a = Parts(candidate);
        var b = Parts(current);

        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var left = i < a.Length ? a[i] : 0;
            var right = i < b.Length ? b[i] : 0;
            if (left != right) return left > right;
        }
        return false;

        static int[] Parts(string version) =>
            [.. version.Split('.', '-', '+')
                .Select(p => int.TryParse(p, out var n) ? n : 0)];
    }
}
