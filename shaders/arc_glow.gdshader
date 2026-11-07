// A light's marker in the showcase: a soft additive disc that always faces the camera, so you
// can see where a pool of light comes from. It flickers like a flame and fades with distance.
shader_type spatial;
render_mode unshaded, fog_disabled, shadows_disabled, blend_add, cull_disabled, depth_draw_never;

#define ARC_STATIC
#include "res://shaders/arc_common.gdshaderinc"
#include "res://shaders/arc_lit.gdshaderinc"

uniform vec3 uColor = vec3(1.0, 0.6, 0.25);
uniform float uStrength = 1.0;

varying vec3 vCentre;

void vertex()
{
	vCentre = MODEL_MATRIX[3].xyz;
	float s = length(MODEL_MATRIX[0].xyz);
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0] * s, INV_VIEW_MATRIX[1] * s, INV_VIEW_MATRIX[2] * s, MODEL_MATRIX[3]);
}

void fragment()
{
	float d = length(UV - 0.5) * 2.0;
	float a = pow(max(1.0 - d, 0.0), 2.2);
	float core = pow(max(1.0 - d * 2.2, 0.0), 2.0);
	float flicker = 0.85 + 0.3 * arc_noise(vec2(TIME * 6.0 + vCentre.x * 3.1, vCentre.z * 2.7));
	vec3 col = (uColor * a + vec3(core) * 0.6) * uStrength * flicker;
	ALBEDO = arc_out(col * arc_light_fade(vCentre, CAMERA_POSITION_WORLD));
	ALPHA = 1.0;
}
