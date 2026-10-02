using Godot;
using Godot.Collections;
namespace QuickDevKit.QuickAudio;

/// <summary>Ordered track definitions; IDs and bus names must be unique.</summary>
[Tool]
public partial class QuickAudioTrackLibrary : Resource
{
    [Export] public Array<QuickAudioTrackDefinition> Tracks { get; set; } = new();
}
