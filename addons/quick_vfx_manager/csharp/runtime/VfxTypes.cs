using Godot;
using System.Threading;

namespace QuickVfx;

public enum VfxPlacement { World, Attached }
public enum VfxMode { OneShot, Loop }
public enum VfxStopMode { Graceful, Immediate }
public enum VfxEndReason { Completed, Stopped, Cancelled, TimedOut, InvalidConfiguration, ParticipantLost, RootExited }
public enum VfxState { Playing, Stopping, Finished }

/// <summary>Per-request overrides. Definitions are never mutated by playback.</summary>
public sealed class VfxPlayOptions
{
    public VfxPlacement? Placement { get; init; }
    public VfxMode? Mode { get; init; }
    public Node3D? Anchor { get; init; }
    public Transform3D? Transform { get; init; }
    public Godot.Collections.Dictionary<StringName, Variant>? Parameters { get; init; }
}

/// <summary>Immutable request identity; check Cancellation before completing asynchronous work.</summary>
public sealed record VfxContext(long Generation, VfxMode Mode, CancellationToken Cancellation,
    Godot.Collections.Dictionary<StringName, Variant> Parameters);
