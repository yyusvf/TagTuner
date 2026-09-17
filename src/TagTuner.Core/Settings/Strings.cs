using System.Globalization;

namespace TagTuner.Core.Settings;

/// <summary>
/// The user interface in English and German.
///
/// English is the source language: the code and the markup carry the English
/// wording, and this table holds the German. Anything that is not in the
/// table shows up in English, which is a readable fallback rather than an
/// empty label or a key name.
/// </summary>
public static class Strings
{
    private static bool _german;

    /// <summary>Either "en" or "de".</summary>
    public static string Current => _german ? "de" : "en";

    /// <summary>
    /// Picks the language. An empty value means the user never chose one, in
    /// which case the system decides. Everything that is not German gets
    /// English.
    /// </summary>
    public static void Use(string? language)
    {
        _german = string.IsNullOrWhiteSpace(language)
            ? SystemIsGerman()
            : language.StartsWith("de", StringComparison.OrdinalIgnoreCase);
    }

    public static bool SystemIsGerman()
    {
        try
        {
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
                .Equals("de", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>The language to use on the very first run.</summary>
    public static string Initial() => SystemIsGerman() ? "de" : "en";

    /// <summary>The text in the chosen language.</summary>
    public static string T(string english) =>
        _german && German.TryGetValue(english, out var text) ? text : english;

    /// <summary>Like <see cref="T(string)"/>, with placeholders as in string.Format.</summary>
    public static string T(string english, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(english), args);

    /// <summary>For tests and for finding gaps.</summary>
    public static IReadOnlyDictionary<string, string> Table => German;

    private static readonly Dictionary<string, string> German = new(StringComparer.Ordinal)
    {
        // ── Columns and lists ───────────────────────────────────
        ["TITLE"] = "TITEL",
        ["ARTIST"] = "INTERPRET",
        ["ALBUM"] = "ALBUM",
        ["FORMAT"] = "FORMAT",
        ["SAMPLE RATE"] = "SAMPLERATE",
        ["LIBRARY"] = "BIBLIOTHEK",
        ["METADATA"] = "METADATEN",
        ["FOLDER ANALYSIS"] = "ORDNER-ANALYSE",
        ["AUDIO"] = "AUDIO",
        ["ON DROP"] = "BEIM ABLEGEN",
        ["Select all"] = "Alle wählen",
        ["with subfolders"] = "mit Unterordnern",
        ["re-encodes"] = "kodiert neu",
        ["on"] = "an",
        ["off"] = "aus",

        // ── Metadata fields ─────────────────────────────────────
        ["Title"] = "Titel",
        ["Artist"] = "Interpret",
        ["Album"] = "Album",
        ["Year"] = "Jahr",
        ["Track"] = "Track",
        ["Disc"] = "Disc",
        ["Genre"] = "Genre",
        ["Album artist"] = "Album-Interpret",
        ["Composer"] = "Komponist",
        ["Comment"] = "Kommentar",
        ["File format"] = "Dateiformat",
        ["Sample rate"] = "Samplerate",
        ["<mixed>"] = "<verschieden>",
        ["no cover"] = "kein Cover",
        ["cover not readable"] = "Cover nicht lesbar",
        ["nothing selected"] = "keine Auswahl",
        ["unknown"] = "unbekannt",

        // ── Buttons ─────────────────────────────────────────────
        ["Apply"] = "Anwenden",
        ["Reset"] = "Zurücksetzen",
        ["Cancel"] = "Abbrechen",
        ["Save"] = "Speichern",
        ["Close"] = "Schließen",
        ["Later"] = "Später",
        ["Install"] = "Installieren",
        ["Align folder"] = "Ordner angleichen",
        ["Align folder ({0})"] = "Ordner angleichen ({0})",
        ["Include subfolders"] = "Unterordner einbeziehen",
        ["Show this folder only"] = "Nur diesen Ordner zeigen",
        ["Rename…"] = "Umbenennen…",
        ["Set cover for all…"] = "Cover für alle setzen…",
        ["Search folders and songs…"] = "Ordner und Lieder suchen…",
        ["Reset to the global setting"] = "Auf die globale Einstellung zurücksetzen",
        ["Reset per-folder settings"] = "Eigene Ordnereinstellungen zurücksetzen",
        ["Check for updates now"] = "Jetzt nach Updates suchen",
        ["Register"] = "Registrieren",
        ["Remove"] = "Entfernen",
        ["Delete all backups"] = "Alle Sicherungen löschen",

        // ── Tooltips ────────────────────────────────────────────
        ["Back"] = "Zurück",
        ["Forward"] = "Vor",
        ["Open folder in a new tab"] = "Ordner in neuem Tab öffnen",
        ["Split the view"] = "Ansicht teilen",
        ["History and undo"] = "Verlauf und Rückgängig",
        ["Settings"] = "Einstellungen",
        ["Manage library"] = "Bibliothek verwalten",
        ["Close tab"] = "Tab schließen",
        ["Folder is not uniform"] = "Ordner ist uneinheitlich",
        ["Play and pause, or press space in the list"] =
            "Abspielen und Pause, oder Leertaste in der Liste",
        ["Right click: set or remove cover"] = "Rechtsklick: Cover setzen oder entfernen",
        ["Backed up first, can be undone from the history"] =
            "Wird gesichert und lässt sich über den Verlauf zurückholen",

        // ── Context menus ───────────────────────────────────────
        ["Play"] = "Abspielen",
        ["Show in Explorer"] = "Im Explorer anzeigen",
        ["Open in Explorer"] = "Im Explorer öffnen",
        ["Go to folder"] = "Zum Ordner springen",
        ["Copy path"] = "Pfad kopieren",
        ["Copy {0} paths"] = "{0} Pfade kopieren",
        ["Delete"] = "Löschen",
        ["Delete {0} files"] = "{0} Dateien löschen",
        ["Open in a new tab"] = "In neuem Tab öffnen",
        ["Search subfolders"] = "Unterordner durchsuchen",
        ["Remove from library"] = "Aus Bibliothek entfernen",
        ["Add folder…"] = "Ordner hinzufügen…",
        ["Show hidden again ({0})"] = "Ausgeblendete zurückholen ({0})",
        ["Set cover…"] = "Cover setzen…",
        ["Set cover for {0}…"] = "Cover für {0} setzen…",
        ["Copy cover"] = "Cover kopieren",
        ["Paste cover"] = "Cover einfügen",
        ["Paste cover into {0}"] = "Cover in {0} einfügen",
        ["Remove cover"] = "Cover entfernen",
        ["Remove cover from {0}"] = "Cover aus {0} entfernen",
        ["Resize…"] = "Größe anpassen…",

        // ── Settings ────────────────────────────────────────────
        ["Language"] = "Sprache",
        ["German"] = "Deutsch",
        ["English"] = "Englisch",
        ["Takes effect after a restart."] = "Wirkt nach einem Neustart der App.",
        ["Default profile"] = "Standardprofil",
        ["Library"] = "Bibliothek",
        ["Naming scheme"] = "Namensschema",
        ["Encoding"] = "Kodierung",
        ["Backups"] = "Sicherungen",
        ["Updates"] = "Aktualisierung",
        ["Explorer context menu"] = "Explorer-Kontextmenü",
        ["On drop"] = "Beim Ablegen",
        ["On start"] = "Beim Start",
        ["Only show folders containing audio"] = "Nur Ordner mit Audiodateien zeigen",
        ["Align format and sample rate"] = "Format und Samplerate angleichen",
        ["Inherit tags from the folder"] = "Tags vom Ordner übernehmen",
        ["File name → title"] = "Dateiname → Titel",
        ["Title → file name"] = "Titel → Dateiname",
        ["Default bitrate"] = "Standard-Bitrate",
        ["Delete automatically"] = "Automatisch löschen",
        ["Never"] = "Nie",
        ["After 7 days"] = "Nach 7 Tagen",
        ["After 30 days"] = "Nach 30 Tagen",
        ["Ask"] = "Fragen",
        ["Automatically"] = "Automatisch",
        ["Registered ✓"] = "Eingetragen ✓",
        ["Not registered"] = "Nicht eingetragen",
        ["not found"] = "nicht gefunden",
        ["Result: "] = "Ergebnis: ",
        ["No folder deviates from this."] = "Kein Ordner weicht davon ab.",
        ["{0} folder has its own setting and is not affected."] =
            "{0} Ordner hat eine eigene Einstellung und bleibt davon unberührt.",
        ["{0} folders have their own setting and are not affected."] =
            "{0} Ordner haben eine eigene Einstellung und bleiben davon unberührt.",

        // ── Explorer context menu ───────────────────────────────
        ["Adds a TagTuner entry to the right click menu for audio files and folders. " +
         "One entry, no submenu: it opens the folder the file is in. " +
         "No administrator rights needed."] =
            "Fügt einen TagTuner-Eintrag zum Rechtsklick-Menü für Audiodateien und Ordner " +
            "hinzu. Ein einzelner Eintrag ohne Untermenü: Er öffnet den Ordner, in dem die " +
            "Datei liegt. Keine Administratorrechte nötig.",
        ["Failed: "] = "Fehlgeschlagen: ",

        // ── History ─────────────────────────────────────────────
        ["History"] = "Verlauf",
        ["Time"] = "Zeit",
        ["Kind"] = "Art",
        ["Description"] = "Beschreibung",
        ["Undo"] = "Rückgängig",
        ["The history is empty."] = "Der Verlauf ist leer.",

        // ── Progress and status ─────────────────────────────────
        ["Reading…"] = "Wird eingelesen…",
        ["Searching…"] = "Wird gesucht…",
        ["Reading the library once…"] = "Bibliothek wird einmalig eingelesen…",
        ["Counting subfolders…"] = "Unterordner werden gezählt…",
        ["calculating…"] = "wird berechnet…",
        ["Nothing selected."] = "Keine Auswahl.",
        ["ffmpeg is missing"] = "ffmpeg fehlt",

        // ── Cover ───────────────────────────────────────────────
        ["Choose the cover area"] = "Coverausschnitt wählen",
        ["The image is not square. Drag the frame over the part you want to keep as the cover."] =
            "Das Bild ist nicht quadratisch. Zieh den Rahmen an die Stelle, die als Cover " +
            "gespeichert werden soll.",
        ["Selection: {0} × {0} pixels"] = "Ausschnitt: {0} × {0} Pixel",
        ["Original {0} × {1} · {2}"] = "Original {0} × {1} · {2}",
        ["Resize cover"] = "Covergröße anpassen",
        ["Maximum edge length"] = "Kantenlänge (höchstens)",
        ["JPEG quality"] = "JPEG-Qualität",
        ["{0} × {0} pixels"] = "{0} × {0} Pixel",
        ["Preview not possible"] = "Vorschau nicht möglich",
        ["Before {0} ({1} × {2}, {3})"] = "Vorher {0} ({1} × {2}, {3})",
        ["After {0} as JPEG"] = "Nachher {0} als JPEG",
        ["{0} % smaller"] = "{0} % kleiner",
        ["larger than the original"] = "größer als das Original",
        ["Image not readable"] = "Bild nicht lesbar",
        ["That format is not supported."] = "Das Format wird nicht unterstützt.",
        ["No image in the clipboard"] = "Kein Bild in der Zwischenablage",
        ["Copy an image or an image file and try again."] =
            "Kopier ein Bild oder eine Bilddatei und versuch es noch einmal.",
        ["Clipboard not readable"] = "Zwischenablage nicht lesbar",
        ["Copy failed"] = "Kopieren fehlgeschlagen",
        ["Image cannot be processed"] = "Bild nicht verarbeitbar",
        ["Converting the image failed."] = "Das Umwandeln ist fehlgeschlagen.",
        ["Resizing failed"] = "Umwandeln fehlgeschlagen",
        ["The image could not be re-encoded."] = "Das Bild ließ sich nicht neu kodieren.",
        ["Cover not readable"] = "Cover nicht lesbar",
        ["Format carries no cover"] = "Format trägt kein Cover",
        ["{0} cannot store a cover."] = "{0} kann kein Cover speichern.",
        ["Set cover"] = "Cover setzen",
        ["Paste cover ({0}, unchanged)"] = "Cover einfügen ({0}, unverändert)",
        ["Shrink cover ({0})"] = "Cover verkleinern ({0})",
        ["{0} ({1}, {2})"] = "{0} ({1}, {2})",
        ["unchanged"] = "unverändert",
        ["cropped"] = "zugeschnitten",
        ["re-encoded"] = "neu kodiert",
        ["Cover copied ({0}, unchanged)"] = "Cover kopiert ({0}, unverändert)",
        ["{0} file(s) will be changed. Each one is backed up first."] =
            "{0} Datei(en) werden geändert. Vorher wird je Datei eine Sicherung angelegt.",
        ["⚠ {0} file(s) stay untouched: {1} carries no cover."] =
            "⚠ {0} Datei(en) bleiben unangetastet: {1} trägt kein Cover.",

        // ── Dialogs and messages ────────────────────────────────
        ["Finished"] = "Abgeschlossen",
        ["Finished with errors"] = "Mit Fehlern abgeschlossen",
        ["{0} file(s) processed."] = "{0} Datei(en) verarbeitet.",
        ["Failed:"] = "Fehlgeschlagen:",
        ["Notes:"] = "Hinweise:",
        ["{0} file(s) processed, backup created"] = "{0} Datei(en) verarbeitet, Sicherung angelegt",
        ["Align files"] = "Dateien angleichen",
        ["Move files"] = "Dateien verschieben",
        ["Move"] = "Verschieben",
        ["Renumber tracks"] = "Track-Nummern neu vergeben",
        ["Renumber"] = "Neu nummerieren",
        ["Apply to the whole folder"] = "Auf den ganzen Ordner anwenden",
        ["Apply to all"] = "Auf alle anwenden",
        ["Delete file"] = "Datei löschen",
        ["Nothing found"] = "Nichts gefunden",
        ["Read"] = "Einlesen",
        ["Update available"] = "Aktualisierung verfügbar",
        ["Skip this version"] = "Diese Version überspringen",
        ["Downloading the update…"] = "Aktualisierung wird geladen…",
        ["Update failed: {0}"] = "Aktualisierung fehlgeschlagen: {0}",
        ["Version {0} is available, {1} is installed."] =
            "Version {0} ist verfügbar, installiert ist {1}.",
        ["Version {0} is available."] = "Version {0} ist verfügbar.",
        ["Version {0} was skipped."] = "Version {0} wurde übersprungen.",
        ["TagTuner is up to date ({0})."] = "TagTuner ist aktuell ({0}).",
        ["Check failed: {0}"] = "Suche fehlgeschlagen: {0}",
        ["\"Never\" stops any connection. \"Ask\" speaks up when there is something new. " +
         "\"Automatically\" downloads and installs without asking."] =
            "„Nie“ verhindert jede Verbindung. „Fragen“ meldet sich, wenn es etwas Neues gibt. " +
            "„Automatisch“ lädt und installiert ohne Rückfrage.",

        // ── Analysis ────────────────────────────────────────────
        ["Target format"] = "Ziel-Format",
        ["Target sample rate"] = "Ziel-Samplerate",
        ["Formats"] = "Formate",
        ["Sample rates"] = "Sampleraten",
        ["from the default profile"] = "aus Standardprofil",
        ["Channels"] = "Kanäle",
        ["Duration"] = "Dauer",
        ["Size"] = "Größe",
        ["Bitrate"] = "Bitrate",
        ["Folder target"] = "Ordner-Ziel",
        ["Selection"] = "Auswahl",
        ["Folder"] = "Ordner",
        ["{0} files"] = "{0} Dateien",
        ["Total duration"] = "Gesamtdauer",
        ["Total size"] = "Gesamtgröße",
        ["Present"] = "Vorhanden",
    };
}
