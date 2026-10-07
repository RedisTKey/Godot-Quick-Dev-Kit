using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using QuickDev.Database;
using GDictionary = Godot.Collections.Dictionary;
using GFile = Godot.FileAccess;

namespace QuickDev.Database.Tests;

/// <summary>Real engine regression suite. The scene always exits 0 on success and 1 on failure.</summary>
public partial class DatabaseTests : Node
{
    private int _passed;
    private int _failed;
    private int _nextPath;
    private string _fixtureRoot = "";
    private readonly List<Node> _nodes = new();

    public override void _Ready() => Callable.From(RunAll).CallDeferred();

    private void RunAll()
    {
        _fixtureRoot = $"user://quick_database_csharp_tests_{Guid.NewGuid():N}";
        try
        {
            Equal(DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(_fixtureRoot)), Error.Ok, "create isolated fixtures");
            RunDocumentTests();
            RunRepositoryTests();
            RunStoreTests();
            RunServiceTests();
            RunPackageTests();
            RunExampleTests();
        }
        catch (Exception exception)
        {
            _failed++;
            GD.PushError($"TEST RUNNER FAILURE: {exception}");
        }
        finally
        {
            FreeNodes();
            try
            {
                var absolute = ProjectSettings.GlobalizePath(_fixtureRoot);
                if (Directory.Exists(absolute)) Directory.Delete(absolute, true);
            }
            catch (Exception exception)
            {
                _failed++;
                GD.PushError($"Fixture cleanup failed: {exception}");
            }
            // Release case-local managed wrappers after their stack frames unwind, while
            // the engine is still alive to process native reference-count cleanup.
            Callable.From(Finish).CallDeferred();
        }
    }

    private async void Finish()
    {
        try
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        catch (Exception exception)
        {
            _failed++;
            GD.PushError($"TEST CLEANUP FAILURE: {exception}");
        }
        GD.Print($"Quick Database C# tests: {_passed} passed, {_failed} failed.");
        if (_failed == 0) GD.Print($"DATABASE_TESTS_PASS cases={_passed}");
        else GD.Print($"DATABASE_TESTS_FAIL cases={_failed}");
        GetTree().Quit(_failed > 0 ? 1 : 0);
    }

    private void Test(string name, Action body)
    {
        try
        {
            body();
            _passed++;
            GD.Print($"PASS {name}");
        }
        catch (Exception exception)
        {
            _failed++;
            GD.PushError($"FAIL {name}: {exception}");
        }
        finally { FreeNodes(); }
    }

    private void FreeNodes()
    {
        foreach (var node in _nodes)
            if (GodotObject.IsInstanceValid(node)) node.Free();
        _nodes.Clear();
    }

    private QuickDatabaseService NewService()
    {
        var service = new QuickDatabaseService();
        _nodes.Add(service);
        return service;
    }

    private string NewPath(string label) => $"{_fixtureRoot}/{++_nextPath}_{label}.cfg";
    private static string Absolute(string path) => ProjectSettings.GlobalizePath(path);
    private static GDictionary Payload(int count) => new() { ["count"] = count };
    private static GDictionary ResultPayload(GDictionary result) => result["payload"].AsGodotDictionary();
    private static int Count(GDictionary result) => ResultPayload(result)["count"].AsInt32();
    private static TestDocument Snapshot(QuickDatabaseService service, string slot = "main") =>
        (TestDocument)(service.GetDocument(slot) ?? throw new Exception($"Missing snapshot for {slot}"));
    private static void SetCount(QuickDatabaseDocument document, int count) => ((TestDocument)document).Count = count;
    private static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static void False(bool condition, string message) => True(!condition, message);
    private static void Equal<T>(T actual, T expected, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
            throw new Exception($"{message}: expected {expected}, got {actual}");
    }
    private static void Sequence(IReadOnlyList<string> actual, params string[] expected)
    {
        Equal(actual.Count, expected.Length, "signal count/order");
        for (var index = 0; index < expected.Length; index++)
            Equal(actual[index], expected[index], $"signal {index}");
    }
    private static void EmptyPayload(GDictionary result, QuickDatabaseStore.LoadStatus status)
    {
        True(result.ContainsKey("payload"), "load result must include payload");
        Equal(ResultPayload(result).Count, 0, "empty result payload");
        Equal(result["status"].AsInt32(), (int)status, "result status");
    }
    private static void WriteText(string path, string text)
    {
        using var file = GFile.Open(path, GFile.ModeFlags.Write);
        True(file != null, $"fixture opens: {path}");
        file!.StoreString(text);
    }
    private static void WriteConfig(string path, Variant? schema, Variant? payload)
    {
        using var config = new ConfigFile();
        if (schema.HasValue) config.SetValue("meta", "schema_version", schema.Value);
        if (payload.HasValue) config.SetValue("data", "payload", payload.Value);
        Equal(config.Save(path), Error.Ok, "fixture ConfigFile saves");
    }
    private static void Seed(string path, int count, int schema = 7)
    {
        using var store = new QuickDatabaseConfigFileStore();
        Equal(store.SaveStore(path, schema, Payload(count)), Error.Ok, "seed save");
    }
    private static void AssertSaved(string path, int count, int schema = 7)
    {
        using var reader = new QuickDatabaseConfigFileStore();
        Equal(Count(reader.LoadStore(path, schema)), count, "persisted count");
    }
    private static void AssertBackup(QuickDatabaseConfigFileStore store, string contents = "")
    {
        var backup = store.GetLastBackupPath();
        True(!string.IsNullOrEmpty(backup), "backup path exposed");
        True(backup.EndsWith(".corrupt.cfg", StringComparison.Ordinal), "backup suffix");
        True(GFile.FileExists(backup), "backup exists");
        if (contents != "") Equal(GFile.GetFileAsString(backup), contents, "backup preserves bytes");
    }
    private static void NoTemporaryFiles(string path)
    {
        var absolute = Absolute(path);
        var files = Directory.GetFiles(Path.GetDirectoryName(absolute)!, Path.GetFileName(absolute) + ".*.tmp");
        Equal(files.Length, 0, "transaction must clean temporary files");
    }
}
