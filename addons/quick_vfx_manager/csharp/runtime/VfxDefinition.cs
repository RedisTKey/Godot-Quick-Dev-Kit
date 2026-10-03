using Godot;

namespace QuickVfx;

[GlobalClass]
public partial class VfxDefinition : Resource
{
    [Export] public StringName Id { get; set; } = "";
    [Export] public PackedScene? Scene { get; set; }
    [Export] public VfxPlacement Placement { get; set; } = VfxPlacement.World;
    [Export] public bool ProcessWhilePaused { get; set; }
    [Export] public VfxMode Mode { get; set; } = VfxMode.OneShot;
    /// <summary>World: multiplied after requested world transform. Attached: after requested local transform.</summary>
    [Export] public Transform3D Offset { get; set; } = Transform3D.Identity;
    /// <summary>Safety bound, not normal duration. A timeout has its own completion reason.</summary>
    [Export(PropertyHint.Range, "0.01,600,0.01,or_greater")] public double OneShotTimeout { get; set; } = 10;
    [Export(PropertyHint.Range, "0.01,600,0.01,or_greater")] public double StopTimeout { get; set; } = 5;
}
