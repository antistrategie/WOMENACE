# Chronometer

Weapon skin from GFL2's `SG_SSR_Cla_S004_WL` model, using its original inventory icon. `raw.glb` faces Unity +Z and includes the MENACE muzzle and hand anchors.

`effects/idle.prefab` contains the original LOD0 effect hierarchy from CAB `a25b879cf8820dfb09bc51d747f555d5`. Its particle curves, gradients and mesh shapes are retained. Effect textures use the 2D import shape. Materials use `Womenace/WeaponParticles` with display exposure so the glows remain visible in the squad preview. The four-second glow clip retains the source material colour and dissolve curves on the two Fresnel meshes.

The `Idle effects` wrapper in `weapon/chronometer/main.prefab` maps the source +X frame to the weapon's +Z frame and applies the same grip offset as the mesh. Preserve that wrapper when rebaking the geometry.
