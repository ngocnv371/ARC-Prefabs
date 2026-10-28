// Shared pieces of the ARC look (ARC/godot/Host/GodotArShaders.cs). The fragment maths is
// ARC's, computed in display (sRGB) values as its GL renderer did. Godot treats an unshaded
// ALBEDO as linear, so every shader decodes its final colour with arc_out().

// Per-instance gameplay feedback on top of the authored colours: a hit flash toward white,
// a status tint added through the rim, and a fade for spawning and dying. Every instance of a
// shader with instance uniforms takes a slot in a global buffer, so scenery (ARC_STATIC)
// leaves them out.
#ifdef ARC_STATIC
const float uFlash = 0.0;
const vec3 uTint = vec3(0.0);
const float uFade = 1.0;
#else
instance uniform float uFlash = 0.0;
instance uniform vec3 uTint = vec3(0.0);
instance uniform float uFade = 1.0;
#endif

vec3 arc_out(vec3 c)
{
	c = clamp(c, 0.0, 1.0);
	vec3 lo = c / 12.92;
	vec3 hi = pow((c + 0.055) / 1.055, vec3(2.4));
	return mix(lo, hi, step(vec3(0.04045), c));
}

vec3 arc_world_normal(vec3 view_normal, mat4 inv_view)
{
	return normalize((inv_view * vec4(view_normal, 0.0)).xyz);
}

float arc_fresnel(vec3 view_normal, vec3 view_dir, float power)
{
	return pow(1.0 - clamp(dot(normalize(view_normal), view_dir), 0.0, 1.0), power);
}

vec3 arc_feedback(vec3 col, float fres)
{
	col += uTint * (0.35 + fres);
	return mix(col, vec3(1.0), clamp(uFlash, 0.0, 1.0));
}
