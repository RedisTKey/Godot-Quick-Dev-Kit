#nullable disable
using Godot;

namespace QuickDevKit.QuickInput;

/// <summary>An existing InputMap action managed by Quick Input.</summary>
public partial class QuickInputActionDefinition : Resource
{
    public const int Keyboard = 1;
    public const int Mouse = 2;
    public const int GamepadButton = 4;

    [Export] public StringName Action { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export(PropertyHint.Range, "1,8,1")] public int BindingSlots { get; set; } = 1;
    [Export] public StringName ConflictGroup { get; set; } = "";
    [Export(PropertyHint.Flags, "Keyboard,Mouse,Gamepad Button")]
    public int AllowedEventTypes { get; set; } = Keyboard | Mouse | GamepadButton;
}
