// The showcase's dungeon floor: flagstones in a biome's ground colours, lit by arc_lit.
shader_type spatial;
render_mode unshaded, fog_disabled, shadows_disabled, cull_back, depth_draw_opaque;

#define ARC_STATIC
#include "res://shaders/arc_common.gdshaderinc"
#include "res://shaders/arc_lit.gdshaderinc"

uniform vec3 uLush = vec3(0.4);
uniform vec3 uDeep = vec3(0.3);
uniform vec3 uDry = vec3(0.5);
uniform float uStone = 2.62; // flagstone size in metres (half a wall)

varying vec3 vWorld;

void vertex()
{
	vWorld = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment()
{
	vec2 g = vWorld.xz / uStone;
	vec2 cell = floor(g);
	// Every other row is offset by half a stone, like laid flags.
	g.x += 0.5 * mod(cell.y, 2.0);
	cell = floor(g);
	vec2 f = abs(fract(g) - 0.5);
	float grout = smoothstep(0.46, 0.5, max(f.x, f.y));
	float tone = arc_hash(cell);
	vec3 albedo = mix(uDeep, uLush, tone);
	albedo = mix(albedo, uDry, 0.45 * smoothstep(0.55, 0.85, arc_noise(vWorld.xz * 0.12)));
	albedo *= 0.88 + 0.12 * arc_noise(vWorld.xz * 1.7);
	albedo *= 1.0 - 0.45 * grout;
	vec3 col = albedo * arc_light_at(vWorld, TIME);
	ALBEDO = arc_out(col * arc_light_fade(vWorld, CAMERA_POSITION_WORLD));
}
