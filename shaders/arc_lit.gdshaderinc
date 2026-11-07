// Dungeon lighting for the showcase (src/Showcase): ARC's shaders are unlit, so the lights are
// baked into a map over the ground around the camera (DungeonLights) and every lit surface reads
// it at its own spot. Light falls off up a wall, flickers a little, and everything fades to the
// dark with distance from the camera. The game's shaders do not include this.
#include "res://shaders/arc_noise.gdshaderinc"

global uniform sampler2D arc_light_map : filter_linear, repeat_disable;
global uniform vec4 arc_light_rect; // x, z of the map's corner, then its width and depth in metres
global uniform vec4 arc_light_mood; // ambient, fade start, fade end (metres from the camera), flicker

vec3 arc_light_at(vec3 world, float time)
{
	vec2 uv = (world.xz - arc_light_rect.xy) / arc_light_rect.zw;
	vec3 light = texture(arc_light_map, uv).rgb;
	float up = 1.0 - 0.55 * smoothstep(1.2, 7.0, world.y);
	float flicker = 1.0 + arc_light_mood.w * (arc_noise(world.xz * 0.35 + vec2(time * 2.3, time * 1.7)) - 0.5);
	return vec3(arc_light_mood.x) + light * up * flicker;
}

float arc_light_fade(vec3 world, vec3 camera)
{
	return 1.0 - smoothstep(arc_light_mood.y, arc_light_mood.z, distance(world, camera));
}
