---
name: support-team-pipeline
description: End-to-end authoring of a GFL2 Armed Echelon (support team) as a WOMENACE roster leader whose one element is the whole crew: client extraction, per-state animation bake in Blender, Unity prefab bake, sprites, KDL, the SupportTeams registry, the unlock event, and in-game testing. Use when adding a team, rebuilding Team Limonad from the client, or debugging a team's body, animation, equipment or UI.
---

# Support team pipeline

## What a team is

An Armed Echelon is two or three Dolls who fight as one unit around one weapon. In WOMENACE a team
is a roster leader (`squad_leader.<team>`) whose squad (`player_squad.<team>`) has exactly one
element, and that element's body is the whole crew with the weapon and props, on one Animator.
It takes no squaddies, keeps the weapon and outfit it is hired with, and joins through a faction
event like vanilla's Honor Guards. Team Limonad (GFL2's `ATG01`, an RPG team: Tarkhun leads,
Citro loads, Bakhmaro fires) is the first, and every file named below exists for it.

| Piece | Where |
|---|---|
| Client sources and export jobs | `scripts/support_team/teams/<team>/team.json` |
| Animation states | `scripts/support_team/teams/<team>/states.json` |
| Pipeline driver | `scripts/support_team/prepare_team.py` |
| Unity bake | `unity/Assets/Authored/<team>/Editor/Build<Team>.cs` |
| Body | `unity/Assets/Prefabs/<team>/default/main.prefab` (asset `<team>/default/main`) |
| Sprites | `assets/sprites/<team>/`, `assets/sprites/weapon/<team>/`, `assets/textures/<team>/` |
| Templates | `templates/support_teams/<team>/` |
| Runtime | `code/Systems/SupportTeams/`, registry in `SupportTeams.cs` |

## Tools

- Python through `uv run`. The GFL2 tools pin Python 3.12 or older for UnityPy.
- AssetStudioMod, whose libraries `scripts/support_team/asexport` builds against (`-p:AssetStudioDir`).
- Blender 5 (the state builder uses layered actions and action slots), run headless.
- The .NET 10 SDK, and the Unity Editor named in `unity/ProjectSettings/ProjectVersion.txt`.
- The GFL2 PC client. Its archives are in `GF2_Exilium_Data/LocalCache/Data/AssetBundles_Windows`.

## 1. Find the team in the client

Most of the client's archives sit end to end inside other `.bundle` files, lightly encrypted,
and are named here as `<pack>:<offset>`. Index the client once (about 61,000 archives):

    uv run scripts/support_team/gfl2_client.py index <AssetBundles_Windows> gfl2_index.jsonl

Each line lists one archive's objects by type, so a search for the team's code (`ATG01`) finds:

- the crew with its idle scene (`c_<code>_Idle` root), the native idle clips, and the ult scene
  with the ult clips (`<code>_UltraSkill` root, `c_<code>_Member?_slg_Ultraskill`);
