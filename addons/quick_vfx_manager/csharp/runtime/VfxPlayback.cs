using Godot;
using System;
using System.Collections.Generic;
using System.Threading;

namespace QuickVfx;

/// <summary>Attach one component to each caller. Its definition registry is local to that caller.</summary>
[GlobalClass]
public partial class VfxPlayback : Node
{
    [Signal] public delegate void PlaybackStartedEventHandler(VfxHandle handle);
    [Signal] public delegate void PlaybackEndedEventHandler(VfxHandle handle, long reason);
    [Signal] public delegate void PlaybackFailedEventHandler(StringName id, string message);
    [Export] public Godot.Collections.Array<VfxDefinition> Definitions { get; set; } = new();
    [Export] public Node3D? DefaultAnchor { get; set; }
    private readonly Dictionary<StringName, VfxDefinition> _definitions = new();
    private readonly Dictionary<long, VfxHandle> _active = new();
    private long _generation;
    private bool _exiting;
    public int ActiveCount => _active.Count;

    public override void _Ready() => ReloadDefinitions();

    /// <summary>Atomic registry reload. Duplicate/invalid entries leave the old registry intact.</summary>
    public bool ReloadDefinitions()
    {
        var next = new Dictionary<StringName, VfxDefinition>();
        foreach (var definition in Definitions)
        {
            if (!Validate(definition, out var error)) { Reject(definition?.Id ?? "", error); return false; }
            if (!next.TryAdd(definition.Id, definition)) { Reject(definition.Id, "Duplicate definition ID."); return false; }
        }
        _definitions.Clear();
        foreach (var entry in next) _definitions.Add(entry.Key, entry.Value);
        return true;
    }

    public bool Register(VfxDefinition definition, bool replace = false)
    {
        if (!Validate(definition, out var error)) { Reject(definition?.Id ?? "", error); return false; }
        if (!replace && _definitions.ContainsKey(definition.Id)) { Reject(definition.Id, "Duplicate definition ID."); return false; }
        _definitions[definition.Id] = definition;
        return true;
    }
    public bool Unregister(StringName id) => _definitions.Remove(id);
    public bool HasDefinition(StringName id) => _definitions.ContainsKey(id);

    public VfxHandle? PlayAt(StringName id, Transform3D worldTransform) => Play(id,
        new VfxPlayOptions { Placement = VfxPlacement.World, Transform = worldTransform });
    public VfxHandle? PlayAttached(StringName id, Node3D anchor, Transform3D? localTransform = null) => Play(id,
        new VfxPlayOptions { Placement = VfxPlacement.Attached, Anchor = anchor, Transform = localTransform });

