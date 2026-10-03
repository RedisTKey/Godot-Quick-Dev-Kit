using Godot;
using System;

namespace QuickVfx;

/// <summary>Subclass for custom shader/tween/audio-independent tails. All calls must occur on Godot's main thread.</summary>
public abstract partial class VfxParticipant : Node
{
    internal event Action<VfxParticipant>? Finished;
    internal event Action<string>? Failed;
    protected VfxContext? Context { get; private set; }
    protected bool Running { get; private set; }
    internal bool IsFinished => !Running;

    internal void Begin(VfxContext context)
    {
        Context = context;
        Running = true;
        StartEffect(context);
    }
    internal void StopGracefully() { if (Running) StopEffect(); }
    internal void Cancel()
    {
        if (!Running) return;
        Running = false;
        CancelEffect();
    }
    protected bool IsEffectTarget(Node? target)
    {
        if (!GodotObject.IsInstanceValid(target) || !target!.IsInsideTree() || target.IsQueuedForDeletion()) return false;
        for (Node? node = GetParent(); node != null; node = node.GetParent())
            if (node is VfxEffect effect) return effect.IsAncestorOf(target);
        return false;
    }
    protected abstract void StartEffect(VfxContext context);
    protected abstract void StopEffect();
    protected virtual void CancelEffect() { }

    /// <summary>Capture the generation at StartEffect, never read a newer context after await.</summary>
    protected void Complete(long generation)
    {
        if (!Running || Context == null || Context.Generation != generation || Context.Cancellation.IsCancellationRequested
            || !GodotObject.IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion()) return;
        Running = false;
        Finished?.Invoke(this);
    }
    protected void Fail(string message) { if (Running) Failed?.Invoke($"{Name}: {message}"); }
}