- the weapon (`<code>_WL`), its rocket or shell, any prop the crew holds (Limonad's `Laptop`), and
  each one's mesh, texture and material archives, which are often separate;
- the 2D art: `Support_Whole_GuildWar_<code>` (the whole team), `Skill_<code>_Active`, the
  faction's `Img_Guild_Faction_Logo_<n>`, and the team's ammo `Support_Ammo_<code>_<tier>`;
- a donor Doll's `slg` clips for what the team has no clip of (run, hit, death, vault). The crew
  rig's core body bones match a Doll's `slg` rig by name, so Doll clips bind directly. Limonad
  uses Lind's (`c_LindSSR01_slg_*`).

Two archives can be the same size: identify one by its bytes, not its size. Record every archive
in `team.json` (`archives`, `art`) with the export jobs that read them (`exports`: output folder,
scene root, archives, and each object with its clips, `@<set>` naming a `clip_sets` list).

## 2. Build the sources

    uv run scripts/support_team/prepare_team.py <team> --work <dir> \
        --bundles <AssetBundles_Windows> --assetstudio <AssetStudioMod folder> --blender <blender>

The work folder sits outside the repository and keeps every step's output, so `--steps` can rerun
any of them alone. The steps, in order:

1. `cut`: each archive in `team.json`, decrypted, into `<work>/bundles`.
2. `export`: each object with its clips to FBX through `asexport`, into `<work>/export`.
3. `art`: the 2D art into `<work>/art`.
4. `states`: `build_team_states.py` bakes one take per state into `<work>/states` (see 3).
5. `render`: `render_weapon.py` renders the weapon's side for its icons.
6. `sprites`: `make_team_sprites.py` writes every sprite into `assets/` (see 5).
7. `install`: the state FBXs and the crew's textures into `unity/Assets/Authored/<team>/default`.

An FBX export differs from the last one in its creation date and object ids, so a rerun shows the
FBXs as changed even when nothing in them is.

## 3. Animation states

`states.json` lists the crew (`crew`, `formation_clip`), the props (`prop_names`, `prop_idle`)
and each state: its source clip per member, length, stagger, how each prop is carried, and fixes
applied over the donor clip. `build_team_states.py`'s header documents every option. Limonad's
states are Idle (native), Run, Kneel (deployed), AimIn, AimHold, Fire, Hit, Death and Vault.

- Each member keeps her place in the formation, her root's location in her own idle.
- The crew run a third of a cycle apart, so they never move in step.
- Aim and fire are cut from the native ult at the frame the weapon settles and the shot leaves.
- A prop's rest is wherever its own scene left it. A prop placed from the ult is read off the
  ult's own objects (`extra_sources`), and a GFL2 prop take moves its `Prop` bone.
- Donor clips leave the crew's hair and coat in their bind pose. `idle_hair` holds each member's
  idle hair, and `swing_coat` turns the coat flaps with the thighs.
- The vault's travel goes on the `motion` node, the avatar's root-motion node, and the crew cross
  in place relative to it (see AGENTS.md, Hard-won constraints, on the vault's root motion).
- Exports are unsimplified (`bake_anim_simplify_factor` 0): a pose held on one frame has
  constant curves, which simplification drops.
- `"review": "<file>.blend"` writes every state back to back with a marker each, for review in
  Blender. `"preview": true` also renders stills.

## 4. Unity bake

`Build<Team>.cs` (Limonad's `BuildLimonad`) imports each state FBX with `motion` as the root node,
copies each take into its own clip on the body's bone paths, and builds the prefab and its
controller on the construct driver contract:

    Unity -batchmode -nographics -quit -projectPath unity \
      -executeMethod WOMENACE.Editor.BuildLimonad.Build

- A state FBX has no meshes, so Unity collapses its single root node and its curves miss the
  body's paths. The bake moves every path onto the body's and logs how many curves bind (all).
- Root transform curves are dropped, since the root is the game's and root motion rides on
  `MotionT` and `MotionQ`. The import's scale of 100 lives on that root.
- The controller: Idle and Run on `Speed`, Kneel on `Stance` above 0.5 (the actor's `ActorStance`),
  `Aiming` plays AimIn into the looping AimHold and back out through AimOut (AimIn reversed and
  baked as its own clip), `Shoot_Single` or `Special_Attack_1` plays Fire, and Hit, Vault and
  Death (any state, on `OnDeath`).
- Run takes vanilla's sprint footstep events and Vault the vanilla crossing's events.
- Materials go on the Doll shaders (`Womenace/DollToon` with the rmo as `_MaskMap`, the shared
  face SDF, the eye shaders). The crew's own ramps are not extracted, so they borrow the nearest.
- `launcher_muzzle`, a marker `build_team_states.py` places at the weapon's tip, is the muzzle.

## 5. Sprites

`make_team_sprites.py` makes the whole set from the art (`sprites` in `team.json`):

- Bark and slot: the crew's faces in panels split by a "||" divider, from a head box per member
  (`heads`, left to right, in the whole-team art).
- Stand images: the crew standing as drawn, feet level, fitted to the unit window's and the
  hiring screen's crops (both read from vanilla's UXML), the outer panels running on with the art.
- Emblem: the skill icon over the faction logo in white with a black outline. The badges follow
  as for a Doll (`scripts/doll/make_doll_sprites.py`).
- Weapon icons from the render, straightened by its measured angle. Disabled icons are flat grey.
- `unit_type`: the skill icon in black and white for the unit window's squad type.
- `SkillIcon`: the skill icon for the fire skill. `--item <name>=<art>` makes an item's icons.

## 6. Templates

`templates/support_teams/<team>/` holds, as Limonad's does:

- The team tag `wmgfl_team_<team>`, never `wmgfl_<team>` (Calibration reads that one as a Doll's).
- The speaker: vanilla tokens only in `Tags` (nothing containing `wmgfl`, which marks a Doll), the
  team's name as `Forename` (it heads the unit window), and the portraits.
- `animator.<team>` from `animator.construct_soldier` with `DeathBehaviour DeathAnimations`.
- `armor.<team>_default` and `weapon.<team>`, each `OnlyEquipableBy` the team tag, `jy_no_sell`.
  The weapon carries no model, since the weapon is in the body. It grants the team's own clone of
  the fire skill, so its uses and icon change without changing the vanilla weapon's.
- `player_squad.<team>`: the team tag and `jy_armor_restricted`, `jy_weapon_restricted`,
  `jy_special_restricted`, `jy_vehicle_restricted`, one element, scale pinned, the badges, and
  the affiliation as `Title`.
- `squad_leader.<team>` with the portraits and badges.
- The unlock event (`unlock.kdl`): a clone of the faction's Honor Guard event with its own trust
  gate, and an effect whose `Effects[0].Leaders[0]` is the team's leader.
- Items for the team alone name its tag in `OnlyEquipableBy` (`ammo.kdl`). Items for every team
  carry `wmgfl_support_item`.

## 7. Code

Add the team to `SupportTeams.All`: its code, its name, and each crew member's rig node with her
name, leader first. Every team system reads the registry:

- `SupportTeamSystem` renders the body for the team's element, loading it on first use.
- `SupportTeamSquadSystem` keeps the squad at one element with no squaddies.
- `SupportTeamEquipSystem` keeps the team's weapon and outfit on it, hides the boxes it cannot
  change, and lists only team items in its other slots.
- `SupportTeamBattleNamesSystem` lists the crew in a mission's selected unit panel.
- `SupportTeamUnitTypeSystem` writes "Armed Echelon" and the `unit_type` icon as the squad type.
- `StrategyDiorama.SquadNames.cs` lays each crew member's name on the armoury floor before her.
- `StrategyConversationGuardSystem` keeps a cloned event off the strategy conversation stage.

## 8. Testing

With the dev loader (`jiangyu loader deploy --variant dev`) and `scripts/bridge.py`:

- Unlock: raise the faction's trust with `Diorama.FactionTrust` (`["story_faction.zayn_beecher",
  <change>]` for Limonad) and finish an operation. The event comes as a message on the home screen.
- Items: `Weapons.Give` with an item id gives one into the shared stock.
- `ConversationTraceSystem` (dev builds) logs each conversation as it plays and where.

Check in the roster that the stand image fits the unit window and the hiring screen, the crew's
names lie in front of each member, the squad type reads ARMED ECHELON, and the team's slots list
only its own items. In a mission, check run, aim, fire, hit, death, the deployed stance, the vault
crossing (the team must move as it vaults), and the crew list in the unit panel.