    public VfxHandle? Play(StringName id, VfxPlayOptions? options = null)
    {
        if (_exiting || !IsInsideTree() || IsQueuedForDeletion()) return Reject(id, "Playback component is outside the tree or being deleted.");
        if (!_definitions.TryGetValue(id, out var definition)) return Reject(id, "Unregistered definition ID.");
        if (!Validate(definition, out var error)) return Reject(id, error);
        options ??= new VfxPlayOptions();
        var placement = options.Placement ?? definition.Placement;
        var mode = options.Mode ?? definition.Mode;
        if (!Enum.IsDefined(placement) || !Enum.IsDefined(mode)) return Reject(id, "Invalid placement/playback mode.");
        var anchor = options.Anchor ?? DefaultAnchor ?? GetParentOrNull<Node3D>();
        if (placement == VfxPlacement.Attached && (!GodotObject.IsInstanceValid(anchor) || !anchor!.IsInsideTree() || anchor.IsQueuedForDeletion()))
            return Reject(id, "Attached playback requires a live Node3D anchor.");
        var transform = options.Transform ?? (placement == VfxPlacement.World && GodotObject.IsInstanceValid(anchor) && anchor!.IsInsideTree()
            ? anchor.GlobalTransform : Transform3D.Identity);
        transform *= definition.Offset;
        if (!transform.IsFinite()) return Reject(id, "Transform must be finite.");
        Node instance;
        try { instance = definition.Scene!.Instantiate(); }
        catch (Exception exception) { return Reject(id, exception.Message); }
        if (instance is not VfxEffect effect) { instance.Free(); return Reject(id, "PackedScene root must be VfxEffect (Node3D)."); }
        var handle = new VfxHandle(this, ++_generation, id) { Effect = effect };
        _active.Add(handle.Generation, handle);
        var parameters = options.Parameters?.Duplicate() ?? new Godot.Collections.Dictionary<StringName, Variant>();
        var context = new VfxContext(handle.Generation, mode, CancellationToken.None, parameters);
        effect.Prepare(context, definition.OneShotTimeout, definition.StopTimeout, (reason, detail) => End(handle, reason, detail));
        // All descendants inherit one pause clock, regardless of the selected attachment parent.
        effect.ProcessMode = definition.ProcessWhilePaused ? ProcessModeEnum.Always : ProcessModeEnum.Pausable;
        if (placement == VfxPlacement.World)
        {
            effect.TopLevel = true;
            effect.Transform = transform;
            AddChild(effect);
        }
        else { effect.TopLevel = false; effect.Transform = transform; anchor!.AddChild(effect); }
        if (!GodotObject.IsInstanceValid(effect) || !effect.IsInsideTree() || effect.IsQueuedForDeletion())
        {
            End(handle, VfxEndReason.RootExited, "Effect removed itself while entering the tree.");
            if (GodotObject.IsInstanceValid(effect) && !effect.IsQueuedForDeletion()) effect.QueueFree();
            return handle;
        }
        effect.ResetPhysicsInterpolation();
        // Ready callbacks can stop/remove playback: Prepare already installed terminal cleanup.
        effect.Begin();
        if (handle.IsActive) EmitSignal(SignalName.PlaybackStarted, handle);
        return handle;
    }

    public bool Stop(VfxHandle handle, VfxStopMode mode = VfxStopMode.Graceful)
    {
        if (!Enum.IsDefined(mode) || !_active.TryGetValue(handle.Generation, out var current) || !ReferenceEquals(current, handle) || !handle.IsActive) return false;
        if (mode == VfxStopMode.Graceful) handle.State = VfxState.Stopping;
        handle.Effect!.Stop(mode);
        return true;
    }
    public void StopAll(VfxStopMode mode = VfxStopMode.Graceful)
    {
        // Snapshot: completion handlers can modify the registry or start a new playback.
        foreach (var handle in new List<VfxHandle>(_active.Values)) Stop(handle, mode);
    }
    private void End(VfxHandle handle, VfxEndReason reason, string error)
    {
        if (!_active.Remove(handle.Generation)) return;
        handle.Resolve(reason, error);
        if (GodotObject.IsInstanceValid(this)) EmitSignal(SignalName.PlaybackEnded, handle, (long)reason);
    }
    private VfxHandle? Reject(StringName id, string message) { EmitSignal(SignalName.PlaybackFailed, id, message); return null; }
    private static bool Validate(VfxDefinition? definition, out string error)
    {
        error = "";
        if (!GodotObject.IsInstanceValid(definition)) error = "Definition is null or freed.";
        else if (string.IsNullOrWhiteSpace(definition!.Id.ToString())) error = "Definition ID must not be empty.";
        else if (!Enum.IsDefined(definition.Placement) || !Enum.IsDefined(definition.Mode)) error = "Invalid definition placement/playback mode.";
        else if (!definition.Offset.IsFinite()) error = "Definition offset must be finite.";
        else if (definition.Scene == null) error = "Definition has no scene.";
        else if (!double.IsFinite(definition.OneShotTimeout) || definition.OneShotTimeout <= 0 || !double.IsFinite(definition.StopTimeout) || definition.StopTimeout <= 0)
            error = "Completion timeouts must be positive finite values.";
        return error.Length == 0;
    }
    public override void _ExitTree()
    {
        _exiting = true;
        foreach (var handle in new List<VfxHandle>(_active.Values))
        {
            if (GodotObject.IsInstanceValid(handle.Effect)) handle.Effect!.Stop(VfxStopMode.Immediate);
            else End(handle, VfxEndReason.Cancelled, "Playback component left the tree.");
        }
        _active.Clear();
    }
    public override void _EnterTree() => _exiting = false;
}
