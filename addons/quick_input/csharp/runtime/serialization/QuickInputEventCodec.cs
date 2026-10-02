using Godot;
using Godot.Collections;

namespace QuickDevKit.QuickInput;

/// <summary>Version-1 ConfigFile-compatible keyboard, mouse, and digital gamepad codec.</summary>
public partial class QuickInputEventCodec : RefCounted
{
    public Dictionary Encode(InputEvent input)
    {
        if (input is InputEventKey key)
        {
            if ((key.PhysicalKeycode == Key.None && key.Keycode == Key.None)
                || !ValidKeycode((long)key.PhysicalKeycode) || !ValidKeycode((long)key.Keycode)
                || (long)key.Location < 0 || (long)key.Location > 2) return new();
            return new Dictionary
            {
                ["type"] = "key", ["physical"] = (long)key.PhysicalKeycode,
                ["keycode"] = (long)key.Keycode, ["location"] = (long)key.Location,
                ["shift"] = key.ShiftPressed, ["ctrl"] = key.CtrlPressed,
                ["alt"] = key.AltPressed, ["meta"] = key.MetaPressed,
            };
        }
        if (input is InputEventMouseButton mouse)
        {
            if ((long)mouse.ButtonIndex < 1 || (long)mouse.ButtonIndex > 9) return new();
            return new Dictionary
            {
                ["type"] = "mouse", ["button"] = (long)mouse.ButtonIndex,
                ["shift"] = mouse.ShiftPressed, ["ctrl"] = mouse.CtrlPressed,
                ["alt"] = mouse.AltPressed, ["meta"] = mouse.MetaPressed,
            };
        }
        if (input is InputEventJoypadButton joy)
        {
            if ((long)joy.ButtonIndex < 0 || (long)joy.ButtonIndex > 127) return new();
            return new Dictionary { ["type"] = "joypad_button", ["button"] = (long)joy.ButtonIndex };
        }
        return new();
    }

    public InputEvent Decode(Dictionary data)
    {
        if (data == null || !data.TryGetValue("type", out var kind) || kind.VariantType != Variant.Type.String)
            return null;
        switch (kind.AsString())
        {
            case "key":
                if (!HasIntegers(data, "physical", "keycode", "location") || !HasModifiers(data)) return null;
                long physical = data["physical"].AsInt64(), keycode = data["keycode"].AsInt64(), location = data["location"].AsInt64();
                if ((physical == 0 && keycode == 0) || !ValidKeycode(physical) || !ValidKeycode(keycode) || location < 0 || location > 2)
                    return null;
                var key = new InputEventKey { PhysicalKeycode = (Key)physical, Keycode = (Key)keycode, Location = (KeyLocation)location };
                ApplyModifiers(key, data);
                return key;
            case "mouse":
                if (!HasIntegers(data, "button") || !HasModifiers(data)) return null;
                long mouseIndex = data["button"].AsInt64();
                if (mouseIndex < 1 || mouseIndex > 9) return null;
                var mouse = new InputEventMouseButton { ButtonIndex = (MouseButton)mouseIndex };
                ApplyModifiers(mouse, data);
                return mouse;
            case "joypad_button":
                if (!HasIntegers(data, "button")) return null;
                long joyIndex = data["button"].AsInt64();
                if (joyIndex < 0 || joyIndex > 127) return null;
                return new InputEventJoypadButton { ButtonIndex = (JoyButton)joyIndex, Device = -1 };
            default:
                return null;
        }
    }

    public string Signature(InputEvent input)
    {
        var data = Encode(input);
        if (data.Count == 0) return "";
        if (data["type"].AsString() == "key" && data["physical"].AsInt64() != 0)
            data["keycode"] = 0;
        return GD.VarToStr(data);
    }

    private static bool HasIntegers(Dictionary data, params string[] fields)
    {
        foreach (string field in fields)
            if (!data.TryGetValue(field, out var value) || value.VariantType != Variant.Type.Int) return false;
        return true;
    }

    private static bool ValidKeycode(long code) => code >= 0 && code <= (long)KeyModifierMask.CodeMask && code != (long)Key.Unknown;

    private static bool HasModifiers(Dictionary data)
    {
        foreach (string field in new[] { "shift", "ctrl", "alt", "meta" })
            if (!data.TryGetValue(field, out var value) || value.VariantType != Variant.Type.Bool) return false;
        return true;
    }

    private static void ApplyModifiers(InputEventWithModifiers input, Dictionary data)
    {
        input.ShiftPressed = data["shift"].AsBool();
        input.CtrlPressed = data["ctrl"].AsBool();
        input.AltPressed = data["alt"].AsBool();
        input.MetaPressed = data["meta"].AsBool();
    }
}
