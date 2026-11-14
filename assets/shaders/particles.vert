#version 430 core
// Two vertices per particle drawn as GL_LINES: sparks become 1px velocity streaks, motes 1px dots.
struct Particle { vec2 pos; vec2 vel; float life; float maxLife; float drag; float gravity; uint ca; uint cb; float intensity; uint kind; };
layout(std430, binding = 0) readonly buffer Particles { Particle p[]; };
uniform vec2 uCam;
uniform vec2 uTarget;
uniform float uTime;
out vec3 vColor;
void main() {
    int i = gl_VertexID / 2;
    int e = gl_VertexID % 2;
    Particle q = p[i];
    if (q.life <= 0.0) { gl_Position = vec4(2.0, 2.0, 0.0, 1.0); vColor = vec3(0.0); return; }
    float k = clamp(q.life / q.maxLife, 0.0, 1.0);
    vec3 col = mix(unpackUnorm4x8(q.cb).rgb, unpackUnorm4x8(q.ca).rgb, k) * q.intensity * min(1.0, k * 3.0);
    vec2 pos = q.pos - uCam;
    if (q.kind == 0u) {
        if (e == 1) pos -= q.vel * 0.025;
    } else {
        col *= 0.55 + 0.45 * sin(uTime * 7.0 + float(i) * 1.7);
        if (e == 1) pos.x += 1.0;
    }
    vec2 t = floor(pos) + 0.5;
    gl_Position = vec4(t.x / uTarget.x * 2.0 - 1.0, 1.0 - t.y / uTarget.y * 2.0, 0.0, 1.0);
    vColor = col;
}
