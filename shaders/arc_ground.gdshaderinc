// The ground's colour at a world point, shared by the ground plane and the grass blades so each
// blade takes the colour of the ground at its root. Field bakes the maps:
//   uLushMap/uDeepMap/uDryMap  the three tones of the biomes under the plane (4 m texels)
//   uGrassMap                  R grass density, G blade height in metres, B height variation,
//                              A the share of grass on the dry patches
//   uMarkMap                   R contact shade under props, G where grass may grow (finer texels)
// Outside the maps the meadow defaults show.
#include "res://shaders/arc_noise.gdshaderinc"

uniform vec3 uLush = vec3(0.45, 0.62, 0.30);
uniform vec3 uDeep = vec3(0.34, 0.52, 0.25);
uniform vec3 uDry = vec3(0.60, 0.63, 0.36);
uniform sampler2D uLushMap : filter_linear, repeat_disable;
uniform sampler2D uDeepMap : filter_linear, repeat_disable;
uniform sampler2D uDryMap : filter_linear, repeat_disable;
uniform sampler2D uGrassMap : filter_linear, repeat_disable;
uniform vec4 uMapRect = vec4(0.0);
uniform sampler2D uMarkMap : filter_linear, repeat_disable;
uniform vec4 uMarkRect = vec4(0.0);

// A hard-edged step from 0 to 1 at `edge`, softened just enough not to alias: `soft` is the
// value's change across a pixel (0 where there are no derivatives, as in a vertex).
float ground_band(float v, float edge, float soft)
{
	float w = max(soft, 0.012);
	return smoothstep(edge - w, edge + w, v);
}

// R contact shade (1 is none), G grass allowed.
vec2 ground_marks(vec2 world)
{
	if (uMarkRect.z <= 0.0)
		return vec2(1.0);
	return texture(uMarkMap, (world - uMarkRect.xy) / uMarkRect.zw).rg;
}

// R density 0..1, G height in metres, B how much the height varies (0 even, 1 ragged),
// A the share that grows on the dry patches.
vec4 ground_grass(vec2 world)
{
	if (uMapRect.z <= 0.0)
		return vec4(1.0, 0.45, 0.5, 0.4);
	return texture(uGrassMap, (world - uMapRect.xy) / uMapRect.zw);
}

// The warped point the patch shapes are drawn from.
vec2 ground_warp(vec2 world)
{
	return world + (vec2(arc_noise(world * 0.03), arc_noise(world * 0.03 + vec2(5.2, 1.3))) - 0.5) * 18.0;
}

// How much of the dry tone shows (sun-bleached grass, fen water, bare ash), up to 0.7: grass
// thins there to the biome's dryGrass share.
float ground_dry(vec2 world, float px)
{
	return ground_band(arc_noise(ground_warp(world) * 0.05 + 17.0), 0.74, px * 0.08) * 0.7;
}

// `px` is metres per pixel (0 in a vertex). Broad patches are domain-warped so their shapes
// look painted, and meet in crisp bands rather than airbrushed blends; clover clumps, light
// tufts and a few pebbles break up each band up close.
vec3 ground_albedo(vec2 world, float px)
{
	vec3 lush = uLush, deep = uDeep, dry = uDry;
	if (uMapRect.z > 0.0)
	{
		vec2 uv = (world - uMapRect.xy) / uMapRect.zw;
		lush = texture(uLushMap, uv).rgb;
		deep = texture(uDeepMap, uv).rgb;
		dry = texture(uDryMap, uv).rgb;
	}

	vec2 q = ground_warp(world);
	float broad = arc_noise(q * 0.025) * 0.75 + arc_noise(q * 0.08) * 0.25;
	float soft = px * 0.04;
	vec3 col = mix(lush, mix(lush, deep, 0.55), ground_band(broad, 0.5, soft));
	col = mix(col, deep, ground_band(broad, 0.64, soft) * 0.8);
	col = mix(col, dry, ground_dry(world, px));

	// Faint clover clumps (darker) and sunlit tufts (lighter); the blades carry the fine detail.
	col *= 1.0 - 0.035 * ground_band(arc_noise(world * 0.7 + 3.1), 0.68, px * 1.0);
	col = mix(col, col * 1.08 + 0.01, 0.5 * ground_band(arc_noise(world * 1.4 + 9.0), 0.78, px * 1.6));

	// Pebbles: a few cells of a 0.6 m grid hold a small light dot.
	vec2 cell = floor(world / 0.6);
	float h = arc_hash(cell + 0.37);
	if (h > 0.96)
	{
		vec2 at = (vec2(arc_hash(cell + 1.7), arc_hash(cell + 4.1)) * 0.6 + 0.2) * 0.6; // inside the cell
		float d = length(world - (cell * 0.6 + at));
		col = mix(col, mix(dry, vec3(0.8, 0.78, 0.7), 0.4), 0.8 * (1.0 - smoothstep(0.04, 0.04 + max(px, 0.01), d)));
	}
	return col;
}
