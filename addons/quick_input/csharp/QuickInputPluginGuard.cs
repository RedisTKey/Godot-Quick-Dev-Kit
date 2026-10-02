using Godot;

namespace QuickDevKit.QuickInput;

/// <summary>Read-only plugin ownership decisions; never mutates editor or project settings.</summary>
public static class QuickInputPluginGuard
{
    public const string AutoloadValue = "*res://addons/quick_input/csharp/runtime/QuickInputManager.cs";
    public const string PeerPluginPath = "res://addons/quick_input/gds/plugin.cfg";

    public static string RegistrationError(bool exists, Variant current, string[] enabled)
    {
        foreach (string plugin in enabled)
            if (plugin == PeerPluginPath)
                return "Quick Input (GDScript) is enabled. Disable it before enabling C#. This activation is inactive.";
        return exists && !OwnsAutoload(current)
            ? "Autoload 'QuickInput' already has a different path or is not a singleton. Existing service was preserved."
            : "";
    }

    public static bool OwnsAutoload(Variant current)
    {
        if (current.VariantType != Variant.Type.String) return false;
        string value = current.AsString();
        if (!value.StartsWith("*", System.StringComparison.Ordinal)) return false;
        string path = value.Substring(1);
        if (path.StartsWith("uid://", System.StringComparison.Ordinal))
        {
            long uid = ResourceUid.TextToId(path);
            if (!ResourceUid.HasId(uid)) return false;
            path = ResourceUid.GetIdPath(uid);
        }
        return path == AutoloadValue.Substring(1);
    }

    public static bool ShouldRemoveAutoload(Variant current, bool rejected) => !rejected && OwnsAutoload(current);
}
