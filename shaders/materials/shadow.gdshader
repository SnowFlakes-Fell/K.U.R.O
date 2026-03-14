shader_type canvas_item;

uniform float softness = 0.5;
uniform float alpha = 0.4;

void fragment() {

    vec2 uv = UV * 2.0 - 1.0;

    float dist = length(uv);

    float shadow = smoothstep(1.0, softness, dist);

    COLOR = vec4(0.0, 0.0, 0.0, shadow * alpha);
}
