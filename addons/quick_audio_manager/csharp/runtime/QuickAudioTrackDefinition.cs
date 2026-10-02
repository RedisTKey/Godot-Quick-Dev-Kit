using Godot;
namespace QuickDevKit.QuickAudio;

/// <summary>Resource configuration, snapshotted by the manager when configured.</summary>
[Tool]
public partial class QuickAudioTrackDefinition : Resource
{
    [Export] public StringName TrackId { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export] public StringName BusName { get; set; } = "";
    [Export] public StringName ParentTrackId { get; set; } = "";
    [Export(PropertyHint.Range, "-80,24,0.1,suffix:dB")] public float VolumeDb { get; set; }
    [Export] public bool Muted { get; set; }
    [Export(PropertyHint.Range, "1,128,1")] public int MaxVoices { get; set; } = 8;
    [Export] public Node.ProcessModeEnum ProcessMode { get; set; } = Node.ProcessModeEnum.Inherit;
}
