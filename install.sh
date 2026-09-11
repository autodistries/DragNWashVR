#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
game_dir="$(realpath -- "${GAME_DIR:-..}")"
GAME_DIR="$game_dir" bash ./build.sh
destination="$game_dir/BepInEx/plugins/WalkNWashVRCompanion"
mkdir -p -- "$destination"
if [[ -f "$destination/WalkNWash.VRCompanion.dll" ]]; then
    backup="$game_dir/BepInEx/companion-backups/$(date +%Y%m%d-%H%M%S-%N)"
    mkdir -p -- "$backup"
    cp -- "$destination/WalkNWash.VRCompanion.dll" "$backup/"
    echo "Previous DLL backed up in $backup"
fi
cp -- dist/WalkNWash.VRCompanion.dll "$destination/"
echo "Installed in $destination. Restart the game to load this build."
