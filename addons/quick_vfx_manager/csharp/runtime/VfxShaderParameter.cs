using Godot;
using System;

namespace QuickVfx;

/// <summary>Animates an instance shader uniform; never writes a shared ShaderMaterial resource.</summary>
[GlobalClass]
public partial class VfxShaderParameter : VfxParticipant
{
    [Export] public GeometryInstance3D? Target { get; set; }
    [Export] public StringName Parameter { get; set; } = "vfx_energy";
    [Export] public float StartValue { get; set; } = 1;
    [Export] public float EndValue { get; set; } = 0;
    [Export(PropertyHint.Range, "0.01,60,0.01,or_greater")] public double Duration { get; set; } = 0.6;
    [Export(PropertyHint.Range, "0.01,60,0.01,or_greater")] public double StopDuration { get; set; } = 0.3;
    private double _elapsed;
    private float _current, _stopFrom;
    private bool _stopping;

    protected override void StartEffect(VfxContext context)
    {
        if (!IsEffectTarget(Target)) throw new InvalidOperationException("Shader Target must belong to this effect scene.");
        if (!double.IsFinite(Duration) || Duration <= 0 || !double.IsFinite(StopDuration) || StopDuration <= 0
            || !float.IsFinite(StartValue) || !float.IsFinite(EndValue)) throw new InvalidOperationException("Shader durations/values must be finite, with positive durations.");
        _elapsed = 0;
        _stopping = false;
        SetValue(StartValue);
    }
    protected override void StopEffect() { _stopFrom = _current; _elapsed = 0; _stopping = true; }
    public override void _Process(double delta)
    {
        if (!Running || Context == null) return;
        if (!GodotObject.IsInstanceValid(Target) || !Target!.IsInsideTree() || Target.IsQueuedForDeletion()) { Fail("Shader target was removed."); return; }
        _elapsed += delta;
        var duration = _stopping ? StopDuration : Duration;
        var progress = _elapsed / duration;
        if (!_stopping && Context.Mode == VfxMode.Loop)
        {
            SetValue(Mathf.Lerp(StartValue, EndValue, (float)(0.5 - 0.5 * Math.Cos(progress * Math.Tau))));
            return;
        }
        SetValue(Mathf.Lerp(_stopping ? _stopFrom : StartValue, EndValue, (float)Math.Min(1, progress)));
        if (progress >= 1) Complete(Context.Generation);
    }
    private void SetValue(float value) { _current = value; Target!.SetInstanceShaderParameter(Parameter, value); }
    protected override void CancelEffect() { if (GodotObject.IsInstanceValid(Target)) SetValue(EndValue); }
}
