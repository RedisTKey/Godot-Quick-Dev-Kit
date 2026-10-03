using Godot;
using System;

namespace QuickVfx;

/// <summary>Owns one GPU emitter. LocalCoords remains a designer choice, independent of root attachment.</summary>
[GlobalClass]
public partial class VfxParticles : VfxParticipant
{
    [Export] public GpuParticles3D? Target { get; set; }
    /// <summary>Extra simulation seconds after the conservative standard-particle lifetime budget.</summary>
    [Export(PropertyHint.Range, "0,60,0.01,or_greater")] public double TailSeconds { get; set; } = 0.1;
    private double _remaining;
    private bool _draining, _oneShot, _signalReceived;
    private Action? _finished;

    protected override void StartEffect(VfxContext context)
    {
        if (!IsEffectTarget(Target))
            throw new InvalidOperationException("Particles Target must be a live node inside this effect scene.");
        if (!double.IsFinite(TailSeconds) || TailSeconds < 0) throw new InvalidOperationException("TailSeconds must be finite and non-negative.");
        _oneShot = context.Mode == VfxMode.OneShot;
        Target!.Emitting = false;
        Target.OneShot = _oneShot;
        _draining = false;
        _signalReceived = false;
        // Finished timing does not reliably scale with SpeedScale in all Godot versions.
        // Gate it with conservative simulation age; custom shader/subemitter tails need TailSeconds.
        _remaining = Target.Lifetime * 2 + (Target.TrailEnabled ? Target.TrailLifetime : 0) + TailSeconds;
        var generation = context.Generation;
        _finished = () => { if (Running && Context?.Generation == generation) _signalReceived = true; };
        Target.Finished += _finished;
        // Restart already enables emission. Setting Emitting=true again cancels Finished in Godot.
        Target.Restart();
    }
    protected override void StopEffect()
    {
        if (!GodotObject.IsInstanceValid(Target)) { Fail("Particle target was deleted."); return; }
        Target!.Emitting = false;
        _draining = true;
        _remaining = Target.Lifetime + (Target.TrailEnabled ? Target.TrailLifetime : 0) + TailSeconds;
    }
    public override void _Process(double delta)
    {
        if (!Running || Context == null) return;
        if (!GodotObject.IsInstanceValid(Target) || !Target!.IsInsideTree() || Target.IsQueuedForDeletion()) { Fail("Particle target was removed."); return; }
        if (!_oneShot && !_draining) return;
        if (Target.CanProcess()) _remaining -= delta * Math.Max(0, Target.SpeedScale);
        if (_remaining <= 0 && (_draining || _signalReceived))
        {
            Disconnect();
            Complete(Context.Generation);
        }
    }
    private void Disconnect()
    {
        if (_finished != null && GodotObject.IsInstanceValid(Target)) Target!.Finished -= _finished;
        _finished = null;
    }
    protected override void CancelEffect()
    {
        Disconnect();
        if (GodotObject.IsInstanceValid(Target)) Target!.Emitting = false;
    }
    public override void _ExitTree() => Disconnect();
}
