#!/bin/zsh
# Legt Demo-Alben zum Ausprobieren an: kurze Sinustöne mit Tags und Cover.
# Zum Testen immer diese nehmen, nie die echte Musik. Absichtlich uneinheitlich
# (gemischte Formate, fehlende Nummern, ein Ausreißer bei der Samplerate),
# damit Album-Modus und Angleichen etwas zu tun haben.
#
#   tools/make-demo-albums.sh [Zielordner]      (Vorgabe: demo/ im Repo)

set -e
cd "${0:A:h}/.."
OUT="${1:-demo}"
FFMPEG="${FFMPEG:-$(command -v ffmpeg || echo /opt/homebrew/bin/ffmpeg)}"

rm -rf "$OUT"
mkdir -p "$OUT"

# Ein Cover je Album: Farbfläche mit Verlauf, als JPEG.
cover() { # datei farbe
  "$FFMPEG" -v error -y -f lavfi -i "color=c=${2}:size=600x600,format=rgb24" \
    -vf "geq=r='r(X,Y)*(1-Y/1200)':g='g(X,Y)*(1-Y/1200)':b='b(X,Y)*(1-X/1500)'" \
    -frames:v 1 "$1"
}

# Ein Lied: Ton mit eigener Tonhöhe, einige Sekunden lang.
song() { # datei frequenz sekunden samplerate cover titel interpret album jahr track genre
  local f="$1" ext="${1##*.}"
  local args=(-v error -y -f lavfi -i "sine=frequency=$2:duration=$3:sample_rate=$4")
  # .ogg als Opus: das ffmpeg aus Homebrew kann kein libvorbis, und Opus kennt nur 48 kHz.
  [[ "$ext" == "ogg" ]] && args=(-v error -y -f lavfi -i "sine=frequency=$2:duration=$3:sample_rate=48000")
  local meta=(-metadata "title=$6" -metadata "artist=$7" -metadata "album=$8"
              -metadata "date=$9" -metadata "genre=${11}")
  [[ -n "${10}" ]] && meta+=(-metadata "track=${10}")
  if [[ -n "$5" && "$ext" != "wav" && "$ext" != "ogg" ]]; then
    args+=(-i "$5" -map 0:a -map 1:v -c:v copy -disposition:v attached_pic)
    [[ "$ext" == "mp3" ]] && args+=(-id3v2_version 3)
  fi
  case "$ext" in
    mp3)  args+=(-c:a libmp3lame -b:a 192k) ;;
    flac) args+=(-c:a flac) ;;
    m4a)  args+=(-c:a aac -b:a 192k) ;;
    ogg)  args+=(-c:a libopus -b:a 128k) ;;
    wav)  args+=(-c:a pcm_s16le) ;;
  esac
  [[ "$ext" != "ogg" ]] && args+=(-ar $4)
  "$FFMPEG" "${args[@]}" "${meta[@]}" "$f"
}

A="$OUT/Northern Lights - Aurora Sessions"
mkdir -p "$A"; cover "$A/cover.jpg" 0x2b6cb0
song "$A/01 Dawn.flac"            440 6 44100 "$A/cover.jpg" "Dawn"            "Northern Lights" "Aurora Sessions" 2021 1 Ambient
song "$A/02 Polar Drift.flac"     494 5 44100 "$A/cover.jpg" "Polar Drift"     "Northern Lights" "Aurora Sessions" 2021 2 Ambient
song "$A/03 Magnetic.flac"        523 7 44100 "$A/cover.jpg" "Magnetic"        "Northern Lights" "Aurora Sessions" 2021 3 Ambient
song "$A/04 Solar Wind.mp3"       587 5 48000 "$A/cover.jpg" "Solar Wind"      "Northern Lights" "Aurora Sessions" 2021 4 Ambient
song "$A/05 Afterglow.flac"       659 6 44100 ""             "Afterglow"       "Northern Lights" ""                2021 "" Ambient

B="$OUT/The Paper Boats - Harbour"
mkdir -p "$B"; cover "$B/front.jpg" 0xc05621
song "$B/Anchor.mp3"              330 5 44100 "$B/front.jpg" "Anchor"          "The Paper Boats" "Harbour" 2019 1 "Indie Rock"
song "$B/Lighthouse.mp3"          370 6 44100 "$B/front.jpg" "Lighthouse"      "The Paper Boats" "Harbour" 2019 2 "Indie Rock"
song "$B/Low Tide.mp3"            392 4 44100 "$B/front.jpg" "Low Tide"        "The Paper Boats" "Harbour" 2019 3 "Indie Rock"
song "$B/Salt.m4a"                415 5 44100 "$B/front.jpg" "Salt"            "The Paper Boats" "Harbour" 2019 4 "Indie Rock"
song "$B/Gulls.mp3"               440 6 44100 "$B/front.jpg" "Gulls"           "The Paper Boats" "Harbour" 2019 "" "Indie Rock"
song "$B/Homeward.mp3"            494 5 44100 "$B/front.jpg" "Homeward"        "The Paper Boats" "Harbour" 2019 6 "Indie Rock"

C="$OUT/Various - Late Night Mix"
mkdir -p "$C"
song "$C/track1.ogg"              262 5 44100 "" "Neon"         "Mira Vale"     "Late Night Mix" 2023 1 Electronic
song "$C/track2.wav"              294 4 44100 "" "Streetlight"  "Oskar Lind"    "Late Night Mix" 2023 2 Electronic
song "$C/track3.mp3"              330 5 44100 "" "Taxi Home"    "Mira Vale"     "Late Night Mix" 2023 3 Electronic

# Zwei Discs: für Disc-Zeilen und Umsortieren über die Disc-Grenze.
D="$OUT/Brandung - Tidewater"
mkdir -p "$D"; cover "$D/cover.jpg" 0x1d4e4a
disc() { # datei frequenz titel track disc
  song "$1" "$2" 4 44100 "$D/cover.jpg" "$3" "Brandung" "Tidewater" 2022 "$4" Post-Rock
  "$FFMPEG" -v error -y -i "$1" -map 0 -c copy -metadata disc="$5" "$1.tmp.mp3" && mv "$1.tmp.mp3" "$1"
}
disc "$D/1-01 Undertow.mp3"   330 "Undertow"   1 1
disc "$D/1-02 Breakwater.mp3" 349 "Breakwater" 2 1
disc "$D/1-03 Salt Marsh.mp3" 370 "Salt Marsh" 3 1
disc "$D/2-01 Driftwood.mp3"  392 "Driftwood"  1 2
disc "$D/2-02 Ebb.mp3"        415 "Ebb"        2 2
disc "$D/2-03 Harbour.mp3"    440 "Harbour"    3 2

echo "Demo-Alben liegen in $OUT"
