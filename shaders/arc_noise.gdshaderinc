// Value noise and the drifting cloud shadows shared by the ground, the grass and scenery, so a
// cloud darkens everything under it alike. Pass TIME in: built-ins are not visible here.
#ifndef ARC_NOISE
#define ARC_NOISE

float arc_hash(vec2 p)
{
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float arc_noise(vec2 p)
{
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(arc_hash(i), arc_hash(i + vec2(1.0, 0.0)), f.x), mix(arc_hash(i + vec2(0.0, 1.0)), arc_hash(i + vec2(1.0, 1.0)), f.x), f.y);
}

// 1 in sunlight, about 0.88 under a cloud, with soft edges; clouds are ~60 m across and drift slowly.
float arc_cloud(vec2 world, float time)
{
	vec2 p = world * 0.014 + time * vec2(0.012, 0.005);
	float c = arc_noise(p) * 0.65 + arc_noise(p * 2.1 + 3.7) * 0.35;
	return 1.0 - 0.12 * smoothstep(0.45, 0.75, c);
}

#endif
