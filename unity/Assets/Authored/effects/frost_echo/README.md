# Frost Echo

Alva's ice summon visual from GFL2, exposed as `effects/frost_echo/main`.
The prefab contains the emitter, ice meshes and 22 native Unity particle systems.
It has no gameplay scripts, collider or skill behaviour.

Source: `AlvaSSR01_Summon` in the GFL2 CN Windows bundle
`449c06feaaf73c9bc46b17d1f0cd884e.bundle`, archive offset `4382879`,
CAB `CAB-a19b03d68398e6923831cb00f87e80b9`.

The emitter uses `Womenace/DollToon`. The particles use
`Womenace/WeaponParticles`, with the source textures, meshes, curves and timing.
Brightness is adjusted for MENACE. GFL2's specialised Fresnel, dissolve-edge
and vertex-animation shading is not reproduced by the shared particle shader.
The normal map is decoded from GFL2's alpha/green packing to RGB.

Every renderer has a valid material, including disabled and particle-only
renderers. MENACE inspects these slots when creating tactical elements.

`active.alva_ssr_frost_echo` places the visual through MENACE's native tile
effects. The skill targets an empty tile and the effect expires after three
rounds. Both are authored in `templates/dolls/alva/weapon.kdl`, without custom
runtime behaviour. The skill deals no damage and applies no status effects.

In a dev build, `FrostEcho.Give` grants Alva's SSR weapon for testing. Equip it
in a special weapon slot, then use Frost Echo during a mission.
