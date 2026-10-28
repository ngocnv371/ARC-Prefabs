// ARC's `Flat` fragment: unlit colour with a hint of hemisphere light. Blob shadows, AoE
// telegraphs and shockwaves. A radial falloff (uSoft) turns a quad into a soft disc.
shader_type spatial;
render_mode unshaded, fog_disabled, shadows_disabled, blend_mix, cull_disabled, depth_draw_never;

#define ARC_STATIC
#include "res://shaders/arc_common.gdshaderinc"

uniform vec3 uBase = vec3(0.0);
uniform vec3 uAccent = vec3(0.0);
uniform float uGlow = 0.0;
uniform float uAlpha = 1.0;
// 0 draws the mesh as is; above 0, alpha falls off from the UV centre to the edge.
uniform float uSoft = 0.0;

void fragment()
{
	vec3 N = arc_world_normal(NORMAL, INV_VIEW_MATRIX);
	float hemi = 0.7 + 0.3 * clamp(N.y, 0.0, 1.0);
	ALBEDO = arc_out(uBase * hemi + uAccent * uGlow);
	float a = uAlpha * uFade;
	if (uSoft > 0.0)
		a *= smoothstep(0.5, 0.5 - uSoft, length(UV - 0.5));
	ALPHA = a;
}
