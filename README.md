# LocalPrep — native Windows-Fassung

Nachfolger der Electron-App. Ein Fenster, Ordner werden wie Playlisten behandelt.

## Stack

| | |
|---|---|
| Runtime | .NET 10 |
| UI | WinUI 3, Windows App SDK 2.4, **unpackaged** |
| Tags/Cover | TagLib# |
| Konvertierung | ffmpeg als Prozess |

Unpackaged, weil die App HKCU für das Kontextmenü schreibt, als einzelne Exe
laufen soll und sich selbst aktualisieren können muss — alles Dinge, die mit
MSIX ein Zertifikat bräuchten.

## Bauen und starten

```powershell
dotnet build
dotnet run --project src\LocalPrep.App

# Auslieferung: eigenständig, ohne Runtime-Installation beim Nutzer
dotnet publish src\LocalPrep.App -c Release -r win-x64 `
  --self-contained true -p:WindowsAppSDKSelfContained=true -o publish
```

ffmpeg liegt nicht im Repository (rund 80 MB). Einmalig holen:

```powershell
pwsh tools\fetch-ffmpeg.ps1
```

Solange sie fehlt, greift `FfmpegLocator` im Entwicklungsbetrieb auf die Kopie
der Electron-Fassung nebenan zurück.

## Aufbau

```
src/LocalPrep.Core/     reine Domäne, ohne UI — vollständig ohne Fenster testbar
  Audio/                Formate, ffmpeg-Argumente und -Lauf, Tag-Leser, Konvertierung
  Folders/              Verzeichnis-Scan und Ordner-Analyse
  Metadata/             Tags schreiben
  Safety/               Sicherungen und Undo-Verlauf
  Settings/             JSON unter %APPDATA%\LocalPrep\settings.json
  Shell/                Kontextmenü und Kommandozeile
src/LocalPrep.App/      WinUI-3-Oberfläche
tools/fetch-ffmpeg.ps1  holt ffmpeg.exe nach tools\ffmpeg\
```

## Entscheidungen, die nicht offensichtlich sind

**Kein gespeichertes Ordner-Profil.** `FolderAnalysis` liest den Ordner bei
jedem Öffnen neu. Ist er einheitlich, bestimmt er sein Ziel selbst; ist er leer
oder gemischt, greift das Standardprofil aus den Einstellungen. Dadurch liegt
nichts in der Musikbibliothek und nichts kann veralten.

**Bitrate wird nie angeglichen.** Nur Format und Samplerate. Eine Bitrate fällt
ausschließlich an, wenn eine Konvertierung ohnehin verlustbehaftet kodiert.

**`FfmpegArgs.cs` ist wörtlich aus der Electron-Fassung übernommen.** Jede Regel
dort hat einen real aufgetretenen Fehler als Ursache:

- Cover überlebt nur mit `-map 0:v? -c:v copy -disposition:v attached_pic`
- MP3 braucht `-id3v2_version 3`, sonst zeigt der Explorer keine Thumbnails
- AIFF braucht Big-Endian-PCM (`pcm_s24be`); mit `le` bricht ffmpeg mit EINVAL ab
- ffmpegs AAC-Kodierer deckelt bei ~256 kbps, höhere Angaben verpuffen
- OGG, WAV und AIFF können kein Cover tragen — dort ist der Verlust kein Fehler

**„Geändert" wird verglichen, nicht gemerkt.** WinUI löst `TextChanged`
verzögert aus, sodass das Befüllen der Felder als Nutzereingabe ankommt. Ein
Flag im Event-Handler meldete deshalb „Tags schreiben", ohne dass jemand etwas
angefasst hatte. `MainWindow` hält stattdessen den geladenen Feldstand fest und
leitet Änderungen daraus ab.

**Track-Nummern werden nach dem Umsortieren nur neu vergeben, wenn der Ordner
lückenlos von 1 an nummeriert war.** Enthält er eine Teilauswahl eines Albums
(Tracks 1, 2, 5, 11 …), wäre Durchnummerieren eine stille Fälschung.

**`dotnet publish` braucht einen Extra-Schritt.** Es lässt das kompilierte XAML
(`*.xbf`) und `LocalPrep.pri` im Build-Ordner liegen; die Anwendung stirbt dann
beim Start mit `0xC000027B` in `Microsoft.UI.Xaml.dll`, ohne verwertbare
Meldung. Das Target `IncludeXamlArtifactsInPublish` in der csproj reicht sie
nach.

## Stand

Geprüft an echten Dateien (36 Prüfungen in zwei Testläufen):

- Ordnerbaum mit Lazy Loading, Trackliste, Metadatenspalte, Ordner-Analyse
- Tags schreiben und zurücklesen, Cover überlebt
- Konvertierung FLAC → MP3 inkl. Cover, ID3v2.3 am Byte verifiziert
- AIFF-Konvertierung (in der Electron-Fassung komplett kaputt)
- Herunterrechnen 96 → 44,1 kHz
- Sicherungen, Aufräumen nach Frist und nach Referenz
- Rückgängig stellt her und entfernt erzeugte Dateien
- Kommandozeile inkl. der Altform, die Chromium zerlegte
- Kontextmenü: Ein Eintrag ohne Untermenü, `--file=` / `--folder=`
- Eigenständiger Release-Build startet

Noch offen:

- Tabs und geteilte Ansicht
- Umbenennen nach Muster (Knopf ist da, ohne Funktion)
- Cover setzen und entfernen über die Oberfläche
- Vorschau-Player
- Selbstaktualisierung (Velopack)
