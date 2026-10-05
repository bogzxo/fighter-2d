#version 410 core

// Albedo and surface are the two attachments of a DeferredRenderer2D, see its summary for what goes where.
// Drawn straight to the window only the first of the two goes anywhere.
layout(location = 0) out vec4 AlbedoColor;
layout(location = 1) out vec4 SurfaceColor;

in vec2 texCoords;
in vec3 color;
in float shouldDiscard;
in vec2 fragPos;

uniform sampler2D uTextureAlbedo;
uniform sampler2D uTextureNormal;

// Not every tile set comes with a normal map.
uniform bool uHasNormal;

// How much of the layer shows no matter the light (a sky, a glowing sign).
uniform float uEmissive;

uniform bool uWireframeEnabled;

void main() {
  AlbedoColor = texture(uTextureAlbedo, texCoords) * vec4(color, 1.0);

  if (shouldDiscard == 1.0 || AlbedoColor.a < 0.1)
    discard;

  vec2 normal = uHasNormal ? texture(uTextureNormal, texCoords).xy : vec2(0.5);
  SurfaceColor = vec4(normal, uEmissive, AlbedoColor.a);
}
