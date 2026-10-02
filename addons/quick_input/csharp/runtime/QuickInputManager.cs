#nullable disable
using Godot;
using Godot.Collections;
using GodotArray = Godot.Collections.Array;
using BindingMap = System.Collections.Generic.Dictionary<Godot.StringName, Godot.InputEvent[]>;
using DefinitionMap = System.Collections.Generic.Dictionary<Godot.StringName, QuickDevKit.QuickInput.QuickInputActionDefinition>;

namespace QuickDevKit.QuickInput;

/// <summary>
/// Validates, previews and persists action bindings before updating InputMap.
/// Configure once per session and restore defaults before disposing the manager.
/// </summary>
public partial class QuickInputManager : Node
{
    public enum ConflictPolicy { Allow, Reject, Replace, Swap }

    [Signal] public delegate void BindingChangedEventHandler(StringName action, int slot);
    [Signal] public delegate void BindingsSaveFailedEventHandler(Error error);

    public QuickInputSettings Settings { get; set; }
    public QuickInputStore Store { get; set; }
    public QuickInputMapAdapter InputMapAdapter { get; set; } = new();

    private readonly QuickInputEventCodec _codec = new();
    private DefinitionMap _definitions = new();
    private BindingMap _defaults = new();
    private BindingMap _current = new();
    private bool _configured;

    public Error Configure(QuickInputSettings settings, QuickInputStore store = null)
    {
        if (_configured)
            return Error.AlreadyInUse;
        if (settings == null || settings.Actions == null || settings.Actions.Count == 0
            || settings.ReservedEvents == null || InputMapAdapter == null)
            return Error.InvalidParameter;

        var definitions = new DefinitionMap();
        var defaults = new BindingMap();
        foreach (QuickInputActionDefinition definition in settings.Actions)
        {
            if (definition == null || string.IsNullOrEmpty(definition.Action?.ToString())
                || definitions.ContainsKey(definition.Action))
                return Error.InvalidData;
            if (definition.BindingSlots < 1 || !InputMapAdapter.HasAction(definition.Action))
                return Error.InvalidData;
            if (definition.AllowedEventTypes < 1 || definition.AllowedEventTypes > 7)
                return Error.InvalidData;

            Array<InputEvent> events = InputMapAdapter.GetEvents(definition.Action);
            if (events == null || events.Count > definition.BindingSlots)
                return Error.InvalidData;
            var slots = new InputEvent[definition.BindingSlots];
            for (int index = 0; index < events.Count; index++)
            {
                if (!IsAllowed(events[index], definition, settings))
                    return Error.InvalidData;
                slots[index] = CopyEvent(events[index]);
            }
            definitions.Add(definition.Action, definition);
            defaults.Add(definition.Action, slots);
        }
        foreach (InputEvent reserved in settings.ReservedEvents)
        {
            if (string.IsNullOrEmpty(_codec.Signature(reserved)))
                return Error.InvalidData;
        }

        QuickInputStore candidateStore = store ?? new QuickInputConfigFileStore();
        Dictionary saved = candidateStore.LoadBindings();
        if (candidateStore.LoadError != Error.Ok)
            return candidateStore.LoadError;
        if (saved == null)
            return Error.InvalidData;

        BindingMap restored = CopyMap(defaults);
        foreach (Variant rawAction in saved.Keys)
        {
            if (rawAction.VariantType != Variant.Type.String
                && rawAction.VariantType != Variant.Type.StringName)
                return Error.InvalidData;
            var action = new StringName(rawAction.AsString());
            if (!definitions.ContainsKey(action))
                continue;

            Variant rawSlots = saved[rawAction];
            if (rawSlots.VariantType != Variant.Type.Array)
                return Error.InvalidData;
            GodotArray encodedSlots = rawSlots.AsGodotArray();
            if (encodedSlots.Count != restored[action].Length)
                return Error.InvalidData;
            var slots = new InputEvent[encodedSlots.Count];
            for (int index = 0; index < encodedSlots.Count; index++)
            {
                Variant encoded = encodedSlots[index];
                if (encoded.VariantType == Variant.Type.Nil)
                    continue;
                if (encoded.VariantType != Variant.Type.Dictionary)
                    return Error.InvalidData;
                InputEvent inputEvent = _codec.Decode(encoded.AsGodotDictionary());
                if (inputEvent == null || !IsAllowed(inputEvent, definitions[action], settings)
                    || IsReserved(inputEvent, settings))
                    return Error.InvalidData;
                slots[index] = inputEvent;
            }
            restored[action] = slots;
        }

        Settings = settings;
        Store = candidateStore;
        _definitions = definitions;
        _defaults = defaults;
        _current = restored;
        _configured = true;
        ApplyMap(restored);
        return Error.Ok;
    }

    public bool IsConfigured() => _configured;

    public InputEvent GetBinding(StringName action, int slot) =>
        IsValidSlot(action, slot) ? CopyEvent(_current[action][slot]) : null;

    public string GetBindingText(StringName action, int slot) =>
        GetBinding(action, slot)?.AsText() ?? "Unbound";

