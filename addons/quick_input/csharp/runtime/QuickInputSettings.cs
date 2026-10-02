#nullable disable
using Godot;
using Godot.Collections;

namespace QuickDevKit.QuickInput;

/// <summary>Managed input actions and the shared event capture restrictions.</summary>
public partial class QuickInputSettings : Resource
{
    [Export] public Array<QuickInputActionDefinition> Actions { get; set; } = new();
    [Export] public Array<InputEvent> ReservedEvents { get; set; } = new();
    [Export] public bool AllowModifierCombinations { get; set; }
}
