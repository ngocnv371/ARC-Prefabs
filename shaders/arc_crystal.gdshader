// Experience crystals: each flat facet is shaded by a fixed key light, so the crystal reads
// as cut rather than as a blob, and facets that mirror the light flash as it spins. A rim of
// the accent colour and a slow shimmer keep it glowing in shadowed grass.
shader_type spatial;
render_mode unshaded, fog_disabled, shadows_disabled, cull_back;

#include "res://shaders/arc_common.gdshaderinc"

uniform vec3 uBase = vec3(1.0);
uniform vec3 uAccent = vec3(1.0);
uniform float uRim = 0.6;
uniform float uGlow = 0.1;
varying vec3 vWorld;

void vertex()
{
	vWorld = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment()
{
	vec3 N = arc_world_normal(NORMAL, INV_VIEW_MATRIX);
	vec3 V = normalize((INV_VIEW_MATRIX * vec4(VIEW, 0.0)).xyz);
	vec3 L = normalize(vec3(-0.45, 0.8, 0.4));
	float fres = arc_fresnel(NORMAL, VIEW, 2.0);
	float key = clamp(dot(N, L) * 0.5 + 0.5, 0.0, 1.0);
	vec3 col = mix(uBase * 0.35, uBase * 1.1, key * key);
	float glint = pow(clamp(dot(reflect(-V, N), L), 0.0, 1.0), 20.0);
	col += uAccent * glint;
	col += uAccent * fres * uRim;
	float shimmer = 0.5 + 0.5 * sin(TIME * 2.6 + (vWorld.x + vWorld.z) * 3.0);
	col += uAccent * uGlow * (0.6 + 0.4 * shimmer);
	ALBEDO = arc_out(arc_feedback(col, fres));
}
