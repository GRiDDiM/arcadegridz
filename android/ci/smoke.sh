#!/usr/bin/env bash
# Emulator smoke test: installs the APK, walks the main screens and saves screenshots + logcat for review.
set -u
OUT=${1:-smoke}
mkdir -p "$OUT"
PKG=com.griddim.arcadegridz
APK=app/build/outputs/apk/arcadegridz/release/app-arcadegridz-release.apk
A() { timeout 60 adb "$@"; }
shot() { timeout 30 adb exec-out screencap -p > "$OUT/$1.png" || true; echo "shot $1"; }
start() { A shell am force-stop $PKG; sleep 1; A shell am start -n $PKG/.MainActivity "$@" >/dev/null; }
rot() { A shell settings put system accelerometer_rotation 0; A shell settings put system user_rotation "$1"; }

adb install -r "$APK" || { echo "install failed"; exit 1; }
A logcat -c
A shell settings put secure immersive_mode_confirmations confirmed
rot 0
start; sleep 22; shot 01-phone-welcome
start --es agSmoke guest; sleep 22; shot 02-phone-launcher
A shell input tap 300 1400; sleep 6; shot 03-phone-details
A shell input keyevent KEYCODE_BACK; sleep 2
rot 1; sleep 8; shot 04-phone-landscape
rot 0
A shell wm size 2560x1600; A shell wm density 320; rot 0
start --es agTheme neon; sleep 25; shot 07-tablet-neon
A shell input keyevent KEYCODE_BUTTON_START; sleep 3; shot 07b-tablet-options
A shell input keyevent KEYCODE_BACK; sleep 2
A shell input tap 1600 120; sleep 3; shot 07c-tablet-after-back
start --es agTheme acid; sleep 15; shot 08-tablet-acid
start --es agTheme happy; sleep 15; shot 09-tablet-happy
start --es agTheme console; sleep 15; shot 10-tablet-console
A shell wm size reset; A shell wm density reset
rot 0
# solo app: Nhà Mèo boots straight into its game (before the WebGL launcher games, which can take the CI emulator down)
if timeout 120 adb install -r app/build/outputs/apk/nhameo/release/app-nhameo-release.apk; then
  A shell am start -n com.griddim.nhameo/com.griddim.arcadegridz.MainActivity >/dev/null; sleep 12; shot 20-solo-splash
  sleep 30; shot 21-solo-nhameo
  A shell input keyevent KEYCODE_BACK; sleep 3; shot 22-solo-menu
  A shell am force-stop com.griddim.nhameo
else echo "WARN solo install failed"; fi
# the game goes last: a WebGL game under the software GPU can stall the CI emulator
start --es agSheet 2diso-shift --es agTrailer 1; sleep 35; shot 05-sheet-trailer
A shell input keyevent KEYCODE_BACK; sleep 3; shot 06-game-menu
start --es agSheet 2diso-shift; sleep 35; shot 07-sheet-play
A logcat -d -v time -t 30000 > "$OUT/logcat-full.txt" || true
grep -E "ArcadeGridz|AndroidRuntime|chromium|FATAL" "$OUT/logcat-full.txt" > "$OUT/logcat.txt" || true
if grep -q "FATAL EXCEPTION" "$OUT/logcat-full.txt"; then
  echo "App crashed:"; grep -A30 "FATAL EXCEPTION" "$OUT/logcat-full.txt" | head -60
  exit 1
fi
echo "smoke ok"
