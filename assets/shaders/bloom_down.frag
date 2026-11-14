#version 430 core
// 4-tap bilinear downsample (16 texels); the first level applies a soft-knee threshold.
in vec2 vUv;
out vec4 oColor;
uniform sampler2D uSrc;
uniform vec2 uTexel;
uniform float uThreshold;
void main() {
    vec3 c = texture(uSrc, vUv + uTexel * vec2(-1.0, -1.0)).rgb
           + texture(uSrc, vUv + uTexel * vec2( 1.0, -1.0)).rgb
           + texture(uSrc, vUv + uTexel * vec2(-1.0,  1.0)).rgb
           + texture(uSrc, vUv + uTexel * vec2( 1.0,  1.0)).rgb;
    c *= 0.25;
    if (uThreshold >= 0.0) {
        float lum = max(c.r, max(c.g, c.b));
        float knee = 0.35;
        float soft = clamp(lum - uThreshold + knee, 0.0, 2.0 * knee);
        soft = soft * soft / (4.0 * knee + 1e-4);
        c *= max(soft, lum - uThreshold) / max(lum, 1e-4);
    }
    oColor = vec4(c, 1.0);
}
