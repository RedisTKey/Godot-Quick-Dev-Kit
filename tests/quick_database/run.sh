#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
GODOT="${GODOT:-godot}"
DOTNET="${DOTNET:-dotnet}"
if [[ "$("$GODOT" --version)" != 4.7.2.*mono* ]]; then
  echo 'Use supported Godot 4.7.2 .NET: set GODOT to its executable.' >&2; exit 2
fi
WORK="${QUICK_DATABASE_TEST_WORK:-$(mktemp -d "${TMPDIR:-/tmp}/quick-database-test.XXXXXX")}"
if [[ -z "${QUICK_DATABASE_TEST_WORK:-}" ]]; then trap 'rm -rf "$WORK"' EXIT; fi
mkdir -p "$WORK/addons/quick_database_probe" "$WORK/logs"
cp -R "$ROOT/addons/quick_database" "$WORK/addons/"
cp "$ROOT/addons/quick_database/csharp/tests/EditorPluginProbe.gd" "$WORK/addons/quick_database_probe/probe.gd"
cat > "$WORK/addons/quick_database_probe/plugin.cfg" <<'CONFIG'
[plugin]
name="Database test observer"
description="Disposable test-only editor observer"
author="Quick Database tests"
version="1.0"
script="probe.gd"
CONFIG
cat > "$WORK/project.godot" <<'PROJECT'
config_version=5
[application]
config/name="Quick Database isolated validation"
config/features=PackedStringArray("4.7", "C#", "GL Compatibility")
[dotnet]
project/assembly_name="QuickDatabaseTests"
[editor_plugins]
enabled=PackedStringArray("res://addons/quick_database_probe/plugin.cfg")
[rendering]
renderer/rendering_method="gl_compatibility"
PROJECT
cat > "$WORK/QuickDatabaseTests.csproj" <<'CSPROJ'
<Project Sdk="Godot.NET.Sdk/4.7.2">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
CSPROJ
run_checked() {
  local label="$1" marker="$2"; shift 2
  "$@" > "$WORK/logs/$label.log" 2>&1 || { cat "$WORK/logs/$label.log"; return 1; }
  cat "$WORK/logs/$label.log"
  grep -q "$marker" "$WORK/logs/$label.log"
  ! grep -qE 'SCRIPT ERROR:|Unhandled|Leaked unsafe reference|TEST RUNNER FAILURE|DATABASE_.*FAIL' "$WORK/logs/$label.log"
  # Native suite deliberately injects ConfigFile errors; its explicit assertion summary
  # and process exit decide its result. Editor must never silently lose its scripts.
  if [[ "$label" == editor-* ]]; then
    ! grep -qE 'SCRIPT ERROR:|^ERROR:|DATABASE_EDITOR_FAIL' "$WORK/logs/$label.log"
  fi
}
test ! -e "$WORK/.godot"
run_checked editor-cold 'DATABASE_EDITOR_PASS phase=cold' env QUICK_DATABASE_EDITOR_PHASE=cold "$GODOT" --headless --editor --path "$WORK" --quit-after 600
RESTORE=()
if [[ -n "${NUGET_CONFIG:-}" ]]; then RESTORE=(--configfile "$NUGET_CONFIG"); fi
"$DOTNET" build "$WORK/QuickDatabaseTests.csproj" -c Debug "${RESTORE[@]}"
"$DOTNET" build "$WORK/QuickDatabaseTests.csproj" -c Release "${RESTORE[@]}"
run_checked editor-built 'DATABASE_EDITOR_PASS phase=built' env QUICK_DATABASE_EDITOR_PHASE=built "$GODOT" --headless --editor --path "$WORK" --quit-after 600
run_checked editor-persist 'DATABASE_EDITOR_PASS phase=persist' env QUICK_DATABASE_EDITOR_PHASE=persist "$GODOT" --headless --editor --path "$WORK" --quit-after 600
run_checked editor-restart 'DATABASE_EDITOR_PASS phase=restart' env QUICK_DATABASE_EDITOR_PHASE=restart "$GODOT" --headless --editor --path "$WORK" --quit-after 600
for suite in document store repository service package; do
  run_checked "gds-$suite" "tests passed" "$GODOT" --headless --path "$WORK" --script "res://addons/quick_database/gds/tests/test_quick_database_$suite.gd"
done
run_checked interop 'DATABASE_INTEROP_PASS' "$GODOT" --headless --path "$WORK" --script res://addons/quick_database/csharp/tests/InteropProbe.gd
run_checked native 'DATABASE_TESTS_PASS' "$GODOT" --headless --path "$WORK" res://addons/quick_database/csharp/tests/DatabaseTests.tscn
run_checked persistence-seed 'DATABASE_PERSISTENCE_SEED_PASS' "$GODOT" --headless --path "$WORK" res://addons/quick_database/csharp/tests/PersistenceProbe.tscn -- --seed
run_checked persistence-verify 'DATABASE_PERSISTENCE_VERIFY_PASS' "$GODOT" --headless --path "$WORK" res://addons/quick_database/csharp/tests/PersistenceProbe.tscn -- --verify
echo "DATABASE_FULL_VALIDATION_PASS logs=$WORK/logs"
