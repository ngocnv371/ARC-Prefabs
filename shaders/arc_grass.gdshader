// Grass tufts, one MultiMesh per chunk near the focus (src/World/Grass.cs). Each tuft takes the
// ground's colour at its root (arc_ground.gdshaderinc), darker at the root and lighter at the
// tip, sways in the wind and leans away from the hero and nearby monsters (uPush). The biome's
// density and height come from the grass map; it thins on the ground's dry patches to the
// map's A share, and the mark map keeps tufts out from under props. INSTANCE_CUSTOM.x is the
// tuft's own random number: it survives where that is below the density.
// Height follows a growth stage that changes gradually across the ground, the way grass comes
// up in batches and waves: curved fronts a few tens of metres apart and patches on top of
// them, from fresh sprouts a few centimetres tall (lighter, yellower) to full growth. The
// biome's variation (the grass map's B) sets how far back the youngest stage goes; tufts in a
// batch differ only a little, and grass is lower where it thins toward dry patches and props.
// A tuft scales as a whole with its height, so a sprout is small all over, and shows one to
// five of the mesh's blades (UV.x is the blade's index): few on young shoots, more on grown
// grass, each blade a little taller or shorter than the next.
// Tufts fade out toward uReach from uFocus.
shader_type spatial;
render_mode unshaded, fog_disabled, shadows_disabled, cull_disabled, depth_draw_opaque;

#define ARC_STATIC
#include "res://shaders/arc_common.gdshaderinc"
#include "res://shaders/arc_ground.gdshaderinc"

const int PUSHERS = 24;
uniform vec4 uPush[PUSHERS]; // xz position, w radius (0 is unused)
uniform vec2 uFocus = vec2(0.0);
uniform float uReach = 64.0;

varying vec3 vRoot;
varying float vHeight;
varying float vGrown;

// 0 fresh sprouts .. 1 full growth, smooth across the ground: warped wave fronts that sweep
// across the field, broken into batches by a patch noise.
float growth(vec2 root)
{
	vec2 q = root + (vec2(arc_noise(root * 0.04), arc_noise(root * 0.04 + vec2(9.1, 2.7))) - 0.5) * 20.0;
	float waves = 0.5 + 0.5 * sin(dot(q, vec2(0.19, 0.11)) + arc_noise(q * 0.03) * 5.0);
	float batches = arc_noise(q * 0.075 + 23.0);
	return smoothstep(0.2, 0.85, waves * 0.55 + batches * 0.55 - 0.05);
}

void vertex()
{
	vec2 root = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xz;
	vec4 grass = ground_grass(root);
	float own = INSTANCE_CUSTOM.x;
	float thin = ground_marks(root).g * mix(1.0, grass.a, smoothstep(0.0, 0.6, ground_dry(root, 0.0)));
	float density = grass.r * thin * (1.0 - smoothstep(uReach * 0.8, uReach, distance(root, uFocus)));
	if (own >= density || grass.g < 0.03)
	{
		VERTEX = vec3(0.0); // culled: a degenerate blade draws nothing
	}
	else
	{
		float h = VERTEX.y; // 0 at the root, 1 at the tip
		// The growth stage here, how far back it can go (the biome's variation), a little of
		// the tuft's own, and shorter where the grass thins.
		float v = grass.b;
		float grown = mix(1.0, growth(root), v);
		float tuft = fract(own * 7.31);
		float height = grass.g
			* mix(0.08, 1.0, grown * grown)
			* (0.85 + 0.3 * tuft)
			* mix(1.0, mix(0.45, 1.0, smoothstep(0.0, 0.8, thin)), v);
		vGrown = grown;
		// Blades: one or two on sprouts, up to five on grown grass, give or take one.
		float blade = UV.x;
		float blades = clamp(floor(mix(1.0, 5.0, grown) + fract(own * 13.7) * 2.0 - 0.5), 1.0, 5.0);
		height *= 0.8 + 0.35 * arc_hash(vec2(own * 91.0, blade));
		VERTEX.y *= height;
		VERTEX.xz *= clamp(height / 0.4, 0.12, 1.4); // the footprint follows the height (mesh suits 40 cm)

		// Wind: a slow gust field plus a quicker flutter, stronger toward the tip.
		float gust = arc_noise(root * 0.08 + TIME * vec2(0.35, 0.12));
		vec2 lean = vec2(0.85, 0.35) * (gust * 0.5 + 0.08 * sin(TIME * 3.1 + INSTANCE_CUSTOM.y * 6.28));
		for (int i = 0; i < PUSHERS; i++)
		{
			vec4 p = uPush[i];
			if (p.w <= 0.0)
				continue;
			vec2 d = root - p.xy;
			float l = length(d);
			if (l < p.w && l > 1e-3)
				lean += d / l * (1.0 - l / p.w) * 1.4;
		}
		vec3 world = vec3(lean.x, 0.0, lean.y) * h * h * height;
		world.y = -length(lean) * 0.35 * h * h * height; // bent blades stand lower
		VERTEX += transpose(mat3(MODEL_MATRIX)) * world;
		if (blade >= blades)
			VERTEX = vec3(0.0); // a blade this tuft does not show

		vRoot = ground_albedo(root, 0.0) * ground_marks(root).r * arc_cloud(root, TIME);
		vHeight = h;
	}
}

void fragment()
{
	// Shadowed at the root, sunlit toward the tip; young shoots are a lighter, yellower green.
	vec3 col = vRoot * mix(0.72, 1.18, vHeight) + vec3(0.03, 0.035, 0.0) * vHeight;
	col = mix(col * vec3(1.06, 1.14, 0.82) + vec3(0.03, 0.04, 0.0), col, smoothstep(0.2, 0.8, vGrown));
	ALBEDO = arc_out(col);
}
