using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>Version und Bundle, wie AppInfo unter Windows.</summary>
internal static class AppInfo
{
    public static string Version =>
        NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleShortVersionString")?.ToString() ?? "0.0.0";
}

/// <summary>
/// Aktualisierungen auf dem Mac. Dieselbe Suche wie unter Windows
/// (UpdateService: „Nie" fragt nie an, beim Start höchstens einmal am Tag,
/// übersprungene Versionen bleiben still), nur mit dem DMG statt des Setups.
///
/// Still installieren wie unter Windows geht erst mit Sparkle und einer
/// signierten App. Bis dahin lädt „Herunterladen und installieren" das DMG
/// in „Downloads" und öffnet es; hineinziehen muss man selbst.
/// </summary>
internal static class MacUpdates
{
    /// <summary>Beim Start, nach den Regeln der Einstellung.</summary>
    public static async void CheckOnStart(NSWindow? parent)
    {
        var settings = AppDelegate.Settings;
        UpdateService.ForgetOutdatedSkip(settings, AppInfo.Version);
        var found = await UpdateService.CheckOnStartAsync(settings, AppInfo.Version, UpdateService.IsMacImage);
        if (found is null) return;
        if (settings.UpdateBehavior == "auto") { _ = InstallAsync(found, parent); return; }
        Offer(found, parent);
    }

    /// <summary>Fragt wie unter Windows: installieren, diese überspringen, später.</summary>
    public static void Offer(UpdateCheck found, NSWindow? parent)
    {
        var a = new NSAlert
        {
            MessageText = Strings.T("Update available"),
            InformativeText = Strings.T("Version {0} is available.", found.Version ?? ""),
        };
        a.AddButton(Strings.T("Download and install"));
        a.AddButton(Strings.T("Later"));
        a.AddButton(Strings.T("Skip this version"));
        void Answer(nint r)
        {
            if (r == (nint)(long)NSAlertButtonReturn.First) _ = InstallAsync(found, parent);
            else if (r == (nint)(long)NSAlertButtonReturn.Third)
            {
                AppDelegate.Settings.SkippedVersion = found.Version;
                AppDelegate.Settings.Save();
            }
        }
        if (parent is not null) a.BeginSheetForResponse(parent, Answer); else Answer(a.RunModal());
    }

    /// <summary>Lädt das DMG nach „Downloads" und öffnet es.</summary>
    public static async Task InstallAsync(UpdateCheck found, NSWindow? parent)
    {
        if (found.SetupUrl is not { } url)
        {
            // Release ohne DMG: dann wenigstens die Seite des Releases.
            NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl(
                $"https://github.com/{UpdateService.Owner}/{UpdateService.Repo}/releases/latest"));
            return;
        }
        try
        {
            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var target = Path.Combine(downloads, Path.GetFileName(new Uri(url).LocalPath));
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"TagTuner/{AppInfo.Version}");
            await File.WriteAllBytesAsync(target, await http.GetByteArrayAsync(url));
            NSWorkspace.SharedWorkspace.OpenUrl(NSUrl.FromFilename(target));
        }
        catch (Exception ex)
        {
            var a = new NSAlert { MessageText = Strings.T("Update failed: {0}", ex.Message) };
            if (parent is not null) a.BeginSheet(parent); else a.RunModal();
        }
    }
}
