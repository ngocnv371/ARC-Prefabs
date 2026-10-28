// Translucent authored surfaces (glTF alphaMode BLEND), drawn both sides like ARC.
shader_type spatial;
render_mode unshaded, fog_disabled, shadows_disabled, blend_mix, cull_disabled, depth_draw_never;

#define ARC_BLEND
#include "res://shaders/arc_model_body.gdshaderinc"
