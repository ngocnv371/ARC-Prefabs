// ARC's `Model` fragment: optional base-colour texture, hemisphere light, soft accent rim.
#include "res://shaders/arc_common.gdshaderinc"

uniform vec3 uBase = vec3(1.0);
uniform vec3 uAccent = vec3(1.0);
uniform float uRim = 0.08;
uniform float uGlow = 0.0;
uniform float uAlpha = 1.0;
// Raw (display-value) sampling, as GL did: no source_color hint.
uniform sampler2D uTex : filter_linear_mipmap, repeat_enable;
uniform bool uUseTex = false;
// The anomaly GLBs keep ARC's OpenGL V origin (ARC's loader flips V; Godot's importer does not).
uniform bool uFlipV = false;

#ifdef ARC_STATIC
// Scenery sits under the same drifting cloud shadows as the ground and grass.
#include "res://shaders/arc_noise.gdshaderinc"
varying vec2 vCloudAt;

void vertex()
{
	vCloudAt = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}
#endif

void fragment()
{
	vec3 N = arc_world_normal(NORMAL, INV_VIEW_MATRIX);
	vec3 albedo = uBase;
	if (uUseTex)
		albedo *= texture(uTex, uFlipV ? vec2(UV.x, 1.0 - UV.y) : UV).rgb;

	float fres = arc_fresnel(NORMAL, VIEW, 2.0);
	float hemi = 0.58 + 0.42 * clamp(N.y * 0.5 + 0.5, 0.0, 1.0);
	vec3 col = albedo * hemi;
	col += uAccent * fres * uRim;
	col += uAccent * uGlow;
#ifdef ARC_STATIC
	col *= arc_cloud(vCloudAt, TIME);
#endif
	ALBEDO = arc_out(arc_feedback(col, fres));
#ifdef ARC_BLEND
	ALPHA = uAlpha * uFade * (0.88 + 0.12 * fres);
#endif
}
