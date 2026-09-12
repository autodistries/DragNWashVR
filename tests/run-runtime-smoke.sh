#!/usr/bin/env bash
# Usage: env <Proton environment> bash tests/run-runtime-smoke.sh /path/to/proton run
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.."
repo_dir="$PWD"
game_dir="$(realpath -- "${GAME_DIR:-..}")"
if [[ $# -eq 0 ]]; then
    echo "Pass a Wine/Proton launcher command (for example: /path/to/proton run)." >&2
    exit 2
fi
if pgrep -x DragNWash.exe >/dev/null; then
    echo "Close DragNWash before running the standalone runtime check." >&2
    exit 2
fi
scratch="$(mktemp -d /tmp/walknwash-runtime.XXXXXX)"
artifact="$repo_dir/dist/runtime-$(date +%Y%m%d-%H%M%S)"
mkdir -p -- "$artifact"
dotnet build -c Release --nologo -p:SmokeTest=true -p:GameDir="$game_dir" -o "$scratch/build"
installed="$game_dir/BepInEx/plugins/WalkNWashVRCompanion/WalkNWash.VRCompanion.dll"
log="$game_dir/BepInEx/LogOutput.log"
cp -- "$installed" "$scratch/original.dll"
had_log=false
if [[ -f "$log" ]]; then cp -- "$log" "$scratch/original.log"; had_log=true; fi
restore() {
    if [[ -f "$log" ]]; then cp -- "$log" "$artifact/BepInEx.log"; fi
    cp -- "$scratch/original.dll" "$installed"
    if $had_log; then cp -- "$scratch/original.log" "$log"; else rm -f -- "$log"; fi
    echo "Restored installed DLL and original log. Test artifacts: $artifact"
}
trap restore EXIT
cp -- "$scratch/build/WalkNWash.VRCompanion.dll" "$installed"
cd -- "$game_dir"
graphics=(-force-d3d11 --vr-companion-ui-capture)
if [[ "${SMOKE_NOGRAPHICS:-0}" == 1 ]]; then graphics=(-nographics); fi
timeout 60 "$@" "$game_dir/DragNWash.exe" -batchmode "${graphics[@]}" --vr-companion-smoke-test -logFile "$artifact/Player.log"
if ! rg -q 'RUNTIME SMOKE PASS' "$log" || rg -q '\[Error  :Walk N Wash VR Companion\]' "$log"; then
    echo "Runtime check failed; inspect $artifact/BepInEx.log after cleanup." >&2
    exit 1
fi
