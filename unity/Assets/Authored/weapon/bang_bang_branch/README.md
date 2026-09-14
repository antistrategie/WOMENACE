# Bang Bang Branch

RF weapon skin from GFL2. The model uses the native MENACE muzzle and left-hand grip anchors. The grip rotation includes the wrist correction from `scripts/weapon/fix_hand_grip.py`, which must not be applied twice.

The inventory icon uses the trimmed bounds of GFL2's `Weapon_RFClaD019_4_1024` sprite. The full texture canvas has excess transparent padding that makes the weapon appear too small in the skin cards.

`weapon/bang_bang_branch/muzzle` uses GFL2's generic `RF_ATK01_ShotFire_Physical` flash from CAB `0cb6af4060f6cd2f904055a5f50ce4b0`. It preserves the ten visible particle systems, with brightness adjusted for HDRP exposure. Distortion and the dynamic light are omitted. An invisible root particle system destroys the flash after two seconds.
