# Taryz strike effects

Taryz's attack visuals from GFL2, exposed as `springfield/taryz/strike_muzzle`, `springfield/taryz/strike_projectile` and `springfield/taryz/strike_impact`.

GFL2's summon attack (timeline `104704010`) plays the eagle's `StandFire` clip, bursts `Skill/SpringfieldSSR01/Springfield_Summon` at her chest 0.32 s in and lands `Skill/BulletHit_Water` on the target. Its projectile is `Buff/empty`, so the hit has no visible travel. The projectile prefab is `Weapon/Common_Bullet_ker_water`, the water round from Springfield's own kit, which pairs with the same impact family.

| Prefab | GFL2 source |
| --- | --- |
| `strike_muzzle` | `Springfield_Summon` |
| `strike_projectile` | `Common_Bullet_ker_water` |
| `strike_impact` | `BulletHit_Water` |

`spec.json` lists the CN client archives (pack file and byte offset) and the source roots. Rebuild from the repository root:

```
uv run --with UnityPy --with lz4 python -I scripts/vfx/extract_gfl2_effects.py \
  --bundles "<GF2_Exilium_Data>/LocalCache/Data/AssetBundles_Windows" \
  --spec unity/Assets/Authored/springfield/taryz/effects/spec.json

Unity -batchmode -nographics -quit -projectPath unity/ \
  -executeMethod Womenace.EditorTools.BuildGfl2Effects.Build \
  -source Assets/Authored/springfield/taryz/effects \
  -output Assets/Prefabs/springfield/taryz -scale 1.2
```

Particle systems keep the source curves, gradients, bursts and timings. Materials use `Womenace/WeaponParticles` with exposure off and brightness capped for MENACE. GFL2's screen refraction layers in the impact are not reproduced, so those renderers stay disabled with valid materials. The bullet casing mesh renders unlit in its base colour.
