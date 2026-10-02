#nullable enable
using Godot;

namespace QuickDevKit.QuickInput;

/// <summary>An isolated two-slot rebind demo; it does not configure the autoload.</summary>
public partial class BasicUsage : VBoxContainer
{
    private const string ExampleAction = "quick_input_csharp_example_jump";
    private QuickInputManager? _manager;
    private StringName _action = new StringName();
    private bool _addedAction;

    public override void _Ready()
    {
        // Never take ownership of an action already used by the host project or another demo.
        _action = ExampleAction;
        int suffix = 1;
        while (InputMap.HasAction(_action))
            _action = $"{ExampleAction}_{suffix++}";

        InputMap.AddAction(_action);
        InputMap.ActionAddEvent(_action, new InputEventKey { PhysicalKeycode = Key.Space });
        _addedAction = true;

        var jump = new QuickInputActionDefinition
        {
            Action = _action,
            DisplayName = "Jump",
            BindingSlots = 2,
            ConflictGroup = "quick_input_csharp_example"
        };
        var settings = new QuickInputSettings();
        settings.Actions.Add(jump);

        _manager = new QuickInputManager();
        AddChild(_manager);
        Error error = _manager.Configure(
            settings, new QuickInputConfigFileStore("user://quick_input_csharp_example.cfg"));
        if (error != Error.Ok)
        {
            GD.PushError($"Quick Input C# example could not configure: {error}");
            return;
        }

        for (int slot = 0; slot < 2; slot++)
        {
            var button = new QuickInputRebindButton { Action = _action, Slot = slot };
            // Inject before entering the tree so the demo never connects to the project autoload.
            button.SetManager(_manager);
            button.ConflictDetected += _ =>
                button.ConfirmConflict(QuickInputManager.ConflictPolicy.Swap);
            AddChild(button);
        }
    }

    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_manager) && _manager!.IsConfigured())
            _manager!.RestoreDefaultsInInputMap();

        if (_addedAction && InputMap.HasAction(_action))
            InputMap.EraseAction(_action);
        _addedAction = false;
    }
}
