#nullable enable
using Godot;

namespace QuickDevKit.QuickInput;

/// <summary>A reusable button that captures a digital input for one binding slot.</summary>
public partial class QuickInputRebindButton : Button
{
    [Signal]
    public delegate void ConflictDetectedEventHandler(QuickInputRebindResult result);

    [Signal]
    public delegate void CaptureFailedEventHandler(Error error);

    [Signal]
    public delegate void CaptureCancelledEventHandler();

    [Export]
    public StringName Action { get; set; } = new StringName();

    [Export(PropertyHint.Range, "0,7,1")]
    public int Slot { get; set; }

    [Export]
    public QuickInputManager.ConflictPolicy ConflictPolicy { get; set; } =
        QuickInputManager.ConflictPolicy.Reject;

    private QuickInputManager? _manager;
    private QuickInputBindingTransaction? _pending;
    private bool _capturing;
    private bool _managerConnected;
    private bool _pressedConnected;

    public override void _EnterTree()
    {
        ProcessMode = ProcessModeEnum.Always;
        if (!_pressedConnected)
        {
            Pressed += BeginCapture;
            _pressedConnected = true;
        }

        ConnectManager();
        RefreshText();
    }

    public override void _Ready()
    {
        if (!GodotObject.IsInstanceValid(_manager))
            SetManager(GetNodeOrNull<QuickInputManager>("/root/QuickInput"));
        else
            RefreshText();
    }

    public override void _ExitTree()
    {
        if (_pressedConnected)
        {
            Pressed -= BeginCapture;
            _pressedConnected = false;
        }

        DisconnectManager();
        _capturing = false;
        _pending = null;
    }

    /// <summary>Injects a manager, or clears it with null. Changing managers ends capture.</summary>
    public void SetManager(QuickInputManager? manager)
    {
        bool cancelled = !ReferenceEquals(_manager, manager) && (_capturing || _pending != null);
        DisconnectManager();
        if (!ReferenceEquals(_manager, manager))
        {
            _capturing = false;
            _pending = null;
        }

        _manager = GodotObject.IsInstanceValid(manager) ? manager : null;
        ConnectManager();
        RefreshText();
        if (cancelled)
            EmitSignal(SignalName.CaptureCancelled);
    }

    public void RefreshText()
    {
        if (HasConfiguredManager())
            Text = _manager!.GetBindingText(Action, Slot);
    }

    public void BeginCapture()
    {
        if (!HasConfiguredManager())
        {
            _capturing = false;
            _pending = null;
            EmitSignal(SignalName.CaptureFailed, (int)Error.Unconfigured);
            return;
        }

        _capturing = true;
        _pending = null;
        Text = "Press a key or button…";
    }

    public void CancelCapture()
    {
        if (!_capturing && _pending == null)
            return;

        _capturing = false;
        _pending = null;
        RefreshText();
        EmitSignal(SignalName.CaptureCancelled);
    }

    public Error ConfirmConflict(QuickInputManager.ConflictPolicy policy)
    {
        if (_pending == null || !HasConfiguredManager())
            return Error.InvalidParameter;

        Error error = _manager!.ApplyRebind(_pending, policy);
        if (error == Error.Ok)
        {
            _capturing = false;
            _pending = null;
            RefreshText();
        }
        else
        {
            EmitSignal(SignalName.CaptureFailed, (int)error);
        }

        return error;
    }

    public override void _Input(InputEvent @event)
    {
        if (!_capturing)
            return;

        switch (@event)
        {
            case InputEventKey key:
                if (!key.Pressed || key.Echo)
                    return;
                MarkInputHandled();
                if (key.Keycode == Key.Escape || key.PhysicalKeycode == Key.Escape)
                {
                    CancelCapture();
                    return;
                }
                break;
            case InputEventMouseButton mouse:
                if (!mouse.Pressed)
                    return;
                MarkInputHandled();
                break;
            case InputEventJoypadButton joypad:
                if (!joypad.Pressed)
                    return;
                MarkInputHandled();
                if (joypad.IsActionPressed("ui_cancel"))
                {
                    CancelCapture();
                    return;
                }
                break;
            default:
                return;
        }

        // A dynamically injected manager may have been freed while capture was open.
        if (!HasConfiguredManager())
        {
            _capturing = false;
            _pending = null;
            EmitSignal(SignalName.CaptureFailed, (int)Error.Unconfigured);
            return;
        }

        QuickInputRebindResult preview = _manager!.PreviewRebind(Action, Slot, @event);
        if (preview.Transaction == null)
        {
            EmitSignal(SignalName.CaptureFailed, (int)Error.InvalidParameter);
            return;
        }

        if (preview.Status == QuickInputRebindResult.RebindStatus.Conflict &&
            ConflictPolicy == QuickInputManager.ConflictPolicy.Reject)
        {
            _pending = preview.Transaction;
            _capturing = false;
            Text = "Binding conflict";
            EmitSignal(SignalName.ConflictDetected, preview);
            return;
        }

        Error error = _manager!.ApplyRebind(preview.Transaction, ConflictPolicy);
        if (error != Error.Ok)
        {
            EmitSignal(SignalName.CaptureFailed, (int)error);
            return;
        }

        _capturing = false;
        _pending = null;
        RefreshText();
    }

    private bool HasConfiguredManager() =>
        GodotObject.IsInstanceValid(_manager) && _manager!.IsConfigured();

    private void ConnectManager()
    {
        if (_managerConnected || !GodotObject.IsInstanceValid(_manager))
            return;
        _manager!.BindingChanged += OnBindingChanged;
        _managerConnected = true;
    }

    private void DisconnectManager()
    {
        if (_managerConnected && GodotObject.IsInstanceValid(_manager))
            _manager!.BindingChanged -= OnBindingChanged;
        _managerConnected = false;
    }

    private void MarkInputHandled()
    {
        // Keeping direct _Input calls safe also makes the control easy to test off-tree.
        if (IsInsideTree())
            GetViewport().SetInputAsHandled();
    }

    private void OnBindingChanged(StringName changedAction, int changedSlot)
    {
        if (changedAction == Action && changedSlot == Slot && !_capturing && _pending == null)
            RefreshText();
    }
}
