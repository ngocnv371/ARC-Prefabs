// The field: ARC's `Flat` lighting over a procedural ground (the Synty packs have no tiling
// ground texture). The colour comes from arc_ground.gdshaderinc, which the grass blades share;
// here it is darkened where props meet the ground and under the drifting cloud shadows.
shader_type spatial;
render_mode unshaded, fog_disabled, shadows_disabled, cull_back, depth_draw_opaque;

#define ARC_STATIC
#include "res://shaders/arc_common.gdshaderinc"
#include "res://shaders/arc_ground.gdshaderinc"

varying vec2 vWorld;

void vertex()
{
	vWorld = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz;
}

void fragment()
{
	vec3 N = arc_world_normal(NORMAL, INV_VIEW_MATRIX);
	float hemi = 0.7 + 0.3 * clamp(N.y, 0.0, 1.0);
	vec2 px = fwidth(vWorld);
	vec3 albedo = ground_albedo(vWorld, max(px.x, px.y));
	vec2 marks = ground_marks(vWorld);
	albedo *= marks.r * arc_cloud(vWorld, TIME);
	ALBEDO = arc_out(albedo * hemi);
}
