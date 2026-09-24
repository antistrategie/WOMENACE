# Local checkout preparation

Delta runs executable `.agents/prepare` from each new managed checkout root.
It requires Python 3, Git and `cp`, but never launches Unity, restores packages,
downloads dependencies or builds the mod.

The source is the checkout behind Delta's `local` Git remote. With no local
source, preparation is a successful no-op. For a different warm checkout:

```sh
WOMENACE_PREPARE_SOURCE=/path/to/WOMENACE .agents/prepare
```

## What is copied

| Local state | Reason |
| --- | --- |
| `.jiangyu/unity_build/` and `.jiangyu/build-state.json` | Jiangyu can reuse both asset and prefab bundles when their content fingerprints match. |
| `.jiangyu/restored_bundles/` | Avoid repeating AnimationClip restoration while assembling compiled output. |
| `unity/Assets/Imported/` | Avoid host-prefab extraction and preserve the imported inputs to the prefab fingerprint. |
| Ignored `.blend1` backups and donor FBXs under `unity/Assets/Authored/`, including their `.meta` files | Jiangyu fingerprints these files even though Git ignores them. Missing files invalidate the copied prefab cache. |
| `code/bin/Release/` and `.jiangyu/code_build_state` | Reuse the compiled C# DLL when its sources and toolchain match. |
| `code/local.props` | Preserve this machine's game and SDK paths for direct .NET builds and IDEs. |

Copies preserve timestamps and use independent copy-on-write reflinks where GNU
`cp` and the filesystem support them. Other filesystems use ordinary copies,
never hard links or shared writable cache symlinks. Existing destinations are
left alone. Cache stamps and their outputs are copied as a group, with the stamp
published last. Run preparation with both checkouts idle, before editing or
compiling in either one. It is an initialisation hook, not a live cache sync.

The hook creates ignored `mise.local.toml` with `JIANGYU_DIR` pointing at the
source checkout's sibling `jiangyu` when Delta's nested layout breaks the usual
`../jiangyu` default. An existing local mise configuration, a sibling Jiangyu
checkout or an explicit `JIANGYU_DIR` is left alone. Mise may ask you to trust a
new checkout's configuration. The hook does not change mise's trust settings.

## What is left out

`compiled/` is deleted and recreated by Jiangyu on every compile. Unity's
`Library/`, staging, temporary files and logs are large and are unnecessary for
the cached no-Unity path. Global Jiangyu caches already belong to the machine.
Private `.env` files, Studio configuration and pipeline credentials are not
copied.

The hook does not make stale bundles valid. Changed assets, Unity editor scripts,
game versions or Jiangyu versions still cause the appropriate rebuild. Asset
work that really needs Unity incurs a cold import without `Library/`. This keeps
preparation fast for every worktree rather than copying gigabytes of import state
for threads that never need the Editor.

## Verification

```sh
python3 -m unittest discover -s tests -p test_prepare.py -v
```

For a workflow check, run `.agents/prepare` in a fresh managed checkout and then
`mise compile`. On matching inputs, Jiangyu reports incremental reuse of both
asset and prefab bundles without starting Unity.
