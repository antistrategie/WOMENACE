# Wooden Path

HG weapon skin from GFL2's `cw_HR_SR_Cla_D013_WL` model (English item name "Wooded Path"). `raw.glb` faces Unity +Z and includes the MENACE muzzle and hand anchors. The textures are the original d, n and rmo sheets.

The GFL2 skin shares the Welrod gun's grip frame, so the mesh carries the same offset against the Hand_R origin as `weapon/welrod`, including her nudged grip. The stem of the banana is the grip. `weapon_hand_l` and `weapon_hand_l_pistol` start from Welrod's nudged left-hand pose. `muzzle` sits on GFL2's muzzle socket at the tip of the banana. Welrod's anchors were seeded after the `bake_weapon.py` rotation fix, so `scripts/weapon/fix_hand_grip.py` does not apply here.

The inventory icon uses the trimmed bounds of GFL2's `Weapon_HRClaD013_4_1024` sprite. The skin has no idle effect and keeps the weapon's own muzzle flash.
