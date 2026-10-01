#!/bin/zsh
# Baut die Mac-App als Release und packt sie in ein DMG nach dist/.
#
#   tools/build-mac.sh [Version]          (Vorgabe: Version aus dem csproj)
#
# Ergebnis: dist/TagTuner-<Version>-macOS.dmg mit TagTuner.app und einem
# Verweis auf /Programme zum Hineinziehen. Universal (Apple Silicon und Intel).
#
# Noch nicht signiert und nicht notarisiert: Dafür braucht es ein
# Apple-Entwicklerkonto. Bis dahin öffnet macOS die App beim ersten Mal nur
# über Rechtsklick → Öffnen. ffmpeg liegt nicht bei; die App findet es in
# Homebrew (brew install ffmpeg), sonst geht alles außer dem Umwandeln.

set -e
cd "${0:A:h}/.."
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"

PROJ=src/TagTuner.Mac/TagTuner.Mac.csproj
VERSION="${1:-$(sed -n 's:.*<ApplicationDisplayVersion>\(.*\)</ApplicationDisplayVersion>.*:\1:p' $PROJ)}"
DMG="dist/TagTuner-$VERSION-macOS.dmg"

echo "Tests …"
dotnet test tests/TagTuner.Tests -v q --nologo | tail -1

echo "Baue TagTuner $VERSION (Universal) …"
rm -rf src/TagTuner.Mac/bin/Release
dotnet publish $PROJ -c Release \
  -p:RuntimeIdentifiers='"osx-arm64;osx-x64"' -p:RuntimeIdentifier= \
  -p:ApplicationDisplayVersion="$VERSION" -p:Version="$VERSION" \
  -p:CreatePackage=false -v q --nologo

# Mit zwei Architekturen legt das SDK das zusammengeführte Paket hier ab.
APP=src/TagTuner.Mac/bin/Release/net10.0-macos/TagTuner.app
[[ -d "$APP" ]] || { echo "TagTuner.app nicht gefunden"; exit 1; }
lipo -info "$APP/Contents/MacOS/TagTuner"

# Ohne Entwicklerkonto wenigstens ad hoc signieren: Auf Apple Silicon startet
# ein gar nicht signiertes Programm überhaupt nicht.
codesign --force --deep --sign - "$APP"

mkdir -p dist
echo "Packe $DMG …"
STAGE=$(mktemp -d)
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
rm -f "$DMG"
hdiutil create -volname "TagTuner $VERSION" -srcfolder "$STAGE" -ov -format UDZO "$DMG" >/dev/null
rm -rf "$STAGE"
ls -lh "$DMG"
