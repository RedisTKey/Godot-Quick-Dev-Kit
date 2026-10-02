#nullable disable
using Godot;
namespace QuickDevKit.QuickAudio;

/// <summary>A stream, its destination track, and per-voice volume and pitch.</summary>
[Tool]
public partial class QuickAudioAsset : Resource
{
    [Export] public AudioStream Stream { get; set; }
    [Export] public QuickAudioTrackDefinition Track { get; set; }
    [Export(PropertyHint.Range, "-80,24,0.1,suffix:dB")] public float VolumeDb { get; set; }
    [Export(PropertyHint.Range, "0.01,4,0.01")] public float PitchScale { get; set; } = 1f;
}
