# Sinbreaker thrusters

Original GFL2 particles for both Sinbreaker outfits. Family `10660801_fire_01`
serves the back and legs, and `10660801_fire_02` serves the waist. Each has
start, thrust, end and idle prefabs. The final mech references these as nested
prefabs, sharing their meshes, materials and textures.

The source enables nozzle mesh `G1` and disables the alternative `G2` mesh.
That visibility applies to the outline copies too. FBX does not preserve it,
and displaying both meshes covers the active exhausts with the three-port housings.

`source.json` preserves the native particle modules, curves, gradients, bursts,
renderer settings and attachment transforms. The extraction recipe is
`scripts/vehicle/extract_sinbreaker_thrusters.py`, including the CN bundle
archive offsets. UnityFS archives inside one bundle are independently encrypted.
Native serialisation versions are retained because missing versions make Unity
apply obsolete colour and burst conversions.

`Womenace/MechThrusters` implements the source shader subset used by these
materials. The three source variants differ in mask distortion and blending.
Source material mode takes precedence over particle custom data. MENACE exposure
is disabled for these decorative glows, as with the other imported GFL2 glows.

The `Thrusters` Animator layer synchronises with the mech's base layer. RunStart
uses start, Run and UltraSkill use thrust, RunStop and UltraSkillHit use end,
Death uses off, and other states use idle. Source skeletal clips contain no
effect activation tracks, so this is a MENACE state mapping rather than an
import of GFL2 runtime timing.

Flames and glows remain visible through GFL2's `LoopUntilReplaced` ring buffers.
Their one-shot emitters do not repeat, which avoids accumulating retained
particles. Sparks and streaks keep their authored continuous emission, and
start and end retain their source settings. Hierarchy scaling makes the effects
follow the resized mech. GFL2 scripts are not included, and every renderer has a
valid material, including disabled source renderers with empty material slots.

Unity batch-mode entry points:

- `WOMENACE.Editor.SinbreakerThrusters.Rebuild` rebuilds only the thrusters and
  their controller bindings, preserving body assets.
- `WOMENACE.Editor.SinbreakerThrusters.Validate` checks both outfits, state
  activation and missing references. It checks flame/glow visibility and stable
  particle counts frame by frame for 13 seconds at 30, 60 and 144 fps. A copy with
  ring buffering disabled verifies that the check detects particle expiry.
- `WOMENACE.Editor.SinbreakerThrusters.Preview` renders the actual HDRP material
  and controller with simulated particles. Supply `-outfit default` or `erwin`,
  `-state Run`, `-out <png>` and optionally `-seconds 5.75` (default 0.5).
  This needs graphics, not `-nographics`. On Linux, use `-force-vulkan` because
  HDRP does not support OpenGL.

The full mech builder also includes the thrusters. Run `mise compile` after
rebaking to update the mod's bundles.
