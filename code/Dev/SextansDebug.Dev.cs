using System.Collections.Generic;
using Il2CppMenace.Tactical;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Dev verbs for diagnosing Sextans' in-mission animation state, invoked over
// the dev-loader bridge as {verb: "SextansDebug.Dump"}. Read-only.
[DevVerb]
public static class SextansDebug
{
    // For every player actor: per element, the transform chain heights (actor
    // ground point, element transform, Root wrapper, Hips bone) plus animator
    // facts. The crouch-sink question is WHERE the vertical offset lives:
    // element transform below the tile = code moved the actor, Root below the
    // element = root motion applied, Hips below Root = the pose itself.
    public static object Dump()
    {
        if (!TacticalManager.IsMissionRunning())
            return new { error = "no mission running" };
        var manager = TacticalManager.Get();
        if (manager == null)
            return new { error = "no tactical manager" };

        var lines = new List<string>();
        var factions = manager.GetFactions();
        for (var i = 0; i < factions.Length; i++)
        {
            var actors = factions[i]?.GetActors();
            if (actors == null)
                continue;
            for (var j = 0; j < actors.Count; j++)
            {
                var actor = actors[j];
                if (actor == null || !actor.IsPlayerControlled(true))
                    continue;
                var template = actor.GetTemplate();
                var elements = actor.GetElements();
                var elementDumps = new List<string>();
                for (var k = 0; k < (elements?.Count ?? 0); k++)
                {
                    var element = elements[k];
                    if (element == null)
                        continue;
                    var t = element.transform;
                    float? rootY = null, hipsY = null;
                    string animState = null;
                    // tile is transiently null mid-move/vault/death: one
                    // tile-less actor must not abort the whole dump
                    var tile = actor.GetTile();
                    float tileY = tile != null ? element.GetTargetPosOnTile(tile, -1).y : float.NaN;
                    Animator animator = null;
                    foreach (var a in element.GetComponentsInChildren<Animator>(true))
                    {
                        animator = a;
                        break;
                    }
                    if (animator != null)
                    {
                        var root = FindDeep(animator.transform, "Root");
                        var hips = FindDeep(animator.transform, "Hips");
                        rootY = root != null ? root.position.y : null;
                        hipsY = hips != null ? hips.position.y : null;
                        var info = animator.GetCurrentAnimatorClipInfo(0);
                        if (info != null && info.Length > 0 && info[0].clip != null)
                            animState = info[0].clip.name;
                    }
                    elementDumps.Add(
                        $"  el{k}: elementY={t.position.y:0.000} animatorY={(animator != null ? animator.transform.position.y.ToString("0.000") : "-")} " +
                        $"rootY={(rootY.HasValue ? rootY.Value.ToString("0.000") : "-")} hipsY={(hipsY.HasValue ? hipsY.Value.ToString("0.000") : "-")} " +
                        $"tileY={tileY:0.000} clip={animState ?? "-"} rootMotion={animator != null && animator.applyRootMotion} " +
                        $"bodyY={(animator != null ? animator.bodyPosition.y.ToString("0.000") : "-")}");
                }
                lines.Add($"{template?.GetID() ?? template?.name} stance={actor.GetStance()}");
                lines.AddRange(elementDumps);
            }
        }
        return string.Join("\n", lines);
    }

    private static Transform FindDeep(Transform node, string name)
    {
        if (node.name == name)
            return node;
        for (var i = 0; i < node.childCount; i++)
        {
            var hit = FindDeep(node.GetChild(i), name);
            if (hit != null)
                return hit;
        }
        return null;
    }
}
