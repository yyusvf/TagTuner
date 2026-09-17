# TagTuner

One window where music folders are treated like playlists. Edit tags, align
format and sample rate, set covers, reorder tracks by dragging. For Windows.

![Version](https://img.shields.io/badge/version-0.4.0-C8F542)
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

**Drop files in.** A file dropped into a folder is brought to that folder's
format and sample rate, gets album, artist, year and genre from the folder,
and the next free track number. You can turn this on or off globally and per
folder.

**Metadata on the left, always visible.** Title, artist, album, year, track,
disc, genre, album artist, composer and comment. With several files selected,
each field only shows what they agree on; anything else stays untouched when
writing. With nothing selected, the whole folder is the target.

**Covers.** Set, copy, paste, remove, shrink. A picture that is not square can
be cropped first. Anything already square and JPEG or PNG is taken as is.

**Converting.** MP3, FLAC, WAV, AIFF, M4A and OGG. The bitrate is never
touched on its own; it only comes into play when a lossy encode happens
anyway.

**Preview player.** Play with a double click or the space bar, volume at the
bottom left. Playback keeps running unless the file being played is the one
being written.

**Library.** Add your own folders, search folders and songs, browse a tree
that shows cover and artist per folder. Optionally only folders that actually
contain music.

**Tabs and split view.** Keep several folders open and drag files between two
halves. Hold Ctrl to copy instead of move.

## Your files are backed up

Before every write, TagTuner copies the file to
`%APPDATA%\TagTuner\Backups`, outside your music library. The history shows
what happened when and undoes it. How long backups are kept is a setting.

After every conversion the result is read back and checked: is the sample rate
right, is the cover still there. If the cover was dropped, it is put back.

## Language

English and German. The system language decides on first run, the setting
after that.

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
.\tools\build-release.ps1 -Version 0.4.0
```

ffmpeg is not in the repository. `tools\fetch-ffmpeg.ps1` fetches it into
`tools\ffmpeg\`, from where the build picks it up.

## Layout

```
src/TagTuner.Core/   formats, conversion, tags, backups, settings
src/TagTuner.App/    user interface (WinUI 3)
installer/           Inno Setup script
tools/               build and helper scripts
```

The core knows nothing about the user interface and can be tested without a
window. Tagging is done with TagLib#, converting with ffmpeg as a separate
process.

## License

MIT
