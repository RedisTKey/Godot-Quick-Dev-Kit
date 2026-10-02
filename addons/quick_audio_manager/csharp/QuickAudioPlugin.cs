#if TOOLS
using Godot;

namespace QuickDevKit.QuickAudio;

[Tool]
public partial class QuickAudioPlugin : EditorPlugin
{
    public const string AutoloadName = "QuickAudioManager";
    public const string AutoloadPath = "res://addons/quick_audio_manager/csharp/runtime/QuickAudioManagerService.cs";
    private bool _registrationRejected;

    public override void _EnablePlugin()
    {
        string setting = "autoload/" + AutoloadName;
        var current = ProjectSettings.GetSetting(setting, "");
        var enabled = ProjectSettings.GetSetting("editor_plugins/enabled", new string[0]).AsStringArray();
        string reason = QuickAudioPluginGuard.RegistrationError(ProjectSettings.HasSetting(setting), current, enabled);
        _registrationRejected = reason.Length != 0;
        if (_registrationRejected)
        {
            GD.PushError("Quick Audio Manager (C#): " + reason);
            return;
        }
        if (QuickAudioPluginGuard.OwnsAutoload(current)) return;
        AddAutoloadSingleton(AutoloadName, AutoloadPath);
    }

    public override void _DisablePlugin()
    {
        // Ownership survives editor restarts. Never remove the sibling or a project-owned service.
        if (QuickAudioPluginGuard.ShouldRemoveAutoload(
            ProjectSettings.GetSetting("autoload/" + AutoloadName, ""), _registrationRejected))
            RemoveAutoloadSingleton(AutoloadName);
        _registrationRejected = false;
    }
}
#endif
