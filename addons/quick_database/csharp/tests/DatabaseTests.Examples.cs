using Godot;
using QuickDev.Database.Examples;

namespace QuickDev.Database.Tests;

public partial class DatabaseTests
{
    private const string ExampleLayout = "Panel/Margin/Layout/";
    private static readonly StringName[] ExampleSubscriptions =
    {
        QuickDatabaseService.SignalName.DataChanged,
        QuickDatabaseService.SignalName.DatabaseLoaded,
        QuickDatabaseService.SignalName.SaveSucceeded,
        QuickDatabaseService.SignalName.SaveFailed
    };

    private BasicUsage NewExample(string slot, string path)
    {
        var scene = ResourceLoader.Load<PackedScene>($"{AddonRoot}/examples/BasicUsage.tscn");
        True(scene != null, "example scene exists");
        var example = scene!.Instantiate<BasicUsage>();
        example.SlotName = slot;
        example.SavePath = path;
        _nodes.Add(example);
        return example;
    }

    private QuickDatabaseService AddTestAutoload()
    {
        True(GetTree().Root.GetNodeOrNull<Node>("QuickDatabase") == null,
            "example lifecycle tests require an isolated root without an existing QuickDatabase autoload");
        var service = NewService();
        service.Name = "QuickDatabase";
        GetTree().Root.AddChild(service);
        return service;
    }

    private static void Press(BasicUsage example, string button) =>
        example.GetNode<Button>($"{ExampleLayout}Buttons/{button}").EmitSignal(Button.SignalName.Pressed);

    private static ExampleDocument ExampleSnapshot(QuickDatabaseService service, StringName slot) =>
        service.GetDocument<ExampleDocument>(slot) ?? throw new System.Exception("example snapshot missing");

