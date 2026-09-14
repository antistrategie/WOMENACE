# Black Mask

Weapon skin from GFL2's `MG_SSR_Cla_S001_WL` model, using its original inventory icon. `raw.glb` faces Unity +Z and includes the MENACE muzzle and hand anchors.

`effects/idle.prefab` contains the original LOD0 effect hierarchy from CAB `c8a56790ade6ba2e7d64f1d580e75ddc`. Its particle curves, gradients and mesh shapes are retained. Effect textures use the 2D import shape. Materials use `Womenace/WeaponParticles` with display exposure so the glows remain visible in the squad preview. The source names the effect group under its LOD0 mesh `vfx_lod1`. The imported group follows the LOD0 mesh, including that group's local offset.

The `Idle effects` wrapper in `weapon/black_mask/main.prefab` maps the source +X frame to the weapon's +Z frame and applies the same grip offset as the mesh. Preserve that wrapper when rebaking the geometry.
