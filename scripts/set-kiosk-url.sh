#!/usr/bin/env bash
# Points a frame's Home HQ kiosk at its router page and prints the saved URL.
# Usage: ADB_SERIAL=<serial> scripts/set-kiosk-url.sh <frame-id>
# The frame id must have an entry under "frames" in screens.json.
set -euo pipefail

router_url="${ROUTER_URL:-https://frame.home.vicio.ovh}"
frame_id="${1:-}"

if [[ ! "$frame_id" =~ ^[a-z0-9-]{1,40}$ ]]; then
  echo "usage: ADB_SERIAL=<serial> $0 <frame-id>   (lowercase letters, digits, -)" >&2
  exit 2
fi

adb_args=()
if [[ -n "${ADB_SERIAL:-}" ]]; then
  adb_args=(-s "$ADB_SERIAL")
elif [[ "$(adb devices | grep -c -w device)" -ne 1 ]]; then
  echo "More or fewer than one device is connected. Set ADB_SERIAL (see: adb devices)." >&2
  exit 2
fi

url="$router_url/?frame=$frame_id"
# Single quotes inside: the device shell must not glob the "?" in the URL.
adb "${adb_args[@]}" shell am start -n com.wesbos.homehq/.MainActivity --es url "'$url'"
echo "Kiosk URL set to $url"
