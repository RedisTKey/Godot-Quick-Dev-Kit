using System;
using Godot;
using GDictionary = Godot.Collections.Dictionary;

namespace QuickDev.Database.Tests;

/// <summary>Run in two distinct Godot processes: first --seed, then --verify.</summary>
public partial class PersistenceProbe : Node
{
    private const string FixturePath = "user://quick_database_csharp_persistence_probe/save.cfg";

    public override void _Ready() => Callable.From(Run).CallDeferred();

    private void Run()
    {
        QuickDatabaseService? service = null;
        var failed = false;
        try
        {
            var arguments = OS.GetCmdlineUserArgs();
            var seed = Array.IndexOf(arguments, "--seed") >= 0;
            var verify = Array.IndexOf(arguments, "--verify") >= 0;
            Check(seed != verify, "exactly one of --seed or --verify is required");
            using var store = new QuickDatabaseConfigFileStore();
            if (seed) Check(store.EraseStore(FixturePath) == Error.Ok, "remove only dedicated prior probe fixture");
            if (verify) Check(Godot.FileAccess.FileExists(FixturePath), "cold process must find seed file");
            service = new QuickDatabaseService();
            AddChild(service);
            Check(service.OpenSlot<TestDocument>("persistent", FixturePath), "probe slot opens");
            if (seed)
            {
                Check(service.UpdateSlot("persistent", "all", document =>
                {
                    var value = (TestDocument)document;
                    value.Count = 418;
                    value.Label = "cold-process / 跨进程 / café";
                    value.Ratios = new GDictionary
                    {
                        ["nested"] = new GDictionary { ["flag"] = true, ["items"] = new Godot.Collections.Array { 3, "two", new Vector2(4, 5) } }
                    };
                }), "seed update");
                Check(service.IsDirty("persistent"), "seed is dirty before lifecycle flush");
                RemoveChild(service); // Exercise real engine _ExitTree, not an explicit save.
                Check(!service.IsDirty("persistent"), "lifecycle seed flush succeeded");
                Check(Godot.FileAccess.FileExists(FixturePath), "seed exists on disk");
                GD.Print("DATABASE_PERSISTENCE_SEED_PASS");
            }
            else
            {
                using var document = service.GetDocument<TestDocument>("persistent");
                Check(document != null, "cold process snapshot exists");
                Check(document!.Count == 418, "cold process scalar restored");
                Check(document.Label == "cold-process / 跨进程 / café", "cold process Unicode restored");
                var nested = document.Ratios["nested"].AsGodotDictionary();
                Check(nested["flag"].AsBool(), "cold process dictionary restored");
                var items = nested["items"].AsGodotArray();
                Check(items.Count == 3 && items[0].AsInt32() == 3 && items[1].AsString() == "two", "cold process array restored");
                Check(items[2].AsVector2() == new Vector2(4, 5), "cold process Godot Variant restored");
                Check(!service.IsDirty("persistent"), "cold process loads clean");
                RemoveChild(service);
                Check(store.EraseStore(FixturePath) == Error.Ok, "dedicated fixture cleanup");
                Check(!Godot.FileAccess.FileExists(FixturePath), "fixture removed");
                GD.Print("DATABASE_PERSISTENCE_VERIFY_PASS");
            }
        }
        catch (Exception exception)
        {
            failed = true;
            GD.PushError($"DATABASE_PERSISTENCE_FAIL {exception}");
        }
        finally
        {
            if (service != null && GodotObject.IsInstanceValid(service)) service.Free();
            GetTree().Quit(failed ? 1 : 0);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
