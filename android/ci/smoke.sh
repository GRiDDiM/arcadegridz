#!/usr/bin/env bash
# Emulator smoke test: installs the APK, walks the main screens and saves screenshots + logcat for review.
set -u
OUT=${1:-smoke}
mkdir -p "$OUT"
PKG=com.griddim.arcadegridz
APK=app/build/outputs/apk/release/app-release.apk
shot() { adb exec-out screencap -p > "$OUT/$1.png" || true; echo "shot $1"; }
start() { adb shell am force-stop $PKG; sleep 1; adb shell am start -n $PKG/.MainActivity "$@" >/dev/null; }
rot() { adb shell settings put system accelerometer_rotation 0; adb shell settings put system user_rotation "$1"; }

adb install -r "$APK" || { echo "install failed"; exit 1; }
adb logcat -c
adb shell settings put secure immersive_mode_confirmations confirmed
rot 0
start; sleep 22; shot 01-phone-welcome
start --es agSmoke guest; sleep 22; shot 02-phone-launcher
adb shell input tap 300 1400; sleep 6; shot 03-phone-details
adb shell input keyevent KEYCODE_BACK; sleep 2
rot 1; sleep 8; shot 04-phone-landscape
rot 0
start --es agPlay hyperball; sleep 40; shot 05-game-hyperball
adb shell input keyevent KEYCODE_BACK; sleep 4; shot 06-game-systemmenu
adb shell wm size 2560x1600; adb shell wm density 320; rot 0
start --es agTheme neon; sleep 25; shot 07-tablet-neon
adb shell input keyevent KEYCODE_BUTTON_START; sleep 3; shot 07b-tablet-options
adb shell input keyevent KEYCODE_BACK; sleep 2
adb shell input tap 1600 120; sleep 3; shot 07c-tablet-after-back
start --es agTheme acid; sleep 15; shot 08-tablet-acid
start --es agTheme happy; sleep 15; shot 09-tablet-happy
start --es agTheme console; sleep 15; shot 10-tablet-console
adb shell wm size reset; adb shell wm density reset
adb logcat -d -v time > "$OUT/logcat-full.txt" || true
grep -E "ArcadeGridz|AndroidRuntime|chromium|FATAL" "$OUT/logcat-full.txt" > "$OUT/logcat.txt" || true
if grep -q "FATAL EXCEPTION" "$OUT/logcat-full.txt"; then
  echo "App crashed:"; grep -A30 "FATAL EXCEPTION" "$OUT/logcat-full.txt" | head -60
  exit 1
fi
echo "smoke ok"
