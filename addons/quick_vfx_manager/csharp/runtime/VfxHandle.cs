using Godot;
using System;

namespace QuickVfx;

/// <summary>A one-use capability. An old handle can never stop a newer playback.</summary>
public partial class VfxHandle : RefCounted
{
    [Signal] public delegate void EndedEventHandler(long reason);
    private readonly WeakReference<VfxPlayback> _owner;
    internal VfxEffect? Effect;
    public long Generation { get; }
    public StringName DefinitionId { get; }
    public VfxState State { get; internal set; } = VfxState.Playing;
    public VfxEndReason? EndReason { get; private set; }
    public string Error { get; private set; } = "";
    public bool IsActive => State != VfxState.Finished && GodotObject.IsInstanceValid(Effect) && !Effect!.IsQueuedForDeletion();

    internal VfxHandle(VfxPlayback owner, long generation, StringName definitionId)
    { _owner = new(owner); Generation = generation; DefinitionId = definitionId; }

    public bool Stop(VfxStopMode mode = VfxStopMode.Graceful) =>
        _owner.TryGetTarget(out var owner) && GodotObject.IsInstanceValid(owner) && owner.Stop(this, mode);

    public VfxEffect? GetEffect() => IsActive ? Effect : null;

    internal void Resolve(VfxEndReason reason, string error)
    {
        if (State == VfxState.Finished) return;
        State = VfxState.Finished;
        EndReason = reason;
        Error = error;
        Effect = null;
        EmitSignal(SignalName.Ended, (long)reason);
    }
}
