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

## Die macOS-App (Plan)

Entscheidung: **C# mit AppKit (.NET für macOS, `net10.0-macos`)**, nicht Swift. Grund: dieselben
nativen Bedienelemente (NSWindow, NSSplitView, NSOutlineView, NSTableView, NSToolbar, Liquid Glass)
bei einer einzigen Logik für beide Systeme. Kein Avalonia/MAUI (nicht nativ genug).

Vorgesehen: `src/TagTuner.Mac` (neues Projekt, referenziert `TagTuner.Core`).

**Schritt 0 auf dem Mac, vor allem anderen:** prüfen, ob .NET 10 mit dem installierten macOS (27)
und Xcode Mac-Apps bauen kann (`dotnet workload install macos`, dann ein leeres `dotnet new macos`
bauen und starten). Neue Xcode-Versionen werden von .NET teils erst Wochen später unterstützt.
`dotnet test tests/TagTuner.Tests` muss dort grün sein.

1. **Grundgerüst:** Fenster mit Seitenleiste (Bibliothek), Trackliste (NSTableView, Spalten wie
   Windows), Metadaten-Inspektor rechts; Ordner öffnen, Tags bearbeiten, Anwenden, Cover.
   Menüleiste mit üblichen Kürzeln (⌘Z Rückgängig, ⌘F Suchen, ⌘, Einstellungen).
2. **Funktionen:** Album-Modus, Umsortieren per Ziehen, Umwandeln mit ffmpeg, Sicherungen,
   Verlauf mit Rückgängig, geteilte Ansicht, Einstellungen, Übersetzungen.
3. **Auslieferung:** Finder-Integration (Dienst oder Finder-Erweiterung statt Explorer-Menü),
   Updates über Sparkle, DMG, ffmpeg als Universal Binary unter `Contents/Resources`
   (`FfmpegLocator` sucht dort), Signieren/Notarisieren (Apple-Entwicklerkonto nötig),
   dritte Download-Karte auf der Website.

Noch Windows-only in Core: `UpdateService` erwartet ein `.exe`-Setup. Für den Mac eine eigene
Update-Strecke (Sparkle) vorsehen, nicht die Windows-Logik verbiegen.

## Funktionsliste Windows / Mac

Neue Windows-Features hier mit „offen“ in der Mac-Spalte eintragen, damit nichts vergessen wird.

| Funktion | Windows | Mac |
|---|---|---|
| Ordner als Playlist, Bibliothek mit eigenen/ausgeblendeten Ordnern | ✓ | offen |
| Metadaten-Spalte (nur Auswahl, `<mixed>`), Anwenden, Zurücksetzen | ✓ | offen |
| Cover setzen/einfügen/kopieren/zuschneiden/entfernen, aus dem Ordner wählen | ✓ | offen |
| Cover für WAV/AIFF/OGG vormerken, beim Umwandeln mitschreiben | ✓ | offen |
| Format/Samplerate umwandeln (ffmpeg), Ordner angleichen mit Protokoll | ✓ | offen |
| Album-Modus (Basis-Tags, Cover, Nummerierung, Dateinamen folgen) | ✓ | offen |
| Umsortieren per Ziehen mit Aufleuchten, Disc-Zeilen | ✓ | offen |
| Dateien hereinziehen (Übernehmen/Verschieben, angleichen) | ✓ | offen |
| Unterordner-Ansicht, geteilte Ansicht, Tabs | ✓ | offen |
| Sortieren nach allen Spalten, Spalten wählen/verschieben | ✓ | offen |
| Suche in Bibliothek und Ordner | ✓ | offen |
| Umbenennen nach Muster (Platzhalter-Knöpfe) | ✓ | offen |
| Tags kopieren/einfügen | ✓ | offen |
| Sicherungen vor jeder Änderung, Verlauf, Rückgängig, Sicherungen-Seite | ✓ | offen |
| Player mit Fortschritt, Lautstärke, Systemmedienanzeige | ✓ | offen |
| Kontextmenü im Dateimanager inkl. kleines Bearbeiten-Fenster | ✓ (Explorer) | offen (Finder) |
| Einstellungen (Sprache, Updates, Standards, Spalten, Sicherungen) | ✓ | offen |
| Stille Updates | ✓ (Inno Setup) | offen (Sparkle) |
| 13 Sprachen | ✓ | offen |
