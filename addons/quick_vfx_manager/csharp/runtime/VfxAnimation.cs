using Godot;
using System;

namespace QuickVfx;

/// <summary>Plays an explicit clip, optionally followed by a non-looping stop/outro clip.</summary>
[GlobalClass]
public partial class VfxAnimation : VfxParticipant
{
    [Export] public AnimationPlayer? Target { get; set; }
    [Export] public StringName PlayAnimation { get; set; } = "play";
    [Export] public StringName StopAnimation { get; set; } = "";
    private bool _stopping;
    private StringName _awaited = "";
    private AnimationMixer.AnimationFinishedEventHandler? _finished;

    protected override void StartEffect(VfxContext context)
    {
        if (!IsEffectTarget(Target))
            throw new InvalidOperationException("Animation Target must be a live node inside this effect scene.");
        if (!Target!.HasAnimation(PlayAnimation)) throw new InvalidOperationException($"Missing animation '{PlayAnimation}'.");
        if (Target.GetAnimation(PlayAnimation).LoopMode != Animation.LoopModeEnum.None && context.Mode == VfxMode.OneShot)
            throw new InvalidOperationException("One-shot definition cannot use a looping animation.");
        if (!string.IsNullOrEmpty(StopAnimation.ToString()) && (!Target.HasAnimation(StopAnimation) || Target.GetAnimation(StopAnimation).LoopMode != Animation.LoopModeEnum.None))
            throw new InvalidOperationException("StopAnimation must exist and must not loop.");
        _stopping = false;
        _awaited = PlayAnimation;
        var generation = context.Generation;
        _finished = name =>
        {
            if (name != _awaited || !Running) return;
            if (context.Mode == VfxMode.Loop && !_stopping) return;
            Disconnect();
            Complete(generation);
        };
        Target.AnimationFinished += _finished;
        Target.Stop();
        Target.Play(PlayAnimation);
    }
    protected override void StopEffect()
    {
        _stopping = true;
        if (!GodotObject.IsInstanceValid(Target)) { Fail("Animation target was deleted."); return; }
        if (!string.IsNullOrEmpty(StopAnimation.ToString()))
        {
            _awaited = StopAnimation;
            Target!.Play(StopAnimation); // Play directly: queuing behind an infinite loop can never finish.
        }
        else if (Context!.Mode == VfxMode.Loop)
        {
            Target.Stop();
            Disconnect();
            Complete(Context!.Generation);
        }
        // A running one-shot with no outro is allowed to finish naturally.
    }
    public override void _Process(double delta)
    {
        if (Running && (!GodotObject.IsInstanceValid(Target) || !Target!.IsInsideTree() || Target.IsQueuedForDeletion())) Fail("Animation target was removed.");
    }
    private void Disconnect()
    {
        if (_finished != null && GodotObject.IsInstanceValid(Target)) Target!.AnimationFinished -= _finished;
        _finished = null;
    }
    protected override void CancelEffect() { Disconnect(); if (GodotObject.IsInstanceValid(Target)) Target!.Stop(); }
    public override void _ExitTree() => Disconnect();
}