    private static void AssertExampleConnections(QuickDatabaseService service, int expected)
    {
        // Godot 4.7 keeps one native bridge per C# signal even with no event subscribers.
        // Inspect the generated event delegate, rather than mistaking that bridge for a view subscription.
        foreach (var signal in ExampleSubscriptions)
        {
            var field = typeof(QuickDatabaseService).GetField("backing_" + signal,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            True(field != null, $"generated event backing field exists for {signal}");
            var handlers = field!.GetValue(service) as System.Delegate;
            Equal(handlers?.GetInvocationList().Length ?? 0, expected, $"example managed subscription count for {signal}");
        }
    }

    private void RunExampleTests()
    {
        Test("example/actual tree fallback and every UI button signal", () =>
        {
            True(GetTree().Root.GetNodeOrNull<Node>("QuickDatabase") == null, "local example starts without autoload");
            var path = NewPath("example-buttons");
            var example = NewExample("ui", path);
            example.InitialTitle = "initial text";
            AddChild(example);
            var service = example.GetNodeOrNull<QuickDatabaseService>("LocalDatabase");
            True(service != null, "example creates local native service");
            var summary = example.GetNode<Label>(ExampleLayout + "Summary");
            var status = example.GetNode<Label>(ExampleLayout + "Status");
            var title = example.GetNode<LineEdit>(ExampleLayout + "Title");
            Equal(title.Text, "initial text", "initial title UI fallback");
            True(summary.Text.Contains("Count: 0"), "initial summary rendered");

            Press(example, "Increment");
            using var first = ExampleSnapshot(service!, "ui");
            Equal(first.Count, 1L, "increment button updates document");
            True(service!.IsDirty("ui"), "increment button marks dirty");
            True(summary.Text.Contains("Count: 1") && summary.Text.Contains("Dirty: True"), "increment refreshes summary");
            Press(example, "Flush");
            False(service.IsDirty("ui"), "flush button clears dirty");
            AssertSaved(path, 1, 1);
            Equal(status.Text, $"Saved: {path}", "flush status rendered");

            Press(example, "Increment");
            Press(example, "Reload");
            using var reloaded = ExampleSnapshot(service, "ui");
            Equal(reloaded.Count, 1L, "reload button discards unsaved increment");
            False(service.IsDirty("ui"), "reload button clears dirty");
            Equal(status.Text, "Loaded: Loaded", "reload status rendered");

            title.Text = "button title / 跨语言";
            Press(example, "Commit");
            using var committed = ExampleSnapshot(service, "ui");
            Equal(committed.Title, title.Text, "commit button saves edited text");
            False(service.IsDirty("ui"), "title commit remains clean");
            using var disk = new QuickDatabaseRepository(() => new ExampleDocument(), path);
            using var diskDocument = (ExampleDocument)disk.LoadDocument();
            Equal(diskDocument.Title, title.Text, "title really persisted");

            Press(example, "Erase");
            using var erased = ExampleSnapshot(service, "ui");
            Equal(erased.Count, 0L, "erase button resets count");
            Equal(erased.Title, "", "erase button resets title");
            False(Godot.FileAccess.FileExists(path), "erase button removes file");
            False(service.IsDirty("ui"), "erased example remains clean");
            Equal(status.Text, "Erase: Ok", "erase status rendered");
            example.Free();
        });
        Test("example/live autoload subscriptions detach reattach and fully unsubscribe on free", () =>
        {
            var service = AddTestAutoload();
            var path = NewPath("example-lifecycle");
            var example = NewExample("lifecycle", path);
            AssertExampleConnections(service, 0);
            AddChild(example);
            True(example.GetNodeOrNull<Node>("LocalDatabase") == null, "existing autoload reused");
            AssertExampleConnections(service, 1);
            var summary = example.GetNode<Label>(ExampleLayout + "Summary");
            for (var iteration = 1; iteration <= 3; iteration++)
            {
                RemoveChild(example);
                AssertExampleConnections(service, 0);
                var detachedText = summary.Text;
                service.UpdateSlot("lifecycle", "count", document => ((ExampleDocument)document).Count = iteration * 10);
                Equal(summary.Text, detachedText, "detached example receives no data callbacks");
                AddChild(example);
                AssertExampleConnections(service, 1);
                service.UpdateSlot("lifecycle", "count", document => ((ExampleDocument)document).Count = iteration * 10 + 1);
                True(summary.Text.Contains($"Count: {iteration * 10 + 1}"), "reattached example receives data callbacks");
            }
            example.Free();
            AssertExampleConnections(service, 0);
            True(GodotObject.IsInstanceValid(service) && service.IsInsideTree(), "autoload outlives example");
            True(service.UpdateSlot("lifecycle", "count", document => ((ExampleDocument)document).Count = 100), "publish data after view freed");
            Equal(service.FlushSlot("lifecycle"), Error.Ok, "publish saved after view freed");
            True(service.ReloadSlot("lifecycle"), "publish loaded after view freed");
            service.EmitSignal(QuickDatabaseService.SignalName.SaveFailed, "lifecycle", path, (int)Error.Busy);
            AssertExampleConnections(service, 0);
            AssertSaved(path, 100, 1);
        });
        Test("example/unrelated autoload slots cannot overwrite view or status", () =>
        {
            var service = AddTestAutoload();
            var path = NewPath("example-filter");
            var otherPath = NewPath("example-filter-other");
            var example = NewExample("visible", path);
            AddChild(example);
            var summary = example.GetNode<Label>(ExampleLayout + "Summary");
            var status = example.GetNode<Label>(ExampleLayout + "Status");
            summary.Text = "keep summary";
            status.Text = "keep status";
            True(service.OpenSlot<ExampleDocument>("other", otherPath), "unrelated slot opens");
            True(service.UpdateSlot("other", "count", document => ((ExampleDocument)document).Count = 9), "unrelated data event");
            Equal(service.CommitSlot("other", "title", document => ((ExampleDocument)document).Title = "other"), Error.Ok, "unrelated saved event");
            True(service.ReloadSlot("other"), "unrelated loaded event");
            service.EmitSignal(QuickDatabaseService.SignalName.SaveFailed, "other", otherPath, (int)Error.Busy);
            Equal(summary.Text, "keep summary", "unrelated signals leave summary alone");
            Equal(status.Text, "keep status", "unrelated signals leave status alone");
            Press(example, "Increment");
            True(summary.Text.Contains("Count: 1"), "matching slot data still refreshes");
            Press(example, "Flush");
            Equal(status.Text, $"Saved: {path}", "matching slot save still refreshes status");
            service.EmitSignal(QuickDatabaseService.SignalName.SaveFailed, "visible", path, (int)Error.Busy);
            Equal(status.Text, $"Save failed: Busy ({path})", "matching slot failure still refreshes status");
            example.Free();
        });
    }
}
