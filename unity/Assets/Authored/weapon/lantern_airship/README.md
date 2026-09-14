# Lantern Airship

AR weapon skin from GFL2's `cw_AR_SSR_Cla_S003_WL` model, with its original sight, grip and `Weapon_ARClaS003_5_1024` icon.

The inventory icon uses the sprite's trimmed bounds. The full texture canvas has excess transparent padding that makes the weapon appear too small in the skin cards.

The idle effects come from `AR_SSR_Cla_S003_WL`, CAB `76882ee37c5ee0ef24109920cdd10a67`. `effects/idle.prefab` preserves its LOD0 particle hierarchy, curves and gradients. The materials map the active source shader properties to `Womenace/WeaponParticles`. Source bundles contain concatenated UnityFS archives, so extracting only their first archive misses this prefab.

Idle materials set `_ExposureWeight` to zero because their glow colours are authored for display, not as physical light intensities. Applying MENACE's camera exposure makes these effects almost invisible in the squad preview.

`weapon/lantern_airship/muzzle` uses GFL2's generic `AR_ATK01_ShotFire_Physical` flash as a provisional match. Its brightness is adjusted for HDRP exposure. It has no distortion or dynamic light. The skin-specific firing effect has not been identified.

`raw.glb` faces forward along Unity +Z. Its hand and muzzle anchors share the existing AR wrist basis. The source model's right-hand origin is `(0.11, 0.009, 0)`. Preserve the nested `Idle effects` prefab when rebaking the weapon geometry. Its root transform places the effects in the same coordinate frame as the mesh.
