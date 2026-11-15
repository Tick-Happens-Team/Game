#version 430 core
// Integer upscale with nearest sampling (crisp pixels), sub-pixel camera offset, smooth bloom on top,
// a highlight-only shoulder, a color grade (contrast, saturation, lift, split toning), diorama tilt-shift,
// chromatic aberration and flash pulses for impacts, and a vignette. The UI is composited last at its own scale.
out vec4 oColor;
uniform sampler2D uScene;
uniform sampler2D uBloom;
uniform sampler2D uUi;        // screen-space UI at art resolution, top row first
uniform float uBloomStrength;
uniform int uScale;
uniform int uUiScale;
uniform vec2 uWindow;
uniform vec2 uTarget;
uniform vec2 uOffset;
uniform int uRawView;
uniform vec4 uGrade;          // contrast, saturation, lift, split
uniform vec3 uShadowTint;
uniform vec3 uHighlightTint;
uniform float uVignette;
uniform float uTiltShift;
uniform float uAberration;
uniform vec3 uFlash;

vec3 shoulder(vec3 c) {
    const float k = 0.82;
    vec3 over = max(c - k, 0.0);
    return min(c, vec3(k)) + (1.0 - k) * (1.0 - exp(-over / (1.0 - k)));
}

vec3 scene(vec2 t) {
    ivec2 ti = ivec2(floor(t));
    ti.y = int(uTarget.y) - 1 - ti.y;
    return texelFetch(uScene, clamp(ti, ivec2(0), ivec2(uTarget) - 1), 0).rgb;
}

void main() {
    vec2 w = vec2(gl_FragCoord.x, uWindow.y - gl_FragCoord.y);
    vec2 t = w / float(uScale) + uOffset;
    vec2 q = gl_FragCoord.xy / uWindow - 0.5;
    vec3 c = scene(t);
    if (uRawView == 0) {
        // Diorama: blur grows toward the top and bottom edges (a 12-tap disc in art pixels).
        float blur = uTiltShift * smoothstep(0.24, 0.5, abs(q.y));
        if (blur > 0.25) {
            vec3 acc = c;
            for (int i = 0; i < 12; i++) {
                float a = float(i) * 2.39996;
                float r = blur * sqrt((float(i) + 0.5) / 12.0);
                acc += scene(t + vec2(cos(a), sin(a)) * r);
            }
            c = acc / 13.0;
        }
        if (uAberration > 0.0) {
            vec2 dir = q * 2.0 * uAberration;
            c.r = scene(t + dir).r;
            c.b = scene(t - dir).b;
        }
        c += texture(uBloom, vec2(t.x / uTarget.x, 1.0 - t.y / uTarget.y)).rgb * uBloomStrength;
        c += uFlash;
        c = shoulder(c);
        // Grade: split toning by luminance, then contrast around mid-grey, saturation and a matte lift.
        float lum = dot(c, vec3(0.2126, 0.7152, 0.0722));
        vec3 tint = mix(uShadowTint, uHighlightTint, smoothstep(0.05, 0.6, lum));
        c = mix(c, c * tint, uGrade.w);
        c = (c - 0.42) * uGrade.x + 0.42;
        lum = dot(c, vec3(0.2126, 0.7152, 0.0722));
        c = max(mix(vec3(lum), c, uGrade.y), 0.0);
        c = c * (1.0 - uGrade.z) + uGrade.z * uShadowTint * 0.5;
        c *= 1.0 - uVignette * dot(q, q) * 2.0;
    }
    // UI: integer-scaled, no camera offset, composited after grading so text stays exact.
    ivec2 ui = ivec2(floor(w / float(uUiScale)));
    vec4 u = texelFetch(uUi, ui, 0);
    c = mix(c, u.rgb, u.a);
    oColor = vec4(c, 1.0);
}