    public QuickInputRebindResult PreviewRebind(StringName action, int slot, InputEvent inputEvent)
    {
        var result = new QuickInputRebindResult();
        if (!IsValidSlot(action, slot))
        {
            result.Status = QuickInputRebindResult.RebindStatus.Invalid;
            result.Message = "Unknown action or slot";
            return result;
        }
        if (inputEvent != null && (!IsAllowed(inputEvent, _definitions[action], Settings)
            || IsReserved(inputEvent, Settings)))
        {
            result.Status = QuickInputRebindResult.RebindStatus.Invalid;
            result.Message = "Unsupported or reserved event";
            return result;
        }

        var transaction = new QuickInputBindingTransaction
        {
            Action = action,
            Slot = slot,
            Before = Flatten(_current)
        };
        transaction.After.Add(new QuickInputBinding(action, slot, inputEvent));
        result.Transaction = transaction;
        if (inputEvent != null)
        {
            string requested = _codec.Signature(inputEvent);
            StringName group = _definitions[action].ConflictGroup;
            foreach (var pair in _definitions)
            {
                StringName otherAction = pair.Key;
                if (pair.Value.ConflictGroup != group)
                    continue;
                InputEvent[] slots = _current[otherAction];
                for (int otherSlot = 0; otherSlot < slots.Length; otherSlot++)
                {
                    if (otherAction == action && otherSlot == slot)
                        continue;
                    InputEvent otherEvent = slots[otherSlot];
                    if (otherEvent != null && _codec.Signature(otherEvent) == requested)
                        result.Conflicts.Add(new QuickInputBinding(otherAction, otherSlot, otherEvent));
                }
            }
        }
        result.Status = result.Conflicts.Count > 0
            ? QuickInputRebindResult.RebindStatus.Conflict
            : QuickInputRebindResult.RebindStatus.Ready;
        return result;
    }

    public Error ApplyRebind(QuickInputBindingTransaction transaction,
        ConflictPolicy policy = ConflictPolicy.Reject)
    {
        if (!_configured || transaction == null || transaction.After == null
            || transaction.After.Count != 1 || transaction.After[0] == null)
            return Error.InvalidParameter;
        if (policy < ConflictPolicy.Allow || policy > ConflictPolicy.Swap)
            return Error.InvalidParameter;
        if (!SnapshotsMatch(transaction.Before, Flatten(_current)))
            return Error.Busy;

        QuickInputBinding requested = transaction.After[0];
        if (requested.Action != transaction.Action || requested.Slot != transaction.Slot)
            return Error.InvalidParameter;
        QuickInputRebindResult result = PreviewRebind(requested.Action, requested.Slot, requested.Event);
        if (result.Status == QuickInputRebindResult.RebindStatus.Invalid)
            return Error.InvalidParameter;
        if (result.Conflicts.Count > 0 && policy == ConflictPolicy.Reject)
            return Error.AlreadyInUse;
        if (policy == ConflictPolicy.Swap && result.Conflicts.Count > 1)
            return Error.InvalidData;

        InputEvent displaced = _current[requested.Action][requested.Slot];
        if (policy == ConflictPolicy.Swap && result.Conflicts.Count > 0 && displaced != null)
        {
            QuickInputBinding conflict = result.Conflicts[0];
            if (!IsAllowed(displaced, _definitions[conflict.Action], Settings) || IsReserved(displaced, Settings))
                return Error.InvalidParameter;
        }
        BindingMap candidate = CopyMap(_current);
        candidate[requested.Action][requested.Slot] = NormalizeEvent(requested.Event);
        if (policy == ConflictPolicy.Replace)
        {
            foreach (QuickInputBinding conflict in result.Conflicts)
                candidate[conflict.Action][conflict.Slot] = null;
        }
        else if (policy == ConflictPolicy.Swap && result.Conflicts.Count > 0)
        {
            QuickInputBinding conflict = result.Conflicts[0];
            candidate[conflict.Action][conflict.Slot] = CopyEvent(displaced);
        }
        return Commit(candidate);
    }

    public Error ClearBinding(StringName action, int slot) => Rebind(action, slot, null);

    public Error Rebind(StringName action, int slot, InputEvent inputEvent,
        ConflictPolicy policy = ConflictPolicy.Reject)
    {
        QuickInputRebindResult result = PreviewRebind(action, slot, inputEvent);
        return result.Transaction == null
            ? Error.InvalidParameter
            : ApplyRebind(result.Transaction, policy);
    }

    public Error ResetAction(StringName action)
    {
        if (!_configured || action == null || !_definitions.ContainsKey(action))
            return Error.InvalidParameter;
        BindingMap candidate = CopyMap(_current);
        candidate[action] = CopySlots(_defaults[action]);
        return Commit(candidate);
    }

    public Error ResetAll() => _configured ? Commit(CopyMap(_defaults)) : Error.Unconfigured;

    public void RestoreDefaultsInInputMap()
    {
        if (!_configured)
            return;
        ApplyMap(_defaults);
        _configured = false;
        _definitions.Clear();
        _defaults.Clear();
        _current.Clear();
        Settings = null;
        Store = null;
    }

