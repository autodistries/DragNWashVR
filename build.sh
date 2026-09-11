#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
game_dir="$(realpath -- "${GAME_DIR:-..}")"
dotnet build WalkNWash.VRCompanion.csproj -c Release --nologo "-p:GameDir=$game_dir"
mkdir -p dist
cp -- bin/Release/netstandard2.1/WalkNWash.VRCompanion.dll dist/
echo "Built dist/WalkNWash.VRCompanion.dll"
