shader_type spatial;
render_mode cull_back;

uniform vec3 albedo : source_color = vec3(0.19607843, 0.4392157, 0.15294118);
uniform float roughness : hint_range(0.0, 1.0) = 0.9;

uniform float wind_strength = 0.12;
uniform float wind_speed = 2.0;
uniform vec2 wind_direction = vec2(1.0, 0.3);
uniform float wind_wave_length = 4.0; // world units per full ripple cycle along wind_direction
uniform float mesh_min_y = -6.273418;
uniform float mesh_max_y = -1.871028;

void vertex() {
    float height_t = clamp((VERTEX.y - mesh_min_y) / max(mesh_max_y - mesh_min_y, 0.0001), 0.0, 1.0);
    height_t *= height_t; // ease-in: base stays anchored, tip sways most

    vec2 dir = normalize(wind_direction);
    vec3 instance_origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
    // Phase is a slowly-varying traveling wave along the wind direction, not a noise hash -
    // nearby blades must stay near in-phase or the sway reads as incoherent instead of directional.
    float phase = dot(instance_origin.xz, dir) * (6.28318530718 / max(wind_wave_length, 0.0001));
    float sway = sin(TIME * wind_speed + phase) * wind_strength * height_t;

    vec3 world_sway = vec3(dir.x, 0.0, dir.y) * sway;
    // Same world-space-force -> local-space technique as instance_color.gdshader,
    // so every blade sways the same world direction regardless of its own random rotation.
    vec3 local_sway = inverse(mat3(MODEL_MATRIX)) * world_sway;

    VERTEX += local_sway;
}

void fragment() {
    ALBEDO = albedo;
    ROUGHNESS = roughness;
}
