#version 430 core
// Integer upscale with nearest sampling (crisp pixels), sub-pixel camera offset, smooth bloom on top,
// a highlight-only shoulder (palette colors at neutral light stay exact) and a light vignette.
out vec4 oColor;
uniform sampler2D uScene;
uniform sampler2D uBloom;
uniform float uBloomStrength;
uniform int uScale;
uniform vec2 uWindow;
uniform vec2 uTarget;
uniform vec2 uOffset;
uniform int uRawView;

vec3 shoulder(vec3 c) {
    const float k = 0.82;
    vec3 over = max(c - k, 0.0);
    return min(c, vec3(k)) + (1.0 - k) * (1.0 - exp(-over / (1.0 - k)));
}

void main() {
    vec2 w = vec2(gl_FragCoord.x, uWindow.y - gl_FragCoord.y);
    vec2 t = w / float(uScale) + uOffset;
    ivec2 ti = ivec2(floor(t));
    ti.y = int(uTarget.y) - 1 - ti.y;
    vec3 c = texelFetch(uScene, ti, 0).rgb;
    if (uRawView == 0) {
        c += texture(uBloom, vec2(t.x / uTarget.x, 1.0 - t.y / uTarget.y)).rgb * uBloomStrength;
        c = shoulder(c);
        vec2 q = gl_FragCoord.xy / uWindow - 0.5;
        c *= 1.0 - 0.28 * dot(q, q);
    }
    oColor = vec4(c, 1.0);
}
