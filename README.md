# TagTuner

**Music folders as playlists.** A Windows app for getting albums in order:
tags, covers, track order and file format, all in one window.

![Version](https://img.shields.io/badge/version-0.8.2-C8F542)
![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-blue)

**Website:** [tagtuner.app](https://tagtuner.app)

## What it's for

You have a folder with an album in it, and the files are a mess: the track
numbers are wrong, half of them have no cover, one is a FLAC among MP3s, the
album name is spelled three different ways.

Open the folder in TagTuner and it shows the songs like a playlist. Drag them
into the right order and the track numbers follow. Fix the album name once and
it applies to every file. Set one cover for all of them. Drop in a missing
song and it gets converted to match the others and tagged like the rest of the
album.

Everything TagTuner writes is backed up first and can be undone.

## Install

Download `TagTuner-x.y.z-Setup.exe` from the
[releases](https://github.com/yyusvf/TagTuner/releases) and run it. No
administrator rights needed, ffmpeg is included. Prefer not to install?
Take the ZIP, unpack it, run `TagTuner.exe`.

## Features

**Tags**
- Edit title, artist, album, year, track, disc, genre, album artist, composer and comment
- Select several files and change only what you type; everything else stays
- Copy the tags of one file and paste them onto others
- Rename files from their tags with a pattern like `{track} - {title}`, with a preview

**Album mode**
- Mark a folder as one release, and it stays uniform: tags, cover and numbering
- Reorder by dragging and the track numbers are rewritten right away
- Apply it to files already in the folder, with a preview of the result
- Points out missing and doubled track numbers
- Guest artists survive: "Artist feat. Guest" is not flattened
- Optional: file names follow the track numbers too

**Covers**
- Set, copy, paste, extract, crop, resize and remove
- One cover for the whole folder in one step
- Square JPEG and PNG are taken over byte for byte, without re-encoding

**Formats**
- Convert between MP3, FLAC, WAV, AIFF, M4A and OGG
- Each folder knows its target format and sample rate; files dropped in are brought to it
- Every conversion is checked afterwards, and a lost cover is put back

**Track list**
- Pick from seventeen columns, reorder them, resize them, sort by any of them
- Albums with several discs get a line per disc
- Search the open folder with Ctrl+F
- Built-in player: double click or space bar

**Browsing**
- Tabs, and a split view to drag files between two folders
- Subfolders open right below the list, several at once
- A library tree with covers, and a search across all your music
- An entry in the Explorer right click menu

**Safety**
- A backup before every write, stored outside your music library
- Undo with Ctrl+Z, or pick any entry from the history
- Restore single files or many at once from the backups page

**Everything else**
- Thirteen languages: English, German, French, Spanish, Italian, Portuguese,
  Dutch, Polish, Russian, Ukrainian, Turkish, Czech, Swedish
- Updates itself: never, ask first, or automatically and silently

## Building it yourself

You need the .NET 10 SDK and Inno Setup 6.

```powershell
dotnet build
dotnet run --project src\TagTuner.App
dotnet test tests\TagTuner.Tests

# Setup and portable ZIP into dist\ (runs the tests first)
.\tools\build-release.ps1 -Version 0.8.0
```

ffmpeg is not in the repository. `tools\fetch-ffmpeg.ps1` fetches it into
`tools\ffmpeg\`, from where the build picks it up.

```
src/TagTuner.Core/   formats, conversion, tags, backups, settings
src/TagTuner.App/    user interface (WinUI 3)
tests/               tests for the core, no window needed
translations/        one JSON file per language
installer/           Inno Setup script
tools/               build and helper scripts
```

Tags are read and written with TagLib#, conversion runs through ffmpeg.
English lives in the code; every other language is one JSON file under
`translations/`, turned into C# by `tools\build-strings.ps1`.

## License

MIT
