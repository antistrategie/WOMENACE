---
name: support-team-pipeline
description: End-to-end authoring of a GFL2 Armed Echelon (support team) as a WOMENACE roster leader whose one element is the whole crew: client extraction, per-state animation bake in Blender, Unity prefab bake, sprites, KDL, the SupportTeams registry, the unlock event, and in-game testing. Use when adding a team, rebuilding a team from the client, or debugging a team's body, animation, equipment or UI.
---

# Support team pipeline

## What a team is

An Armed Echelon is two or three Dolls who fight as one unit around one weapon. In WOMENACE a team
is a roster leader (`squad_leader.<team>`) whose squad (`player_squad.<team>`) has exactly one
element, and that element's body is the whole crew with the weapon and props, on one Animator.
It takes no squaddies, keeps the weapon and outfit it is hired with, and joins through a faction
event like vanilla's Honor Guards. Each team is named by its GFL2 code (`ATG01` and so on) in
the client and by its asset name (`<team>`) in the mod.

| Piece | Where |
|---|---|
| Client sources and export jobs | `scripts/support_team/teams/<team>/team.json` |
| Animation states | `scripts/support_team/teams/<team>/states.json` |
| Pipeline driver | `scripts/support_team/prepare_team.py` |
| Unity bake | `unity/Assets/Editor/BuildSupportTeam.cs`, with `scripts/support_team/teams/<team>/bake.json` |
| Body | `unity/Assets/Prefabs/<team>/default/main.prefab` (asset `<team>/default/main`) |
| Sprites | `assets/sprites/<team>/`, `assets/sprites/weapon/<team>/`, `assets/textures/<team>/` |
| Templates | `templates/support_teams/<team>/` |
| Runtime | `code/Systems/SupportTeams/`, registry in `SupportTeams.cs` |

## Tools

- Python through `uv run`. The GFL2 tools pin Python 3.12 or older for UnityPy.
- AssetStudioMod, whose libraries `scripts/support_team/asexport` builds against (`-p:AssetStudioDir`).
  It runs through `dotnet` on Windows or Linux. On Linux use the portable AssetStudioModCLI build,
  which carries the native FBX library for Linux.
- Blender 5 (the state builder uses layered actions and action slots), run headless.
- The .NET 10 SDK, and the Unity Editor named in `unity/ProjectSettings/ProjectVersion.txt`.
- The GFL2 PC client. Its archives are in `GF2_Exilium_Data/LocalCache/Data/AssetBundles_Windows`.

## 1. Find the team in the client

Most of the client's archives sit end to end inside other `.bundle` files, lightly encrypted,
and are named here as `<pack>:<offset>` (`scripts/gfl2/client.py` reads them). Index the client
once (about 61,000 archives). The index opens every archive, since the client's GFF index names
archives but not the objects in them:

    uv run scripts/support_team/gfl2_client.py index <AssetBundles_Windows> gfl2_index.jsonl

Each line lists one archive's objects by type, so a search for the team's code finds:

- the crew with its idle scene (`c_<code>_Idle` root), the native idle clips, and the ult scene
  with the ult clips (`<code>_UltraSkill` root, `c_<code>_Member?_slg_Ultraskill`).
- the weapon (`<code>_WL`), its rocket or shell, any prop the crew holds, and each one's mesh,
  texture and material archives, which are often separate.
- the 2D art: `Support_Whole_GuildWar_<code>` (the whole team), `Skill_<code>_Active`, the
  faction's `Img_Guild_Faction_Logo_<n>`, and the team's ammo `Support_Ammo_<code>_<tier>`.
