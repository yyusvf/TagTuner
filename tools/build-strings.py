#!/usr/bin/env python3
"""
Erzeugt aus translations/*.json die Sprachdateien in TagTuner.Core, wie
tools/build-strings.ps1, für den Mac (dort gibt es kein PowerShell).

Prüft dasselbe: alle Sprachen haben denselben Schlüsselsatz, kein Text ist
leer, die Platzhalter {0}, {1} … stimmen mit dem Englischen überein.

Die Reihenfolge der Einträge bleibt, wie sie in der erzeugten Datei schon
steht; neue Schlüssel werden eingereiht. PowerShell sortiert nach der
Kultur von Windows, und die lässt sich hier nicht genau nachbilden. So
entsteht bei einem späteren Lauf unter Windows höchstens eine andere
Reihenfolge, nie ein anderer Inhalt.

    python3 tools/build-strings.py
"""
import json, locale, os, re, sys

LANGUAGES = [("de", "German"), ("fr", "French"), ("es", "Spanish"), ("it", "Italian"),
             ("pt", "Portuguese"), ("nl", "Dutch"), ("pl", "Polish"), ("ru", "Russian"),
             ("uk", "Ukrainian"), ("tr", "Turkish"), ("cs", "Czech"), ("sv", "Swedish")]

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
source = os.path.join(root, "translations")
target = os.path.join(root, "src", "TagTuner.Core", "Settings", "Languages")

try:
    locale.setlocale(locale.LC_COLLATE, "en_US.UTF-8")
except locale.Error:
    pass


def quote(text):
    out = text.replace("\\", "\\\\").replace('"', '\\"')
    out = out.replace("\r", "\\r").replace("\n", "\\n").replace("\t", "\\t")
    return '"' + out + '"'


def slots(text):
    return sorted(set(re.findall(r"\{(\d+)", text)))


def existing_order(path):
    if not os.path.exists(path):
        return []
    src = open(path, encoding="utf-8-sig").read()
    return [json.loads('"' + m.group(1) + '"')
            for m in re.finditer(r'^        \["((?:[^"\\]|\\.)*)"\] = ', src, re.M)]


def ordered(keys, before):
    known = [k for k in before if k in keys]
    for k in sorted(set(keys) - set(known), key=locale.strxfrm):
        at = next((i for i, x in enumerate(known) if locale.strxfrm(x) > locale.strxfrm(k)), len(known))
        known.insert(at, k)
    return known


problems, reference = [], None
for code, field in LANGUAGES:
    table = json.load(open(os.path.join(source, code + ".json"), encoding="utf-8"))
    keys = set(table)
    if reference is None:
        reference = keys
    else:
        problems += [f"{code}: fehlt {k}" for k in sorted(reference - keys)]
        problems += [f"{code}: unbekannt {k}" for k in sorted(keys - reference)]

    out = os.path.join(target, f"Strings.{code}.cs")
    lines = []
    for key in ordered(keys, existing_order(out)):
        value = table[key]
        if not value.strip():
            problems.append(f"{code}: leer {key}")
            continue
        if slots(key) != slots(value):
            problems.append(f"{code}: Platzhalter stimmen nicht bei {key}")
        lines.append(f"        [{quote(key)}] = {quote(value)},")

    body = "\n".join([
        f"// Erzeugt aus translations\\{code}.json von tools\\build-strings.ps1.",
        "// Nicht von Hand bearbeiten: die JSON-Datei aendern und das Skript laufen lassen.",
        "",
        "namespace TagTuner.Core.Settings;",
        "",
        "public static partial class Strings",
        "{",
        f"    internal static readonly Dictionary<string, string> {field} =",
        "        new(StringComparer.Ordinal)",
        "    {",
        *lines,
        "    };",
        "}",
    ])
    with open(out, "w", encoding="utf-8-sig", newline="") as f:
        f.write(body + "\n")
    print(f"{code:<4} {len(keys):4} Texte  ->  Strings.{code}.cs")

if problems:
    for p in problems:
        print("  " + p, file=sys.stderr)
    sys.exit(f"{len(problems)} Problem(e) in den Uebersetzungen.")
print(f"\nFertig: {len(LANGUAGES)} Sprachen, Englisch kommt aus dem Code.")
