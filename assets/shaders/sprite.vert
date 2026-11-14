#version 430 core
layout(location = 0) in vec2 aQuad;
layout(location = 1) in vec4 aRect;   // x, y, w, h in target pixels (y down)
layout(location = 2) in vec4 aUv;     // u, v, w, h in atlas texels (y down)
layout(location = 3) in vec4 aTint;
layout(location = 4) in uint aFlags;
uniform vec2 uTarget;
out vec2 vLocal;
flat out vec4 vUv;
flat out vec4 vTint;
flat out uint vFlags;
void main() {
    vec2 p = aRect.xy + aQuad * aRect.zw;
    gl_Position = vec4(p.x / uTarget.x * 2.0 - 1.0, 1.0 - p.y / uTarget.y * 2.0, 0.0, 1.0);
    vLocal = aQuad * aRect.zw;
    vUv = aUv;
    vTint = aTint;
    vFlags = aFlags;
}
