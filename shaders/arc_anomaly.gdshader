// ARC's `Anomaly` fragment: stronger fresnel rim, hemisphere light and drifting bands. Used for
// elites, bosses and spectral effects.
shader_type spatial;
render_mode unshaded, fog_disabled, shadows_disabled, blend_mix, cull_back, depth_draw_always;

#include "res://shaders/arc_common.gdshaderinc"

uniform vec3 uBase = vec3(1.0);
uniform vec3 uAccent = vec3(1.0);
uniform float uRim = 0.8;
uniform float uGlow = 0.0;
uniform float uAlpha = 1.0;
varying vec3 vWorld;

void vertex()
{
	vWorld = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment()
{
	vec3 N = arc_world_normal(NORMAL, INV_VIEW_MATRIX);
	float fres = arc_fresnel(NORMAL, VIEW, 2.1);
	float hemi = 0.55 + 0.45 * clamp(N.y * 0.5 + 0.5, 0.0, 1.0);
	float bands = 0.5 + 0.5 * sin(vWorld.y * 9.0 - TIME * 1.1);
	vec3 col = uBase * hemi;
	col += uAccent * fres * uRim;
	col += uAccent * uGlow;
	col += uBase * bands * 0.035;
	ALBEDO = arc_out(arc_feedback(col, fres));
	ALPHA = uAlpha * uFade * (0.76 + 0.24 * fres);
}
