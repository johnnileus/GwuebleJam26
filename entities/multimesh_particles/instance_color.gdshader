shader_type spatial;
render_mode unshaded, depth_prepass_alpha, cull_back, shadows_disabled;

uniform sampler2D color_ramp : source_color, repeat_disable, filter_linear;
uniform sampler2D scale_curve : repeat_disable, filter_linear;
uniform sampler2D opacity_curve : repeat_disable, filter_linear;
uniform vec3 constant_force = vec3(0.0);
uniform float lifetime = 1.0;

varying float v_age_t;
varying float v_alpha;
varying float v_alive;

void vertex() {
    float life_time = INSTANCE_CUSTOM.x;
    float alive = step(0.0001, life_time);
    float age = clamp(lifetime - life_time, 0.0, max(lifetime, 0.0001));
    float age_t = clamp(age / max(lifetime, 0.0001), 0.0, 1.0);

    float scale = texture(scale_curve, vec2(age_t, 0.5)).r;

    // Transform the desired world-space displacement into local space and let Godot's
    // normal MODEL_MATRIX * VERTEX pipeline apply it, instead of using world_vertex_coords
    // (which distorts instances once MODEL_MATRIX is off the world origin).
    vec3 world_offset = constant_force * age * alive;
    vec3 local_offset = (inverse(MODEL_MATRIX) * vec4(world_offset, 0.0)).xyz;
    VERTEX = VERTEX * scale + local_offset;

    v_age_t = age_t;
    v_alive = alive;
    v_alpha = texture(opacity_curve, vec2(age_t, 0.5)).r;
}

void fragment() {
    if (v_alive < 0.5 || v_alpha <= 0.001) {
        discard;
    }
    ALBEDO = texture(color_ramp, vec2(v_age_t, 0.5)).rgb;
    ALPHA = v_alpha;
}
