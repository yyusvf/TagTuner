# TagTuner

One window where music folders are treated like playlists. Edit tags, align
format and sample rate, set covers, reorder tracks by dragging. For Windows.

![Version](https://img.shields.io/badge/version-0.7.2-C8F542)
![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-blue)

## Install

Download `TagTuner-x.y.z-Setup.exe` from the
[releases](https://github.com/yyusvf/TagTuner/releases) and run it. No
administrator rights are needed and ffmpeg is already included.

If you would rather not install anything, take the ZIP, unpack it and run
`TagTuner.exe` from there.

## What it does

**Folders are playlists.** When you open a folder, TagTuner checks whether all
files share the same format and sample rate. If they do, that becomes the
target for the folder. If the folder is mixed or empty, the default profile
from the settings applies.

**Album mode.** Say that a folder is one release, an album, an EP, a single,
and TagTuner keeps it uniform. Three switches decide what that covers: base
metadata, cover, track numbering. A dropped file lands where you dropped it
and the folder is numbered from 1 without gaps; reordering by hand renumbers
right away. A guest artist survives, so "Kollektiv Halle feat. Gast" is not
flattened to the folder artist. Leave the mode off for folders where you
collect mixed music, and nothing is touched. Turning it on for a folder that
already has music in it offers to bring those files in line too, with a
preview that looks exactly like the track list afterwards.

**Metadata on the left, always visible.** Title, artist, album, year, track,
disc, genre, album artist, composer and comment. With several files selected,
each field only shows what they agree on; anything else stays untouched when
writing. With nothing selected, the whole folder is the target.

**The track list is yours.** Pick the columns, put them in the order you want,
drag them as wide as you like. Seventeen to choose from, including year, disc,
genre, album artist, composer, comment, bitrate, tag format and codec. Title
and artist can share a column or have one each. Every column sorts. Albums
with several discs get a line per disc, like in a streaming app; click it to
select that disc.

**Ctrl+F searches the open folder.** A small bar like the one in a browser,
with a match counter, up, down and Escape. Matches are highlighted in the
list.

**Covers.** Set, copy, paste, extract, remove, resize. Choosing a section and
scaling happen in the same window. Setting one cover for a whole folder offers
the covers already in it, so you rarely need a file at all. Anything already
square and JPEG or PNG is taken over byte for byte, without re-encoding.

**Copy tags from one file to the rest.** By default everything except title
and track number, because those two differ in every file. The other mode takes
them along. Which one applies is a setting.

**Rename by metadata.** A pattern like `{track} - {title}`, with a preview of
what comes out before anything is renamed.

**Converting.** MP3, FLAC, WAV, AIFF, M4A and OGG. A lossy encode uses the
highest bitrate the format allows. Existing files are never touched because of
their bitrate.

**Preview player.** Play with a double click or the space bar, volume at the
bottom left. Playback keeps running unless the file being played is the one
being written.

**Library.** Add your own folders, reorder them, search folders and songs,
browse a tree that shows cover and artist per folder. Search results carry
covers and their own right click menus. Optionally only folders that actually
contain music.

**Settings with a sidebar.** Six categories instead of one long roll, so
there is room for settings that would not have fit. Everything applies as you
change it.

**Tabs and split view.** Keep several folders open and drag files between two
halves. Hold Ctrl to copy instead of move. Opening a folder from the Explorer
context menu adds a tab to the running window instead of starting a second
one. Subfolders show up below the list and open in place, several at once, so
an artist folder with its albums works like one view.

## Your files are backed up

Before every write, TagTuner copies the file to
`%APPDATA%\TagTuner\Backups`, outside your music library. The history shows
what happened when and undoes it. How long backups are kept is a setting.
The backups page in the settings lists every backup, newest first, with what
changed since, and restores one or many at once. Restoring backs up the
current version first, so it can be undone as well.

After every conversion the result is read back and checked: is the sample rate
right, is the cover still there. If the cover was dropped, it is put back.

## Language

Thirteen languages: English, German, French, Spanish, Italian, Portuguese,
Dutch, Polish, Russian, Ukrainian, Turkish, Czech and Swedish. The system
language decides on first run, the setting after that.

English is the source language and lives in the code. Every other language is
one JSON file under `translations/`. `tools\build-strings.ps1` turns those
into C# and refuses to run if a language is missing a text or if a placeholder
like `{0}` was dropped in translation. Anything not in a table falls back to
English rather than showing an empty label.

## Updates

TagTuner checks for a new version on start, at most once a day. In the
settings this can be set to never, ask or automatic. Never means never, no
request goes out at all.

## Building it yourself

You need the .NET 10 SDK and Inno Setup 6.

```powershell
dotnet build
dotnet run --project src\TagTuner.App

# Setup and portable ZIP into dist\
.\tools\build-release.ps1 -Version 0.7.0
```

ffmpeg is not in the repository. `tools\fetch-ffmpeg.ps1` fetches it into
`tools\ffmpeg\`, from where the build picks it up.

## Layout

```
src/TagTuner.Core/   formats, conversion, tags, backups, settings
src/TagTuner.App/    user interface (WinUI 3)
translations/        one JSON file per language
installer/           Inno Setup script
tools/               build and helper scripts
```

The core knows nothing about the user interface and can be tested without a
window. Tagging is done with TagLib#, converting with ffmpeg as a separate
process.

## License

MIT
