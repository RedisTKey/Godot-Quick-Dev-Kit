#if TOOLS
using Godot;

namespace QuickDevKit.QuickInput;

[Tool]
public partial class QuickInputPlugin : EditorPlugin
{
    public const string AutoloadName = "QuickInput";
    public const string AutoloadPath = "res://addons/quick_input/csharp/runtime/QuickInputManager.cs";
    private bool _registrationRejected;

    public override void _EnablePlugin()
    {
        string setting = "autoload/" + AutoloadName;
        var current = ProjectSettings.GetSetting(setting, "");
        var enabled = ProjectSettings.GetSetting("editor_plugins/enabled", new string[0]).AsStringArray();
        string reason = QuickInputPluginGuard.RegistrationError(ProjectSettings.HasSetting(setting), current, enabled);
        _registrationRejected = reason.Length != 0;
        if (_registrationRejected)
        {
            GD.PushError("Quick Input (C#): " + reason);
            return;
        }
        if (QuickInputPluginGuard.OwnsAutoload(current)) return;
        AddAutoloadSingleton(AutoloadName, AutoloadPath);
    }

    public override void _DisablePlugin()
    {
        // Ownership survives editor restarts. Never remove the sibling or a project-owned service.
        if (QuickInputPluginGuard.ShouldRemoveAutoload(
            ProjectSettings.GetSetting("autoload/" + AutoloadName, ""), _registrationRejected))
            RemoveAutoloadSingleton(AutoloadName);
        _registrationRejected = false;
    }
}
#endif
