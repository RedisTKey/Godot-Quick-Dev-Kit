using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QuickVfx.Tests;

/// <summary>Headless runtime/serialization regressions. Exits 1 on any failed test.</summary>
public partial class VfxTests : Node
{
    private int _passed, _failed, _checks, _serial;
    private readonly List<Node> _temporaryNodes = new();
    private string Next(string prefix) => $"{prefix}_{++_serial}";
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Engine.MaxFps = 120;
        CallDeferred(MethodName.RunSuite);
    }
    private async void RunSuite()
    {
        try
        {
            await Run("registration, duplicates, null and atomic reload", Registration);
            await Run("world snapshots and attached local transforms", Transforms);
            await Run("one-shot completion and looping lifetime", LifetimeModes);
            await Run("all-participant start and stop barriers", CompletionBarrier);
            await Run("graceful drain and immediate cancellation", StopModes);
            await Run("reentrant stop and completion callback replay", Reentrancy);
            await Run("startup stop, cancellation and free callbacks", AdversarialCallbacks);
            await Run("old handles and captured generations", GenerationSafety);
            await Run("owner, anchor, root and participant deletion", Deletion);
            await Run("scene replacement cleanup", SceneReplacement);
            await Run("paused freeze and ProcessWhilePaused", PauseClocks);
            await Run("one-shot and graceful-stop timeout", Timeouts);
            await Run("invalid packed scenes and animation configuration", InvalidScenes);
            await Run("actual AnimationPlayer loop outro", AnimationOutro);
            await Run("native GPU particle one-shot and simulation-speed gates", NativeParticleOneShots);
            await Run("native GPU particle graceful loop drain", NativeParticleLoopDrain);
            await Run("authored WorldPulse and CompositeBurst natural completion", AuthoredSceneCompletion);
            await Run("cancelled custom async callbacks", AsyncCancellation);
            await Run("per-play overrides and copied custom parameters", ParametersAndOverrides);
            await Run("typed .tres/.tscn Inspector round-trip", Serialization);
        }
        catch (Exception exception)
        {
            _failed++;
            GD.PushError($"VFX SUITE ERROR: {exception}");
        }
        finally
        {
            GetTree().Paused = false;
            GD.Print($"VFX TESTS: {_passed} passed, {_failed} failed; {_checks} assertions");
            GetTree().Quit(_failed == 0 ? 0 : 1);
        }
    }
    private async Task Run(string name, Func<Task> test)
    {
        try { await test(); _passed++; GD.Print($"PASS: {name}"); }
        catch (Exception exception) { _failed++; GD.PushError($"FAIL: {name}\n{exception}"); }
        finally
        {
            GetTree().Paused = false;
            foreach (var node in _temporaryNodes)
                if (GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion()) node.QueueFree();
            _temporaryNodes.Clear();
            await Frames(3);
        }
    }
    private void Check(bool condition, string message)
    { _checks++; if (!condition) throw new InvalidOperationException(message); }
    private async Task Frames(int count = 1)
    { for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Delay(double seconds)
    { await ToSignal(GetTree().CreateTimer(seconds, processAlways: true), SceneTreeTimer.SignalName.Timeout); await Frames(2); }
    private T Track<T>(T node) where T : Node
    { _temporaryNodes.Add(node); return node; }
    private Node3D Anchor(string name = "Anchor")
    { var anchor = Track(new Node3D { Name = name, ProcessMode = ProcessModeEnum.Pausable }); AddChild(anchor); return anchor; }
    private VfxPlayback Player(params VfxDefinition[] definitions)
    {
        var owner = Anchor(Next("Owner"));
        var player = new VfxPlayback { Name = "Playback", Definitions = new(definitions) };
        owner.AddChild(player);
        return player;
    }
    private static PackedScene Pack(Node root)
    {
        void Assign(Node node) { foreach (var child in node.GetChildren()) { child.Owner = root; Assign(child); } }
        Assign(root);
        var packed = new PackedScene();
        var result = packed.Pack(root);
        root.Free();
        if (result != Error.Ok) throw new InvalidOperationException($"Pack failed: {result}");
        return packed;
    }
    private static DeterministicParticipant Probe(string id, int start = -1, int stop = 0, double asyncDelay = 0) =>
        new() { Name = id, ProbeId = id, StartFrames = start, StopFrames = stop, AsyncDelay = asyncDelay };
    private static PackedScene EffectScene(params DeterministicParticipant[] parts)
    {
        var effect = new VfxEffect { Name = "TestEffect" };
        foreach (var part in parts) effect.AddChild(part);
        return Pack(effect);
    }
    private VfxDefinition Definition(string id, PackedScene? scene = null, VfxMode mode = VfxMode.OneShot) =>
        new() { Id = id, Scene = scene ?? EffectScene(Probe(Next("hold"))), Mode = mode, OneShotTimeout = 3, StopTimeout = 3 };
    private VfxHandle Play(VfxPlayback player, string id, VfxPlayOptions? options = null)
    { var handle = player.Play(id, options); Check(handle != null, $"Play({id}) returned null"); return handle!; }
    private void Ended(VfxHandle handle, VfxEndReason reason, string context)
    { Check(handle.State == VfxState.Finished && !handle.IsActive && handle.EndReason == reason, $"{context}: expected {reason}, got {handle.State}/{handle.EndReason}: {handle.Error}"); }

    private Task Registration()
    {
        var stable = Definition("stable");
        var player = Player(stable);
        var errors = 0;
        player.PlaybackFailed += (_, _) => errors++;
        Check(player.HasDefinition("stable"), "Exported definitions not loaded on Ready");
        Check(!player.Register(stable), "Duplicate registration accepted");
        Check(!player.Register(null!), "Null definition accepted");
        Check(!player.Register(new VfxDefinition { Id = "missing-scene" }), "Missing scene accepted");
        Check(!player.Register(Definition(" ")), "Blank ID accepted");
        var badTimeout = Definition("bad-timeout"); badTimeout.StopTimeout = double.NaN;
        Check(!player.Register(badTimeout), "Non-finite timeout accepted");
        Check(player.Register(Definition("stable"), replace: true), "Explicit replacement rejected");
        player.Definitions = new() { Definition("new"), Definition("new") };
        Check(!player.ReloadDefinitions(), "Duplicate reload accepted");
        Check(player.HasDefinition("stable") && !player.HasDefinition("new"), "Failed reload partially changed registry");
        player.Definitions = new() { Definition("valid"), null! };
        Check(!player.ReloadDefinitions() && !player.HasDefinition("valid") && player.HasDefinition("stable"), "Null reload was not atomic");
        Check(player.Play("unknown") == null, "Unknown ID returned a handle");
        Check(errors == 8, $"Expected 8 validation events, got {errors}");
        player.Definitions = new() { Definition("new") };
        Check(player.ReloadDefinitions() && player.HasDefinition("new") && !player.HasDefinition("stable"), "Valid reload failed");
        Check(player.Unregister("new") && !player.Unregister("new"), "Unregister result incorrect");
        return Task.CompletedTask;
    }
    private async Task Transforms()
    {
        var authoredTopLevel = new VfxEffect { TopLevel = true }; authoredTopLevel.AddChild(Probe(Next("authored_top_level")));
        var def = Definition("transform", Pack(authoredTopLevel), VfxMode.Loop);
        def.Offset = new Transform3D(Basis.FromEuler(new Vector3(0, 0.3f, 0)), new Vector3(0, 2, 1));
        var player = Player(def);
        var anchor = Anchor(); anchor.Transform = new Transform3D(Basis.FromEuler(new Vector3(0, 0.5f, 0)), new Vector3(10, 0, 0));
        var requested = new Transform3D(Basis.FromEuler(new Vector3(0, 0.7f, 0)), new Vector3(4, 5, 6));
        var world = player.PlayAt("transform", requested)!;
        var attached = player.PlayAttached("transform", anchor, requested)!;
        Check(world.GetEffect()!.GlobalTransform.IsEqualApprox(requested * def.Offset), "World transform/offset multiplication incorrect");
        Check(attached.GetEffect()!.Transform.IsEqualApprox(requested * def.Offset), "Attached local transform/offset incorrect");
        var worldBefore = world.GetEffect()!.GlobalTransform;
        anchor.Position += new Vector3(5, 0, 0);
        ((Node3D)player.GetParent()).Position = new Vector3(100, 100, 100);
        await Frames(2);
        Check(world.GetEffect()!.GlobalTransform.IsEqualApprox(worldBefore), "World effect moved with caller");
        Check(attached.GetEffect()!.GlobalTransform.IsEqualApprox(anchor.GlobalTransform * requested * def.Offset), "Attached effect did not follow anchor");
        player.DefaultAnchor = anchor;
        var defaults = Play(player, "transform");
        Check(defaults.GetEffect()!.GlobalTransform.IsEqualApprox(anchor.GlobalTransform * def.Offset), "Default world anchor snapshot incorrect");
        var detached = new Node3D();
        Check(player.PlayAttached("transform", detached) == null, "Detached anchor accepted"); detached.Free();
    }
    private async Task LifetimeModes()
    {
        var player = Player(Definition("shot", EffectScene(Probe(Next("shot"), 3))), Definition("loop", EffectScene(Probe(Next("finite_loop"), 1)), VfxMode.Loop));
        var shot = Play(player, "shot"); var loop = Play(player, "loop");
        await Frames(7);
        Ended(shot, VfxEndReason.Completed, "One-shot");
        Check(loop.IsActive && player.ActiveCount == 1, "Loop ended when its finite participant finished");
        Check(loop.Stop(), "Loop stop rejected"); Ended(loop, VfxEndReason.Stopped, "Loop stop after participant completion");
        Check(player.ActiveCount == 0, "Completed handles retained");
    }
    private async Task CompletionBarrier()
    {
        var startSlow = Next("start_slow"); var stopSlow = Next("stop_slow");
        var player = Player(
            Definition("start", EffectScene(Probe(Next("start_sync"), 0), Probe(startSlow, 4))),
            Definition("stop", EffectScene(Probe(Next("stop_sync")), Probe(stopSlow, -1, 4)), VfxMode.Loop));
        var start = Play(player, "start");
        Check(start.IsActive && DeterministicParticipant.Read(startSlow).Starts == 1, "Synchronous first participant ended effect before remaining participants started");
        await Frames(2); Check(start.IsActive, "Start barrier ended before slow participant");
        await Frames(5); Ended(start, VfxEndReason.Completed, "Start barrier");
        var stop = Play(player, "stop"); stop.Stop();
        Check(stop.State == VfxState.Stopping && DeterministicParticipant.Read(stopSlow).Stops == 1, "Synchronous stop skipped remaining participant");
        await Frames(2); Check(stop.IsActive, "Stop barrier ended before slow tail");
        await Frames(5); Ended(stop, VfxEndReason.Stopped, "Stop barrier");
    }
    private async Task StopModes()
    {
        var slow = Next("drain");
        var player = Player(Definition("drain", EffectScene(Probe(slow, -1, 4)), VfxMode.Loop));
        var graceful = Play(player, "drain");
        Check(graceful.Stop() && graceful.State == VfxState.Stopping, "Graceful stop did not enter Stopping");
        graceful.Stop(); Check(DeterministicParticipant.Read(slow).Stops == 1, "Repeated graceful stop reset tail");
        await Frames(7); Ended(graceful, VfxEndReason.Stopped, "Graceful drain");
        var immediate = Play(player, "drain"); var effect = immediate.GetEffect()!;
        Check(immediate.Stop(VfxStopMode.Immediate), "Immediate stop rejected");
        Ended(immediate, VfxEndReason.Cancelled, "Immediate stop");
        Check(!effect.Visible && effect.IsQueuedForDeletion(), "Immediate cancellation did not hide and queue-free root immediately");
        Check(DeterministicParticipant.Read(slow).TokenCancelled, "Cancellation token not signalled");
    }
    private Task Reentrancy()
    {
        var player = Player(Definition("loop", mode: VfxMode.Loop));
        var original = Play(player, "loop"); var oldRoot = original.GetEffect()!; VfxHandle? replay = null; var ends = 0;
        player.PlaybackEnded += (handle, _) => { ends++; if (ReferenceEquals(handle, original)) replay = player.Play("loop"); };
        oldRoot.StopRequested += () => original.Stop(VfxStopMode.Immediate);
        Check(original.Stop(), "Reentrant graceful request rejected");
        Ended(original, VfxEndReason.Cancelled, "Reentrant immediate stop");
        Check(ends == 1 && replay?.IsActive == true && player.ActiveCount == 1, "PlaybackEnded replay corrupted active registry");
        Check(!original.Stop(VfxStopMode.Immediate) && replay!.IsActive, "Old handle affected replay");
        VfxHandle? fromStopAll = null;
        player.PlaybackEnded += (handle, _) => { if (ReferenceEquals(handle, replay)) fromStopAll = player.Play("loop"); };
        player.StopAll(VfxStopMode.Immediate);
        Check(fromStopAll?.IsActive == true && player.ActiveCount == 1, "StopAll snapshot cancelled playback created by callback");
        fromStopAll!.Stop(VfxStopMode.Immediate);
        return Task.CompletedTask;
    }
    private Task AdversarialCallbacks()
    {
        var earlyId = Next("early_stop"); var lateId = Next("late_stop");
        var player = Player(Definition("startup", EffectScene(Probe(earlyId), Probe(lateId)), VfxMode.Loop));
        DeterministicParticipant.StartActions[earlyId] = _ => player.StopAll();
        var handle = Play(player, "startup");
        Ended(handle, VfxEndReason.Stopped, "Graceful stop during first participant startup");
        Check(DeterministicParticipant.Read(lateId).Starts == 1 && DeterministicParticipant.Read(lateId).Stops == 1, "Startup stop skipped late participant stop");
        var throwId = Next("throw_cancel");
        DeterministicParticipant.CancellationActions[throwId] = _ => throw new InvalidOperationException("EXPECTED test cancellation exception");
        player.Register(Definition("throw", EffectScene(Probe(throwId)), VfxMode.Loop));
        var throwing = Play(player, "throw"); throwing.Stop(VfxStopMode.Immediate);
        Ended(throwing, VfxEndReason.Cancelled, "Throwing cancellation callback");
        var freeId = Next("free_cancel");
        DeterministicParticipant.CancellationActions[freeId] = participant => participant.GetParent().Free();
        player.Register(Definition("free", EffectScene(Probe(freeId)), VfxMode.Loop));
        var freeing = Play(player, "free"); freeing.Stop(VfxStopMode.Immediate);
        Ended(freeing, VfxEndReason.Cancelled, "Free root inside cancellation callback");
        player.Register(Definition("signal-free", mode: VfxMode.Loop));
        var signalFree = Play(player, "signal-free"); var effect = signalFree.GetEffect()!;
        // Godot locks a signal emitter against Free during its signal; QueueFree is the supported action.
        effect.Finished += _ => effect.QueueFree();
        signalFree.Stop(VfxStopMode.Immediate);
        Ended(signalFree, VfxEndReason.Cancelled, "QueueFree root inside Finished callback");
        Check(player.ActiveCount == 0, "Adversarial callbacks leaked active handles");
        return Task.CompletedTask;
    }
    private Task GenerationSafety()
    {
        var player = Player(Definition("hold"));
        var old = Play(player, "hold"); var participant = old.GetEffect()!.GetChild<DeterministicParticipant>(0);
        participant.CompleteForTest(old.Generation + 99);
        Check(old.IsActive, "Wrong captured generation completed current effect");
        old.Stop(VfxStopMode.Immediate);
        var next = Play(player, "hold");
        participant.CompleteForTest(old.Generation);
        Check(next.Generation > old.Generation && next.IsActive && !old.Stop(), "Stale completion or handle modified newer playback");
        var another = Player(Definition("hold")); var foreign = Play(another, "hold");
        Check(!player.Stop(foreign) && foreign.IsActive && next.IsActive, "Foreign handle accepted by another component");
        return Task.CompletedTask;
    }
    private async Task Deletion()
    {
        var player = Player(Definition("hold", mode: VfxMode.Loop)); var anchor = Anchor();
        var owned = player.PlayAttached("hold", anchor)!; var ended = 0; owned.Ended += _ => ended++;
        player.QueueFree(); await Frames(4);
        Ended(owned, VfxEndReason.Cancelled, "Owner deletion with external anchor"); Check(ended == 1, "Owner deletion emitted multiple terminals");
        var survivor = Player(Definition("hold", mode: VfxMode.Loop));
        var anchored = survivor.PlayAttached("hold", anchor)!; anchor.QueueFree(); await Frames(4);
        Ended(anchored, VfxEndReason.RootExited, "Anchor deletion");
        Check(survivor.ActiveCount == 0, "Deleted anchor retained handle");
        var rootDeleted = Play(survivor, "hold"); rootDeleted.GetEffect()!.QueueFree(); await Frames(4);
        Ended(rootDeleted, VfxEndReason.RootExited, "Effect root deletion");
        var partDeleted = Play(survivor, "hold"); partDeleted.GetEffect()!.GetChild(0).QueueFree(); await Frames(4);
        Ended(partDeleted, VfxEndReason.ParticipantLost, "Participant deletion");
    }
    private async Task SceneReplacement()
    {
        var tree = GetTree(); var originalScene = tree.CurrentScene;
        var outgoing = Track(new Node3D { Name = "OutgoingScene" }); tree.Root.AddChild(outgoing);
        var player = new VfxPlayback { Definitions = new() { Definition("scene-loop", mode: VfxMode.Loop) } }; outgoing.AddChild(player);
        var handle = Play(player, "scene-loop"); var ended = 0; handle.Ended += _ => ended++;
        tree.CurrentScene = outgoing;
        Check(tree.ChangeSceneToPacked(Pack(new Node3D { Name = "ReplacementScene" })) == Error.Ok, "Scene replacement call failed");
        await Frames(5);
        var replacement = tree.CurrentScene;
        tree.CurrentScene = originalScene;
        if (replacement != null && replacement != this) Track(replacement);
        Check(!handle.IsActive && handle.State == VfxState.Finished && ended == 1, "Scene replacement leaked active playback or repeated completion");
    }
    private async Task PauseClocks()
    {
        var frozenId = Next("frozen"); var activeId = Next("active_pause");
        var frozen = Definition("frozen", EffectScene(Probe(frozenId, 4))); frozen.OneShotTimeout = 0.2;
        var active = Definition("active", EffectScene(Probe(activeId, 4))); active.ProcessWhilePaused = true;
        var player = Player(frozen, active); var anchor = Anchor();
        var stopped = player.PlayAttached("frozen", anchor)!;
        GetTree().Paused = true;
        var live = player.PlayAttached("active", anchor)!;
        await Delay(0.3);
        Check(stopped.IsActive && DeterministicParticipant.Read(frozenId).Frames == 0, "Paused particle/custom clock or timeout kept advancing");
        Ended(live, VfxEndReason.Completed, "ProcessWhilePaused descendant");
        GetTree().Paused = false;
        await Frames(7); Ended(stopped, VfxEndReason.Completed, "Resume frozen playback");
        var tailId = Next("paused_tail"); var loop = Definition("paused_tail", EffectScene(Probe(tailId, -1, 4)), VfxMode.Loop);
        loop.StopTimeout = 0.2; player.Register(loop);
        var tail = Play(player, "paused_tail"); tail.Stop(); GetTree().Paused = true; await Delay(0.3);
        Check(tail.IsActive && tail.State == VfxState.Stopping && DeterministicParticipant.Read(tailId).Frames == 0, "Graceful tail or stop timeout advanced while paused");
        GetTree().Paused = false; await Frames(7); Ended(tail, VfxEndReason.Stopped, "Resume graceful tail");
    }
    private async Task Timeouts()
    {
        var shotDef = Definition("shot"); shotDef.OneShotTimeout = 0.04;
        var stopDef = Definition("stop", EffectScene(Probe(Next("never_stop"), -1, -1)), VfxMode.Loop); stopDef.StopTimeout = 0.04;
        var player = Player(shotDef, stopDef);
        var shot = Play(player, "shot"); var stop = Play(player, "stop"); stop.Stop();
        await Delay(0.1);
        Ended(shot, VfxEndReason.TimedOut, "One-shot timeout"); Ended(stop, VfxEndReason.TimedOut, "Graceful stop timeout");
        Check(shot.Error.Contains("OneShotTimeout") && stop.Error.Contains("StopTimeout"), "Timeout errors lost diagnostic context");
    }
    private static PackedScene AnimationScene(bool loop, string playName = "play", string stopName = "outro")
    {
        var root = new VfxEffect { Name = "AnimatedEffect" };
        var target = new AnimationPlayer { Name = "AnimationPlayer" };
        var library = new AnimationLibrary();
        library.AddAnimation("play", new Animation { Length = 0.03, LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None });
        library.AddAnimation("outro", new Animation { Length = 0.06 });
        target.AddAnimationLibrary("", library); root.AddChild(target);
        root.AddChild(new VfxAnimation { Name = "AnimationParticipant", Target = target, PlayAnimation = playName, StopAnimation = stopName });
        return Pack(root);
    }
    private async Task InvalidScenes()
    {
        var player = Player(Definition("wrong-root", Pack(new Node3D())), Definition("empty", Pack(new VfxEffect())),
            Definition("bad-animation", AnimationScene(false, "missing")), Definition("bad-loop", AnimationScene(true)),
            Definition("bad-outro", AnimationScene(false, "play", "missing")));
        Check(player.Play("wrong-root") == null && player.ActiveCount == 0, "Wrong packed root accepted or leaked");
        foreach (var id in new[] { "empty", "bad-animation", "bad-loop", "bad-outro" })
        {
            var handle = Play(player, id); Ended(handle, VfxEndReason.InvalidConfiguration, id); Check(handle.Error.Length > 0, "Invalid effect missing diagnostic");
        }
        var nested = new VfxEffect(); nested.AddChild(new VfxEffect()); player.Register(Definition("nested", Pack(nested)));
        Ended(Play(player, "nested"), VfxEndReason.InvalidConfiguration, "Nested root");
        var nullTarget = new VfxEffect(); nullTarget.AddChild(new VfxAnimation()); player.Register(Definition("null-target", Pack(nullTarget)));
        Ended(Play(player, "null-target"), VfxEndReason.InvalidConfiguration, "Null animation target");
        await Frames(2); Check(player.ActiveCount == 0, "Invalid effect retained handle");
    }
    private async Task AnimationOutro()
    {
        var player = Player(Definition("animated", AnimationScene(true), VfxMode.Loop), Definition("shot", AnimationScene(false, "play", "")),
            Definition("finite-intro", AnimationScene(false), VfxMode.Loop));
        var loop = Play(player, "animated");
        await Delay(0.08); Check(loop.IsActive, "Loop animation ended before stop");
        var target = loop.GetEffect()!.GetNode<AnimationPlayer>("AnimationPlayer"); loop.Stop();
        Check(loop.State == VfxState.Stopping && target.CurrentAnimation == "outro", "Stop did not interrupt infinite loop with outro");
        await Delay(0.13); Ended(loop, VfxEndReason.Stopped, "Real animation outro");
        var shot = Play(player, "shot"); await Delay(0.08); Ended(shot, VfxEndReason.Completed, "Real one-shot animation");
        var finiteIntro = Play(player, "finite-intro"); await Delay(0.08);
        Check(finiteIntro.IsActive, "Finite loop intro did not wait for Stop");
        var introPlayer = finiteIntro.GetEffect()!.GetNode<AnimationPlayer>("AnimationPlayer"); finiteIntro.Stop();
        Check(finiteIntro.IsActive && introPlayer.CurrentAnimation == "outro", "Finite intro completed participant before its outro");
        await Delay(0.12); Ended(finiteIntro, VfxEndReason.Stopped, "Finite intro loop outro");
    }
    private static PackedScene ParticleScene(double speedScale = 1, double lifetime = 0.08, double tailSeconds = 0.02)
    {
        var root = new VfxEffect { Name = "ParticleEffect" };
        var emitter = new GpuParticles3D
        {
            Name = "Particles", Emitting = false, OneShot = true, Amount = 4,
            Lifetime = lifetime, SpeedScale = speedScale, Explosiveness = 1,
            ProcessMaterial = new ParticleProcessMaterial(), DrawPass1 = new SphereMesh()
        };
        root.AddChild(emitter);
        root.AddChild(new VfxParticles { Name = "ParticleTail", Target = emitter, TailSeconds = tailSeconds });
        return Pack(root);
    }
    private async Task NativeParticleOneShots()
    {
        var normalDefinition = Definition("native-normal", ParticleScene());
        var slowDefinition = Definition("native-slow", ParticleScene(0.1, 0.06));
        var zeroDefinition = Definition("native-zero", ParticleScene(0, 0.06)); zeroDefinition.OneShotTimeout = 0.55;
        var player = Player(normalDefinition, slowDefinition, zeroDefinition);
        var normal = Play(player, "native-normal"); var slow = Play(player, "native-slow"); var zero = Play(player, "native-zero");
        var normalSignals = 0; var slowSignals = 0; var zeroSignals = 0;
        var normalEmitter = normal.GetEffect()!.GetNode<GpuParticles3D>("Particles");
        normalEmitter.Finished += () => normalSignals++;
        slow.GetEffect()!.GetNode<GpuParticles3D>("Particles").Finished += () => slowSignals++;
        zero.GetEffect()!.GetNode<GpuParticles3D>("Particles").Finished += () => zeroSignals++;
        Check(normalEmitter.OneShot && normalEmitter.Emitting, "Restart did not start the native one-shot emitter");
        await Delay(0.3);
        Ended(normal, VfxEndReason.Completed, "Native GPU particle one-shot");
        Check(normalSignals == 1, $"Native one-shot Finished must fire exactly once, got {normalSignals}");
        // Godot 4.7's native finished bookkeeping does not scale with SpeedScale.
        // The adapter must still wait for its speed-scaled conservative simulation budget.
        Check(slowSignals == 1 && slow.IsActive, $"Slow one-shot bypassed simulation-age gate or native signal missing: signals={slowSignals}, state={slow.State}");
        Check(zeroSignals == 1 && zero.IsActive, $"Zero-speed simulation completed despite no simulation progress: signals={zeroSignals}, state={zero.State}");
        await Delay(1.35);
        Ended(slow, VfxEndReason.Completed, "Low SpeedScale native particle gate");
        Ended(zero, VfxEndReason.TimedOut, "Zero SpeedScale native particle timeout");
        Check(player.ActiveCount == 0, "Native particle terminals leaked handles");
    }
    private async Task NativeParticleLoopDrain()
    {
        var player = Player(Definition("native-loop", ParticleScene(0.25, 0.06), VfxMode.Loop));
        var handle = Play(player, "native-loop");
        var emitter = handle.GetEffect()!.GetNode<GpuParticles3D>("Particles");
        Check(!emitter.OneShot && emitter.Emitting, "Loop mode failed to override emitter OneShot setting");
        await Delay(0.2); Check(handle.IsActive, "Native looping emitter ended before Stop");
        handle.Stop();
        Check(handle.State == VfxState.Stopping && !emitter.Emitting, "Graceful stop did not stop native emission and begin drain");
        // (Lifetime .06 + TailSeconds .02) / SpeedScale .25 = .32 simulation seconds.
        await Delay(0.12); Check(handle.IsActive, "Native loop drain ignored speed-scaled lifetime/tail budget");
        await Delay(0.3); Ended(handle, VfxEndReason.Stopped, "Native GPU particle loop drain");
        Check(player.ActiveCount == 0, "Native loop drain retained handle");
    }
    private async Task AuthoredSceneCompletion()
    {
        var pulseDefinition = ResourceLoader.Load<VfxDefinition>("res://addons/quick_vfx_manager/csharp/examples/definitions/WorldPulse.tres");
        var burstDefinition = ResourceLoader.Load<VfxDefinition>("res://addons/quick_vfx_manager/csharp/examples/definitions/CompositeBurst.tres");
        Check(pulseDefinition != null && burstDefinition != null, "Authored example definitions failed to load");
        var player = Player(pulseDefinition!, burstDefinition!);
        var pulse = Play(player, "world_pulse"); var burst = Play(player, "composite_burst");
        var pulseSignals = 0; var burstSignals = 0;
        pulse.GetEffect()!.GetNode<GpuParticles3D>("Particles").Finished += () => pulseSignals++;
        burst.GetEffect()!.GetNode<GpuParticles3D>("Particles").Finished += () => burstSignals++;
        await Delay(0.2);
        Check(pulse.IsActive && burst.IsActive, "Authored composites finished before all visual participants completed");
        await Delay(2.0);
        Ended(pulse, VfxEndReason.Completed, "Authored WorldPulse natural completion");
        Ended(burst, VfxEndReason.Completed, "Authored CompositeBurst natural completion");
        Check(pulseSignals == 1 && burstSignals == 1, $"Authored native particle signals missing/repeated: pulse={pulseSignals}, burst={burstSignals}");
        Check(player.ActiveCount == 0, "Authored natural completions left active handles");
    }
    private async Task AsyncCancellation()
    {
        var id = Next("async"); var player = Player(Definition("async", EffectScene(Probe(id, -1, -1, 0.08)), VfxMode.Loop));
        var first = Play(player, "async"); first.Stop(VfxStopMode.Immediate);
        var second = Play(player, "async"); await Delay(0.14);
        var observation = DeterministicParticipant.Read(id);
        Check(observation.AsyncError == null, $"Async callback threw: {observation.AsyncError}");
        Check(observation.AsyncCallbacks == 2 && observation.CancelledCallbacks == 1, $"Expected 2 async callbacks with 1 cancelled, got {observation.AsyncCallbacks}/{observation.CancelledCallbacks}");
        Check(second.IsActive && !first.Stop(), "Cancelled generation affected new effect");
        Ended(first, VfxEndReason.Cancelled, "Cancelled async effect");
        second.Stop(); Ended(second, VfxEndReason.Stopped, "Loop whose async participant finished");
    }
    private Task ParametersAndOverrides()
    {
        var def = Definition("overrides"); def.Placement = VfxPlacement.Attached;
        var originalOffset = def.Offset; var player = Player(def);
        var parameters = new Godot.Collections.Dictionary<StringName, Variant> { ["energy"] = 0.75, ["tint"] = Colors.Cyan };
        var handle = Play(player, "overrides", new VfxPlayOptions { Placement = VfxPlacement.World, Mode = VfxMode.Loop, Parameters = parameters });
        parameters["energy"] = 9.0;
        var context = handle.GetEffect()!.Context!;
        Check(context.Mode == VfxMode.Loop && context.Parameters["energy"].AsDouble() == 0.75 && context.Parameters["tint"].AsColor() == Colors.Cyan, "Per-play parameters were not copied or mode override failed");
        Check(def.Mode == VfxMode.OneShot && def.Placement == VfxPlacement.Attached && def.Offset.IsEqualApprox(originalOffset), "Play overrides mutated shared definition");
        Check(player.Play("overrides", new VfxPlayOptions { Mode = (VfxMode)999 }) == null, "Invalid mode accepted");
        Check(player.Play("overrides", new VfxPlayOptions { Placement = (VfxPlacement)999 }) == null, "Invalid placement accepted");
        Check(player.PlayAt("overrides", new Transform3D(Basis.Identity, new Vector3(float.NaN, 0, 0))) == null, "Non-finite transform accepted");
        Check(!handle.Stop((VfxStopMode)999) && handle.IsActive, "Invalid stop mode changed playback");
        return Task.CompletedTask;
    }
    private Task Serialization()
    {
        const string folder = "user://quick_vfx_tests";
        Check(DirAccess.MakeDirRecursiveAbsolute(folder) == Error.Ok, "Cannot create round-trip folder");
        var scene = AnimationScene(true);
        Check(ResourceSaver.Save(scene, folder + "/effect.tscn") == Error.Ok, "Packed effect save failed");
        var loadedScene = ResourceLoader.Load<PackedScene>(folder + "/effect.tscn", cacheMode: ResourceLoader.CacheMode.Ignore);
        var def = Definition("roundtrip", loadedScene, VfxMode.Loop);
        def.Placement = VfxPlacement.Attached; def.ProcessWhilePaused = true; def.OneShotTimeout = 4.25; def.StopTimeout = 1.75;
        def.Offset = new Transform3D(Basis.FromEuler(new Vector3(0, 0.4f, 0)), new Vector3(1, 2, 3));
        Check(ResourceSaver.Save(def, folder + "/definition.tres") == Error.Ok, "Definition .tres save failed");
        var loaded = ResourceLoader.Load<VfxDefinition>(folder + "/definition.tres", cacheMode: ResourceLoader.CacheMode.Ignore);
        Check(loaded != null && loaded.Id == "roundtrip" && loaded.Scene != null && loaded.Mode == VfxMode.Loop && loaded.Placement == VfxPlacement.Attached, "Typed definition or enum round-trip failed");
        Check(loaded!.ProcessWhilePaused && loaded.Offset.IsEqualApprox(def.Offset) && loaded.OneShotTimeout == 4.25 && loaded.StopTimeout == 1.75, "Definition property round-trip failed");
        var authorRoot = new Node3D { Name = "AuthorRoot" }; var anchor = new Node3D { Name = "TypedAnchor" }; authorRoot.AddChild(anchor);
        authorRoot.AddChild(new VfxPlayback { Name = "Playback", Definitions = new() { loaded }, DefaultAnchor = anchor });
        Check(ResourceSaver.Save(Pack(authorRoot), folder + "/caller.tscn") == Error.Ok, "Caller .tscn save failed");
        var caller = Track(ResourceLoader.Load<PackedScene>(folder + "/caller.tscn", cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<Node3D>()); AddChild(caller);
        var playback = caller.GetNode<VfxPlayback>("Playback");
        Check(playback.Definitions.Count == 1 && playback.Definitions[0] is VfxDefinition && playback.DefaultAnchor == caller.GetNode<Node3D>("TypedAnchor"), "Typed array or node reference round-trip failed");
        Check(playback.HasDefinition("roundtrip"), "Deserialized exported registry not loaded");
        var instance = loadedScene.Instantiate<VfxEffect>();
        var animation = instance.GetNode<VfxAnimation>("AnimationParticipant");
        Check(animation.Target == instance.GetNode<AnimationPlayer>("AnimationPlayer") && animation.PlayAnimation == "play" && animation.StopAnimation == "outro", "Participant's typed target reference or clips lost on load");
        instance.Free();
        var builtins = new VfxEffect { Name = "Builtins" };
        var particles = new GpuParticles3D { Name = "Particles", Emitting = false, Lifetime = 0.1, OneShot = true, LocalCoords = true };
        var mesh = new MeshInstance3D { Name = "Mesh", Mesh = new SphereMesh() };
        builtins.AddChild(particles); builtins.AddChild(mesh);
        builtins.AddChild(new VfxParticles { Name = "ParticleTail", Target = particles, TailSeconds = 0.27 });
        builtins.AddChild(new VfxShaderParameter { Name = "ShaderTail", Target = mesh, Parameter = "energy", StartValue = 0.8f, EndValue = 0.2f, Duration = 0.7, StopDuration = 0.4 });
        Check(ResourceSaver.Save(Pack(builtins), folder + "/builtins.tscn") == Error.Ok, "Built-in participant scene save failed");
        var loadedBuiltins = ResourceLoader.Load<PackedScene>(folder + "/builtins.tscn", cacheMode: ResourceLoader.CacheMode.Ignore).Instantiate<VfxEffect>();
        var particleTail = loadedBuiltins.GetNode<VfxParticles>("ParticleTail"); var shaderTail = loadedBuiltins.GetNode<VfxShaderParameter>("ShaderTail");
        Check(particleTail.Target == loadedBuiltins.GetNode<GpuParticles3D>("Particles") && particleTail.TailSeconds == 0.27 && particleTail.Target.LocalCoords, "Particle typed target/designer-local settings lost on round-trip");
        Check(shaderTail.Target == loadedBuiltins.GetNode<MeshInstance3D>("Mesh") && shaderTail.Parameter == "energy" && shaderTail.StartValue == 0.8f && shaderTail.EndValue == 0.2f && shaderTail.Duration == 0.7 && shaderTail.StopDuration == 0.4, "Shader typed target/properties lost on round-trip");
        loadedBuiltins.Free();
        var properties = def.GetPropertyList();
        Check(properties.Any(p => p["name"].AsString() == nameof(VfxDefinition.Scene) && p["hint_string"].AsString().Contains("PackedScene")), "Scene Inspector property is not PackedScene-typed");
        Check(playback.GetPropertyList().Any(p => p["name"].AsString() == nameof(VfxPlayback.DefaultAnchor) && p["hint_string"].AsString().Contains("Node3D")), "DefaultAnchor Inspector property is not Node3D-typed");
        Check(playback.GetPropertyList().Any(p => p["name"].AsString() == nameof(VfxPlayback.Definitions) && p["hint_string"].AsString().Contains("VfxDefinition")), "Definitions Inspector array is not VfxDefinition-typed");
        var handle = Play(playback, "roundtrip"); Check(handle.GetEffect()!.GetParent() == playback.DefaultAnchor, "Deserialized placement/default anchor unusable");
        handle.Stop(VfxStopMode.Immediate);
        return Task.CompletedTask;
    }
}
