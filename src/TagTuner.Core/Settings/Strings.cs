using System.Globalization;

namespace TagTuner.Core.Settings;

/// <summary>
/// Die Oberfläche auf Deutsch und Englisch.
///
/// Geschlüsselt wird mit dem deutschen Text selbst, nicht mit Kürzeln. Das
/// hält die Aufrufstellen lesbar, und was noch nicht übersetzt ist, erscheint
/// auf Deutsch statt leer oder als Schlüsselname.
/// </summary>
public static class Strings
{
    private static bool _english;

    /// <summary>„de" oder „en".</summary>
    public static string Current => _english ? "en" : "de";

    /// <summary>
    /// Legt die Sprache fest. Leer heißt: noch nie gewählt, dann entscheidet
    /// das System. Alles außer Deutsch bekommt Englisch.
    /// </summary>
    public static void Use(string? language)
    {
        _english = string.IsNullOrWhiteSpace(language)
            ? !SystemIsGerman()
            : !language.StartsWith("de", StringComparison.OrdinalIgnoreCase);
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

    /// <summary>Die Sprache, die beim allerersten Start gelten soll.</summary>
    public static string Initial() => SystemIsGerman() ? "de" : "en";

    /// <summary>Der Text in der eingestellten Sprache.</summary>
    public static string T(string german) =>
        _english && English.TryGetValue(german, out var text) ? text : german;

    /// <summary>Wie <see cref="T(string)"/>, mit Platzhaltern wie in string.Format.</summary>
    public static string T(string german, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(german), args);

    /// <summary>Für Tests und für die Prüfung auf Lücken.</summary>
    public static IReadOnlyDictionary<string, string> Table => English;

    private static readonly Dictionary<string, string> English = new(StringComparer.Ordinal)
    {
        // ── Spalten und Listen ──────────────────────────────────
        ["TITEL"] = "TITLE",
        ["INTERPRET"] = "ARTIST",
        ["ALBUM"] = "ALBUM",
        ["FORMAT"] = "FORMAT",
        ["SAMPLERATE"] = "SAMPLE RATE",
        ["BIBLIOTHEK"] = "LIBRARY",
        ["METADATEN"] = "METADATA",
        ["ORDNER-ANALYSE"] = "FOLDER ANALYSIS",
        ["AUDIO"] = "AUDIO",
        ["BEIM ABLEGEN"] = "ON DROP",
        ["Alle wählen"] = "Select all",
        ["mit Unterordnern"] = "with subfolders",
        ["kodiert neu"] = "re-encodes",

        // ── Metadatenfelder ─────────────────────────────────────
        ["Titel"] = "Title",
        ["Interpret"] = "Artist",
        ["Album"] = "Album",
        ["Jahr"] = "Year",
        ["Track"] = "Track",
        ["Disc"] = "Disc",
        ["Genre"] = "Genre",
        ["Album-Interpret"] = "Album artist",
        ["Komponist"] = "Composer",
        ["Kommentar"] = "Comment",
        ["Dateiformat"] = "File format",
        ["Samplerate"] = "Sample rate",
        ["<verschieden>"] = "<mixed>",
        ["kein Cover"] = "no cover",
        ["Cover nicht lesbar"] = "cover not readable",
        ["keine Auswahl"] = "nothing selected",
        ["unbekannt"] = "unknown",

        // ── Knöpfe ──────────────────────────────────────────────
        ["Anwenden"] = "Apply",
        ["Zurücksetzen"] = "Reset",
        ["Abbrechen"] = "Cancel",
        ["Übernehmen"] = "Apply",
        ["Schließen"] = "Close",
        ["OK"] = "OK",
        ["Ordner angleichen"] = "Align folder",
        ["Unterordner einbeziehen"] = "Include subfolders",
        ["Nur diesen Ordner zeigen"] = "Show this folder only",
        ["Umbenennen…"] = "Rename…",
        ["Cover für alle setzen…"] = "Set cover for all…",
        ["Ordner und Lieder suchen…"] = "Search folders and songs…",

        // ── Kurzhinweise ────────────────────────────────────────
        ["Zurück"] = "Back",
        ["Vor"] = "Forward",
        ["Ordner in neuem Tab öffnen"] = "Open folder in a new tab",
        ["Ansicht teilen"] = "Split the view",
        ["Verlauf und Rückgängig"] = "History and undo",
        ["Einstellungen"] = "Settings",
        ["Bibliothek verwalten"] = "Manage library",
        ["Tab schließen"] = "Close tab",
        ["Ordner ist uneinheitlich"] = "Folder is not uniform",
        ["Abspielen und Pause, oder Leertaste in der Liste"] =
            "Play and pause, or press space in the list",
        ["Rechtsklick: Cover setzen oder entfernen"] = "Right click: set or remove cover",
        ["Wird gesichert und lässt sich über den Verlauf zurückholen"] =
            "Backed up first, can be undone from the history",

        // ── Kontextmenüs ────────────────────────────────────────
        ["Abspielen"] = "Play",
        ["Im Explorer anzeigen"] = "Show in Explorer",
        ["Im Explorer öffnen"] = "Open in Explorer",
        ["Zum Ordner springen"] = "Go to folder",
        ["Pfad kopieren"] = "Copy path",
        ["Löschen"] = "Delete",
        ["In neuem Tab öffnen"] = "Open in a new tab",
        ["Unterordner durchsuchen"] = "Search subfolders",
        ["Aus Bibliothek entfernen"] = "Remove from library",
        ["Ordner hinzufügen…"] = "Add folder…",
        ["Cover setzen…"] = "Set cover…",
        ["Cover kopieren"] = "Copy cover",
        ["Cover einfügen"] = "Paste cover",
        ["Cover entfernen"] = "Remove cover",
        ["Größe anpassen…"] = "Resize…",

        // ── Einstellungen ───────────────────────────────────────
        ["Standardprofil"] = "Default profile",
        ["Bibliothek"] = "Library",
        ["Namensschema"] = "Naming scheme",
        ["Kodierung"] = "Encoding",
        ["Sicherungen"] = "Backups",
        ["Aktualisierung"] = "Updates",
        ["Explorer-Kontextmenü"] = "Explorer context menu",
        ["Sprache"] = "Language",
        ["Deutsch"] = "German",
        ["Englisch"] = "English",
        ["Nur Ordner mit Audiodateien zeigen"] = "Only show folders containing audio",
        ["Format und Samplerate angleichen"] = "Align format and sample rate",
        ["Tags vom Ordner übernehmen"] = "Inherit tags from the folder",
        ["Dateiname → Titel"] = "File name → title",
        ["Titel → Dateiname"] = "Title → file name",
        ["Standard-Bitrate"] = "Default bitrate",
        ["Automatisch löschen"] = "Delete automatically",
        ["Registrieren"] = "Register",
        ["Entfernen"] = "Remove",
        ["Eingetragen ✓"] = "Registered ✓",
        ["Nicht eingetragen"] = "Not registered",
        ["Eigene Ordnereinstellungen zurücksetzen"] = "Reset per-folder settings",
        ["Kein Ordner weicht davon ab."] = "No folder deviates from this.",
        ["Jetzt nach Updates suchen"] = "Check for updates now",
        ["Nie"] = "Never",
        ["Fragen"] = "Ask",
        ["Automatisch"] = "Automatically",
        ["ffmpeg fehlt"] = "ffmpeg is missing",

        // ── Verlauf ─────────────────────────────────────────────
        ["Verlauf"] = "History",
        ["Zeit"] = "Time",
        ["Art"] = "Kind",
        ["Beschreibung"] = "Description",
        ["Rückgängig"] = "Undo",
        ["Der Verlauf ist leer."] = "The history is empty.",

        // ── Fortschritt und Status ──────────────────────────────
        ["Wird eingelesen…"] = "Reading…",
        ["Wird gesucht…"] = "Searching…",
        ["Bibliothek wird einmalig eingelesen…"] = "Reading the library once…",
        ["Unterordner werden gezählt…"] = "Counting subfolders…",
        ["wird berechnet…"] = "calculating…",
        ["Keine Auswahl."] = "Nothing selected.",

        // ── Cover ───────────────────────────────────────────────
        ["Coverausschnitt wählen"] = "Choose the cover area",
        ["Covergröße anpassen"] = "Resize cover",
        ["Kantenlänge (höchstens)"] = "Maximum edge length",
        ["JPEG-Qualität"] = "JPEG quality",
        ["Vorschau nicht möglich"] = "Preview not possible",
        ["Bild nicht lesbar"] = "Image not readable",
        ["Bild ist leer"] = "Image is empty",
        ["Das Format wird nicht unterstützt."] = "That format is not supported.",
        ["Die Datei enthält keine Daten."] = "The file contains no data.",
        ["Kein Bild in der Zwischenablage"] = "No image in the clipboard",
        ["Zwischenablage nicht lesbar"] = "Clipboard not readable",
        ["Kopieren fehlgeschlagen"] = "Copy failed",
        ["Umwandeln fehlgeschlagen"] = "Conversion failed",
        ["Bild nicht verarbeitbar"] = "Image cannot be processed",
        ["Das Umwandeln ist fehlgeschlagen."] = "Converting the image failed.",
        ["Das Bild ließ sich nicht neu kodieren."] = "The image could not be re-encoded.",
        ["Format trägt kein Cover"] = "Format carries no cover",
        ["Cover setzen"] = "Set cover",
        ["Cover verkleinern"] = "Shrink cover",
        ["unverändert"] = "unchanged",
        ["zugeschnitten"] = "cropped",
        ["neu kodiert"] = "re-encoded",
        ["größer als das Original"] = "larger than the original",

        // ── Meldungen und Dialoge ───────────────────────────────
        ["Abgeschlossen"] = "Finished",
        ["Mit Fehlern abgeschlossen"] = "Finished with errors",
        ["Fehlgeschlagen:"] = "Failed:",
        ["Hinweise:"] = "Notes:",
        ["Dateien angleichen"] = "Align files",
        ["Dateien verschieben"] = "Move files",
        ["Verschieben"] = "Move",
        ["Track-Nummern neu vergeben"] = "Renumber tracks",
        ["Neu nummerieren"] = "Renumber",
        ["Auf den ganzen Ordner anwenden"] = "Apply to the whole folder",
        ["Auf alle anwenden"] = "Apply to all",
        ["Datei löschen"] = "Delete file",
        ["Nichts gefunden"] = "Nothing found",
        ["Einlesen"] = "Read",

        // ── Analyse ─────────────────────────────────────────────
        ["Ziel-Format"] = "Target format",
        ["Ziel-Samplerate"] = "Target sample rate",
        ["Formate"] = "Formats",
        ["Sampleraten"] = "Sample rates",
        ["aus Standardprofil"] = "from the default profile",
        ["Kanäle"] = "Channels",
        ["Dauer"] = "Duration",
        ["Größe"] = "Size",
        ["Bitrate"] = "Bitrate",
        ["Ordner-Ziel"] = "Folder target",
        ["Auswahl"] = "Selection",
        ["Ordner"] = "Folder",
        ["Gesamtdauer"] = "Total duration",
        ["Gesamtgröße"] = "Total size",
    };
}
