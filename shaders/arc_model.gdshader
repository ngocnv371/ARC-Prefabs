// Opaque authored surfaces: anomalies, the hero and Synty props. ARC drew every part as a
// soft alpha blend; a crowd needs depth-sorted opaque parts, so only BLEND materials use
// arc_model_blend.gdshader.
shader_type spatial;
render_mode unshaded, fog_disabled, shadows_disabled, cull_back, depth_draw_opaque;

#include "res://shaders/arc_model_body.gdshaderinc"
