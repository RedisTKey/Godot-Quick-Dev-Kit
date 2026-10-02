#nullable disable
using Godot;
namespace QuickDevKit.QuickAudio.Examples;

public partial class BasicUsage : Node
{
    [Export] public QuickAudioAsset AudioAsset { get; set; }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_accept")) PlayAudio();
    }

    public AudioStreamPlayer PlayAudio()
    {
        if (AudioAsset == null) return null;
        return GetNodeOrNull<QuickAudioManagerService>("/root/QuickAudioManager")?.Play(AudioAsset);
    }
}
