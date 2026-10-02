#nullable disable
using System;
using System.Collections.Generic;
using Godot;
using GArray = Godot.Collections.Array;
using GDictionary = Godot.Collections.Dictionary;
using ConflictPolicy = QuickDevKit.QuickInput.QuickInputManager.ConflictPolicy;
using RebindStatus = QuickDevKit.QuickInput.QuickInputRebindResult.RebindStatus;

namespace QuickDevKit.QuickInput;

/// <summary>
/// Dependency-free runtime regression suite. Run runtime_tests.tscn with Godot .NET.
/// Every action and user:// fixture is unique to this run and cleaned in finally blocks.
/// </summary>
public partial class QuickInputRuntimeTests : Node
{
    private readonly string _runId = Guid.NewGuid().ToString("N");
    private readonly List<Node> _ownedNodes = new();
    private readonly HashSet<string> _fixturePaths = new();
    private readonly QuickInputEventCodec _codec = new();
    private StringName _actionA;
    private StringName _actionB;
    private StringName _actionC;
    private StringName _missingAction;
    private int _assertions;
    private int _failures;

    public override void _Ready() => Callable.From(Run).CallDeferred();

    private void Run()
    {
        _actionA = $"__quick_input_csharp_test_{_runId}_a";
        _actionB = $"__quick_input_csharp_test_{_runId}_b";
        _actionC = $"__quick_input_csharp_test_{_runId}_c";
        _missingAction = $"__quick_input_csharp_test_{_runId}_missing";
        try
        {
            RunCase("codec round-trips and malformed inputs", TestCodec);
            RunCase("configuration and saved-data validation", TestConfiguration);
            RunCase("event masks, modifiers and reserved events", TestAllowedEvents);
            RunCase("conflict policies and stale transactions", TestConflicts);
            RunCase("swap destination validation", TestSwapTargetValidation);
            RunCase("transaction and getter isolation", TestClonesAndTransactions);
            RunCase("persistence failure rollback and reset", TestRollbackAndReset);
            RunCase("ConfigFile format and backup recovery", TestPersistence);
            RunCase("invalid ConfigFile saves", TestInvalidFiles);
            RunCase("GDScript codec and save interoperability", TestGdScriptInteroperability);
            RunCase("reusable input capture button", TestCaptureButton);
            RunCase("capture lifecycle and failure recovery", TestCaptureLifecycle);
            RunCase("editor ownership guards", TestPluginGuard);
            RunCase("editor resource serialization", TestResourceSerialization);
        }
        finally
        {
            CleanupNodes();
            foreach (StringName action in Actions())
                if (InputMap.HasAction(action))
                    InputMap.EraseAction(action);
            foreach (string path in _fixturePaths)
                foreach (string suffix in new[] { "", ".previous", ".tmp" })
                {
                    string absolute = ProjectSettings.GlobalizePath(path + suffix);
                    if (Godot.FileAccess.FileExists(absolute))
                        Check(DirAccess.RemoveAbsolute(absolute) == Error.Ok, $"Clean test fixture {path + suffix}");
                }
        }
        GD.Print($"Quick Input C# runtime tests: {_assertions} assertions, {_failures} failures.");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private void RunCase(string name, Action test)
    {
        int initialFailures = _failures;
        try
        {
            SetupActions();
            test();
        }
        catch (Exception exception)
        {
            Check(false, $"{name} threw {exception}");
        }
        finally
        {
            CleanupNodes();
        }
        if (_failures == initialFailures)
            GD.Print($"PASS: {name}");
    }

    private StringName[] Actions() => new[] { _actionA, _actionB, _actionC };

    private void SetupActions()
    {
        var codes = new[] { Key.A, Key.B, Key.C };
        StringName[] actions = Actions();
        for (int index = 0; index < actions.Length; index++)
        {
            if (!InputMap.HasAction(actions[index]))
                InputMap.AddAction(actions[index]);
            InputMap.ActionEraseEvents(actions[index]);
            InputMap.ActionAddEvent(actions[index], KeyEvent(codes[index]));
        }
    }

    private void CleanupNodes()
    {
        for (int index = _ownedNodes.Count - 1; index >= 0; index--)
        {
            Node node = _ownedNodes[index];
            if (!GodotObject.IsInstanceValid(node))
                continue;
            if (node is QuickInputManager manager)
                manager.RestoreDefaultsInInputMap();
            node.Free();
        }
        _ownedNodes.Clear();
    }

    private T Own<T>(T node) where T : Node
    {
        _ownedNodes.Add(node);
        return node;
    }

    private QuickInputSettings Settings()
    {
        var settings = new QuickInputSettings();
        foreach (StringName action in Actions())
            settings.Actions.Add(new QuickInputActionDefinition
            {
                Action = action,
                BindingSlots = 2,
                ConflictGroup = action == _actionC ? "other" : "gameplay",
            });
        settings.ReservedEvents.Add(KeyEvent(Key.Escape));
        return settings;
    }

    private QuickInputManager Manager(QuickInputSettings settings = null, QuickInputStore store = null)
    {
        var manager = Own(new QuickInputManager());
        Equal(Error.Ok, manager.Configure(settings ?? Settings(), store ?? new TestsMemoryStore()), "Configure test manager");
        return manager;
    }

    private static InputEventKey KeyEvent(Key code, bool pressed = false) =>
        new() { PhysicalKeycode = code, Pressed = pressed };

    private static InputEventMouseButton MouseEvent(MouseButton button, bool pressed = false) =>
        new() { ButtonIndex = button, Pressed = pressed };

    private static InputEventJoypadButton JoyEvent(JoyButton button, bool pressed = false) =>
        new() { ButtonIndex = button, Pressed = pressed, Device = 3 };

    private GArray Slots(InputEvent first, InputEvent second) => new()
    {
        first == null ? default(Variant) : _codec.Encode(first),
        second == null ? default(Variant) : _codec.Encode(second),
    };

    private string MapSnapshot()
    {
        var snapshots = new List<string>();
        foreach (StringName action in Actions())
        {
            var events = new List<string>();
            foreach (InputEvent input in InputMap.ActionGetEvents(action))
                events.Add(_codec.Signature(input));
            snapshots.Add($"{action}:{string.Join("|", events)}");
        }
        return string.Join("\n", snapshots);
    }

    private string FixturePath(string label)
    {
        string path = $"user://quick_input_csharp_test_{_runId}_{label}.cfg";
        _fixturePaths.Add(path);
        return path;
    }

    private void TestCodec()
    {
        var key = KeyEvent(Key.Z, true);
        key.Keycode = Key.Y;
        key.Location = KeyLocation.Left;
        key.ShiftPressed = key.CtrlPressed = key.AltPressed = key.MetaPressed = true;
        key.Echo = true;
        var logical = new InputEventKey { Keycode = Key.F1 };
        var mouse = MouseEvent(MouseButton.Xbutton1, true);
        mouse.ShiftPressed = mouse.CtrlPressed = mouse.AltPressed = mouse.MetaPressed = true;
        var joy = JoyEvent(JoyButton.A, true);
        foreach (InputEvent input in new InputEvent[] { key, logical, mouse, joy, MouseEvent((MouseButton)9), JoyEvent((JoyButton)127) })
        {
            GDictionary encoded = _codec.Encode(input);
            Check(encoded.Count > 0, $"{input.GetClass()} serializes");
            InputEvent decoded = _codec.Decode(encoded);
            Check(decoded != null, $"{input.GetClass()} decodes");
            Equal(_codec.Signature(input), _codec.Signature(decoded), "Codec round-trip preserves canonical signature");
            Check(decoded != input && !decoded.IsPressed(), "Codec creates a fresh, unpressed binding");
        }
        Equal(-1, _codec.Decode(_codec.Encode(joy)).Device, "Gamepad binding is device-independent");
        Check(!((InputEventKey)_codec.Decode(_codec.Encode(key))).Echo, "Echo is capture state, not persisted state");
        var alternateLogical = (InputEventKey)key.Duplicate();
        alternateLogical.Keycode = Key.X;
        Equal(_codec.Signature(key), _codec.Signature(alternateLogical), "Physical key signature ignores keyboard-layout keycode");
        alternateLogical.Location = KeyLocation.Right;
        Check(_codec.Signature(key) != _codec.Signature(alternateLogical), "Key location distinguishes bindings");
        alternateLogical = (InputEventKey)key.Duplicate();
        alternateLogical.CtrlPressed = false;
        Check(_codec.Signature(key) != _codec.Signature(alternateLogical), "Modifiers distinguish bindings");
        var secondJoy = JoyEvent(JoyButton.A);
        secondJoy.Device = 7;
        Equal(_codec.Signature(joy), _codec.Signature(secondJoy), "Gamepad signature ignores captured controller");
        foreach (InputEvent unsupported in new InputEvent[]
        {
            null, new InputEventKey(), new InputEventMouseMotion(), new InputEventJoypadMotion(),
            new InputEventAction(), MouseEvent((MouseButton)0), MouseEvent((MouseButton)10),
            JoyEvent((JoyButton)(-1)), JoyEvent((JoyButton)128),
            KeyEvent(Key.Unknown), KeyEvent((Key)((long)KeyModifierMask.CodeMask + 1)),
            new InputEventKey { PhysicalKeycode = Key.A, Keycode = Key.Unknown },
            new InputEventKey { PhysicalKeycode = Key.A, Location = (KeyLocation)3 },
        })
        {
            Equal(0, _codec.Encode(unsupported).Count, "Unsupported input does not serialize");
            Equal("", _codec.Signature(unsupported), "Unsupported input has no signature");
        }
        Check(_codec.Decode(null) == null, "Null dictionary is invalid");
        Check(_codec.Decode(new GDictionary()) == null, "Missing event type is invalid");
        Check(_codec.Decode(new GDictionary { ["type"] = "axis" }) == null, "Unknown event type is invalid");
        Check(_codec.Decode(new GDictionary { ["type"] = 1 }) == null, "Non-string event type is invalid");
        GDictionary encodedKey = _codec.Encode(key);
        foreach (string field in new[] { "physical", "keycode", "location", "shift", "ctrl", "alt", "meta" })
        {
            GDictionary missing = encodedKey.Duplicate(true);
            missing.Remove(field);
            Check(_codec.Decode(missing) == null, $"Missing {field} is invalid");
            RejectValue(encodedKey, field, "bad", $"String {field} is invalid");
        }
        foreach (string field in new[] { "physical", "keycode", "location" })
        {
            RejectValue(encodedKey, field, 1.0, $"Float {field} is invalid");
            RejectValue(encodedKey, field, true, $"Boolean {field} is invalid");
        }
        foreach (string field in new[] { "shift", "ctrl", "alt", "meta" })
            RejectValue(encodedKey, field, 1, $"Integer modifier {field} is invalid");
        foreach (long invalidCode in new[] { -1L, (long)Key.Unknown, (long)KeyModifierMask.CodeMask + 1 })
        {
            RejectValue(encodedKey, "physical", invalidCode, "Invalid physical keycode rejected");
            RejectValue(encodedKey, "keycode", invalidCode, "Invalid logical keycode rejected");
        }
        RejectValue(encodedKey, "location", -1, "Negative key location rejected");
        RejectValue(encodedKey, "location", 3, "Out-of-range key location rejected");
        var noKey = encodedKey.Duplicate(true);
        noKey["physical"] = 0;
        noKey["keycode"] = 0;
        Check(_codec.Decode(noKey) == null, "Both keycodes empty is invalid");
        foreach (int index in new[] { 0, 10 })
            RejectValue(_codec.Encode(mouse), "button", index, "Out-of-range mouse button rejected");
        foreach (int index in new[] { -1, 128 })
            RejectValue(_codec.Encode(joy), "button", index, "Out-of-range joypad button rejected");
        RejectValue(_codec.Encode(mouse), "button", "1", "String mouse button rejected");
        RejectValue(_codec.Encode(joy), "button", 0.0, "Float joypad button rejected");
    }

    private void RejectValue(GDictionary template, string field, Variant value, string label)
    {
        var copy = template.Duplicate(true);
        copy[field] = value;
        Check(_codec.Decode(copy) == null, label);
    }

    private void TestConfiguration()
    {
        var unconfigured = Own(new QuickInputManager());
        Equal(Error.Unconfigured, unconfigured.ResetAll(), "Unconfigured reset rejected");
        Equal(Error.InvalidParameter, unconfigured.Configure(null), "Null settings rejected");
        Equal(Error.InvalidParameter, unconfigured.Configure(new QuickInputSettings()), "Empty settings rejected");
        Check(unconfigured.GetBinding(_actionA, 0) == null, "Unconfigured getter is empty");
        Equal("Unbound", unconfigured.GetBindingText(_actionA, 0), "Unconfigured text is safe");
        Equal(RebindStatus.Invalid, unconfigured.PreviewRebind(_actionA, 0, KeyEvent(Key.F1)).Status, "Unconfigured preview rejected");
        Equal(Error.InvalidParameter, unconfigured.ApplyRebind(null), "Null transaction rejected");
        ExpectConfigurationFailure(s => s.Actions.Add(s.Actions[0]), Error.InvalidData, "Duplicate action");
        ExpectConfigurationFailure(s => s.Actions.Add(null), Error.InvalidData, "Null definition");
        ExpectConfigurationFailure(s => s.Actions[0].Action = "", Error.InvalidData, "Empty action");
        ExpectConfigurationFailure(s => s.Actions[0].Action = _missingAction, Error.InvalidData, "Missing InputMap action");
        ExpectConfigurationFailure(s => s.Actions[0].BindingSlots = 0, Error.InvalidData, "No binding slots");
        ExpectConfigurationFailure(s => s.Actions[0].AllowedEventTypes = 0, Error.InvalidData, "Empty event mask");
        ExpectConfigurationFailure(s => s.Actions[0].AllowedEventTypes = 8, Error.InvalidData, "Unknown event-mask bit");
        ExpectConfigurationFailure(s => s.Actions[0].AllowedEventTypes = QuickInputActionDefinition.Mouse,
            Error.InvalidData, "Default incompatible with event mask");
        ExpectConfigurationFailure(s => s.ReservedEvents.Add(new InputEventJoypadMotion()),
            Error.InvalidData, "Unsupported reserved event");
        ExpectConfigurationFailure(s => s.ReservedEvents.Add(null), Error.InvalidData, "Null reserved event");
        InputMap.ActionAddEvent(_actionA, KeyEvent(Key.D));
        ExpectConfigurationFailure(s => s.Actions[0].BindingSlots = 1, Error.InvalidData, "More defaults than slots");
        SetupActions();
        InputMap.ActionAddEvent(_actionA, new InputEventJoypadMotion());
        ExpectConfigurationFailure(null, Error.InvalidData, "Unsupported default event");
        SetupActions();
        var modifiedDefault = KeyEvent(Key.A);
        modifiedDefault.CtrlPressed = true;
        InputMap.ActionEraseEvents(_actionA);
        InputMap.ActionAddEvent(_actionA, modifiedDefault);
        ExpectConfigurationFailure(null, Error.InvalidData, "Modified default forbidden by settings");
        SetupActions();
        var noAdapter = Own(new QuickInputManager { InputMapAdapter = null });
        Equal(Error.InvalidParameter, noAdapter.Configure(Settings(), new TestsMemoryStore()), "Missing adapter rejected");
        ExpectConfigurationFailure(null, Error.FileCorrupt, "Store load failure",
            new TestsMemoryStore { LoadFailure = Error.FileCorrupt });
        ExpectSavedFailure(new GDictionary { [17] = Slots(null, null) }, "Non-string saved action");
        ExpectSavedFailure(new GDictionary { [_actionA.ToString()] = 5 }, "Non-array slots");
        ExpectSavedFailure(new GDictionary { [_actionA.ToString()] = new GArray() }, "Wrong saved slot count");
        ExpectSavedFailure(new GDictionary { [_actionA.ToString()] = new GArray { "bad", default(Variant) } }, "Non-dictionary saved event");
        ExpectSavedFailure(new GDictionary { [_actionA.ToString()] = new GArray { new GDictionary { ["type"] = "bad" }, default(Variant) } }, "Invalid encoded event");
        ExpectSavedFailure(new GDictionary { [_actionA.ToString()] = Slots(KeyEvent(Key.Escape), null) }, "Reserved saved event");
        var modified = KeyEvent(Key.F1);
        modified.AltPressed = true;
        ExpectSavedFailure(new GDictionary { [_actionA.ToString()] = Slots(modified, null) }, "Forbidden saved modifier");
        ExpectConfigurationFailure(s => s.Actions[0].AllowedEventTypes = QuickInputActionDefinition.Keyboard,
            Error.InvalidData, "Forbidden saved event type", new TestsMemoryStore
            {
                Bindings = new GDictionary { [_actionA.ToString()] = Slots(MouseEvent(MouseButton.Left), null) },
            });
        // The valid earlier action must not be partially applied when a later action fails.
        ExpectSavedFailure(new GDictionary
        {
            [_actionA.ToString()] = Slots(KeyEvent(Key.F7), null),
            [_actionB.ToString()] = new GArray { "corrupt", default(Variant) },
        }, "Late saved-data error remains atomic");
        var store = new TestsMemoryStore
        {
            Bindings = new GDictionary
            {
                [_missingAction.ToString()] = "old action entries are ignored",
                [_actionA] = Slots(null, KeyEvent(Key.F2)),
            },
        };
        var manager = Manager(store: store);
        Check(manager.IsConfigured(), "Valid configuration marked ready");
        Equal(Error.AlreadyInUse, manager.Configure(Settings(), store), "Second configuration rejected");
        Check(manager.GetBinding(_actionA, 0) == null, "Saved empty slot restored");
        AssertKey(manager, _actionA, 1, Key.F2, "StringName saved action restored");
        Equal(1, InputMap.ActionGetEvents(_actionA).Count, "Unbound slots omitted from InputMap");
        Equal(RebindStatus.Invalid, manager.PreviewRebind(_missingAction, 0, KeyEvent(Key.F1)).Status, "Unknown action rejected");
        Equal(Error.InvalidParameter, manager.ClearBinding(_actionA, -1), "Negative slot rejected");
        Equal(Error.InvalidParameter, manager.Rebind(_actionA, 2, KeyEvent(Key.F1)), "Out-of-range slot rejected");
        Equal(Error.InvalidParameter, manager.ResetAction(_missingAction), "Unknown reset action rejected");
        manager.RestoreDefaultsInInputMap();
        Check(!manager.IsConfigured() && manager.Settings == null && manager.Store == null, "Restore releases configured state");
        Equal(Key.A, ((InputEventKey)InputMap.ActionGetEvents(_actionA)[0]).PhysicalKeycode, "Restore recovers original InputMap");
        Equal(Error.Ok, manager.Configure(Settings(), new TestsMemoryStore()), "Manager can configure after restoring defaults");
        using var baseStore = new QuickInputStore();
        Equal(0, baseStore.LoadBindings().Count, "Base store load is empty");
        Equal(Error.Unavailable, baseStore.LoadError, "Base store reports unavailable load");
        Equal(Error.Unavailable, baseStore.SaveBindings(new GDictionary()), "Base store save is unavailable");
    }

    private void ExpectConfigurationFailure(Action<QuickInputSettings> mutate, Error expected, string label, TestsMemoryStore store = null)
    {
        QuickInputSettings settings = Settings();
        mutate?.Invoke(settings);
        var manager = Own(new QuickInputManager());
        string before = MapSnapshot();
        Equal(expected, manager.Configure(settings, store ?? new TestsMemoryStore()), label);
        Check(!manager.IsConfigured(), $"{label}: remains unconfigured");
        Equal(before, MapSnapshot(), $"{label}: InputMap unchanged");
    }

    private void ExpectSavedFailure(GDictionary bindings, string label) =>
        ExpectConfigurationFailure(null, Error.InvalidData, label, new TestsMemoryStore { Bindings = bindings });

    private void TestAllowedEvents()
    {
        QuickInputSettings settings = Settings();
        settings.Actions[0].AllowedEventTypes = QuickInputActionDefinition.Keyboard;
        var manager = Manager(settings);
        Equal(Error.InvalidParameter, manager.Rebind(_actionA, 1, MouseEvent(MouseButton.Left)), "Keyboard-only action rejects mouse");
        Equal(Error.InvalidParameter, manager.Rebind(_actionA, 1, JoyEvent(JoyButton.A)), "Keyboard-only action rejects gamepad");
        Equal(Error.InvalidParameter, manager.Rebind(_actionA, 1, new InputEventJoypadMotion()), "Axes unsupported");
        Equal(Error.InvalidParameter, manager.Rebind(_actionA, 1, KeyEvent(Key.Escape)), "Reserved key rejected");
        string originalMap = MapSnapshot();
        foreach (InputEvent malformed in new InputEvent[]
        {
            KeyEvent(Key.Unknown), KeyEvent((Key)((long)KeyModifierMask.CodeMask + 1)),
            new InputEventKey { PhysicalKeycode = Key.A, Location = (KeyLocation)3 },
        })
        {
            var invalid = manager.PreviewRebind(_actionA, 0, malformed);
            Equal(RebindStatus.Invalid, invalid.Status, "Malformed key preview rejected");
            Check(invalid.Transaction == null, "Malformed key has no transaction");
            Equal(Error.InvalidParameter, manager.Rebind(_actionA, 0, malformed), "Malformed key cannot silently clear occupied slot");
            Equal(originalMap, MapSnapshot(), "Malformed key leaves InputMap unchanged");
        }
        Equal(0, ((TestsMemoryStore)manager.Store).SaveCalls, "Rejected inputs never reach persistence");
        foreach (string modifier in new[] { "shift", "ctrl", "alt", "meta" })
        {
            var modified = KeyEvent(Key.F3);
            modified.ShiftPressed = modifier == "shift";
            modified.CtrlPressed = modifier == "ctrl";
            modified.AltPressed = modifier == "alt";
            modified.MetaPressed = modifier == "meta";
            Equal(Error.InvalidParameter, manager.Rebind(_actionA, 1, modified), $"Forbidden {modifier} combination rejected");
        }
        var modifiedMouse = MouseEvent(MouseButton.Right);
        modifiedMouse.CtrlPressed = true;
        Equal(Error.InvalidParameter, manager.Rebind(_actionB, 1, modifiedMouse), "Mouse modifiers obey settings");
        settings.AllowModifierCombinations = true;
        var combination = KeyEvent(Key.F3);
        combination.CtrlPressed = combination.ShiftPressed = true;
        Equal(Error.Ok, manager.Rebind(_actionA, 1, combination), "Enabled key modifiers accepted");
        Equal(Error.Ok, manager.Rebind(_actionB, 1, modifiedMouse), "Enabled mouse modifiers accepted");
        Check(((InputEventKey)manager.GetBinding(_actionA, 1)).CtrlPressed, "Modifiers preserved by commit");
        manager.RestoreDefaultsInInputMap();
        InputMap.ActionEraseEvents(_actionA);
        settings = Settings();
        settings.Actions[0].AllowedEventTypes = QuickInputActionDefinition.Mouse;
        manager = Manager(settings);
        Equal(Error.Ok, manager.Rebind(_actionA, 0, MouseEvent(MouseButton.WheelUp)), "Mouse-only action accepts wheel button");
        Equal(Error.InvalidParameter, manager.Rebind(_actionA, 1, KeyEvent(Key.F4)), "Mouse-only action rejects key");
        manager.RestoreDefaultsInInputMap();
        settings = Settings();
        settings.Actions[0].AllowedEventTypes = QuickInputActionDefinition.GamepadButton;
        manager = Manager(settings);
        Equal(Error.Ok, manager.Rebind(_actionA, 0, JoyEvent(JoyButton.A)), "Gamepad-only action accepts digital button");
        Equal(-1, manager.GetBinding(_actionA, 0).Device, "Manager normalizes controller device");
        Equal(Error.InvalidParameter, manager.Rebind(_actionA, 1, MouseEvent(MouseButton.Left)), "Gamepad-only action rejects mouse");
    }

    private void TestConflicts()
    {
        var store = new TestsMemoryStore();
        var manager = Manager(store: store);
        var preview = manager.PreviewRebind(_actionA, 0, KeyEvent(Key.B));
        Equal(RebindStatus.Conflict, preview.Status, "Same-group conflict detected");
        Equal(1, preview.Conflicts.Count, "Single occupied slot reported");
        Equal(_actionB, preview.Conflicts[0].Action, "Conflict names action");
        Equal(0, preview.Conflicts[0].Slot, "Conflict names slot");
        string before = MapSnapshot();
        Equal(Error.AlreadyInUse, manager.ApplyRebind(preview.Transaction), "Default policy rejects conflict");
        Equal(before, MapSnapshot(), "Rejected conflict changes no bindings");
        Equal(0, store.SaveCalls, "Rejected conflict does not save");
        Equal(Error.Ok, manager.ApplyRebind(preview.Transaction, ConflictPolicy.Swap), "Swap succeeds");
        AssertKey(manager, _actionA, 0, Key.B, "Swap assigns requested key");
        AssertKey(manager, _actionB, 0, Key.A, "Swap relocates displaced key");
        Equal(Key.B, ((InputEventKey)InputMap.ActionGetEvents(_actionA)[0]).PhysicalKeycode, "Swap updates live InputMap");
        Equal(Error.Busy, manager.ApplyRebind(preview.Transaction, ConflictPolicy.Allow), "Stale transaction rejected");
        Equal(0, manager.PreviewRebind(_actionA, 1, KeyEvent(Key.C)).Conflicts.Count, "Other conflict group independent");
        Equal(RebindStatus.Ready, manager.PreviewRebind(_actionA, 0, KeyEvent(Key.B)).Status, "Same slot does not conflict with itself");
        Equal(Error.Ok, manager.ResetAll(), "Reset after swap");
        Equal(Error.Ok, manager.Rebind(_actionA, 1, KeyEvent(Key.B), ConflictPolicy.Allow), "Allow preserves duplicate key");
        AssertKey(manager, _actionB, 0, Key.B, "Allow keeps occupied binding");
        preview = manager.PreviewRebind(_actionA, 0, KeyEvent(Key.B));
        Equal(2, preview.Conflicts.Count, "Multiple conflicts, including same-action slot, reported");
        before = MapSnapshot();
        Equal(Error.InvalidData, manager.ApplyRebind(preview.Transaction, ConflictPolicy.Swap), "Ambiguous multi-conflict swap rejected");
        Equal(before, MapSnapshot(), "Ambiguous swap is atomic");
        Equal(Error.Ok, manager.ApplyRebind(preview.Transaction, ConflictPolicy.Replace), "Replace clears all conflicts");
        AssertKey(manager, _actionA, 0, Key.B, "Replace assigns key");
        Check(manager.GetBinding(_actionA, 1) == null && manager.GetBinding(_actionB, 0) == null,
            "Replace clears both occupied slots");
        Equal(Error.Ok, manager.ResetAll(), "Reset after replace");
        Equal(Error.Ok, manager.Rebind(_actionA, 1, KeyEvent(Key.B), ConflictPolicy.Swap), "Swap from empty slot succeeds");
        Check(manager.GetBinding(_actionB, 0) == null, "Swap transfers unbound state to displaced slot");
        Equal(Error.Ok, manager.ResetAll(), "Reset after empty-slot swap");
        var mouse = MouseEvent(MouseButton.Right);
        Equal(Error.Ok, manager.Rebind(_actionA, 1, mouse), "Mouse secondary binding succeeds");
        preview = manager.PreviewRebind(_actionB, 1, mouse);
        Equal(RebindStatus.Conflict, preview.Status, "Mouse conflicts detected");
        Equal(Error.Ok, manager.ApplyRebind(preview.Transaction, ConflictPolicy.Replace), "Mouse conflict replacement succeeds");
        Check(manager.GetBinding(_actionA, 1) == null && manager.GetBinding(_actionB, 1) is InputEventMouseButton,
            "Mouse replacement transfers ownership");
        Equal(Error.Ok, manager.Rebind(_actionA, 1, JoyEvent(JoyButton.A)), "Gamepad binding succeeds");
        Equal(RebindStatus.Conflict, manager.PreviewRebind(_actionB, 0, JoyEvent(JoyButton.A)).Status,
            "Gamepad conflict ignores controller device");
    }

    private void TestSwapTargetValidation()
    {
        var settings = Settings();
        settings.Actions[1].AllowedEventTypes = QuickInputActionDefinition.Keyboard;
        var store = new TestsMemoryStore();
        var manager = Manager(settings, store);
        Equal(Error.Ok, manager.Rebind(_actionA, 0, MouseEvent(MouseButton.Left)), "Prepare displaced mouse binding");
        var preview = manager.PreviewRebind(_actionA, 0, KeyEvent(Key.B));
        Equal(RebindStatus.Conflict, preview.Status, "Prepare mouse-to-keyboard swap conflict");
        string before = MapSnapshot();
        int saves = store.SaveCalls;
        int signals = 0;
        manager.BindingChanged += (_, _) => signals++;
        Equal(Error.InvalidParameter, manager.ApplyRebind(preview.Transaction, ConflictPolicy.Swap),
            "Swap cannot move mouse into keyboard-only action");
        Equal(before, MapSnapshot(), "Rejected swap leaves both bindings unchanged");
        Equal(saves, store.SaveCalls, "Rejected swap never writes persistence");
        Equal(0, signals, "Rejected swap emits no binding change");
        settings.Actions[1].AllowedEventTypes = 7;
        settings.ReservedEvents.Add(MouseEvent(MouseButton.Left));
        Equal(Error.InvalidParameter, manager.ApplyRebind(preview.Transaction, ConflictPolicy.Swap),
            "Swap cannot transfer a now-reserved displaced event");
        Equal(before, MapSnapshot(), "Reserved displaced event rejection is atomic");
        settings.ReservedEvents.RemoveAt(settings.ReservedEvents.Count - 1);
        settings.AllowModifierCombinations = true;
        var modified = MouseEvent(MouseButton.Left);
        modified.CtrlPressed = true;
        Equal(Error.Ok, manager.Rebind(_actionA, 0, modified), "Prepare displaced modifier combination");
        settings.AllowModifierCombinations = false;
        preview = manager.PreviewRebind(_actionA, 0, KeyEvent(Key.B));
        before = MapSnapshot();
        saves = store.SaveCalls;
        Equal(Error.InvalidParameter, manager.ApplyRebind(preview.Transaction, ConflictPolicy.Swap),
            "Swap validates current modifier policy at destination");
        Equal(before, MapSnapshot(), "Disallowed modifier swap is atomic");
        Equal(saves, store.SaveCalls, "Disallowed modifier swap never saves");
    }

    private void TestClonesAndTransactions()
    {
        var manager = Manager();
        var binding = (InputEventKey)manager.GetBinding(_actionA, 0);
        binding.PhysicalKeycode = Key.Z;
        AssertKey(manager, _actionA, 0, Key.A, "Binding getter returns an independent event");
        Equal(Key.A, ((InputEventKey)InputMap.ActionGetEvents(_actionA)[0]).PhysicalKeycode, "Getter mutation cannot affect InputMap");
        var candidate = KeyEvent(Key.F4);
        var preview = manager.PreviewRebind(_actionA, 1, candidate);
        candidate.PhysicalKeycode = Key.F5;
        Equal(Key.F4, ((InputEventKey)preview.Transaction.After[0].Event).PhysicalKeycode, "Preview clones requested input");
        ((InputEventKey)preview.Transaction.Before[0].Event).PhysicalKeycode = Key.F6;
        AssertKey(manager, _actionA, 0, Key.A, "Snapshot mutation cannot change current binding");
        Equal(Error.Busy, manager.ApplyRebind(preview.Transaction), "Tampered snapshot rejected");
        preview = manager.PreviewRebind(_actionA, 1, KeyEvent(Key.B));
        ((InputEventKey)preview.Conflicts[0].Event).PhysicalKeycode = Key.F7;
        AssertKey(manager, _actionB, 0, Key.B, "Conflict details clone occupied events");
        preview = manager.PreviewRebind(_actionA, 1, KeyEvent(Key.F4));
        Equal(Error.InvalidParameter, manager.ApplyRebind(preview.Transaction, (ConflictPolicy)99), "Unknown conflict policy rejected");
        Equal(Error.InvalidParameter, manager.ApplyRebind(new QuickInputBindingTransaction()), "Empty transaction rejected");
        preview.Transaction.After.Add(new QuickInputBinding(_actionB, 1, KeyEvent(Key.F5)));
        Equal(Error.InvalidParameter, manager.ApplyRebind(preview.Transaction), "Multiple requested writes rejected");
        preview = manager.PreviewRebind(_actionA, 1, KeyEvent(Key.F4));
        preview.Transaction.Action = _actionB;
        Equal(Error.InvalidParameter, manager.ApplyRebind(preview.Transaction), "Mismatched transaction action rejected");
        preview = manager.PreviewRebind(_actionA, 1, KeyEvent(Key.F4));
        preview.Transaction.Slot = 0;
        Equal(Error.InvalidParameter, manager.ApplyRebind(preview.Transaction), "Mismatched transaction slot rejected");
        preview = manager.PreviewRebind(_actionA, 1, KeyEvent(Key.F4));
        preview.Transaction.After[0].Event = KeyEvent(Key.Escape);
        Equal(Error.InvalidParameter, manager.ApplyRebind(preview.Transaction), "Edited request is validated again at apply");
        var original = KeyEvent(Key.F8);
        Equal(Error.Ok, manager.Rebind(_actionA, 1, original), "New input committed");
        original.PhysicalKeycode = Key.F9;
        AssertKey(manager, _actionA, 1, Key.F8, "Commit clones caller-owned input");
        var adapter = new QuickInputMapAdapter();
        var adapterEvents = adapter.GetEvents(_actionA);
        ((InputEventKey)adapterEvents[0]).PhysicalKeycode = Key.F10;
        Equal(Key.A, ((InputEventKey)InputMap.ActionGetEvents(_actionA)[0]).PhysicalKeycode, "Adapter getter clones InputMap events");
    }

    private void TestRollbackAndReset()
    {
        var store = new TestsMemoryStore();
        var manager = Manager(store: store);
        var changed = new List<string>();
        var failures = new List<Error>();
        manager.BindingChanged += (action, slot) => changed.Add($"{action}:{slot}");
        manager.BindingsSaveFailed += error => failures.Add(error);
        Equal(Error.Ok, manager.Rebind(_actionA, 0, KeyEvent(Key.A)), "No-op rebind succeeds");
        Equal(0, store.SaveCalls, "No-op does not save");
        Equal(0, changed.Count, "No-op does not emit changed signal");
        Equal(Error.Ok, manager.Rebind(_actionA, 1, MouseEvent(MouseButton.Right)), "Secondary slot saved");
        Equal(1, changed.Count, "Changed slot emits once");
        Check(store.Bindings.ContainsKey(_actionA.ToString()) && !store.Bindings.ContainsKey(_actionB.ToString()),
            "Store includes only overridden actions");
        GArray savedSlots = store.Bindings[_actionA.ToString()].AsGodotArray();
        Equal(2, savedSlots.Count, "Overrides retain all configured slots");
        Equal("key", savedSlots[0].AsGodotDictionary()["type"].AsString(), "Override retains original first slot");
        var saved = store.Bindings.Duplicate(true);
        var map = MapSnapshot();
        int previousSignals = changed.Count;
        store.Failure = Error.CantCreate;
        Equal(Error.CantCreate, manager.Rebind(_actionA, 1, JoyEvent(JoyButton.A)), "Write failure propagated");
        Equal(map, MapSnapshot(), "Write failure leaves exact InputMap unchanged");
        Check(manager.GetBinding(_actionA, 1) is InputEventMouseButton, "Write failure leaves current state unchanged");
        Equal(previousSignals, changed.Count, "Write failure emits no changed signal");
        Check(saved.RecursiveEqual(store.Bindings), "Write failure leaves saved overrides unchanged");
        Equal(1, failures.Count, "Write failure emits save failure signal once");
        Equal(Error.CantCreate, failures[0], "Save failure signal includes error");
        Equal(Error.CantCreate, manager.ResetAction(_actionA), "Reset action failure propagated");
        Equal(map, MapSnapshot(), "Failed action reset is atomic");
        Equal(Error.CantCreate, manager.ResetAll(), "Reset all failure propagated");
        Equal(map, MapSnapshot(), "Failed full reset is atomic");
        var replace = manager.PreviewRebind(_actionB, 0, MouseEvent(MouseButton.Right));
        Equal(Error.CantCreate, manager.ApplyRebind(replace.Transaction, ConflictPolicy.Replace), "Replace write failure propagated");
        Equal(map, MapSnapshot(), "Failed replace preserves both actions");
        store.Failure = Error.Ok;
        Equal(Error.Ok, manager.ApplyRebind(replace.Transaction, ConflictPolicy.Replace), "Same transaction can retry after failed persistence");
        Check(manager.GetBinding(_actionA, 1) == null, "Successful retry clears conflict");
        Equal(Error.Ok, manager.ClearBinding(_actionB, 0), "Clear binding succeeds");
        Check(manager.GetBinding(_actionB, 0) == null, "Cleared slot is empty");
        Equal("Unbound", manager.GetBindingText(_actionB, 0), "Cleared slot has unbound label");
        Equal(Variant.Type.Nil, store.Bindings[_actionB.ToString()].AsGodotArray()[0].VariantType, "Cleared slot persists as null");
        Equal(Error.Ok, manager.ResetAction(_actionB), "Reset action succeeds");
        AssertKey(manager, _actionB, 0, Key.B, "Reset action restores original key");
        Equal(Error.Ok, manager.Rebind(_actionA, 1, KeyEvent(Key.F11)), "Override before full reset");
        Equal(Error.Ok, manager.ResetAll(), "Reset all succeeds");
        Equal(0, store.Bindings.Count, "Reset all removes overrides");
        AssertKey(manager, _actionA, 0, Key.A, "Reset all restores first action");
        Check(manager.GetBinding(_actionA, 1) == null, "Reset all restores empty secondary slot");
    }

    private void TestPersistence()
    {
        string path = FixturePath("persistence");
        using var store = new QuickInputConfigFileStore(path);
        Equal(0, store.LoadBindings().Count, "Missing save starts empty");
        Equal(Error.Ok, store.LoadError, "Missing save is not an error");
        var first = new GDictionary { [_actionA.ToString()] = Slots(KeyEvent(Key.F7), null) };
        var second = new GDictionary { [_actionA.ToString()] = Slots(KeyEvent(Key.A), MouseEvent(MouseButton.Left)) };
        Equal(Error.Ok, store.SaveBindings(first), "First ConfigFile save succeeds");
        using (var config = new ConfigFile())
        {
            Equal(Error.Ok, config.Load(path), "Saved ConfigFile opens directly");
            Equal(Variant.Type.Int, config.GetValue("meta", "version").VariantType, "Schema version is an integer");
            Equal(1L, config.GetValue("meta", "version").AsInt64(), "Shared schema version is 1");
            Check(config.GetValue("input", "bindings").AsGodotDictionary().RecursiveEqual(first), "Shared input/bindings payload exact");
        }
        GDictionary loaded = store.LoadBindings();
        Check(loaded.RecursiveEqual(first), "File payload round-trips");
        loaded[_actionA.ToString()].AsGodotArray()[0].AsGodotDictionary()["physical"] = (long)Key.F12;
        Check(store.LoadBindings().RecursiveEqual(first), "Loaded payload mutation cannot alter saved data");
        Equal(Error.Ok, store.SaveBindings(second), "Atomic replacement save succeeds");
        Check(!Godot.FileAccess.FileExists(path + ".tmp"), "Successful save consumes temporary file");
        Check(Godot.FileAccess.FileExists(path + ".previous"), "Atomic replacement retains previous save");
        Check(store.LoadBindings().RecursiveEqual(second), "Newest save loaded");
        var manager = Manager(store: store);
        Check(manager.GetBinding(_actionA, 1) is InputEventMouseButton, "Manager restores file event");
        manager.RestoreDefaultsInInputMap();
        manager = Manager(store: new QuickInputConfigFileStore(path));
        Check(manager.GetBinding(_actionA, 1) is InputEventMouseButton, "Fresh store restores persisted event");
        manager.RestoreDefaultsInInputMap();
        Equal(Error.Ok, DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path)), "Simulate interruption after old save renamed");
        Check(store.LoadBindings().RecursiveEqual(first), "Missing primary recovers complete previous payload");
        Equal(Error.Ok, store.LoadError, "Backup recovery succeeds");
        Check(Godot.FileAccess.FileExists(path) && !Godot.FileAccess.FileExists(path + ".previous"), "Recovery promotes backup to primary");
        manager = Manager(store: new QuickInputConfigFileStore(path));
        AssertKey(manager, _actionA, 0, Key.F7, "Manager consumes recovered backup");
        manager.RestoreDefaultsInInputMap();
        Equal(Error.Ok, store.SaveBindings(second), "Saving after recovery succeeds");
        Equal(Error.Ok, store.SaveBindings(first), "Existing backup is safely replaced");
        using var backup = new ConfigFile();
        Equal(Error.Ok, backup.Load(path + ".previous"), "Replaced backup remains valid");
        Check(backup.GetValue("input", "bindings").AsGodotDictionary().RecursiveEqual(second), "Backup contains immediately previous payload");
        Equal(Error.InvalidParameter, store.SaveBindings(null), "Null save payload rejected");
    }

    private void TestInvalidFiles()
    {
        string path = FixturePath("invalid");
        using var store = new QuickInputConfigFileStore(path);
        WriteConfig(path, 2, new GDictionary());
        Equal(0, store.LoadBindings().Count, "Unknown schema returns no bindings");
        Equal(Error.FileUnrecognized, store.LoadError, "Unknown schema version rejected");
        WriteConfig(path, "1", new GDictionary());
        store.LoadBindings();
        Equal(Error.FileUnrecognized, store.LoadError, "Non-integer schema version rejected");
        WriteConfig(path, 1, new GArray());
        store.LoadBindings();
        Equal(Error.InvalidData, store.LoadError, "Non-dictionary root payload rejected");
        var corrupt = new GDictionary
        {
            [_actionA.ToString()] = new GArray { new GDictionary { ["type"] = "key", ["physical"] = "bad" }, default(Variant) },
        };
        WriteConfig(path, 1, corrupt);
        var manager = Own(new QuickInputManager());
        string snapshot = MapSnapshot();
        Equal(Error.InvalidData, manager.Configure(Settings(), store), "Malformed encoded save rejected by manager");
        Check(!manager.IsConfigured(), "Malformed save does not configure manager");
        Equal(snapshot, MapSnapshot(), "Malformed file leaves InputMap unchanged");
        // A corrupt primary must not be silently replaced by an older backup.
        using (var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write))
        {
            Check(file != null, "Corrupt fixture opens");
            file?.StoreString("[meta\nversion = definitely-not-a-config-file\n");
        }
        using (var validBackup = new QuickInputConfigFileStore(path + ".previous"))
            Equal(Error.Ok, validBackup.SaveBindings(new GDictionary()), "Prepare valid backup beside corrupt primary");
        // ConfigFile logs parser diagnostics even when failure is the expected result.
        // Suppress only this deliberately corrupt read, and always restore engine diagnostics.
        bool printErrors = Engine.PrintErrorMessages;
        try
        {
            Engine.PrintErrorMessages = false;
            store.LoadBindings();
        }
        finally
        {
            Engine.PrintErrorMessages = printErrors;
        }
        Check(store.LoadError != Error.Ok, "Corrupt primary is reported instead of hiding data loss");
        using (var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read))
            Check(file != null && file.GetAsText().StartsWith("[meta\n", StringComparison.Ordinal), "Corrupt primary preserved for diagnosis");
        string absentDirectory = FixturePath("absent_directory") + "/bindings.cfg";
        using var unwritable = new QuickInputConfigFileStore(absentDirectory);
        Error unavailableError;
        printErrors = Engine.PrintErrorMessages;
        try
        {
            Engine.PrintErrorMessages = false;
            unavailableError = unwritable.SaveBindings(new GDictionary());
        }
        finally
        {
            Engine.PrintErrorMessages = printErrors;
        }
        Check(unavailableError != Error.Ok, "Unavailable destination reports save error");
    }

    private void WriteConfig(string path, Variant version, Variant bindings)
    {
        using var config = new ConfigFile();
        config.SetValue("meta", "version", version);
        config.SetValue("input", "bindings", bindings);
        Equal(Error.Ok, config.Save(path), "Write test ConfigFile fixture");
    }

    private void TestGdScriptInteroperability()
    {
        const string codecPath = "res://addons/quick_input/gds/runtime/serialization/quick_input_event_codec.gd";
        const string storePath = "res://addons/quick_input/gds/runtime/storage/quick_input_config_file_store.gd";
        // This suite also works when only the independently installable C# package is copied.
        if (!ResourceLoader.Exists(codecPath) || !ResourceLoader.Exists(storePath))
        {
            GD.Print("SKIP: GDScript interoperability (GDScript package is not installed).");
            return;
        }
        GDScript codecScript = GD.Load<GDScript>(codecPath);
        GDScript storeScript = GD.Load<GDScript>(storePath);
        Check(codecScript != null && storeScript != null, "GDScript interoperability scripts load");
        if (codecScript == null || storeScript == null)
            return;
        using GodotObject gdCodec = codecScript.New().AsGodotObject();
        var modified = KeyEvent(Key.Z);
        modified.CtrlPressed = modified.MetaPressed = true;
        foreach (InputEvent input in new InputEvent[] { modified, MouseEvent(MouseButton.Xbutton1), JoyEvent(JoyButton.A) })
        {
            GDictionary gdEncoded = gdCodec.Call("encode", input).AsGodotDictionary();
            GDictionary csEncoded = _codec.Encode(input);
            Check(csEncoded.RecursiveEqual(gdEncoded), "C# and GDScript encode the identical schema");
            Equal(_codec.Signature(input), gdCodec.Call("signature", input).AsString(), "C# and GDScript signatures agree");
            Equal(_codec.Signature(input), _codec.Signature(_codec.Decode(gdEncoded)), "C# decodes GDScript event");
            InputEvent gdDecoded = gdCodec.Call("decode", csEncoded).AsGodotObject() as InputEvent;
            Equal(_codec.Signature(input), _codec.Signature(gdDecoded), "GDScript decodes C# event");
        }
        string path = FixturePath("interop");
        using GodotObject gdStore = storeScript.New(path).AsGodotObject();
        using var csStore = new QuickInputConfigFileStore(path);
        var gdPayload = new GDictionary { [_actionA.ToString()] = Slots(KeyEvent(Key.A), MouseEvent(MouseButton.Left)) };
        Equal(Error.Ok, (Error)gdStore.Call("save_bindings", gdPayload).AsInt32(), "GDScript writes shared ConfigFile");
        Check(csStore.LoadBindings().RecursiveEqual(gdPayload), "C# reads GDScript save");
        Equal(Error.Ok, csStore.LoadError, "C# accepts GDScript file metadata");
        var manager = Manager(store: csStore);
        Check(manager.GetBinding(_actionA, 1) is InputEventMouseButton, "C# manager restores GDScript save");
        Equal(Error.Ok, manager.Rebind(_actionA, 1, JoyEvent(JoyButton.A)), "C# manager updates GDScript save");
        GDictionary gdRead = gdStore.Call("load_bindings").AsGodotDictionary();
        Equal(Error.Ok, (Error)gdStore.Get("load_error").AsInt32(), "GDScript accepts C# file metadata");
        Check(gdRead.RecursiveEqual(csStore.LoadBindings()), "GDScript reads identical C# persisted overrides");
        Equal("joypad_button", gdRead[_actionA.ToString()].AsGodotArray()[1].AsGodotDictionary()["type"].AsString(),
            "GDScript observes C# gamepad update");
    }

    private void TestCaptureButton()
    {
        var store = new TestsMemoryStore();
        var manager = Manager(store: store);
        var button = Own(new QuickInputRebindButton { Action = _actionA, Slot = 1 });
        AddChild(button);
        var errors = new List<Error>();
        var conflicts = new List<QuickInputRebindResult>();
        int cancelled = 0;
        button.CaptureFailed += error => errors.Add(error);
        button.ConflictDetected += result => conflicts.Add(result);
        button.CaptureCancelled += () => cancelled++;
        button.SetManager(null); // Do not depend on an optional project autoload.
        button.BeginCapture();
        Equal(Error.Unconfigured, errors[0], "Capture without configured manager fails");
        button.SetManager(manager);
        Equal("Unbound", button.Text, "Injected manager refreshes button label");
        button._Input(KeyEvent(Key.F5, true));
        Check(manager.GetBinding(_actionA, 1) == null, "Inactive control ignores input");
        button.BeginCapture();
        string captureLabel = button.Text;
        button._Input(KeyEvent(Key.F5));
        var echo = KeyEvent(Key.F5, true);
        echo.Echo = true;
        button._Input(echo);
        button._Input(new InputEventMouseMotion());
        button._Input(new InputEventJoypadMotion());
        Check(manager.GetBinding(_actionA, 1) == null, "Released, echo and motion input ignored");
        Equal(captureLabel, button.Text, "Ignored events keep capture open");
        button._Input(KeyEvent(Key.F5, true));
        AssertKey(manager, _actionA, 1, Key.F5, "Keyboard press captured");
        Equal(manager.GetBindingText(_actionA, 1), button.Text, "Capture refreshes label");
        button._Input(KeyEvent(Key.F6, true));
        AssertKey(manager, _actionA, 1, Key.F5, "Capture ends after success");
        button.BeginCapture();
        button._Input(MouseEvent(MouseButton.Right));
        AssertKey(manager, _actionA, 1, Key.F5, "Mouse release ignored");
        button._Input(MouseEvent(MouseButton.Right, true));
        Check(manager.GetBinding(_actionA, 1) is InputEventMouseButton, "Mouse press captured");
        button.BeginCapture();
        button._Input(JoyEvent(JoyButton.A));
        Check(manager.GetBinding(_actionA, 1) is InputEventMouseButton, "Gamepad release ignored");
        button._Input(JoyEvent(JoyButton.A, true));
        Check(manager.GetBinding(_actionA, 1) is InputEventJoypadButton, "Gamepad press captured");
        Equal(-1, manager.GetBinding(_actionA, 1).Device, "Captured gamepad device normalized");
        string before = MapSnapshot();
        button.BeginCapture();
        button._Input(KeyEvent(Key.Escape, true));
        Equal(before, MapSnapshot(), "Physical Escape cancels without changing map");
        Equal(1, cancelled, "Escape emits cancellation");
        button.BeginCapture();
        button._Input(new InputEventKey { Keycode = Key.Escape, Pressed = true });
        Equal(2, cancelled, "Logical Escape also cancels");
        button.CancelCapture();
        Equal(2, cancelled, "Cancel is idempotent while idle");
        button.BeginCapture();
        button.CancelCapture();
        Equal(3, cancelled, "Explicit cancellation emits once");
        button.BeginCapture();
        button._Input(KeyEvent(Key.B, true));
        Equal(1, conflicts.Count, "Capture reports conflict for confirmation");
        Equal("Binding conflict", button.Text, "Pending conflict label shown");
        Equal(before, MapSnapshot(), "Conflict preview does not commit");
        button._Input(KeyEvent(Key.F8, true));
        Equal(before, MapSnapshot(), "Pending conflict ignores new capture events");
        Equal(Error.AlreadyInUse, button.ConfirmConflict(ConflictPolicy.Reject), "Rejected confirmation stays pending");
        Equal(Error.Ok, button.ConfirmConflict(ConflictPolicy.Swap), "Pending conflict can be confirmed with swap");
        AssertKey(manager, _actionA, 1, Key.B, "Conflict confirmation commits requested key");
        Check(manager.GetBinding(_actionB, 0) is InputEventJoypadButton, "Conflict confirmation swaps displaced binding");
        Equal(Error.InvalidParameter, button.ConfirmConflict(ConflictPolicy.Allow), "Successful confirmation clears pending transaction");
        Equal(Error.Ok, manager.ResetAll(), "Reset bindings for next capture");
        Equal("Unbound", button.Text, "External manager change refreshes idle control");
        button.BeginCapture();
        button._Input(KeyEvent(Key.B, true));
        Equal(Error.Ok, manager.Rebind(_actionB, 1, KeyEvent(Key.F8)), "Change state while conflict pending");
        Equal(Error.Busy, button.ConfirmConflict(ConflictPolicy.Replace), "Stale pending conflict is rejected");
        button.CancelCapture();
        Equal(Error.InvalidParameter, button.ConfirmConflict(ConflictPolicy.Replace), "Cancelling clears stale pending transaction");
        button.ConflictPolicy = ConflictPolicy.Allow;
        button.BeginCapture();
        button._Input(KeyEvent(Key.B, true));
        AssertKey(manager, _actionA, 1, Key.B, "Configured allow policy commits without confirmation");
        AssertKey(manager, _actionB, 0, Key.B, "Allow capture preserves occupied slot");
    }

    private void TestCaptureLifecycle()
    {
        var store = new TestsMemoryStore();
        var manager = Manager(store: store);
        var button = Own(new QuickInputRebindButton { Action = _actionA, Slot = 1 });
        AddChild(button);
        button.SetManager(manager);
        var errors = new List<Error>();
        int cancelled = 0;
        button.CaptureFailed += error => errors.Add(error);
        button.CaptureCancelled += () => cancelled++;
        button.BeginCapture();
        var modified = KeyEvent(Key.F5, true);
        modified.CtrlPressed = true;
        button._Input(modified);
        Equal(Error.InvalidParameter, errors[errors.Count - 1], "Invalid capture reports validation failure");
        Check(manager.GetBinding(_actionA, 1) == null, "Invalid capture does not commit");
        store.Failure = Error.CantCreate;
        string snapshot = MapSnapshot();
        button._Input(KeyEvent(Key.F5, true));
        Equal(Error.CantCreate, errors[errors.Count - 1], "Capture reports storage failure");
        Equal(snapshot, MapSnapshot(), "Failed capture leaves InputMap unchanged");
        store.Failure = Error.Ok;
        button._Input(KeyEvent(Key.F6, true));
        AssertKey(manager, _actionA, 1, Key.F6, "Capture remains usable after invalid input and failed save");
        button.BeginCapture();
        string label = button.Text;
        Equal(Error.Ok, manager.Rebind(_actionA, 1, KeyEvent(Key.F7)), "External change during capture");
        Equal(label, button.Text, "External change does not hide capture prompt");
        button.SetManager(null);
        Equal(1, cancelled, "Changing managers cancels active capture");
        button._Input(KeyEvent(Key.F8, true));
        AssertKey(manager, _actionA, 1, Key.F7, "Detached manager cannot receive capture");
        button.SetManager(manager);
        Equal(manager.GetBindingText(_actionA, 1), button.Text, "Reattached manager refreshes label");
        RemoveChild(button);
        AddChild(button);
        Equal(Error.Ok, manager.Rebind(_actionA, 1, KeyEvent(Key.F9)), "Update after control reenters tree");
        Equal(manager.GetBindingText(_actionA, 1), button.Text, "Reentered control reconnects manager changes");
        button.EmitSignal(BaseButton.SignalName.Pressed);
        button._Input(KeyEvent(Key.F10, true));
        AssertKey(manager, _actionA, 1, Key.F10, "Reentered control reconnects button pressed signal");
        button.BeginCapture();
        manager.RestoreDefaultsInInputMap();
        manager.Free();
        button._Input(KeyEvent(Key.F11, true));
        Equal(Error.Unconfigured, errors[errors.Count - 1], "Freed manager during capture is handled safely");
        var replacement = Manager();
        button.SetManager(replacement);
        RemoveChild(button);
        // Off-tree input injection is useful to hosts and must not dereference a missing viewport.
        button.BeginCapture();
        button._Input(KeyEvent(Key.F12, true));
        AssertKey(replacement, _actionA, 1, Key.F12, "Off-tree capture remains safe");
    }

    private void TestPluginGuard()
    {
        long uid = ResourceLoader.GetResourceUid(QuickInputPluginGuard.AutoloadValue.Substring(1));
        Check(uid != ResourceUid.InvalidId, "C# manager has imported resource UID");
        string uidValue = "*" + ResourceUid.IdToText(uid);
        Check(QuickInputPluginGuard.OwnsAutoload(uidValue), "UID autoload resolves to owned C# script");
        Check(QuickInputPluginGuard.ShouldRemoveAutoload(uidValue, false), "UID autoload is removed after reload");
        string owned = QuickInputPluginGuard.AutoloadValue;
        string peer = QuickInputPluginGuard.PeerPluginPath;
        string[] none = System.Array.Empty<string>();
        Equal("", QuickInputPluginGuard.RegistrationError(false, default, none), "Unused autoload can be registered");
        Equal("", QuickInputPluginGuard.RegistrationError(true, owned, none), "Own autoload survives plugin reload");
        Check(QuickInputPluginGuard.OwnsAutoload(owned), "Ownership requires exact singleton path");
        Check(QuickInputPluginGuard.ShouldRemoveAutoload(owned, false), "Successfully enabled owner can remove own autoload");
        Check(!QuickInputPluginGuard.ShouldRemoveAutoload(owned, true), "Rejected enable preserves autoload on disable");
        foreach (Variant foreign in new Variant[]
        {
            "*res://other/QuickInput.cs", owned.Substring(1), "", 42, default,
            "*res://addons/quick_input/gds/runtime/quick_input_manager.gd",
        })
        {
            Check(!QuickInputPluginGuard.OwnsAutoload(foreign), "Foreign or malformed autoload not owned");
            Check(QuickInputPluginGuard.RegistrationError(true, foreign, none).Length > 0, "Foreign autoload registration rejected");
            Check(!QuickInputPluginGuard.ShouldRemoveAutoload(foreign, false), "Foreign autoload preserved on disable");
        }
        Check(QuickInputPluginGuard.RegistrationError(false, default, new[] { peer }).Length > 0,
            "Enabled GDScript sibling rejects C# activation");
        Check(QuickInputPluginGuard.RegistrationError(true, owned, new[] { peer }).Length > 0,
            "Sibling guard also applies when own autoload exists");
    }

    private void TestResourceSerialization()
    {
        string path = $"user://quick_input_csharp_test_{_runId}_settings.tres";
        _fixturePaths.Add(path);
        var settings = new CSharpQuickInputSettings { AllowModifierCombinations = true };
        settings.Actions.Add(new CSharpQuickInputActionDefinition
        {
            Action = _actionA,
            DisplayName = "Parity action",
            BindingSlots = 2,
            ConflictGroup = "gameplay",
            AllowedEventTypes = QuickInputActionDefinition.Keyboard | QuickInputActionDefinition.Mouse,
        });
        settings.ReservedEvents.Add(KeyEvent(Key.Escape));
        Equal(Error.Ok, ResourceSaver.Save(settings, path), "Editor resource wrapper saves as tres");
        QuickInputSettings restored = ResourceLoader.Load<QuickInputSettings>(path, "", ResourceLoader.CacheMode.Ignore);
        Check(restored is CSharpQuickInputSettings, "Editor settings wrapper restores through base API");
        if (restored == null)
            return;
        Check(restored.AllowModifierCombinations, "Serialized modifier policy preserved");
        Equal(1, restored.Actions.Count, "Typed base array preserves nested action count");
        Check(restored.Actions[0] is CSharpQuickInputActionDefinition, "Nested editor action wrapper preserves script identity");
        Equal(_actionA, restored.Actions[0].Action, "Serialized action name preserved");
        Equal("Parity action", restored.Actions[0].DisplayName, "Serialized display name preserved");
        Equal(2, restored.Actions[0].BindingSlots, "Serialized slot count preserved");
        Equal(new StringName("gameplay"), restored.Actions[0].ConflictGroup, "Serialized conflict group preserved");
        Equal(3, restored.Actions[0].AllowedEventTypes, "Serialized allowed-event mask preserved");
        Equal(1, restored.ReservedEvents.Count, "Serialized reserved-event array preserved");
        Equal(_codec.Signature(KeyEvent(Key.Escape)), _codec.Signature(restored.ReservedEvents[0]), "Nested reserved event preserved");
        var manager = Manager(restored);
        Check(manager.IsConfigured(), "Round-tripped resource configures runtime manager");
    }

    private void AssertKey(QuickInputManager manager, StringName action, int slot, Key expected, string label)
    {
        InputEvent input = manager.GetBinding(action, slot);
        Check(input is InputEventKey key && key.PhysicalKeycode == expected, label);
    }

    private void Equal<T>(T expected, T actual, string label) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"{label} (expected {expected}, got {actual})");

    private void Check(bool condition, string label)
    {
        _assertions++;
        if (condition)
            return;
        _failures++;
        GD.PushError($"Quick Input C# test failed: {label}");
    }
}
