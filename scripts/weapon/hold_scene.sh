#!/bin/bash
# Opens a weapon in Blender with a doll holding it, as the game holds it, to place it by eye.
#
#   scripts/weapon/hold_scene.sh <weapon> [doll]
#
# The doll is the weapon's own (its name without _ssr) unless one is named. She stands in a
# stance recorded from a mission by the Rig.Record dev verb, which Unity poses her in first:
# her own (scripts/weapon/holds/<doll>.txt) where she has one, for a doll who holds her weapon
# her own way, and the aiming stance every other doll shares (holds/aim.txt). UNITY and BLENDER
# name the two programs.
set -e
cd "$(dirname "$0")/../.."
weapon=$1
doll=${2:-${weapon%_ssr}}
[ -f "unity/Assets/Prefabs/weapon/$weapon/main.prefab" ] || { echo "no weapon prefab for $weapon"; exit 1; }
[ -f "unity/Assets/Prefabs/$doll/default/main.prefab" ] || { echo "no doll $doll: name one as the second argument"; exit 1; }
version=$(sed -n 's/m_EditorVersion: //p' unity/ProjectSettings/ProjectVersion.txt)
unity=${UNITY:-$HOME/Unity/Hub/Editor/$version/Editor/Unity}
out=$(mktemp -d)
stance=scripts/weapon/holds/$doll.txt
[ -f "$stance" ] || stance=scripts/weapon/holds/aim.txt
echo "$weapon Assets/Prefabs/$doll/default/main.prefab Assets/Prefabs/weapon/$weapon/main.prefab $PWD/$stance" > "$out/list.txt"
echo "posing $doll with $weapon in $stance (about a minute)"
"$unity" -batchmode -nographics -quit -projectPath unity -executeMethod Womenace.EditorTools.WeaponHoldCheck.Run -list "$out/list.txt" -out "$out" -logFile "$out/unity.log" > /dev/null 2>&1 || true
[ -f "$out/$weapon.obj" ] || { echo "the pose was not written: see $out/unity.log"; exit 1; }
exec "${BLENDER:-blender}" --python scripts/weapon/hold_scene.py -- --weapon "$weapon" --glb "$PWD/unity/Assets/Authored/weapon/$weapon/raw.glb" --pose "$out/$weapon"
