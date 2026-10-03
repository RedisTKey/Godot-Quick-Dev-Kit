using Godot;
using System;
using System.Collections.Generic;
using System.Threading;

namespace QuickVfx;

/// <summary>Required scene root. Coordinates every descendant participant, including composite tails.</summary>
[GlobalClass]
public partial class VfxEffect : Node3D
{
    [Signal] public delegate void StartedEventHandler(long generation);
    [Signal] public delegate void StopRequestedEventHandler();
    [Signal] public delegate void FinishedEventHandler(long reason);
    private readonly List<VfxParticipant> _parts = new();
    private readonly CancellationTokenSource _cancellation = new();
    private VfxContext? _context;
    private Action<VfxEndReason, string>? _ended;
    private bool _starting, _terminal, _stopping, _stopPending;
    private double _elapsed, _oneShotTimeout, _stopTimeout;
    public VfxContext? Context => _context;

    internal void Prepare(VfxContext context, double oneShotTimeout, double stopTimeout, Action<VfxEndReason, string> ended)
    {
        if (_context != null) throw new InvalidOperationException("VfxEffect instances cannot be replayed.");
        _context = context with { Cancellation = _cancellation.Token };
        _ended = ended;
        _oneShotTimeout = oneShotTimeout;
        _stopTimeout = stopTimeout;
    }

    internal void Begin()
    {
        if (_terminal || _context == null) return;
        try
        {
            Collect(this);
            if (_parts.Count == 0) throw new InvalidOperationException("The effect needs at least one VfxParticipant.");
            _starting = true;
            foreach (var part in _parts) { part.Finished += OnPartFinished; part.Failed += OnPartFailed; }
            foreach (var part in _parts)
            {
                if (_terminal) break;
                part.Begin(_context);
            }
            _starting = false;
            if (_terminal) return;
            if (_stopPending) { _stopPending = false; Stop(VfxStopMode.Graceful); }
            if (_terminal) return;
            EmitSignal(SignalName.Started, _context.Generation);
            CheckComplete();
        }
        catch (Exception exception) { Finish(VfxEndReason.InvalidConfiguration, exception.Message); }
    }

    private void Collect(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            child.ProcessMode = ProcessModeEnum.Inherit;
            if (child is VfxEffect) throw new InvalidOperationException("Nested VfxEffect roots are not supported; use participants in one scene.");
            if (child is VfxParticipant part) _parts.Add(part);
            Collect(child);
        }
    }
    private void OnPartFinished(VfxParticipant _) => CheckComplete();
    private void OnPartFailed(string error) => Finish(VfxEndReason.InvalidConfiguration, error);
    private void CheckComplete()
    {
        if (_terminal || _starting || _context == null || (!_stopping && _context.Mode == VfxMode.Loop)) return;
        foreach (var part in _parts) if (!part.IsFinished) return;
        Finish(_stopping ? VfxEndReason.Stopped : VfxEndReason.Completed);
    }

    internal void Stop(VfxStopMode mode)
    {
        if (_terminal) return;
        if (mode == VfxStopMode.Immediate) { Finish(VfxEndReason.Cancelled); return; }
        if (_stopping) return;
        if (_starting) { _stopPending = true; return; }
        _stopping = true;
        _elapsed = 0;
        _starting = true;
        try
        {
            EmitSignal(SignalName.StopRequested);
            foreach (var part in _parts)
            {
                if (_terminal) break;
                if (GodotObject.IsInstanceValid(part)) part.StopGracefully();
            }
        }
        catch (Exception exception) { Finish(VfxEndReason.InvalidConfiguration, exception.Message); }
        finally { _starting = false; }
        CheckComplete();
    }

    public override void _Process(double delta)
    {
        if (_terminal || _context == null) return;
        foreach (var part in _parts)
        {
            if (!GodotObject.IsInstanceValid(part) || !part.IsInsideTree() || part.IsQueuedForDeletion())
            { Finish(VfxEndReason.ParticipantLost, "A participant was removed before effect completion."); return; }
        }
        _elapsed += delta;
        if ((_stopping && _elapsed >= _stopTimeout) || (!_stopping && _context.Mode == VfxMode.OneShot && _elapsed >= _oneShotTimeout))
            Finish(VfxEndReason.TimedOut, _stopping ? "Graceful stop exceeded StopTimeout." : "One-shot exceeded OneShotTimeout.");
    }

    private void Finish(VfxEndReason reason, string error = "", bool free = true)
    {
        if (_terminal) return;
        _terminal = true;
        SetProcess(false);
        try { _cancellation.Cancel(); } catch (AggregateException exception) { GD.PushWarning($"VFX cancellation callback: {exception.Message}"); }
        foreach (var part in _parts)
        {
            part.Finished -= OnPartFinished;
            part.Failed -= OnPartFailed;
            if (!GodotObject.IsInstanceValid(part)) continue;
            try { part.Cancel(); } catch (Exception exception) { GD.PushWarning($"VFX cleanup: {exception.Message}"); }
        }
        if (GodotObject.IsInstanceValid(this)) Visible = false;
        var ended = _ended;
        _ended = null;
        // Resolve/remove the handle before external signals; callbacks may immediately play another effect.
        ended?.Invoke(reason, error);
        if (GodotObject.IsInstanceValid(this))
        {
            EmitSignal(SignalName.Finished, (long)reason);
            if (free && GodotObject.IsInstanceValid(this) && !IsQueuedForDeletion()) QueueFree();
        }
    }

    public override void _ExitTree()
    {
        Finish(VfxEndReason.RootExited, "Effect or attachment anchor left the scene tree.");
        _cancellation.Dispose();
    }
}
