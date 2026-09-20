using System.Globalization;

namespace TagTuner.Core.Settings;

/// <summary>
/// Die Oberfläche in dreizehn Sprachen.
///
/// Englisch ist die Ausgangssprache: Code und Markup tragen den englischen
/// Wortlaut, jede andere Sprache ist eine Tabelle, die darauf abbildet. Was
/// nicht in der Tabelle steht, erscheint englisch. Das ist eine lesbare
/// Rückfallebene statt einer leeren Beschriftung oder eines Schlüsselnamens,
/// und es erlaubt, eine Sprache nach und nach zu füllen.
///
/// Die Tabellen selbst liegen in Languages\Strings.&lt;kürzel&gt;.cs und werden
/// aus translations\*.json erzeugt. Von Hand bearbeitet wird die JSON-Datei,
/// nicht das C#. tools\build-strings.ps1 prüft dabei, dass alle Sprachen
/// denselben Schlüsselsatz haben und die Platzhalter zusammenpassen.
/// </summary>
public static partial class Strings
{
    /// <summary>Die Kürzel, in der Reihenfolge der Auswahlliste.</summary>
    public static readonly string[] Supported =
        ["en", "de", "fr", "es", "it", "pt", "nl", "pl", "ru", "uk", "tr", "cs", "sv"];

    /// <summary>
    /// Wie sich jede Sprache selbst nennt. Gleiche Reihenfolge wie
    /// <see cref="Supported"/>, der Index ist die Verbindung. Eigenbezeichnungen
    /// deshalb, weil sonst dreizehn Sprachen mal dreizehn Namen nötig wären und
    /// niemand seine eigene Sprache auf Türkisch sucht.
    /// </summary>
    public static readonly string[] SupportedNames =
        ["English", "Deutsch", "Français", "Español", "Italiano", "Português (Brasil)",
         "Nederlands", "Polski", "Русский", "Українська", "Türkçe", "Čeština", "Svenska"];

    private static string _current = "en";
    private static Dictionary<string, string>? _table;

    /// <summary>Das Kürzel der gewählten Sprache, immer eines aus <see cref="Supported"/>.</summary>
    public static string Current => _current;

    /// <summary>
    /// Legt die Sprache fest. Leer heißt, der Nutzer hat nie eine gewählt,
    /// dann entscheidet Windows. Ein unbekanntes Kürzel wird zu Englisch.
    /// Muss vor dem ersten Textzugriff aufgerufen werden.
    /// </summary>
    public static void Use(string? language)
    {
        var code = string.IsNullOrWhiteSpace(language)
            ? Detect()
            : Normalize(language);

        _current = code;
        _table = TableFor(code);
    }

    /// <summary>Die Sprache für den allerersten Start.</summary>
    public static string Initial() => Detect();

    /// <summary>Nur noch dafür da, dass älterer Code weiterläuft.</summary>
    public static bool SystemIsGerman() => Detect() == "de";

    /// <summary>Der Text in der gewählten Sprache.</summary>
    public static string T(string english) =>
        _table is not null && _table.TryGetValue(english, out var text) ? text : english;

    /// <summary>Wie <see cref="T(string)"/>, mit Platzhaltern wie bei string.Format.</summary>
    public static string T(string english, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(english), args);

    /// <summary>Die Tabelle der aktuellen Sprache. Für Tests und zum Lückensuchen.</summary>
    public static IReadOnlyDictionary<string, string> Table =>
        _table ?? Empty;

    /// <summary>Die Tabelle einer bestimmten Sprache, leer für Englisch.</summary>
    public static IReadOnlyDictionary<string, string> TableOf(string code) =>
        TableFor(Normalize(code)) ?? Empty;

    private static readonly Dictionary<string, string> Empty = new(StringComparer.Ordinal);

    /// <summary>
    /// Macht aus einer Angabe wie „de-AT" oder „DE" ein bekanntes Kürzel.
    /// Alles Unbekannte landet bei Englisch.
    /// </summary>
    private static string Normalize(string language)
    {
        var code = language.Trim().ToLowerInvariant();

        // Regionsanhänge wegschneiden: pt-BR und pt sind für uns dasselbe.
        var dash = code.IndexOfAny(['-', '_']);
        if (dash > 0) code = code[..dash];

        return Array.IndexOf(Supported, code) >= 0 ? code : "en";
    }

    /// <summary>
    /// Maßgeblich ist die Anzeigesprache, nicht das Regionsformat. Wer auf
    /// englischem Windows deutsche Datumsangaben eingestellt hat, erwartet
    /// eine englische Oberfläche.
    /// </summary>
    private static string Detect()
    {
        try { return Normalize(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName); }
        catch { return "en"; }
    }

    private static Dictionary<string, string>? TableFor(string code) => code switch
    {
        "de" => German,
        "fr" => French,
        "es" => Spanish,
        "it" => Italian,
        "pt" => Portuguese,
        "nl" => Dutch,
        "pl" => Polish,
        "ru" => Russian,
        "uk" => Ukrainian,
        "tr" => Turkish,
        "cs" => Czech,
        "sv" => Swedish,
        _ => null,   // Englisch braucht keine Tabelle
    };
}
