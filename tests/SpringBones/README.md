# Spring bone replay

Run from the repository root:

```sh
mise compile
dotnet run --project tests/SpringBones -c Release -- <recording.csv[.gz]> <source outfit> [outfit filter] [--set chain.Field=value ...]
```

Replays recorded motion through `SpringBody`, which drives the game's rigs, for
every outfit profile under `code/Systems/SpringBones/Profiles/`, against each
outfit's own baked skeleton read from `unity/Assets/Prefabs/<doll>/<outfit>/main.prefab`.
The chains are built by `SpringChainBuilder`, as the live rig builds them, from
the prefab's bind pose.

Each chain category gets a line, and FAILs past the thresholds in `Report`. The
exit code is non-zero when anything fails. The line reports:

- jerk: the worst chain's tip jerk at p99
- kink: how much sharper a link bends than its target shape
- sink: how far particles sit inside a capsule they collide with, beyond what
  their own rest shape overlaps
- drift: how far a leg-following chain's tip moves around the hips. Reported,
  not judged, since columns near the middle legitimately swing round as a leg
  pushes them aside.
- layer: how deep a garment's particles sit inside the garment under it, after
  the solver has held them out
- swing: how far tips roam, as a measure of liveliness

Recordings come from the dev verbs on a live body:

```sh
python3 scripts/bridge.py verb Springs.Record --args '[20000, "ots14"]'
# play: idle, aim, turn, walk, run, stop
python3 scripts/bridge.py verb Springs.Dump
```

`Springs.Dump` writes `springs_record_<time>_<n>.csv` into the game's
`UserData/womenace-springs/`, outside the mod folder, which a deploy deletes whole.
The reference recording the roster is checked against is kept gzipped in
`tests/SpringBones/recordings/`, named after the outfit it was taken on. The
replay reads a `.csv.gz` as it does a plain dump. The
source outfit is the one the recording was taken on (`ots14/default` above). Its
humanoid motion is retargeted onto every other outfit by each bone's rotation away
from its bind pose in the root's space, which transfers because every Doll is
T-posed against the same reference avatar. A recording without the humanoid pose
columns replays root travel only, on a bind-posed body.

Options:

- `--set chain.Field=value` retunes every chain spec of that name for the run,
  the way `Springs.Set` does live.
- `SPRING_PER_CHAIN=1` lists every chain, not only a failing group's worst.
- `SPRING_TRACE=<chain root>` prints that chain's per-link bends, target kinks
  and collider pushes for the first `SPRING_TRACE_FRAMES` frames (default 200).
- `SPRING_POSE_FRAMES=<frame,...>` with `SPRING_POSE_OUT=<file.json>` writes
  every bone's root-space pose at those frames, plus the bind pose and each
  chain's targets, for rendering the skinned mesh as replayed. Clipping between
  the cloth's bones and between layered garments only shows on the mesh.
- `SPRING_LAYERS=1` lists which garments were found hanging over which, and the
  panels either side of a split up the front that let the legs through.
- `SPRING_MEASURE_LEGS=1` also measures leg-following chains that do not collide
  with the legs against them.

This replays the solver, not the game: there is no LOD culling and no Animator,
and retargeting is an approximation of Mecanim's. Use it to catch folds, jitter
and sinking across the roster and to compare tunings, then confirm feel in game.
