#ifndef WOMENACE_DOLL_KEY_LIGHT_INCLUDED
#define WOMENACE_DOLL_KEY_LIGHT_INCLUDED

// The key the strategy diorama gives a doll in a room, set on all her renderers (body, eyes,
// outline) in one property block, so every part of her is lit from one place in one colour.
// The shader including this declares _KeyDirection, _KeyColour, _KeyOcclusion and _RoomLit:
// the body in its material block, the eyes and outline as plain uniforms, which a property
// block alone sets.

// A doll lit from a window takes the key from the way that light comes (xyz, towards it) in
// place of the sun's own way. One lit from the room's lamps takes their colour (rgb,
// illuminance) in place of the sun's.
void ApplyDioramaKey(inout float3 L, inout float3 colour)
{
    if (_KeyDirection.w > 0.5)
        L = normalize(_KeyDirection.xyz);
    if (_KeyColour.w > 0.5)
        colour = _KeyColour.rgb;
}

// The share of the sun the room lets through to her, for a part that has no material default
// for it: outside a room the uniform is unset and the key is whole.
float DioramaKeyOcclusion()
{
    return _RoomLit > 0.5 ? _KeyOcclusion : 1.0;
}

#endif
