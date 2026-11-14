#version 430 core
// Quantized normal-mapped point lighting at art resolution + the CPU pixel-VFX layer, HDR output.
out vec4 oColor;
uniform sampler2D uAlbedo;
uniform sampler2D uNormal;
uniform sampler2D uEmission;
uniform sampler2D uVfxColor;   // uploaded top row first (y down)
uniform sampler2D uVfxLight;
uniform vec3 uAmbient;
uniform int uBands;
uniform int uLighting;
uniform int uDebug;
uniform int uLightCount;
uniform float uVfxOpacity;
uniform float uTime;
uniform vec4 uLights[128];     // per light: [x, y, radius, height], [r, g, b, 0]; x,y in target px, y down

const float BAYER[16] = float[](0., 8., 2., 10., 12., 4., 14., 6., 3., 11., 1., 9., 15., 7., 13., 5.);

void main() {
    ivec2 ip = ivec2(gl_FragCoord.xy);
    ivec2 size = textureSize(uAlbedo, 0);
    vec4 alb = texelFetch(uAlbedo, ip, 0);
    vec3 n = normalize(texelFetch(uNormal, ip, 0).xyz * 2.0 - 1.0);
    vec3 emi = texelFetch(uEmission, ip, 0).rgb;
    ivec2 vp = ivec2(ip.x, size.y - 1 - ip.y);
    vec4 vc = texelFetch(uVfxColor, vp, 0);
    vec3 vl = texelFetch(uVfxLight, vp, 0).rgb;
    if (alb.a == 0.0) n = vec3(0.0, 0.0, 1.0);

    vec2 p = vec2(float(ip.x) + 0.5, float(size.y - ip.y) - 0.5);
    float dither = (BAYER[(ip.y & 3) * 4 + (ip.x & 3)] + 0.5) / 16.0;
    vec3 light = uAmbient;
    for (int i = 0; i < uLightCount; i++) {
        vec4 a = uLights[i * 2];
        vec3 c = uLights[i * 2 + 1].rgb;
        vec2 d = a.xy - p;
        float dist = length(d);
        if (dist >= a.z) continue;
        vec3 L = normalize(vec3(d.x, -d.y, a.w));
        float ndl = max(dot(n, L), 0.0) * 0.8 + 0.2;   // slight wrap keeps silhouettes readable
        float att = 1.0 - dist / a.z;
        float v = ndl * att * att;
        if (uBands > 0) v = floor(v * float(uBands) + dither) / float(uBands);
        light += c * v;
    }
    // Stacked VFX glows (e.g. 6 chain-lightning hops at once) saturate softly instead of blowing out
    // the hero: readability beats raw intensity.
    vl = vl / (1.0 + 0.8 * max(vl.r, max(vl.g, vl.b)));
    light += vl * 1.6;                                 // VFX light spills onto the world

    vec3 col = uLighting == 1 ? alb.rgb * light : alb.rgb;
    col += emi * 1.8;                                  // emissive pixels exceed 1.0 and feed bloom
    col = mix(col, vc.rgb * 1.1, vc.a * uVfxOpacity);  // VFX pixels are self-lit, mild HDR push
    col += vl * 0.35;

    if (uDebug == 1) col = alb.rgb;
    else if (uDebug == 2) col = n * 0.5 + 0.5;
    else if (uDebug == 3) col = emi;
    else if (uDebug == 4) col = vl + vc.rgb * vc.a;
    oColor = vec4(col, 1.0);
}
