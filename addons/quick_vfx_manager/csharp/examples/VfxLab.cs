using Godot;
using QuickVfx;

namespace QuickVfx.Examples;

/// <summary>Standalone view-only lab. Every visual effect is an authored PackedScene.</summary>
public partial class VfxLab : Node3D
{
    private VfxPlayback _playback = null!;
    private Node3D _worldSource = null!, _attachment = null!;
    private Label _worldStatus = null!, _burstStatus = null!, _loopStatus = null!, _activity = null!;
    private CheckButton _auto = null!;
    private VfxHandle? _loop;
    private double _motion, _shotClock, _loopClock;
    private bool _stopNextImmediately;
    private int _worldGeneration, _burstGeneration;

    public override void _Ready()
    {
        _playback = GetNode<VfxPlayback>("VfxPlayback");
        _worldSource = GetNode<Node3D>("WorldSource");
        _attachment = GetNode<Node3D>("Attachment");
        _worldStatus = GetNode<Label>("Interface/Panel/Cards/World/Content/Status");
        _burstStatus = GetNode<Label>("Interface/Panel/Cards/Composite/Content/Status");
        _loopStatus = GetNode<Label>("Interface/Panel/Cards/Attached/Content/Status");
        _activity = GetNode<Label>("Interface/Activity");
        _auto = GetNode<CheckButton>("Interface/AutoDemo");
        GetNode<Button>("Interface/Panel/Cards/World/Content/Play").Pressed += () => { Manual(); PlayWorld(); };
        GetNode<Button>("Interface/Panel/Cards/Composite/Content/Play").Pressed += () => { Manual(); PlayBurst(); };
        GetNode<Button>("Interface/Panel/Cards/Attached/Content/Play").Pressed += () => { Manual(); StartLoop(); };
        GetNode<Button>("Interface/Panel/Cards/Attached/Content/Stops/Graceful").Pressed += () => { Manual(); StopLoop(VfxStopMode.Graceful); };
        GetNode<Button>("Interface/Panel/Cards/Attached/Content/Stops/Immediate").Pressed += () => { Manual(); StopLoop(VfxStopMode.Immediate); };
        _auto.Toggled += enabled =>
        {
            _shotClock = _loopClock = 0;
            if (enabled) { PlayWorld(); PlayBurst(); StartLoop(); }
        };
        _playback.PlaybackFailed += (id, error) => _activity.Text = $"Configuration error / {id}: {error}";
        _playback.PlaybackEnded += (handle, reason) =>
        {
            if ((VfxEndReason)reason is not (VfxEndReason.Completed or VfxEndReason.Stopped or VfxEndReason.Cancelled))
                _activity.Text = $"{handle.DefinitionId} / {(VfxEndReason)reason}: {handle.Error}";
        };
        PlayWorld();
        PlayBurst();
        StartLoop();
    }

    public override void _Process(double delta)
    {
        _motion += delta;
        // These sources are presentation anchors, with no gameplay or input add-on dependencies.
        _worldSource.Position = new Vector3(-4.1f + (float)System.Math.Sin(_motion * 1.35) * 0.85f, 0.42f, (float)System.Math.Cos(_motion * 1.35) * 0.28f);
        _attachment.Position = new Vector3(4.1f + (float)System.Math.Sin(_motion * 1.35) * 0.85f, 0.62f + (float)System.Math.Sin(_motion * 2.7) * 0.12f, (float)System.Math.Cos(_motion * 1.35) * 0.28f);
        _attachment.RotateY((float)delta * 0.7f);
        if (!_auto.ButtonPressed) return;
        _shotClock += delta;
        _loopClock += delta;
        if (_shotClock >= 3.0)
        {
            _shotClock = 0;
            PlayWorld();
            PlayBurst();
        }
        if (_loopClock >= 5.0 && _loop?.State == VfxState.Playing)
            StopLoop(_stopNextImmediately ? VfxStopMode.Immediate : VfxStopMode.Graceful);
        if (_loopClock >= 7.2)
        {
            _loopClock = 0;
            _stopNextImmediately = !_stopNextImmediately;
            StartLoop();
        }
    }

    private void Manual() => _auto.ButtonPressed = false;

    private void PlayWorld()
    {
        var generation = ++_worldGeneration;
        var handle = _playback.PlayAt("world_pulse", _worldSource.GlobalTransform);
        if (handle == null) return;
        _worldStatus.Text = $"PLAYING  /  #{handle.Generation} · fixed world transform";
        handle.Ended += reason =>
        {
            if (generation == _worldGeneration)
                _worldStatus.Text = $"{(VfxEndReason)reason}  /  #{handle.Generation} · {((VfxEndReason)reason == VfxEndReason.Completed ? "all tails finished" : "handle finished")}";
        };
    }

    private void PlayBurst()
    {
        var generation = ++_burstGeneration;
        var handle = _playback.PlayAt("composite_burst", new Transform3D(Basis.Identity, new Vector3(0, 0.35f, 0)));
        if (handle == null) return;
        _burstStatus.Text = $"PLAYING  /  #{handle.Generation} · 3 participants";
        handle.Ended += reason =>
        {
            if (generation == _burstGeneration)
                _burstStatus.Text = $"{(VfxEndReason)reason}  /  #{handle.Generation} · {((VfxEndReason)reason == VfxEndReason.Completed ? "slowest tail wins" : "handle finished")}";
        };
    }

    private void StartLoop()
    {
        if (_loop?.IsActive == true)
        {
            _activity.Text = "The attached loop already has a live handle. Stop it before starting another.";
            return;
        }
        var handle = _playback.PlayAttached("attached_loop", _attachment);
        if (handle == null) return;
        _loop = handle;
        _loopStatus.Text = $"LOOPING  /  #{handle.Generation} · follows the anchor";
        _activity.Text = "One-shot tails finish independently. The attached loop stays alive until its handle is stopped.";
        handle.Ended += reason =>
        {
            if (_loop != handle) return;
            _loopStatus.Text = $"{(VfxEndReason)reason}  /  #{handle.Generation} · handle finished";
            _activity.Text = (VfxEndReason)reason switch
            {
                VfxEndReason.Stopped => "Graceful stop completed: emission stopped, the authored outro played, and the particle tail drained.",
                VfxEndReason.Cancelled => "Immediate stop completed: the effect was cancelled and removed without waiting for its visual tails.",
                _ => $"Attached loop ended with {((VfxEndReason)reason)}: {handle.Error}"
            };
        };
    }

    private void StopLoop(VfxStopMode mode)
    {
        if (_loop?.IsActive != true)
        {
            _activity.Text = "Start the attached loop first, then compare the two stop modes.";
            return;
        }
        if (mode == VfxStopMode.Graceful)
        {
            _loopStatus.Text = $"STOPPING  /  #{_loop.Generation} · waiting for every tail";
            _activity.Text = "Graceful: stop new particles, play the outro, fade the instance shader, then release the handle.";
        }
        _loop.Stop(mode);
    }
}
