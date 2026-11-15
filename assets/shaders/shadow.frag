#version 430 core
// Contact shadow: a squashed ellipse multiplied into the G-buffer albedo (blend DstColor, Zero), with an
// ordered-dither rim so the edge stays pixel art. The rect's tint alpha is the darkness.
in vec2 vLocal;
flat in vec4 vUv;
flat in vec4 vTint;
flat in uint vFlags;
layout(location = 0) out vec4 oAlbedo;
layout(location = 1) out vec4 oNormal;
layout(location = 2) out vec4 oEmission;
const float BAYER[16] = float[](0., 8., 2., 10., 12., 4., 14., 6., 3., 11., 1., 9., 15., 7., 13., 5.);
void main() {
    vec2 size = vUv.zw;
    vec2 p = (floor(vLocal) + 0.5) / size * 2.0 - 1.0;
    float d = dot(p, p);
    if (d > 1.0) discard;
    ivec2 ip = ivec2(gl_FragCoord.xy);
    float b = (BAYER[(ip.y & 3) * 4 + (ip.x & 3)] + 0.5) / 16.0;
    float k = vTint.a * (d < 0.45 ? 1.0 : (b > (d - 0.45) / 0.55 ? 0.75 : 0.0));
    if (k <= 0.0) discard;
    oAlbedo = vec4(vec3(1.0 - k), 1.0);
    oNormal = vec4(0.5, 0.5, 1.0, 1.0);
    oEmission = vec4(0.0);
}
