# TagTuner

Ein Fenster, in dem Musikordner wie Playlisten behandelt werden. Tags
bearbeiten, Format und Samplerate angleichen, Cover setzen, Reihenfolge per
Ziehen ändern. Für Windows.

![Version](https://img.shields.io/badge/version-0.4.0-C8F542)
![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-blue)

## Installation

Lade `TagTuner-x.y.z-Setup.exe` aus den
[Releases](https://github.com/yyusvf/TagTuner/releases) und führe sie aus. Es
werden keine Administratorrechte gebraucht, und ffmpeg ist bereits enthalten.

Wer nichts installieren möchte, nimmt das ZIP, entpackt es und startet
`TagTuner.exe` daraus.

## Was die App macht

**Ordner sind Playlisten.** Beim Öffnen eines Ordners liest TagTuner nach, ob
alle Dateien dasselbe Format und dieselbe Samplerate haben. Ist er
einheitlich, gilt das als Ziel des Ordners. Ist er gemischt oder leer, greift
das Standardprofil aus den Einstellungen.

**Dateien hineinziehen.** Wer eine Datei in einen Ordner zieht, bekommt sie
auf dessen Format und Samplerate gebracht, mit Album, Interpret, Jahr und
Genre aus dem Ordner und der nächsten freien Track-Nummer. Ob das geschieht,
lässt sich global und je Ordner einstellen.

**Metadaten links, immer sichtbar.** Titel, Interpret, Album, Jahr, Track,
Disc, Genre, Album-Interpret, Komponist und Kommentar. Bei mehreren
ausgewählten Dateien zeigt jedes Feld nur, worin sie sich einig sind; alles
andere bleibt beim Schreiben unangetastet. Ist nichts ausgewählt, gilt der
ganze Ordner.

**Cover.** Setzen, kopieren, einfügen, entfernen, verkleinern. Ein nicht
quadratisches Bild lässt sich vorher zuschneiden. Was schon quadratisch und
JPEG oder PNG ist, wird unverändert übernommen.

**Konvertieren.** MP3, FLAC, WAV, AIFF, M4A und OGG. Die Bitrate wird nie
angefasst, sie fällt nur an, wenn ohnehin verlustbehaftet kodiert wird.

**Vorschau-Player.** Abspielen per Doppelklick oder Leertaste, Lautstärke
unten links. Läuft weiter, solange nicht genau die abgespielte Datei
beschrieben wird.

**Bibliothek.** Eigene Ordner hinzufügen, nach Ordnern und Liedern suchen,
Ordner mit Cover und Interpret in einer Baumansicht. Auf Wunsch werden nur
Ordner gezeigt, unter denen Musik liegt.

**Tabs und geteilte Ansicht.** Mehrere Ordner offen halten und Dateien
zwischen zwei Hälften ziehen. Strg kopiert statt zu verschieben.

## Sicherheit der Daten

Vor jedem Schreibvorgang legt TagTuner eine Kopie der Datei an, außerhalb der
Musikbibliothek unter `%APPDATA%\TagTuner\Backups`. Der Verlauf zeigt, was
wann geschehen ist, und macht es rückgängig. Wie lange Sicherungen liegen
bleiben, steht in den Einstellungen.

Nach jeder Konvertierung wird das Ergebnis zurückgelesen und geprüft: Stimmt
die Samplerate, ist das Cover noch da. Fehlt es, wird es erneut eingesetzt.

## Sprache

Deutsch und Englisch. Beim ersten Start entscheidet die Sprache des Systems,
danach die Einstellung.

## Aktualisierung

TagTuner sucht beim Start höchstens einmal am Tag nach einer neuen Fassung.
In den Einstellungen lässt sich das auf „Nie", „Fragen" oder „Automatisch"
stellen. „Nie" heißt nie, es geht dann keine Anfrage hinaus.

## Selbst bauen

Gebraucht werden das .NET 10 SDK und Inno Setup 6.

```powershell
dotnet build
dotnet run --project src\TagTuner.App

# Setup und portables ZIP nach dist\
.\tools\build-release.ps1 -Version 0.4.0
```

ffmpeg liegt nicht im Repository. `tools\fetch-ffmpeg.ps1` holt es nach
`tools\ffmpeg\`, von wo der Build es einpackt.

## Aufbau

```
src/TagTuner.Core/   Formate, Konvertierung, Tags, Sicherungen, Einstellungen
src/TagTuner.App/    Oberfläche (WinUI 3)
installer/           Inno-Setup-Skript
tools/               Build- und Hilfsskripte
```

Der Kern kennt keine Oberfläche und lässt sich ohne Fenster testen. Getagged
wird mit TagLib#, konvertiert mit ffmpeg als eigenem Prozess.

## Lizenz

MIT
