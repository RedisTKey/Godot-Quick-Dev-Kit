#!/usr/bin/env python3
"""Validate only Quick Audio Manager in disposable, isolated Godot projects."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]
ADDON = Path("addons/quick_audio_manager")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--godot", default="godot", help="Official Godot .NET executable for C# or all")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--backend", choices=("gds", "csharp", "all"), default="all")
    parser.add_argument("--sdk-version", default="4.7.2")
    parser.add_argument("--framework", default="net9.0")
    args = parser.parse_args()
    godot = shutil.which(args.godot) or str(Path(args.godot).resolve())
    with tempfile.TemporaryDirectory(prefix="quick-audio-tests-") as folder:
        project = Path(folder)
        shutil.copytree(ROOT / ADDON, project / ADDON)
        if args.backend != "all":
            shutil.rmtree(project / ADDON / ("csharp" if args.backend == "gds" else "gds"))
        config = '''config_version=5
[application]
config/name="QuickAudioTests"
[rendering]
renderer/rendering_method="gl_compatibility"
'''
        if args.backend != "gds":
            config += '\n[dotnet]\nproject/assembly_name="QuickAudioTests"\n'
            (project / "QuickAudioTests.csproj").write_text(f'''<Project Sdk="Godot.NET.Sdk/{escape(args.sdk_version)}">
<PropertyGroup><TargetFramework>{escape(args.framework)}</TargetFramework><EnableDynamicLoading>true</EnableDynamicLoading><Nullable>disable</Nullable></PropertyGroup>
</Project>
''')
            bundled = Path(godot).resolve().parent / "GodotSharp/Tools/nupkgs"
            if bundled.is_dir():
                (project / "NuGet.Config").write_text('<configuration><packageSources><clear />'
                    '<add key="godot-bundled" value="' + escape(str(bundled), {'"': '&quot;'}) + '" />'
                    '</packageSources></configuration>')
        (project / "project.godot").write_text(config)
        env = os.environ.copy()
        for key, name in (("XDG_DATA_HOME", "data"), ("XDG_CONFIG_HOME", "config"), ("XDG_CACHE_HOME", "cache"), ("DOTNET_CLI_HOME", "dotnet-home"), ("NUGET_PACKAGES", "nuget-packages")):
            env[key] = str(project / name)
            (project / name).mkdir()
        env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        env["DOTNET_NOLOGO"] = "1"

        def run(command, extra_env=None):
            print("+", " ".join(map(str, command)), flush=True)
            result = subprocess.run(command, cwd=project, env=env | (extra_env or {}), stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT, text=True, timeout=300)
            print(result.stdout, flush=True)
            if result.returncode or "SCRIPT ERROR:" in result.stdout or "ERROR:" in result.stdout:
                raise SystemExit(result.returncode or 1)

        base = [godot, "--headless", "--audio-driver", "Dummy", "--path", str(project)]
        if args.backend != "gds":
            run([args.dotnet, "build", "QuickAudioTests.csproj", "--nologo", "-warnaserror"])
            run([args.dotnet, "build", "QuickAudioTests.csproj", "--nologo", "-c", "ExportRelease", "-warnaserror"])
        run(base + ["--editor", "--import", "--quit"])
        if args.backend != "csharp":
            for script in ("test_quick_audio_manager_package.gd", "test_quick_audio_manager.gd"):
                run(base + ["--script", "res://addons/quick_audio_manager/gds/tests/" + script])
            run(base + ["--quit-after", "3", "res://addons/quick_audio_manager/gds/examples/basic_usage.tscn"])
        if args.backend != "gds":
            run(base + ["res://addons/quick_audio_manager/csharp/tests/runtime_tests.tscn"])
            run(base + ["--quit-after", "3", "res://addons/quick_audio_manager/csharp/examples/basic_usage.tscn"])
        if args.backend == "all":
            driver = project / "addons/quick_audio_test_driver"
            driver.mkdir()
            shutil.copyfile(project / ADDON / "gds/tests/editor_lifecycle.gd", driver / "driver.gd")
            (driver / "plugin.cfg").write_text('[plugin]\nname="Quick Audio test driver"\nscript="driver.gd"\n')
            (project / "foreign_service.gd").write_text("extends Node\n")
            with (project / "project.godot").open("a") as stream:
                stream.write('\n[editor_plugins]\nenabled=PackedStringArray("res://addons/quick_audio_test_driver/plugin.cfg")\n')
            for phase in ("exercise", "prepare_gds", "verify_gds", "prepare_csharp", "verify_csharp"):
                run(base + ["--editor"], {"QUICK_AUDIO_EDITOR_PHASE": phase})
        print("Quick Audio checks passed for " + args.backend, flush=True)


if __name__ == "__main__":
    main()
