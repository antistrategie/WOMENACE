# Clockwork Guard

Weapon skin from GFL2's `Knife_SSR_Cla_S007_WL` model, using its original inventory icon. `raw.glb` faces Unity +Z and includes the MENACE muzzle and hand anchors.

`effects/idle.prefab` contains the original LOD0 effect hierarchy from CAB `d43d53dec9346d5a4a432efa5b1a457c`. Its particle curves, gradients and mesh shapes are retained. Effect textures use the 2D import shape. Materials use `Womenace/WeaponParticles` with display exposure so the glows remain visible in the squad preview. Its blade glows and flowing highlights use the original mesh particles.

The `Idle effects` wrapper in `weapon/clockwork_guard/main.prefab` maps the source +X frame to the weapon's +Z frame and applies the same grip offset as the mesh. Preserve that wrapper when rebaking the geometry.
