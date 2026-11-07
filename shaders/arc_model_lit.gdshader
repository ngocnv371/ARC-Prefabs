// Synty props in the showcase's dungeon: arc_model_static's colour, lit by arc_lit.
shader_type spatial;
render_mode unshaded, fog_disabled, shadows_disabled, cull_back, depth_draw_opaque;

#define ARC_STATIC
#include "res://shaders/arc_common.gdshaderinc"
#include "res://shaders/arc_lit.gdshaderinc"

uniform vec3 uAccent = vec3(1.0);
uniform float uRim = 0.3;
uniform sampler2D uTex : filter_linear_mipmap, repeat_enable;

varying vec3 vWorld;

void vertex()
{
	vWorld = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment()
{
	vec3 N = arc_world_normal(NORMAL, INV_VIEW_MATRIX);
	vec3 albedo = texture(uTex, UV).rgb;
	float fres = arc_fresnel(NORMAL, VIEW, 2.0);
	float hemi = 0.58 + 0.42 * clamp(N.y * 0.5 + 0.5, 0.0, 1.0);
	vec3 col = albedo * hemi * arc_light_at(vWorld, TIME);
	// A faint rim keeps silhouettes readable in the dark.
	col += uAccent * fres * uRim * 0.35;
	ALBEDO = arc_out(col * arc_light_fade(vWorld, CAMERA_POSITION_WORLD));
}
