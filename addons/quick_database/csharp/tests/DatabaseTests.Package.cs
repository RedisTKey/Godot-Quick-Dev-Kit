using System;
using Godot;
using GFile = Godot.FileAccess;

namespace QuickDev.Database.Tests;

public partial class DatabaseTests
{
    private const string AddonRoot = "res://addons/quick_database/csharp";
    private static readonly string[] RequiredFiles =
    {
        "plugin.cfg", "QuickDatabasePlugin.cs", "README.md",
        "runtime/QuickDatabaseDocument.cs", "runtime/QuickDatabaseRepository.cs", "runtime/QuickDatabaseService.cs",
        "runtime/stores/QuickDatabaseStore.cs", "runtime/stores/QuickDatabaseConfigFileStore.cs",
        "examples/ExampleDocument.cs", "examples/BasicUsage.cs", "examples/BasicUsage.tscn"
    };
    private static readonly string[] ForbiddenReferences =
    {
        "res://_Core/", "res://_Main/", "res://_Level/", "res://_UI/",
        "/root/GameSave", "/root/GameProgress", "/root/AudioManager",
        "GameSaveData", "GameSaveService", "GameSaveRepository", "highest_unlocked_level_id"
    };

    private void RunPackageTests()
    {
        Test("package/required native C# files are distributable", () =>
        {
            foreach (var relative in RequiredFiles)
                True(GFile.FileExists($"{AddonRoot}/{relative}"), $"required native addon file: {relative}");
        });
        Test("package/editor registration contract", () =>
        {
            using var config = new ConfigFile();
            Equal(config.Load($"{AddonRoot}/plugin.cfg"), Error.Ok, "plugin config readable");
            Equal(config.GetValue("plugin", "script", "").AsString(), "QuickDatabasePlugin.cs", "native EditorPlugin entry");
            var source = GFile.GetFileAsString($"{AddonRoot}/QuickDatabasePlugin.cs");
            True(source.Contains("AutoloadName = \"QuickDatabase\"", StringComparison.Ordinal), "isolated singleton name");
            True(source.Contains("AddAutoloadSingleton", StringComparison.Ordinal), "plugin enables singleton");
            True(source.Contains("RemoveAutoloadSingleton", StringComparison.Ordinal), "plugin removes owned singleton");
            True(source.Contains("[Tool]", StringComparison.Ordinal), "plugin uses editor tool annotation");
        });
        Test("package/runtime is independent of source game's classes", () =>
        {
            foreach (var relative in RequiredFiles)
            {
                if (!relative.EndsWith(".cs", StringComparison.Ordinal)) continue;
                var source = GFile.GetFileAsString($"{AddonRoot}/{relative}");
                foreach (var forbidden in ForbiddenReferences)
                    False(source.Contains(forbidden, StringComparison.Ordinal), $"unexpected project dependency {forbidden} in {relative}");
            }
        });
        Test("package/example scene loads instantiates and binds native script", () =>
        {
            var scene = ResourceLoader.Load<PackedScene>($"{AddonRoot}/examples/BasicUsage.tscn");
            True(scene != null, "example scene loads");
            var instance = scene!.Instantiate();
            _nodes.Add(instance);
            True(instance is QuickDev.Database.Examples.BasicUsage, "example instantiates actual native C# script");
            True(instance.GetNodeOrNull<Label>("Panel/Margin/Layout/Summary") != null, "example summary exists");
            True(instance.GetNodeOrNull<Button>("Panel/Margin/Layout/Buttons/Increment") != null, "example controls exist");
        });
    }
}
