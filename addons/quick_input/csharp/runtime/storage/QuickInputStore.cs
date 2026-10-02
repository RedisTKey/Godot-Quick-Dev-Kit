using Godot;
using Godot.Collections;

namespace QuickDevKit.QuickInput;

/// <summary>Override to provide another backend. Loads report failure through LoadError.</summary>
public partial class QuickInputStore : RefCounted
{
    public Error LoadError { get; set; } = Error.Ok;

    public virtual Dictionary LoadBindings()
    {
        LoadError = Error.Unavailable;
        return new();
    }

    public virtual Error SaveBindings(Dictionary bindings) => Error.Unavailable;
}
