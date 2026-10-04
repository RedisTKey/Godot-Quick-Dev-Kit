#!/usr/bin/env bash
set -euo pipefail
TESTS="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ADDON="$(cd "$TESTS/../.." && pwd)"
GODOT="${GODOT:-godot}"
DOTNET="${DOTNET:-dotnet}"
if [[ "$("$GODOT" --version)" != 4.7.2.*mono* ]]; then
  echo 'This cold-load regression requires the supported Godot 4.7.2 .NET build.' >&2; exit 2
fi
WORK="$(mktemp -d "${TMPDIR:-/tmp}/quick-vfx-cold.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$WORK/addons/quick_vfx_load_probe"
cp -R "$ADDON" "$WORK/addons/quick_vfx_manager"
cp "$TESTS/EditorLoadProbe.gd" "$WORK/addons/quick_vfx_load_probe/probe.gd"
cat > "$WORK/addons/quick_vfx_load_probe/plugin.cfg" <<'CONFIG'
[plugin]
name="Quick VFX editor-load regression"
description="Temporary test-only observer"
author="Quick VFX tests"
version="1.0"
script="probe.gd"
CONFIG
cat > "$WORK/project.godot" <<'PROJECT'
config_version=5
[application]
config/name="Quick VFX cold-load regression"
config/features=PackedStringArray("4.7", "C#", "GL Compatibility")
[dotnet]
project/assembly_name="QuickVfxColdLoad"
[editor_plugins]
enabled=PackedStringArray("res://addons/quick_vfx_load_probe/plugin.cfg", "res://addons/quick_vfx_manager/csharp/plugin.cfg")
[rendering]
renderer/rendering_method="gl_compatibility"
PROJECT
cat > "$WORK/QuickVfxColdLoad.csproj" <<'CSPROJ'
<Project Sdk="Godot.NET.Sdk/4.7.2">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
CSPROJ
run_editor_phase() {
  local phase="$1" compiled="$2"
  if ! QUICK_VFX_EXPECT_COMPILED="$compiled" "$GODOT" --headless --editor --path "$WORK" --quit-after 300 > "$WORK/$phase.log" 2>&1; then
    cat "$WORK/$phase.log"; return 1
  fi
  cat "$WORK/$phase.log"
  # A missing/broken probe must not be mistaken for a pass simply because Godot exited 0.
  grep -q "VFX_EDITOR_LOAD_PASS phase=$phase " "$WORK/$phase.log"
  ! grep -qE 'SCRIPT ERROR:|^ERROR:|VFX_EDITOR_LOAD_FAIL' "$WORK/$phase.log"
}
# Deliberately open the enabled plugin before any compilation or .godot cache exists.
test ! -e "$WORK/.godot"
run_editor_phase cold 0
RESTORE=()
if [[ -n "${NUGET_CONFIG:-}" ]]; then RESTORE=(--configfile "$NUGET_CONFIG"); fi
"$DOTNET" build "$WORK/QuickVfxColdLoad.csproj" -c Debug "${RESTORE[@]}"
run_editor_phase built 1
