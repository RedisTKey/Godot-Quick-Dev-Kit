# VFX Playback Lab

Open `VfxLab.tscn` in Godot **4.7 .NET** and run the scene (F6), or run the repository's standalone demo project (F5). Build the C# project first if Godot requests it. This lab uses the Compatibility renderer; its shader declares a supported per-instance uniform rather than changing a shared material.

The scene starts in **Auto demo** mode. World and composite effects replay every three seconds. The attached loop plays for five seconds, then alternates graceful and immediate stops on successive cycles. Clicking any playback or stop button turns off automatic playback, so the buttons are safe to explore without a second controller interfering. Turn **Auto demo** back on to resume.

## Three stations

- **World pulse (cyan):** The small white source moves. `PlayAt` captures its world transform when pressed, so a live pulse remains at its spawn position while the source moves away. Repeated clicks make independent overlapping effects.
- **Composite burst (amber):** A GPU burst, mesh/light animation, and ring shader fade live inside one scene. The handle completes only after every participant finishes, including the longer particle tail.
- **Attached loop (violet):** `PlayAttached` parents the authored effect to the moving white anchor. Start is idempotent in the lab while its current handle is active. **Stop gracefully** stops emission, plays `outro`, fades the shader, and waits for remaining particles. **Stop immediately** cancels the handle and removes the effect at once.

The status text shows handle generation, running/stopping state, and terminal reason. An effect instance is never reused. Status callbacks from an older world/composite request cannot overwrite a newer request's label.

## Inspector-first authoring

1. Select `VfxPlayback` in `VfxLab.tscn`. Its exported **Definitions** array already contains the three `.tres` resources below; the demo does not register them in code.
2. Open a definition resource to change its ID, PackedScene, world/attached placement, one-shot/loop mode, offset, pause policy, or timeout safety bounds.
3. Open its effect scene. The root is `VfxEffect` (`Node3D`). All visible nodes and participant wiring are authored in the scene, not constructed by the caller.
4. Edit `Particles` (`GPUParticles3D`) and its `ParticleProcessMaterial`, `AnimationPlayer` clips, or the ring's `ShaderMaterial` as usual. The initial emitter is disabled and the animation has no autoplay; playback owns the start.
5. Inspect the three participant nodes. Their exported **Target** references point at nodes inside the same scene. Keep those references valid when reorganizing the effect.

| Definition | Authored scene | ID | Default behavior |
| --- | --- | --- | --- |
| `definitions/WorldPulse.tres` | `effects/WorldPulse.tscn` | `world_pulse` | World / one-shot |
| `definitions/CompositeBurst.tres` | `effects/CompositeBurst.tscn` | `composite_burst` | World / one-shot |
| `definitions/AttachedLoop.tres` | `effects/AttachedLoop.tscn` | `attached_loop` | Attached / loop |

Every example has the same simple participant layout:

```text
VfxEffect (Node3D)
├── Ring (MeshInstance3D; Pulse.gdshader)
├── Core (MeshInstance3D)
├── AccentLight (OmniLight3D)
├── Particles (GPUParticles3D)
├── AnimationPlayer
├── ParticleTail (VfxParticles → Particles)
├── MeshAndLightAnimation (VfxAnimation → AnimationPlayer)
└── InstanceShaderFade (VfxShaderParameter → Ring)
```

`Pulse.gdshader` declares `instance uniform float vfx_energy`; each participant changes only its own mesh instance. `tint` is an ordinary authored material color. `play` animates mesh scale and light energy. The loop additionally supplies a non-looping `outro` clip. The shader participant's loop oscillates between its exported values, and graceful stop fades to its end value.

Particle `local_coords` is intentionally **false** for the two world examples and **true** for the attached example. Attachment controls the effect root; this particle setting separately controls whether already-emitted particles follow that root. Change it to false on the attached example to try a moving trail.

## Reuse in another scene

Add a `VfxPlayback` child to your own caller, assign definitions in its Inspector, and retain returned handles for effects you will stop. The lab's complete calling logic is in `VfxLab.cs`. It only moves presentation anchors and wires ordinary Godot UI buttons; it has no gameplay, collisions, input-map dependencies, Quick Input, audio, or project-specific rendering components.

The examples are demonstrations, not a globally registered catalog. Copy only the needed scenes/resources and update their paths if you move them. Participant targets must stay within one `VfxEffect`; do not nest additional `VfxEffect` roots.
