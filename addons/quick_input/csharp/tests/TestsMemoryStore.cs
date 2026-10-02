#nullable disable
using Godot;
using Godot.Collections;

namespace QuickDevKit.QuickInput;

/// <summary>Deterministic, deep-copying test double; never touches the default user save.</summary>
public partial class TestsMemoryStore : QuickInputStore
{
    public Dictionary Bindings { get; set; } = new();
    public Error Failure { get; set; } = Error.Ok;
    public Error LoadFailure { get; set; } = Error.Ok;
    public int SaveCalls { get; private set; }

    public override Dictionary LoadBindings()
    {
        LoadError = LoadFailure;
        return Bindings.Duplicate(true);
    }

    public override Error SaveBindings(Dictionary bindings)
    {
        SaveCalls++;
        if (Failure != Error.Ok)
            return Failure;
        Bindings = bindings.Duplicate(true);
        return Error.Ok;
    }
}
