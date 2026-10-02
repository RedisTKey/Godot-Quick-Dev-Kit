using Godot;

namespace QuickDevKit.QuickAudio;

/// <summary>Read-only plugin ownership decisions; never mutates editor or project settings.</summary>
public static class QuickAudioPluginGuard
{
    public const string AutoloadValue = "*res://addons/quick_audio_manager/csharp/runtime/QuickAudioManagerService.cs";
    public const string PeerPluginPath = "res://addons/quick_audio_manager/gds/plugin.cfg";

    public static string RegistrationError(bool exists, Variant current, string[] enabled)
    {
        foreach (string plugin in enabled)
            if (plugin == PeerPluginPath)
                return "Quick Audio Manager (GDScript) is enabled. Disable it before enabling C#. This activation is inactive.";
        return exists && !OwnsAutoload(current)
            ? "Autoload 'QuickAudioManager' already has a different path or is not a singleton. Existing service was preserved."
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