- a donor Doll's `slg` clips (`c_<Doll>SSR01_slg_*`) for what the team has no clip of (run, hit,
  death, vault). The crew rig's core body bones match a Doll's `slg` rig by name, so Doll clips
  bind directly.

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
5. `render`: `scripts/weapon/render_weapon.py` renders the weapon's side for its icons, from
   `weapon_render` in `team.json` (`fbx`, `diffuse`, `size`, and `axis`, the side it is seen from
   once its length lies along X). `hide_groups` leaves pieces out by their vertex group (the
   mortar's rounds, which lie beside it and hang above it at rest). A weapon that stands at rest
   is drawn folded for carrying, as vanilla's mortar icons are, by a `fold` block in place of
   `axis` (the renderer's header documents its keys): it parts the prop with the `split` rules of
   `states.json`, swings the legs onto the tube and frames the base to the left with the tube
   rising.
6. `sprites`: `make_team_sprites.py` writes every sprite into `assets/` (see 5).
7. `install`: the state FBXs and the crew's textures into `unity/Assets/Authored/<team>/default`,
   less `install.skip_textures`. That list holds what the export writes beside the crew that the
   Unity bake does not use: textures the client's materials reference but the Doll shaders take
   from elsewhere (a prop's rmo, the shared ramp), and another team's texture a crew material in
   the client points at (`c_AGL01_MemberA_slg_hair_spc`, beside the crew's own hair map).

An FBX export differs from the last one in its creation date and object ids, so a rerun shows the
FBXs as changed even when nothing in them is.

## 3. Animation states

`states.json` lists the crew (`crew`, `formation_clip`), the props (`prop_names`, `prop_idle`),
the rig facts the fixes need (`spine_bone`, `coat`, `prop_materials`) and each state: its source
clip per member, length, `offset`, how each prop is carried, and fixes applied over the donor
clip. `build_team_states.py`'s header documents every option. The states the Unity controller
plays are Idle (native), Run, AimIn, AimHold, Fire, Hit, Death and Vault, and deployed either
Kneel or a line of still states joined by transitions (Fabric's IdleDeployed and SetUp, with
Deploy and Man between them).

- A team whose own idle has the weapon set up (Fabric's mortar) gets a carry `Idle`, the
  default, on the carry the run uses, and keeps the native idle as `IdleDeployed`.
- Each member keeps her place in the formation, her root's location in her own idle.
- Each member's `offset` starts her clip at another point, so the crew never move in step.
- Aim and fire are cut from the native ult at the frame the weapon settles and the shot leaves.
  Fire runs on to the ult's end, so it lasts past the moment the game launches the shot.
- A crew held on one frame (Fabric's `SetUp`, the ult's firing positions) breathes with
  `"breathe"`: each named bone turns from the held pose as it turns from the member's own
  idle's first frame (rotation only, `"scale"` to strengthen it), over a state `"length"` of
  one idle cycle. Spine, chest, neck and head keep held hands within about a centimetre of
  what they hold.
- Vanilla's Deploy and the heavy weapon's set-up both leave the actor in the deployed stance.
  The team's controller tells them apart by a `SetUp` bool, which `SupportTeamStanceSystem`
  writes from `Actor.IsHeavyWeaponDeployed`. A controller with the parameter plays its set-up
  state for it.
- A prop's rest is wherever its own scene left it. A prop placed from the ult is read off the
  ult's own objects (`extra_sources`). A GFL2 prop's take moves the prop by animating its
  armature's `Prop` bone, not its object, so its placement at a frame is that bone's pose against
  its rest.
- A weapon set on the ground (Fabric's mortar) must not move between states. A state with
  `"anchor"` carries the whole ult, crew and props, by the turn and shift that put the ult's
  weapon where the idle has it (`"place": "anchor"`), so the crew keep their places around it. A
  prop's own bones can play the ult too (`"bones"`, the shell down the tube), ride a crew member's
  bone (`"bone_match"`, the shell in the loader's hands), shrink away from a frame (`"hide"`),
  fold for the carry (`"fold"`, the bipod against the tube), or be slung on another member
  (`"bone_sling"`, the bipod on the loader's back, tilted with her spine by `"along"` and placed
  across, out from and up her back by `"at_spine"`). GFL2 builds a weapon from loose pieces, each
  on one bone, and `"split"` gives pieces that share a bone one of their own (Fabric's baseplate,
  cut from its barrel's bone, less the barrel's base cap, which a second cut hands back to the
  barrel, and its bipod's legs, each hinged at its top and folded together by a constant
  `"turn"`). `"stay"` keeps a bone where the prop is placed while its parent is carried elsewhere
  (the barrel in the gunner's hands, its parent the bipod on the loader's back), and `upper` with
  `"of"` borrows another member's clip. A member's own `"of"` plays another member's whole clip
  (the gunner standing as the spotter stands). `upper` may be a list, held in order, so a later
  hold wins. A `"turn"` with `"shift"` also moves a split part (the bipod's traverse head, off its
  top and down alongside the folded legs, clear of the loader's head). `"local"` places a part in
  the ride bone's own frame, read off the rig (the baseplate flat on the gunner's backpack, which
  is rigid on Chest_M).
- A sling's `"tilt"` turns the placed prop and its grips about the contact point, so a lean
  can be tried without working the grips out again.
- A sling's `"ride"` places the prop on the ride bone's own frame (`"at"`, `"rot"`), and
  `"grip_space": "prop"` gives the grips in the prop's own space. Both are read off another
  state's carry, so the barrel sits on the gunner's chest as she stands just as it does in the run,
  whose forward lean would otherwise change it. A `bone_sling` part pinned with `"local"` does the
  same for the bipod on the loader's spine.
- The IK that puts a carrier's hands on her grips turns each arm bone the shortest way, which
  leaves the upper arm's roll to chance and can fold the forearm against the elbow. A grip with
  `"hinge": true` rolls the upper arm first so the forearm folds the way the rig's elbow folds
  at rest. A grip's palm direction alone can ask for a forearm turned half round, which the
  skin shows as a wrung wrist, so `"twist_limit"` (degrees) turns the hand about its knuckles
  by the least that keeps the forearm's twist within it and gives the forearm's twist bone
  (`ElbowPart1`) half the roll. The crew's own clips stay within about 70 degrees. Check a held
  pose from the front and side before baking the whole set.
- An arm held from the idle can be reshaped over the run: `"sway"` swings it forward and back
  about her own side axis, read off Chest_M (`"swing"` sets the amplitude, since Lind's upper
  arms barely swing), with `"hand"` an elbow bend that deepens on the forward swing
  (`"bend"`, `"bend_swing"`), and
  `"raise"` and `"open"` swing upper arms forward and open elbows by set angles, so held
  hands come out of the coat the run's lean pushes them into.
- Check a carry for clipping on the review blend: count the prop vertices that sit within a few
  centimetres behind each body surface, frame by frame, and the hand vertices inside what they
  hold. A normal-side test alone misreads the open coat meshes far from the surface.
- Donor clips key bone locations in the donor's proportions. Lind's clips lengthen the
  crew's arms (upper arm 0.212 and forearm 0.24 against the rig's 0.165 each), so a state on a
  crew member's own clip that must match a Lind state holds Lind's arms (`upper` from her
  scapulae) to keep the same lengths.
- Donor clips leave the crew's hair and coat in their bind pose. `idle_hair` holds each member's
  idle hair. With `coat` set, every member on a donor clip has her coat flaps turned with her
  thighs, which is not an option a state sets.
- The vault's travel goes on the `motion` node, the avatar's root-motion node, and the crew cross
  in place relative to it (see AGENTS.md, Hard-won constraints, on the vault's root motion).
- Exports are unsimplified (`bake_anim_simplify_factor` 0): a pose held on one frame has
  constant curves, which simplification drops.
- A state with `"between": [<from>, <to>]` is a transition, baked from the last frame of one
  built state to the first frame of the next, so a cross-fade does not slide the crew or fly the
  props (Fabric's Deploy, the mortar put down, and Man, the crew going to it). A member goes
  through `"keys"`, poses at shares of the take: either end or a frame of another built state,
  turned (`"turn"`) or moved across the ground (`"shift"`, clear of a crewmate). Between keys her
  feet step the ground, in as many steps as `"stride"` metres cover or `"steps"` per foot sets
  (a key's own `"steps"` for the steps that bring her to it), planted by leg IK. Her body goes as
  far as her feet have on average and her knees bend a little while she steps (`"crouch"`), so a
  planted foot stays in reach between poses that stand on straight legs. Key-to-key stepping is
  for short moves: rising from a kneel, a shuffle into a stance. A leg's knee bends towards the
  pose's own knee where it stands forward of the hip-to-foot line and towards straight ahead as
  it comes back to the line, so a straight or blended leg cannot flip its knee over and a kneel
  keeps its pose.
- Any real move across the ground is a walk (`"walks"`, a stretch from one key to another), not
  a side step, which reads as a crab walk (Cashmere in Deploy and Man, Chiffon in both). Her hips
  glide a straight line at a speed that rises and falls, she turns into the walk and into her end
  facing while she moves, and her shape settles into the end pose once the closing step is down.
  Keep the step count to what the travel needs: efficient steps read as natural, uniform extra
  ones as mechanical.
  - Footfalls follow the body: half a step's travel before the first, each foot under the body
    in the middle of its time alone on the ground, none more than 0.42 m ahead of the other foot
    or past where the hips stop, the last two on the end key's feet. Lifts are brisk and landings
    soft, with heel and toe roll and a little fixed variation, and the builder keeps each crew
    member's lifts at least 4 frames from the others', so nobody steps off together.
  - The standing leg stays straight and the hips ride at the height it reaches (5 to 6 cm of rise
    and fall for the Fabric crew). Each foot counts for that height as far as it is grounded, and
    the hips rise and fall at a limited rate, so a lift or a landing never jumps them.
  - A planted foot pivots on its ball once her body has turned past it by 20 degrees, so turns
    need no steps of their own. A walking knee hinges over the way its foot points more than the
    way her hips face, and follows the pose's own knee only as she settles into the end pose, so a
    foot planted through a turn or set far behind her never twists the leg under it.
  - The first step off a wide stance leads with the foot away from where she goes (`"lead"`),
    quick in the air (`"first"`), so her weight passes over the other foot. A walk ending in a
    kneel finishes turning before she goes down (`"turn_end"`, `"settle"`).
  - A walk back from a kneel starts from a standing key placed over the front foot and turned part
    way to the way back, so she rises with one step of the back foot, and the walk's turns are
    spread wide enough (`"turn_in"`, `"turn_out"`) for the knees to follow.
  - A prop held through a walk rides the hand that holds it (a `"carrier"` rule on that bone), or
    it slides from end to end on its own.
  - Check a walk over every frame, not single renders: the lift frames the bake prints, each
    leg's twist (knee direction against foot direction, within about 20 degrees), the body over
    its support (within about 0.2 m of the planted foot), the hips' ground speed (one rise and
    fall, no dips), the feet's side gap and no leg stretched past its length.
- `"bend"` bends her forward from the waist and `"look"` turns her head towards where a prop ends
  up. `"grab"` puts her hands on points of a prop's parts after the props are placed, so they
  follow it (the loader holding the bipod's legs near the hinge as they open). A grab needs the
  points within arm's reach, about 0.33 m from the shoulder for the Fabric crew: move her kneel or
  turn her torso towards the prop (`"twist"`) until the logged worst miss is a couple of
  centimetres. A twist chooses its side once, when it begins, or takes `"side"` outright: chosen
  afresh each frame it flips when the prop comes straight ahead and the torso snaps across.
  `"look"` with `"level"` turns the head about the vertical only, so she faces the prop without
  dropping her head to it. A prop's `"path"` takes it along its way in stages (the barrel lowered,
  held, then set on its plate), and `"hands"` with `"slide"` moves the grips along the part's long
  axis, read from its mesh, so hands set by a carry still reach it when it stands up. A hand that
  holds a part closes round it (`wrap_hand`): the knuckles go on in the forearm's line squared to
  the part, the palm faces across it, the part lies in the palm at its radius read from the mesh,
  and the elbow aims out from the part so the forearm comes to it across it rather than along it.
  The palm's side is kept from frame to frame, the elbow's aim swings round the arm with the
  hold's weight (a point slid straight between two aims crosses the shoulder-to-hand line and
  turns the elbow over), the hand blends on its forearm, and its bend off the forearm is held to
  45 degrees. `"wrap"` (a window) closes a carry's grip this way and `"fingers": "from"` keeps the
  fingers closed as the first state has them, on a grab too. A holding elbow aims down and out
  from her side (`elbow_out`): aimed from the part back to her shoulder it ends up in her chest
  when the part is in front of her. A landing part takes `"clear"` to keep its axis a set distance
  from her spine while she carries it (a barrel lowered across her chest leaves her hands only her
  body to close in), faded so neither end moves. `"coat"` turns the coat's chains with how far the
  leg IK moved her thighs off the blended pose (stepping, kneeling, the hips sinking), where the
  blended coat otherwise lets the legs through.
- Check an animation over many frames, not one: scan every bone for single-frame jumps (more than
  4 cm in a frame and three times its speed either side), measure each hand against what it holds
  (the palm's gap to the part's surface, the forearm's clearance and the wrist's bend off the
  forearm), each arm against her own torso (its distance from the spine line), and the clearance
  between crew members' heads and the props, and render the risky stretches every two or three
  frames, with close-ups aimed at the hands. `"twist"` turns her spine towards where a prop ends
  up, rising and settling, or rising, holding and turning back, and `"hands"` keeps her hands on a
  prop as the first state holds it, each hand letting go at its own time, so her arms follow the
  prop and it never floats free of her (the gunner lowering the barrel). A prop goes with a
  `"carrier"` on one of her bones, or with `"lands"` is put down where the next state has it.
  `"phases"` runs rules in turn: held at either end, hidden, held on a bone as it was when the
  phase began, `"upright"`, whole in its first shape and stood as the second state stands it, at
  her hand (the folded bipod upright in the loader's hand), or `"opens"`, from the phase before
  into the second state's placement over its own span (the bipod's legs swinging open).
  `"hide_between"` shows a prop only on the first and last frames (the rounds), and `"bones"`
  gives each part of a prop its own rule. The mortar's barrel is parented under its bipod, so each
  part needs a rule of its own or it takes its parent's. Every state a transition reads is built
  in the same run, and naming a transition builds them too. The way back is the take reversed in
  Unity. Check a transition's seams on the review blend: every bone's world position at each end
  should match its neighbour's to within a centimetre.

## 4. Unity bake

`BuildSupportTeam` imports each state FBX with `motion` as the root node, copies each take into
its own clip on the body's bone paths, and builds the prefab and its controller, which declares
every parameter the driver of vanilla's `animator.construct_soldier` (the template
`animator.<team>` is cloned from) sets:

    Unity -batchmode -nographics -quit -projectPath unity \
      -executeMethod WOMENACE.Editor.BuildSupportTeam.Build -team <team>

- The states, their `loop` flags, which one has a `travel`, and the `crew` come from
  `states.json`. Idle is the body, `<team>_default.fbx`, and every other state is
  `<team>_<state>.fbx`. The controller needs Run, AimIn, AimHold, Fire, Hit, Vault and Death,
  and Kneel for a team without a `line`.
- `bake.json` holds what only the bake reads: `canon_height` and `head_bone_height` (GFL2 metres,
  the scale comes from the head bones), `muzzle_marker`, `damage_height`, the renderer
  `stowed_while_aiming`, the props' `prop_texture_sets`, the `ramps` by part with a
  `default_ramp`, the vanilla clips whose `events` a state takes, and where Fire hands back to
  AimHold (`fire_exit`, `fire_blend`; Fabric's ult runs on past the shot, so it hands back later
  and slower). `line` and `aim_from` set up a deployed line (below).
- The materials come from `Gfl2Materials`, which every bake of a GFL2 rip shares.
- A state FBX has no meshes, so Unity collapses its single root node and its curves miss the
  body's paths. The bake moves every path onto the body's and logs how many curves bind (all).
- Root transform curves are dropped, since the root is the game's and root motion rides on
  `MotionT` and `MotionQ`. The import's scale of 100 lives on that root.
- The controller: Idle and Run on `Speed`, Kneel on `Stance` above 0.5 (the actor's `ActorStance`),
  `Aiming` plays AimIn into the looping AimHold and back out through AimOut (AimIn reversed and
  baked as its own clip), `Shoot_Single` or `Special_Attack_1` plays Fire, and Hit, Vault and
  Death (any state, on `OnDeath`).
- A deployed line (`line` in `bake.json`) lists still states from Idle, each later one with the
  parameter that asks for it (`"if"` a bool, `"greater"` above a value): Fabric's IdleDeployed on
  `Stance` above 0.5 and SetUp on the `SetUp` bool that `SupportTeamStanceSystem` writes. The
  bake adds a parameter the line names. Each next pair is joined by the `states.json` state
  `between` them, and that take reversed (`Un<state>`, Undeploy and Unman) is the way back. The
  team wants the last state whose parameter asks for it and goes along the line towards it: a
  still state goes into a bridge on its condition with a short blend, and a bridge hands over at
  exit time 1 with no blend, on along the line, into the still state it reached, or back. AimIn
  starts on `aim_from` (the line's last state by default), so AimOut hands back there or goes
  along the line from it. `Speed` leaves any bridge for Run at once, since the game moves the
  team without waiting for it.
- Run takes vanilla's sprint footstep events and Vault the vanilla crossing's events.
- Materials go on the Doll shaders (`Womenace/DollToon` with the rmo as `_MaskMap`, the shared
  face SDF, the eye shaders). The crew's own ramps are not extracted, so they borrow the nearest.
  Every material is built before any is assigned: building one imports its textures, which can
  reimport the body FBX and undo assignments already made.
- The muzzle sits on `muzzle_marker`, a marker `build_team_states.py` places at the weapon's tip.
  Without it the bake fails.

## 5. Sprites

`make_team_sprites.py` makes the whole set from the art (`sprites` in `team.json`):

- Bark and slot: the leader's face, cut as a Doll's bark is, from a head box per member
  (`heads`, left to right, in the whole-team art) and the leader's place among them (`lead`).
- Stand images: the leader in front, cut from the art by `front_outline` (a polygon round her and
  what she holds, in the art's pixels, feathered), the others smaller and dimmed behind her,
  fitted to the unit window's and the hiring screen's crops (both read from vanilla's UXML).
  Trace the outline on the art with a grid over it, and check the stand image after.
- Emblem: the skill icon over the faction logo in white with a black outline. The badges follow
  as for a Doll (`scripts/doll/make_doll_sprites.py`).
- Weapon icons from the render, straightened by its measured angle. Disabled icons are flat grey.
  A weapon that stands (a mortar) keeps the render's own angle with `"straighten": false` in
  `sprites`, and `"weapon_zoom"` draws it larger than its icon box, up to the box's width, so a
  tall base is cut at the card's edges and the tube fills its length.
- `unit_type`: the skill icon in white with black edges for the unit window's squad type.
- `SkillIcon`: the skill icon for the fire skill. `--item <name>=<art>` makes an item's icons.

## 6. Templates

`templates/support_teams/<team>/` holds:

- The team tag `wmgfl_team_<team>`, never `wmgfl_<team>` (Calibration reads that one as a Doll's).
- The speaker: vanilla tokens only in `Tags` (nothing containing `wmgfl`, which marks a Doll), the
  team's name as `Forename` (it heads the unit window), and the portraits.
- `animator.<team>` from `animator.construct_soldier` with `DeathBehaviour DeathAnimations`.
- `armor.<team>_default` and `weapon.<team>`, each `OnlyEquipableBy` the team tag, `jy_no_sell`.
  The weapon carries no model, since the weapon is in the body. It grants the team's own clone of
  the fire skill, so its uses and icon change without changing the vanilla weapon's.
- `player_squad.<team>`: the team tag and `jy_armor_restricted`, `jy_weapon_restricted`,
  `jy_special_restricted`, `jy_vehicle_restricted`, one element, scale pinned, the badges, and
  the affiliation as `Title`. `AimWithVisualSlot` is `EntireModel`, so aiming turns the whole
  element to its target as a construct's does. The Doll squads it is cloned from aim with the
  `Animation` slot, which leaves the turn to the human controller's rotation parameters, and
  the team's controller has no states that read them.
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
- `SupportTeamUnitTypeSystem` writes "Armed Echelon" and the shared `support_team/unit_type` icon
  (Limonad's skill icon in black and white) as the squad type. A new team uses it as it is.
- `StrategyDiorama.SquadNames.cs` lays each crew member's name on the armoury floor before her.

## 8. Testing

With the dev loader (`jiangyu loader deploy --variant dev`) and `scripts/bridge.py`:

- Unlock: raise the faction's trust with `Diorama.FactionTrust` (`["<faction id>", <change>]`, the
  faction the team's unlock event clones) and finish an operation. The event comes as a message
  on the home screen.
- Items: `Weapons.Give` with an item id gives one into the shared stock.
- `ConversationTraceSystem` (dev builds) logs each conversation as it plays and where.

Check in the roster that the stand image fits the unit window and the hiring screen, the crew's
names lie in front of each member, the squad type reads ARMED ECHELON, and the team's slots list
only its own items. In a mission, check run, aim, fire, hit, death, the deployed stance, the vault
crossing (the team must move as it vaults), and the crew list in the unit panel.
