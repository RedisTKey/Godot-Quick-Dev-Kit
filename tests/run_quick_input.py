#!/usr/bin/env python3
"""Run only Quick Input in a temporary Godot project; never enable unrelated addons."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--godot", default="godot", help="Godot executable (.NET edition for all/C#)")
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--backend", choices=("gds", "csharp", "all"), default="all")
    args = parser.parse_args()
    godot = shutil.which(args.godot) or str(Path(args.godot).resolve())
    with tempfile.TemporaryDirectory(prefix="quick-input-tests-") as folder:
        project = Path(folder)
        shutil.copytree(ROOT / "addons/quick_input", project / "addons/quick_input")
        if args.backend != "all":
            other = "csharp" if args.backend == "gds" else "gds"
            shutil.rmtree(project / "addons/quick_input" / other)
        config = '''config_version=5
[application]
config/name="Quick Input Tests"
config/use_custom_user_dir=true
config/custom_user_dir_name="quick-input-tests"
[rendering]
renderer/rendering_method="gl_compatibility"
'''
        if args.backend != "gds":
            config += '\n[dotnet]\nproject/assembly_name="QuickInputTests"\n'
            (project / "QuickInputTests.csproj").write_text('''<Project Sdk="Godot.NET.Sdk/4.6.3">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <Nullable>disable</Nullable>
  </PropertyGroup>
</Project>
''')
        (project / "project.godot").write_text(config)
        # Official .NET Godot downloads ship these packages. Prefer the verified
        # local SDK over a network restore when available (also works in CI).
        bundled = Path(godot).resolve().parent / "GodotSharp/Tools/nupkgs"
        if args.backend != "gds" and bundled.is_dir():
            (project / "NuGet.Config").write_text(
                '<configuration><packageSources><clear />'
                '<add key="godot-bundled" value="' + escape(str(bundled), {'"': '&quot;'}) + '" />'
                '</packageSources></configuration>')
        env = os.environ.copy()
        # Isolate imports, editor settings, .NET state, and user:// saves from the user's projects.
        for key, name in (("XDG_DATA_HOME", "data"), ("XDG_CONFIG_HOME", "config"), ("XDG_CACHE_HOME", "cache")):
            env[key] = str(project / name)
            (project / name).mkdir()
        env["DOTNET_CLI_HOME"] = str(project / "dotnet-home")
        env["NUGET_PACKAGES"] = str(project / "nuget-packages")
        env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        env["DOTNET_NOLOGO"] = "1"

        def run(command):
            print("+", " ".join(map(str, command)), flush=True)
            result = subprocess.run(command, cwd=project, env=env, stdout=subprocess.PIPE,
                                    stderr=subprocess.STDOUT, text=True, timeout=300)
            print(result.stdout, flush=True)
            # Godot can exit zero after an import/parse error: fail closed on these errors too.
            if result.returncode or "SCRIPT ERROR:" in result.stdout or "ERROR:" in result.stdout:
                raise SystemExit(result.returncode or 1)

        if args.backend != "gds":
            run([args.dotnet, "build", "QuickInputTests.csproj", "--nologo"])
        run([godot, "--headless", "--path", str(project), "--editor", "--import"])
        if args.backend != "csharp":
            for script in ("test_quick_input_package.gd", "test_quick_input_runtime.gd"):
                run([godot, "--headless", "--path", str(project), "--script",
                     "res://addons/quick_input/gds/tests/" + script])
        if args.backend != "gds":
            run([godot, "--headless", "--path", str(project),
                 "res://addons/quick_input/csharp/tests/runtime_tests.tscn"])
        if args.backend != "csharp":
            run([godot, "--headless", "--path", str(project), "--quit-after", "3",
                 "res://addons/quick_input/gds/examples/basic_usage.tscn"])
        if args.backend != "gds":
            run([godot, "--headless", "--path", str(project), "--quit-after", "3",
                 "res://addons/quick_input/csharp/examples/basic_usage.tscn"])
        if args.backend == "all":
            driver = project / "addons/quick_input_test_driver"
            driver.mkdir()
            shutil.copyfile(project / "addons/quick_input/gds/tests/editor_lifecycle.gd", driver / "driver.gd")
            (driver / "plugin.cfg").write_text('[plugin]\nname="Quick Input test driver"\nscript="driver.gd"\n')
            (project / "foreign_service.gd").write_text("extends Node\n")
            with (project / "project.godot").open("a") as stream:
                stream.write('\n[editor_plugins]\nenabled=PackedStringArray("res://addons/quick_input_test_driver/plugin.cfg")\n')
            run([godot, "--headless", "--path", str(project), "--editor"])
        print("Quick Input checks passed for " + args.backend, flush=True)


if __name__ == "__main__":
    main()
