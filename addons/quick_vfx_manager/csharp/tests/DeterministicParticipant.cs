using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace QuickVfx.Tests;

/// <summary>A deterministic test double. Negative frame budgets mean "wait until stopped".</summary>
public partial class DeterministicParticipant : VfxParticipant
{
    [Export] public string ProbeId { get; set; } = "probe";
    [Export] public int StartFrames { get; set; } = -1;
    [Export] public int StopFrames { get; set; }
    [Export] public double AsyncDelay { get; set; }
    public sealed class Observation
    {
        public int Starts, Stops, Cancels, Frames, AsyncCallbacks, CancelledCallbacks;
        public long Generation;
        public bool TokenCancelled;
        public Exception? AsyncError;
    }
    public static readonly Dictionary<string, Observation> Observations = new();
    public static readonly Dictionary<string, Action<DeterministicParticipant>> StartActions = new();
    public static readonly Dictionary<string, Action<DeterministicParticipant>> CancellationActions = new();
    public static Observation Read(string id) => Observations.TryGetValue(id, out var observation)
        ? observation : Observations[id] = new Observation();
    private int _remaining;
    private long _capturedGeneration;
    public long CapturedGeneration => _capturedGeneration;
    protected override void StartEffect(VfxContext context)
    {
        var observation = Read(ProbeId);
        observation.Starts++;
        observation.Generation = _capturedGeneration = context.Generation;
        context.Cancellation.Register(() => observation.TokenCancelled = true);
        if (CancellationActions.TryGetValue(ProbeId, out var onCancelled)) context.Cancellation.Register(() => onCancelled(this));
        _remaining = StartFrames;
        if (_remaining == 0) Complete(context.Generation);
        if (AsyncDelay > 0) _ = CompleteLater(context, observation);
        if (StartActions.TryGetValue(ProbeId, out var onStart)) onStart(this);
    }
    private async Task CompleteLater(VfxContext context, Observation observation)
    {
        try
        {
            // Task.Delay models external work whose callback survives the originating node.
            // Godot's synchronization context resumes this continuation on the main thread.
            await Task.Delay(TimeSpan.FromSeconds(AsyncDelay));
            observation.AsyncCallbacks++;
            if (context.Cancellation.IsCancellationRequested) observation.CancelledCallbacks++;
            // Even an uncancelled callback must carry its captured generation, never a newer one.
            Complete(context.Generation);
        }
        catch (Exception exception) { observation.AsyncError = exception; }
    }
    protected override void StopEffect()
    {
        Read(ProbeId).Stops++;
        _remaining = StopFrames;
        if (_remaining == 0) Complete(_capturedGeneration);
    }
    protected override void CancelEffect() => Read(ProbeId).Cancels++;
    public void CompleteForTest(long generation) => Complete(generation);
    public override void _Process(double delta)
    {
        if (!Running) return;
        Read(ProbeId).Frames++;
        if (_remaining > 0 && --_remaining == 0) Complete(_capturedGeneration);
    }
}
