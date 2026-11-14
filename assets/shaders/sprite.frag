#version 430 core
// Writes the G-buffer. Binary alpha (alpha test), exact texel fetch: no filtering ever touches pixel art.
in vec2 vLocal;
flat in vec4 vUv;
flat in vec4 vTint;
flat in uint vFlags;
layout(location = 0) out vec4 oAlbedo;
layout(location = 1) out vec4 oNormal;
layout(location = 2) out vec4 oEmission;
uniform sampler2D uAlbedo;
uniform sampler2D uNormal;
uniform sampler2D uEmission;
void main() {
    ivec2 size = ivec2(vUv.zw);
    ivec2 l = clamp(ivec2(floor(vLocal)), ivec2(0), size - 1);
    bool flip = (vFlags & 1u) != 0u;
    if (flip) l.x = size.x - 1 - l.x;
    ivec2 t = ivec2(vUv.xy) + l;
    vec4 a = texelFetch(uAlbedo, t, 0);
    if (a.a < 0.5) discard;
    float flash = 1.0 - vTint.a;                       // hit flash: tint alpha < 1 whitens
    vec3 alb = mix(a.rgb * vTint.rgb, vec3(1.0), flash);
    vec4 n = texelFetch(uNormal, t, 0);
    if (flip) n.x = 1.0 - n.x;
    float boost = float((vFlags >> 8) & 255u) / 255.0;
    vec3 e = texelFetch(uEmission, t, 0).rgb + alb * boost + vec3(flash * 0.6);
    oAlbedo = vec4(alb, 1.0);
    oNormal = vec4(n.xyz, 1.0);
    oEmission = vec4(e, 1.0);
}
