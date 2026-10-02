#nullable disable
using Godot;
using Godot.Collections;

namespace QuickDevKit.QuickInput;

/// <summary>Overridable boundary around Godot's global InputMap.</summary>
public partial class QuickInputMapAdapter : RefCounted
{
    public virtual Array<InputEvent> GetEvents(StringName action)
    {
        var events = new Array<InputEvent>();
        if (!InputMap.HasAction(action))
            return events;

        foreach (InputEvent inputEvent in InputMap.ActionGetEvents(action))
            events.Add((InputEvent)inputEvent.Duplicate());
        return events;
    }

    public virtual bool HasAction(StringName action) => InputMap.HasAction(action);

    public virtual void SetEvents(StringName action, Array<InputEvent> events)
    {
        InputMap.ActionEraseEvents(action);
        foreach (InputEvent inputEvent in events)
            InputMap.ActionAddEvent(action, (InputEvent)inputEvent.Duplicate());
    }
}
