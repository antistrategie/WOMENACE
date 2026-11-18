#ifndef DOLL_ROOM_FOG_INCLUDED
#define DOLL_ROOM_FOG_INCLUDED
// GFL2's linear fog in the strategy rooms, as Womenace/BakedScene's ApplyFog lays it over the
// room (colour and start and end distances set by StrategyDiorama.Rooms.cs), over a doll the
// diorama lights in a room (_RoomLit, set on her renderers with her probes). Every surface round
// her takes it, and without it she stands out of the room's haze, warmer and harder than it.
// Outside a room, and before any room has set the fog, a doll takes none.
float4 _WmFogColor;
float4 _WmFogLinear;

float3 DollRoomFog(float3 colour, float viewDepth, float roomLit)
{
    if (roomLit < 0.5 || _WmFogLinear.w <= _WmFogLinear.z)
        return colour;
    return lerp(colour, _WmFogColor.rgb, saturate((viewDepth - _WmFogLinear.z) / (_WmFogLinear.w - _WmFogLinear.z)));
}
#endif
