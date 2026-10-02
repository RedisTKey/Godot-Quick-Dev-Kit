#nullable disable
using Godot;

namespace QuickDevKit.QuickInput;

/// <summary>A defensive snapshot of one action's binding slot.</summary>
public partial class QuickInputBinding : RefCounted
{
    public StringName Action { get; set; } = "";
    public int Slot { get; set; }
    public InputEvent Event { get; set; }

    public QuickInputBinding() { }

    public QuickInputBinding(StringName action, int slot = 0, InputEvent inputEvent = null)
    {
        Action = action;
        Slot = slot;
        Event = inputEvent?.Duplicate() as InputEvent;
    }
}
