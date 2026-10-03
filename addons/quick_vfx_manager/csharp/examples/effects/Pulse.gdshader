shader_type spatial;
render_mode unshaded, cull_disabled, blend_add, depth_draw_never;

// Per-instance state: concurrent effect instances never change a shared material.
instance uniform float vfx_energy : hint_range(0.0, 1.0) = 1.0;
uniform vec4 tint : source_color = vec4(0.2, 0.9, 1.0, 1.0);

void fragment() {
    float rim = 0.4 + 0.6 * pow(1.0 - abs(dot(NORMAL, VIEW)), 2.0);
    ALBEDO = tint.rgb * (0.65 + rim);
    ALPHA = tint.a * clamp(vfx_energy, 0.0, 1.0);
}
