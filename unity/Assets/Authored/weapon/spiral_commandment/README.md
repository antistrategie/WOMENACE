# Spiral Commandment

Weapon skin from GFL2's `SMG_SSR_Cla_S005_WL` model, using its original inventory icon. `raw.glb` faces Unity +Z and includes the MENACE muzzle and hand anchors.

`effects/idle.prefab` contains the original LOD0 effect hierarchy from CAB `3d6ea03609d51762aa3da7869fef2329`. Its particle curves, gradients and mesh shapes are retained. Effect textures use the 2D import shape. Materials use `Womenace/WeaponParticles` with display exposure so the glows remain visible in the squad preview. Its rotating fan uses the weapon material. The remaining effects use the particle shader.

The `Idle effects` wrapper in `weapon/spiral_commandment/main.prefab` maps the source +X frame to the weapon's +Z frame and applies the same grip offset as the mesh. Preserve that wrapper when rebaking the geometry.
