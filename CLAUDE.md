# TagTuner – Hinweise für Claude Code

TagTuner ist ein Audio-Tag-Editor, der Musikordner wie Playlists behandelt: Tags, Cover,
Track-Reihenfolge und Dateiformat in einem Fenster. Es gibt eine native **Windows-App**
(WinUI 3, veröffentlicht) und eine geplante native **macOS-App** (C# mit AppKit).
Repo: https://github.com/yyusvf/TagTuner · Website: https://tagtuner.app

## Mit dem Nutzer

- Der Nutzer schreibt Deutsch, Antworten auf Deutsch. README, Release-Notes und Website auf Englisch.
- **Releases, Pushes und alles Öffentliche nur auf ausdrücklichen Wunsch.** Committen lokal ist in Ordnung.
- **Den PC/Mac des Nutzers nie steuern:** keine Maus- oder Tastaturautomation, kein Fenster in den
  Vordergrund holen, keine Bildschirmfotos. Er spielt nebenbei. Erlaubt: eigene Builds starten, per
  UI Automation (ohne Maus) prüfen, Aufnahmen nur des eigenen Fensters (PrintWindow bzw. auf dem Mac
  `screencapture -l <windowid>`).
- Laufende TagTuner-Instanzen des Nutzers dürfen für einen Build beendet werden; danach wieder starten.
- Zum Testen die Demo-Alben benutzen, nie die echte Musik des Nutzers. Änderungen an Demo-Dateien
  danach zurücknehmen.
- Commit-Nachrichten auf Deutsch, am Ende: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`

## Aufbau

```
src/TagTuner.Core   net10.0, plattformneutral. Die ganze Logik: Tags lesen/schreiben (TagLib#),
                    Umwandeln (ffmpeg), Album-Modus, Nummerierung, Sicherungen, Verlauf,
                    Einstellungen, Übersetzungen, Update-Prüfung.
src/TagTuner.App    Windows-App, WinUI 3, net10.0-windows, unpackaged. Nur Oberfläche und
                    Windows-Eigenes (Explorer-Kontextmenü über die Registry, Single-Instance-Pipe).
tests/TagTuner.Tests  xUnit, net10.0, läuft auf Windows und macOS (dotnet test).
translations/*.json   12 Sprachen; Englisch sind die Schlüssel im Code.
installer/          Inno Setup (Windows).
site/               Website, statisch (HTML/CSS/JS), Cloudflare Pages baut bei jedem Push auf master.
tools/              build-release.ps1, build-strings.ps1, fetch-ffmpeg.ps1 (Windows).
```

**Regel für neue Features:** so viel wie möglich in `TagTuner.Core` (mit Tests), nur das Sichtbare
in die jeweilige App. Dann bekommt die andere Plattform die Logik geschenkt. Beispiel:
`Folders/RowReorder.cs` rechnet das Umsortieren, `TrackPane.Reorder.cs` zeichnet es unter Windows.

## Konventionen im Code

- Kommentare deutsch und erklärend (das Warum, nicht das Was). Stil der Umgebung übernehmen.
- Sichtbare Texte immer über `Strings.T("English text")`. Neue Schlüssel in allen
  `translations/*.json` ergänzen (json: `indent=1`, `sort_keys`, `ensure_ascii=False`), danach
  `tools\build-strings.ps1` (erzeugt `src/TagTuner.Core/Settings/Languages/Strings.*.cs`).
- Keine Gedankenstriche (—) in sichtbaren Texten.
- Einstellungen/Verlauf/Sicherungen: Windows `%APPDATA%\TagTuner`, macOS
  `~/Library/Application Support/TagTuner` (siehe `AppSettings.Directory`).
- Mehrere Prozesse teilen sich den Verlauf; `HistoryStore` liest nach, wenn die Datei sich ändert.

## Bauen, testen, veröffentlichen (Windows)

```
dotnet test tests/TagTuner.Tests
dotnet build src/TagTuner.App -c Debug
powershell tools\build-release.ps1 -Version X.Y.Z   # Tests, Publish, Setup, ZIP nach dist/
```

Versionsnummer an drei Stellen: `README.md` (Badge), `installer/TagTuner.iss` (AppVersion),
`src/TagTuner.App/TagTuner.App.csproj` (Version). Byte-genau ersetzen (BOM/CRLF erhalten),
nicht mit PowerShell `Set-Content` (zerstört UTF-8). Semver: neue Funktionen heben die mittlere,
Korrekturen die letzte Zahl; der Nutzer hat die Nummern bisher als 0.8.x gewünscht, im Zweifel fragen.
Release: `gh release create vX.Y.Z dist/TagTuner-X.Y.Z-Setup.exe dist/TagTuner-X.Y.Z-Windows-Portable.zip`
mit englischen Notes (## New / ## Changed / ## Fixed).

**Beide Plattformen, ein Release:** gemeinsame Versionsnummer, ein GitHub-Release pro Version mit
Setup, ZIP und später dem Mac-DMG. Der Windows-Updater (`UpdateService`) nimmt gezielt die `.exe`,
die Website-Download-Karten suchen sich ihre Datei aus dem neuesten Release. Ein Release muss daher
immer auch die Windows-Dateien enthalten.

**Nicht gleichzeitig auf Windows und Mac arbeiten.** Zu Beginn jeder Session `git pull`.

## WinUI-Fallstricke, die schon Zeit gekostet haben

- Eckenradius genau halbe Höhe zeichnet eine helle Naht quer durch → knapp darunter bleiben.
- `ProgressBar` zeichnet bei 0 % eine eigene Linie → eigener Balken.
- `ComboBox` legt den gewählten Eintrag übers Feld → überall `Dropdown` (App/Dropdown.cs).
- Nur ein `ContentDialog` gleichzeitig, sonst Absturz.
- `DispatcherQueueTimer` als Feld halten, als lokale Variable kann er vor dem Feuern verschwinden.
- `ItemContainerTransitions` der Trackliste nicht zur Laufzeit austauschen.

## Die macOS-App

Entscheidung: **C# mit AppKit (.NET für macOS, `net10.0-macos`)**, nicht Swift. Grund: dieselben
nativen Bedienelemente (NSWindow, NSSplitView, NSOutlineView, NSTableView, NSToolbar, Liquid Glass)
bei einer einzigen Logik für beide Systeme. Kein Avalonia/MAUI (nicht nativ genug).

Vorgesehen: `src/TagTuner.Mac` (neues Projekt, referenziert `TagTuner.Core`).

**Schritt 0 auf dem Mac, vor allem anderen:** prüfen, ob .NET 10 mit dem installierten macOS (27)
und Xcode Mac-Apps bauen kann (`dotnet workload install macos`, dann ein leeres `dotnet new macos`
bauen und starten). Neue Xcode-Versionen werden von .NET teils erst Wochen später unterstützt.
`dotnet test tests/TagTuner.Tests` muss dort grün sein.

**Ergebnis Schritt 0 (28.09.2026):** geht. macOS 27.0 (arm64), Xcode 27.0, .NET SDK 10.0.401 (arm64,
ohne sudo nach `~/.dotnet` installiert; das x64-SDK unter `/usr/local/share/dotnet/x64` nicht nehmen).
Tests grün (53/53). Der Workload `macos` (26.5.10318) verlangt eigentlich Xcode 26.6 und bricht sonst
ab, mit `-p:ValidateXcodeVersion=false` (bzw. als Property im csproj) baut und startet eine leere
`dotnet new macos`-App mit Xcode 27 aber einwandfrei, auch mit Verweis auf `TagTuner.Core` (TagLib# wird
mitgepackt). Sobald ein Workload für Xcode 27 erscheint (`dotnet workload update`), den Schalter entfernen.
Shell: `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`.

1. **Grundgerüst:** Fenster mit Seitenleiste (Bibliothek), Trackliste (NSTableView, Spalten wie
   Windows), Metadaten-Inspektor rechts; Ordner öffnen, Tags bearbeiten, Anwenden, Cover.
   Menüleiste mit üblichen Kürzeln (⌘Z Rückgängig, ⌘F Suchen, ⌘, Einstellungen).
2. **Funktionen:** Album-Modus, Umsortieren per Ziehen, Umwandeln mit ffmpeg, Sicherungen,
   Verlauf mit Rückgängig, geteilte Ansicht, Einstellungen, Übersetzungen.
3. **Auslieferung:** Finder-Integration (Dienst oder Finder-Erweiterung statt Explorer-Menü),
   Updates über Sparkle, DMG, ffmpeg als Universal Binary unter `Contents/Resources`
   (`FfmpegLocator` sucht dort), Signieren/Notarisieren (Apple-Entwicklerkonto nötig),
   dritte Download-Karte auf der Website.

### Stand und Arbeitsweise auf dem Mac

`src/TagTuner.Mac` steht (Schritte 1 und 2 größtenteils, siehe Tabelle unten). AppKit ganz im Code,
kein Storyboard. Aufbau: `MainWindowController` verbindet `LibraryController` (Seitenleiste),
`TrackListController` (NSTableView) und `InspectorController` (Felder, Cover, `FolderOverview` ohne
Auswahl) über ein `NSSplitViewController` mit Sidebar/Inspector-Items (Liquid Glass von selbst).
Geschrieben wird immer über `Batch` (Sicherung, Verlauf), Übernehmen über `FolderImport` aus Core.

```
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
dotnet test tests/TagTuner.Tests
dotnet build src/TagTuner.Mac                  # App: src/TagTuner.Mac/bin/Debug/net10.0-macos/osx-arm64/TagTuner.app
tools/make-demo-albums.sh                      # Demo-Alben nach demo/ (ignoriert), danach wieder so erzeugen
python3 tools/build-strings.py                 # Sprachdateien ohne PowerShell, gleiche Prüfungen
```

- **Ohne Maus testen:** Der Debug-Build nimmt Bedienschritte als Argumente, z. B.
  `TagTuner.app/Contents/MacOS/TagTuner <ordner> --do "select:0,1" --do "set:genre:X" --do apply --do state`
  (Liste in `DebugScript.cs`: select, set, apply, undo, album!, move, drop, rename, confirm, newtab,
  snapall:<pfad> …). `snapall` speichert die eigenen Fenster als PNG, auch wenn `screencapture` nicht geht.
- Die Bildschirmaufnahme `screencapture -l <id>` klappt nur, solange der Bildschirm aktiv ist.
- Release wird nicht getrimmt (`LinkMode None`): Einstellungen und Verlauf laufen per Reflection durch
  System.Text.Json. Erst mit JSON-Quellgenerierung in Core wieder trimmen (App dann deutlich kleiner).
- Die Sprache kommt auf dem Mac aus `NSLocale.PreferredLanguages`; .NET meldet dort sonst Englisch.
- Neu nach Core gewandert und von Windows noch nicht benutzt: `FolderImport` (Hereinziehen) und
  `AlbumCover` (Mehrheits-Cover). Beim nächsten Arbeiten unter Windows `MainWindow.Import.cs` und
  `MainWindow.AlbumApply.cs` darauf umstellen. `FileNaming.Plan` benutzt Windows schon (ungetestet
  gebaut, weil auf dem Mac entstanden: beim nächsten Windows-Build prüfen).

Offen für den Mac: Unterordner-Ansicht und geteilte Ansicht, Suche in der Bibliothek, Cover
zuschneiden, Sicherungen-Seite, Disc-Zeilen, Finder-Integration, Sparkle, DMG mit ffmpeg,
Signieren/Notarisieren, Universal Binary (`RuntimeIdentifier` ist noch nur `osx-arm64`),
Mac-eigenes App-Symbol (jetzt das Windows-Symbol).

Noch Windows-only in Core: `UpdateService` erwartet ein `.exe`-Setup. Für den Mac eine eigene
Update-Strecke (Sparkle) vorsehen, nicht die Windows-Logik verbiegen.

## Funktionsliste Windows / Mac

Neue Windows-Features hier mit „offen“ in der Mac-Spalte eintragen, damit nichts vergessen wird.

| Funktion | Windows | Mac |
|---|---|---|
| Ordner als Playlist, Bibliothek mit eigenen/ausgeblendeten Ordnern | ✓ | ✓ |
| Metadaten-Spalte (nur Auswahl, `<mixed>`), Anwenden, Zurücksetzen | ✓ | ✓ |
| Cover setzen/einfügen/kopieren/zuschneiden/entfernen, aus dem Ordner wählen | ✓ | teils (kein Zuschneiden, nicht „aus dem Ordner“) |
| Format/Samplerate umwandeln (ffmpeg), Ordner angleichen mit Protokoll | ✓ | ✓ (ohne eigenes Protokollfenster) |
| Album-Modus (Basis-Tags, Cover, Nummerierung, Dateinamen folgen) | ✓ | ✓ |
| Umsortieren per Ziehen mit Aufleuchten, Disc-Zeilen | ✓ | teils (ohne Aufleuchten und Disc-Zeilen) |
| Dateien hereinziehen (Übernehmen/Verschieben, angleichen) | ✓ | ✓ (⌘ = verschieben) |
| Unterordner-Ansicht, geteilte Ansicht, Tabs | ✓ | teils (native Tabs, der Rest offen) |
| Sortieren nach allen Spalten, Spalten wählen/verschieben | ✓ | ✓ |
| Suche in Bibliothek und Ordner | ✓ | teils (nur Ordner) |
| Umbenennen nach Muster (Platzhalter-Knöpfe) | ✓ | ✓ |
| Tags kopieren/einfügen | ✓ | ✓ |
| Sicherungen vor jeder Änderung, Verlauf, Rückgängig, Sicherungen-Seite | ✓ | teils (ohne Sicherungen-Seite) |
| Player mit Fortschritt, Lautstärke, Systemmedienanzeige | ✓ | ✓ |
| Kontextmenü im Dateimanager inkl. kleines Bearbeiten-Fenster | ✓ (Explorer) | offen (Finder) |
| Einstellungen (Sprache, Updates, Standards, Spalten, Sicherungen) | ✓ | teils (ohne Updates) |
| Stille Updates | ✓ (Inno Setup) | offen (Sparkle) |
| 13 Sprachen | ✓ | ✓ |
