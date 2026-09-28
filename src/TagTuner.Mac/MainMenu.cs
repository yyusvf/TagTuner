using ObjCRuntime;
using TagTuner.Core.Settings;

namespace TagTuner.Mac;

/// <summary>
/// Die Menüleiste. Die Einträge schicken ihre Selektoren an die
/// Responder-Kette, nicht an ein festes Ziel: So landet ⌘C im Textfeld beim
/// Textfeld und sonst beim Fenster, und ein Eintrag ist von selbst
/// ausgegraut, wenn gerade niemand ihn bedienen kann.
/// </summary>
internal static class MainMenu
{
    public static NSMenu Build(AppDelegate app)
    {
        var bar = new NSMenu();

        // ── TagTuner ─────────────────────────────────────────────
        var appMenu = Sub(bar, "TagTuner");
        Item(appMenu, Strings.T("About TagTuner"), "orderFrontStandardAboutPanel:");
        appMenu.AddItem(NSMenuItem.SeparatorItem);
        Item(appMenu, Strings.T("Settings…"), "showSettings:", ",");
        appMenu.AddItem(NSMenuItem.SeparatorItem);
        var services = new NSMenu();
        var servicesItem = new NSMenuItem(Strings.T("Services")) { Submenu = services };
        appMenu.AddItem(servicesItem);
        NSApplication.SharedApplication.ServicesMenu = services;
        appMenu.AddItem(NSMenuItem.SeparatorItem);
        Item(appMenu, Strings.T("Hide TagTuner"), "hide:", "h");
        Item(appMenu, Strings.T("Hide Others"), "hideOtherApplications:", "h",
             NSEventModifierMask.CommandKeyMask | NSEventModifierMask.AlternateKeyMask);
        Item(appMenu, Strings.T("Show All"), "unhideAllApplications:");
        appMenu.AddItem(NSMenuItem.SeparatorItem);
        Item(appMenu, Strings.T("Quit TagTuner"), "terminate:", "q");

        // ── Ablage ───────────────────────────────────────────────
        var file = Sub(bar, Strings.T("File"));
        Item(file, Strings.T("Open Folder…"), "openFolder:", "o");
        Item(file, Strings.T("Add folder…"), "addLibraryFolder:", "o",
             NSEventModifierMask.CommandKeyMask | NSEventModifierMask.ShiftKeyMask);
        file.AddItem(NSMenuItem.SeparatorItem);
        Item(file, Strings.T("Show in Finder"), "revealInFinder:", "r",
             NSEventModifierMask.CommandKeyMask | NSEventModifierMask.ShiftKeyMask);
        Item(file, Strings.T("Reload"), "reloadFolder:", "r");
        Item(file, Strings.T("Rename…"), "renameFiles:", "r",
             NSEventModifierMask.CommandKeyMask | NSEventModifierMask.AlternateKeyMask);
        file.AddItem(NSMenuItem.SeparatorItem);
        Item(file, Strings.T("Close"), "performClose:", "w");

        // ── Bearbeiten ───────────────────────────────────────────
        var edit = Sub(bar, Strings.T("Edit"));
        Item(edit, Strings.T("Undo"), "undo:", "z");
        Item(edit, Strings.T("Redo"), "redo:", "z",
             NSEventModifierMask.CommandKeyMask | NSEventModifierMask.ShiftKeyMask);
        edit.AddItem(NSMenuItem.SeparatorItem);
        Item(edit, Strings.T("Cut"), "cut:", "x");
        Item(edit, Strings.T("Copy"), "copy:", "c");
        Item(edit, Strings.T("Paste"), "paste:", "v");
        Item(edit, Strings.T("Select all"), "selectAll:", "a");
        edit.AddItem(NSMenuItem.SeparatorItem);
        Item(edit, Strings.T("Apply"), "applyChanges:", "s");
        Item(edit, Strings.T("Reset"), "revertChanges:");
        edit.AddItem(NSMenuItem.SeparatorItem);
        Item(edit, Strings.T("Copy tags"), "copyTags:", "c",
             NSEventModifierMask.CommandKeyMask | NSEventModifierMask.AlternateKeyMask);
        Item(edit, Strings.T("Paste tags"), "pasteTags:", "v",
             NSEventModifierMask.CommandKeyMask | NSEventModifierMask.AlternateKeyMask);
        edit.AddItem(NSMenuItem.SeparatorItem);
        Item(edit, Strings.T("Set cover…"), "chooseCover:");
        Item(edit, Strings.T("Paste cover"), "pasteCover:");
        Item(edit, Strings.T("Remove cover"), "removeCover:");
        edit.AddItem(NSMenuItem.SeparatorItem);
        Item(edit, Strings.T("Find in this folder…"), "focusSearch:", "f");

        // ── Ordner ───────────────────────────────────────────────
        var folder = Sub(bar, Strings.T("Folder"));
        Item(folder, Strings.T("Apply to existing files…"), "applyAlbumMode:", "l");
        folder.AddItem(NSMenuItem.SeparatorItem);
        Rule(folder, Strings.T("Album mode"), MainWindowController.RuleSwitch.AlbumMode);
        Rule(folder, Strings.T("Base metadata"), MainWindowController.RuleSwitch.BaseTags, indent: 1);
        Rule(folder, Strings.T("Cover"), MainWindowController.RuleSwitch.Cover, indent: 1);
        Rule(folder, Strings.T("Track numbering"), MainWindowController.RuleSwitch.Numbering, indent: 1);
        Rule(folder, Strings.T("File names follow"), MainWindowController.RuleSwitch.RenameFiles, indent: 2);
        folder.AddItem(NSMenuItem.SeparatorItem);
        Rule(folder, Strings.T("Reset to the global setting"), MainWindowController.RuleSwitch.Reset);

        // ── Darstellung ──────────────────────────────────────────
        var view = Sub(bar, Strings.T("View"));
        Item(view, Strings.T("Show Sidebar"), "toggleSidebar:", "s",
             NSEventModifierMask.CommandKeyMask | NSEventModifierMask.ControlKeyMask);
        Item(view, Strings.T("Show Inspector"), "toggleInspector:", "i",
             NSEventModifierMask.CommandKeyMask | NSEventModifierMask.AlternateKeyMask);
        view.AddItem(NSMenuItem.SeparatorItem);
        Item(view, Strings.T("Back in playlist order"), "playlistOrder:", "0");
        view.AddItem(NSMenuItem.SeparatorItem);
        Item(view, Strings.T("Enter Full Screen"), "toggleFullScreen:", "f",
             NSEventModifierMask.CommandKeyMask | NSEventModifierMask.ControlKeyMask);

        // ── Wiedergabe ───────────────────────────────────────────
        var play = Sub(bar, Strings.T("Playback"));
        Item(play, Strings.T("Play/Pause"), "playPause:", "p");
        Item(play, Strings.T("Next track"), "nextTrack:", NSRightArrow(),
             NSEventModifierMask.CommandKeyMask);
        Item(play, Strings.T("Previous track"), "previousTrack:", NSLeftArrow(),
             NSEventModifierMask.CommandKeyMask);

        // ── Fenster ──────────────────────────────────────────────
        var window = Sub(bar, Strings.T("Window"));
        Item(window, Strings.T("Minimize"), "performMiniaturize:", "m");
        Item(window, Strings.T("Zoom"), "performZoom:");
        window.AddItem(NSMenuItem.SeparatorItem);
        Item(window, Strings.T("History"), "showHistory:", "y");
        window.AddItem(NSMenuItem.SeparatorItem);
        Item(window, Strings.T("Bring All to Front"), "arrangeInFront:");
        NSApplication.SharedApplication.WindowsMenu = window;

        // ── Hilfe ────────────────────────────────────────────────
        var help = Sub(bar, Strings.T("Help"));
        Item(help, Strings.T("TagTuner website"), "openProjectPage:");
        Item(help, Strings.T("Report a problem"), "reportProblem:");
        NSApplication.SharedApplication.HelpMenu = help;

        return bar;
    }

    private static void Rule(NSMenu menu, string title, MainWindowController.RuleSwitch sw, int indent = 0)
    {
        var item = Item(menu, title, "toggleRule:");
        item.Tag = (int)sw;
        item.IndentationLevel = indent;
    }

    private static NSMenu Sub(NSMenu bar, string title)
    {
        var menu = new NSMenu(title);
        bar.AddItem(new NSMenuItem(title) { Submenu = menu });
        return menu;
    }

    private static NSMenuItem Item(NSMenu menu, string title, string selector, string key = "",
                                   NSEventModifierMask mods = NSEventModifierMask.CommandKeyMask)
    {
        var item = new NSMenuItem(title, new Selector(selector), key);
        if (key.Length > 0) item.KeyEquivalentModifierMask = mods;
        menu.AddItem(item);
        return item;
    }

    private static string NSRightArrow() => ((char)0xF703).ToString();
    private static string NSLeftArrow() => ((char)0xF702).ToString();
}
