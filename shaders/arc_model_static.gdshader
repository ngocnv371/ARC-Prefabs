// Opaque scenery (Synty props): arc_model without the per-instance feedback uniforms.
shader_type spatial;
render_mode unshaded, fog_disabled, shadows_disabled, cull_back, depth_draw_opaque;

#define ARC_STATIC
#include "res://shaders/arc_model_body.gdshaderinc"
