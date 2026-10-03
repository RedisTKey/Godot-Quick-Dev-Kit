# Runtime regression tests

Run from this repository with Godot **4.7 .NET** and .NET **9** installed:

```sh
dotnet build QuickVfxDemo.csproj
godot --headless --path . res://addons/quick_vfx_manager/csharp/tests/VfxTests.tscn
```

Use the .NET-enabled Godot executable, not the standard non-C# edition. The scene
prints one `PASS` line per test group and a final assertion summary. It returns
exit code `1` if any test fails, including an exception in the runner; success
returns `0`. A single `EXPECTED test cancellation exception` warning is intentional:
it proves a misbehaving cancellation callback cannot strand a handle.

## Coverage

- Registration/replacement, duplicate/null/invalid entries, atomic registry reload
- World transform snapshots, attached transforms following an anchor, composition
  order for offsets, authored top-level overrides, and invalid anchors
- One-shot completion, loop persistence, all-participant start/stop barriers,
  synchronous completions and slow tails
- Graceful versus immediate stop, repeated stops, reentrant immediate stop,
  playback from completion callbacks, `StopAll` snapshots, and startup-time stop
- Old/foreign handles and captured generation guards
- Owner/anchor/effect/participant deletion and an actual scene replacement
- Pause/resume, paused timeout clocks, graceful-tail clocks, and
  `ProcessWhilePaused` effects under a paused attachment parent
- Distinct one-shot/stop timeout results, invalid packed scenes, missing or looping
  clips, nested effect roots, missing targets
- Actual `GPUParticles3D` native completion signals, low `SpeedScale` simulation-age
  gating, zero-speed timeout, and speed-scaled graceful loop drain
- Authored `WorldPulse` and `CompositeBurst` scenes completing naturally with
  their particle, animation and shader participants
- Actual `AnimationPlayer` one-shots, looping clips with an outro, finite intro
  clips held in loop mode until their outro
- Cancelled asynchronous custom work, throwing cancellation callbacks, immediate
  free during cancellation, and supported queued-free in a `Finished` callback
- Per-play overrides and copied parameter dictionaries
- `.tres`/`.tscn` save/load round trips for definitions, typed exported arrays,
  node references, placement/mode values, offsets, particles, animation and shader
  participant properties, with Inspector type-hint checks

The test double uses frame budgets for deterministic lifecycle checks. Its async
case uses managed `Task.Delay` to represent work that can outlive a deleted node;
Godot resumes it on the main-thread synchronization context. Files for the
serialization checks are written below `user://quick_vfx_tests/`.

These headless tests verify lifecycle and configuration, including real
`GPUParticles3D` native completion signals. Godot runs the emitter's CPU-side
completion bookkeeping in headless mode; tests do not synthesize those signals.
GPU-rendered appearance, visible trails and overall visual quality should
additionally be checked in the interactive example scene. Godot forbids immediate `Free()` on a node while that
node is emitting a signal; callbacks should use `QueueFree()` instead.