    private Error Commit(BindingMap candidate)
    {
        if (SnapshotsMatch(Flatten(candidate), Flatten(_current)))
            return Error.Ok;
        var overrides = new Dictionary();
        foreach (StringName action in _definitions.Keys)
        {
            InputEvent[] slots = candidate[action];
            InputEvent[] original = _defaults[action];
            bool differs = false;
            var encoded = new GodotArray();
            for (int index = 0; index < slots.Length; index++)
            {
                InputEvent inputEvent = slots[index];
                if (Signature(inputEvent) != Signature(original[index]))
                    differs = true;
                encoded.Add(inputEvent != null ? _codec.Encode(inputEvent) : default(Variant));
            }
            if (differs)
                overrides[action.ToString()] = encoded;
        }
        Error error = Store.SaveBindings(overrides);
        if (error != Error.Ok)
        {
            EmitSignal(SignalName.BindingsSaveFailed, (int)error);
            return error;
        }

        BindingMap previous = _current;
        _current = candidate;
        ApplyMap(candidate);
        foreach (StringName action in _definitions.Keys)
        {
            InputEvent[] slots = candidate[action];
            InputEvent[] oldSlots = previous[action];
            for (int index = 0; index < slots.Length; index++)
            {
                if (Signature(slots[index]) != Signature(oldSlots[index]))
                    EmitSignal(SignalName.BindingChanged, action, index);
            }
        }
        return Error.Ok;
    }

    private void ApplyMap(BindingMap bindings)
    {
        foreach (var pair in bindings)
        {
            var events = new Array<InputEvent>();
            foreach (InputEvent inputEvent in pair.Value)
            {
                if (inputEvent != null)
                    events.Add(inputEvent);
            }
            InputMapAdapter.SetEvents(pair.Key, events);
        }
    }

    private bool IsValidSlot(StringName action, int slot) =>
        _configured && action != null && _definitions.ContainsKey(action) && slot >= 0 && slot < _current[action].Length;

    private bool IsReserved(InputEvent inputEvent, QuickInputSettings settings)
    {
        string signature = _codec.Signature(inputEvent);
        foreach (InputEvent reserved in settings.ReservedEvents)
        {
            if (_codec.Signature(reserved) == signature)
                return true;
        }
        return false;
    }

    private bool IsAllowed(InputEvent inputEvent, QuickInputActionDefinition definition,
        QuickInputSettings settings)
    {
        Dictionary encoded = _codec.Encode(inputEvent);
        if (encoded.Count == 0)
            return false;
        if (inputEvent is InputEventWithModifiers modifiers && !settings.AllowModifierCombinations
            && (modifiers.ShiftPressed || modifiers.CtrlPressed || modifiers.AltPressed || modifiers.MetaPressed))
            return false;
        return encoded["type"].AsString() switch
        {
            "key" => (definition.AllowedEventTypes & QuickInputActionDefinition.Keyboard) != 0,
            "mouse" => (definition.AllowedEventTypes & QuickInputActionDefinition.Mouse) != 0,
            "joypad_button" => (definition.AllowedEventTypes & QuickInputActionDefinition.GamepadButton) != 0,
            _ => false
        };
    }

    private string Signature(InputEvent inputEvent) => inputEvent != null ? _codec.Signature(inputEvent) : "";

    private static InputEvent CopyEvent(InputEvent inputEvent) => inputEvent?.Duplicate() as InputEvent;

    private InputEvent NormalizeEvent(InputEvent inputEvent) =>
        inputEvent != null ? _codec.Decode(_codec.Encode(inputEvent)) : null;

    private static InputEvent[] CopySlots(InputEvent[] source)
    {
        var copy = new InputEvent[source.Length];
        for (int index = 0; index < source.Length; index++)
            copy[index] = CopyEvent(source[index]);
        return copy;
    }

    private static BindingMap CopyMap(BindingMap source)
    {
        var copy = new BindingMap();
        foreach (var pair in source)
            copy.Add(pair.Key, CopySlots(pair.Value));
        return copy;
    }

    private static Array<QuickInputBinding> Flatten(BindingMap source)
    {
        var bindings = new Array<QuickInputBinding>();
        foreach (var pair in source)
        {
            for (int index = 0; index < pair.Value.Length; index++)
                bindings.Add(new QuickInputBinding(pair.Key, index, pair.Value[index]));
        }
        return bindings;
    }

    private bool SnapshotsMatch(Array<QuickInputBinding> left, Array<QuickInputBinding> right)
    {
        if (left == null || right == null || left.Count != right.Count)
            return false;
        for (int index = 0; index < left.Count; index++)
        {
            QuickInputBinding leftBinding = left[index];
            QuickInputBinding rightBinding = right[index];
            if (leftBinding == null || rightBinding == null || leftBinding.Action != rightBinding.Action
                || leftBinding.Slot != rightBinding.Slot || Signature(leftBinding.Event) != Signature(rightBinding.Event))
                return false;
        }
        return true;
    }
}
