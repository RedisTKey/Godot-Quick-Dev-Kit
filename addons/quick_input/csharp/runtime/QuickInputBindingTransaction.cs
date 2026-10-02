#nullable disable
using Godot;
using Godot.Collections;

namespace QuickDevKit.QuickInput;

/// <summary>A preview snapshot and its single requested binding change.</summary>
public partial class QuickInputBindingTransaction : RefCounted
{
    public Array<QuickInputBinding> Before { get; set; } = new();
    public Array<QuickInputBinding> After { get; set; } = new();
    public StringName Action { get; set; } = "";
    public int Slot { get; set; }
}
