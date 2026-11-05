using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tools;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Divination, Nemesis's starting perk: her attacks are fated. Each target she can attack has a
// fixed set of dice for the turn: one roll per hit check, per critical hit check and per armour
// penetration check, drawn once and kept, so aiming again never rerolls them and changing position only changes the
// chances they are compared against. The preview reads the same dice, so what it shows is what
// happens.
//
// Every attack roll in MENACE is PseudoRandom.NextPercent (0..99) on the mission generator
// (TacticalManager.s_Random), passing when roll <= chance. While her attack resolves, the draw
// right after Skill.GetHitchance on her skill is a hit roll, and the first draw inside the target's
// Entity.OnDamageReceived (base, 4 arguments) is its penetration roll. The critical hit check is
// PseudoRandom.NextTry(chance) (Next(1, 101) <= chance) inside Skill.FillDamageInfo, which runs for
// each hit between those two. Those draws return her fated values. Every other draw (element
// choice, impact spread, death effects) is left alone.
[JiangyuType("Divination")]
public sealed partial class Divination : SkillEventHandlerTemplate
{
    public override SkillEventHandler Create() => new DivinationHandler();
}

[JiangyuType("DivinationHandler")]
public sealed partial class DivinationHandler : SkillEventHandler
{
    private Actor _actor;

    // GetActor can be null when the perk is added at mission start, so she is registered on the
    // first update with a live actor (Makiatto's Interception does the same).
    public override void OnUpdate(EntityProperties _properties)
    {
        if (_actor != null)
            return;
        var actor = GetActor();
        if (actor == null || !actor.IsAlive())
            return;
        _actor = actor;
        DivinationSystem.Register(actor);
    }

    public override void OnDeath() => DivinationSystem.Unregister(_actor);

    public override void OnMissionFinished()
    {
        DivinationSystem.Unregister(_actor);
        _actor = null;
    }
}

// The dice for one target this turn.
public sealed class FatedRolls
{
    public readonly List<int> HitRolls = new();
    public readonly List<int> PenetrationRolls = new();
    public readonly List<int> CritRolls = new();
    // Rolls already spent by attacks on this target this round. The next attack, and the preview
    // of it, carry on from here, so a second shot at the same target gets fresh dice.
    public int HitsUsed;
    public int PenetrationsUsed;
    public int CritsUsed;
    private readonly System.Random _dice;

    public FatedRolls(int seed) => _dice = new System.Random(seed);

    public int Penetration(int index) => Roll(PenetrationRolls, index, 0);

    // NextTry draws 1..100 where NextPercent draws 0..99.
    public int Crit(int index) => Roll(CritRolls, index, 1);

    public int Hit(int index) => Roll(HitRolls, index, 0);

    private int Roll(List<int> rolls, int index, int low)
    {
        while (rolls.Count <= index)
            rolls.Add(_dice.Next(low, low + 100));
        return rolls[index];
    }
}

// What an attack will do on one target, as Divination foresees it.
public sealed class Foresight
{
    public Entity Target;
    public int Shots;
    public int Hits;
    public float HitChance;
    public bool AlwaysHits;
    public int PenetrationChance;
    public int Penetrating;
    public int DamagePerHit;
    public int CritChance;
    public int Crits;
    // Damage from the penetrating hits, critical ones at their critical damage.
    public int Damage;

    public override string ToString() => Hits == 0
        ? $"MISS ({Shots} shot(s) at {HitChance:F0}%)"
        : $"{Hits} of {Shots} hit at {HitChance:F0}% ({Crits} critical at {CritChance}%), {Penetrating} penetrate at {PenetrationChance}%, {Damage} damage ({DamagePerHit} per normal hit)";
}

public sealed class DivinationSystem : JiangyuSystem
{
    private enum Pending { None, Hit, Penetration }

    private static DivinationSystem _instance;
    private static readonly HashSet<IntPtr> Seers = new();
    // Fates by round, skill and target tile. A new round clears them.
    private static readonly Dictionary<(IntPtr Skill, IntPtr Tile), FatedRolls> Fates = new();
    private static int _fateRound = -1;
    private static readonly int Salt = Environment.TickCount;

    // The attack being resolved: its skill, the entity it was aimed at and its fate, whose rolls
    // it spends as it goes. Only rolls against that entity are fated, so others caught in the
    // attack roll freely.
    private static IntPtr _activeSkill;
    private static IntPtr _activeTarget;
    private static FatedRolls _active;
    private static Pending _pending;
    // Set while a preview reads the hit chance and damage, so the read does not spend the attack's
    // rolls. While it is set, the critical hit check returns _previewCrit.
    private static bool _previewing;
    private static bool _previewCrit;
    // Set while FillDamageInfo builds a hit of her attack on its target: its critical hit check
    // is fated.
    private static bool _critPending;


    public override void OnInit()
    {
        _instance = this;
        Context.Patches.Prefix("Il2CppMenace.Tactical.Skills.Skill", "Use", OnUse);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "GetHitchance", OnHitchance);
        Context.Patches.Prefix("Il2CppMenace.Tactical.Entity", "OnDamageReceived", 4, OnDamageReceivedPre);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Entity", "OnDamageReceived", 4, OnDamageReceivedPost);
        Context.Patches.Postfix("Il2CppMenace.Tools.PseudoRandom", "NextPercent", OnNextPercent);
        Context.Patches.Prefix("Il2CppMenace.Tactical.Skills.Skill", "FillDamageInfo", OnFillDamagePre);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "FillDamageInfo", OnFillDamagePost);
        Context.Patches.Postfix("Il2CppMenace.Tools.PseudoRandom", "NextTry", OnNextTry);
        Context.Patches.Postfix("Il2CppMenace.Tactical.TacticalManager", "InvokeOnTurnEnd", OnTurnEnd);
        // The foresight label follows the game's own skill targeting: SkillAction handles the
        // cursor moving onto a tile while a skill is being aimed (TileHighlighter's hover hook only
        // runs outside targeting), and the label goes when targeting is cancelled or put away.
        Context.Patches.Postfix("Il2CppMenace.States.SkillAction", "HandleMouseMoveOnTile", OnAimTile);
        Context.Patches.Postfix("Il2CppMenace.States.SkillAction", "Cancel", OnHideSkill);
        Context.Patches.Postfix("Il2CppMenace.States.SkillAction", "CancelWithoutCancelAim", OnHideSkill);
        Context.Patches.Postfix("Il2CppMenace.Tactical.TileHighlighter", "HideSkill", OnHideSkill);
    }

    // The seers and fates belong to this mission's actors. A scene change (a mission ending,
    // quitting to the menu, loading a save) leaves their pointers stale, so they go.
    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        Seers.Clear();
        Fates.Clear();
        End();
    }

    public override void OnUnload()
    {
        ClearLabel();
        Seers.Clear();
        Fates.Clear();
        End();
        _instance = null;
    }

    internal static void Register(Actor actor)
    {
        if (actor != null)
            Seers.Add(actor.Pointer);
    }

    internal static void Unregister(Actor actor)
    {
        if (actor != null)
            Seers.Remove(actor.Pointer);
    }

    internal static bool IsSeer(Actor actor) => actor != null && Seers.Contains(actor.Pointer);

    // The fate for this skill on this tile this round, drawn on first sight and kept.
    internal static FatedRolls FateFor(Skill skill, Tile tile)
    {
        var round = TacticalManager.Get()?.GetRound() ?? 0;
        if (round != _fateRound)
        {
            Fates.Clear();
            _fateRound = round;
        }
        var key = (skill.Pointer, tile.Pointer);
        if (!Fates.TryGetValue(key, out var fate))
            Fates[key] = fate = new FatedRolls(HashCode.Combine(Salt, round, skill.GetID(), tile.Pointer));
        return fate;
    }

    // What her attack with `skill` on `tile` will do, read from the same dice the attack will
    // use. Every element of a squad fires from its tile, so all shots share one hit chance, and
    // the count of hits is how many of her hit rolls fall at or under it, whichever element takes
    // which roll. Each hit then takes the next penetration roll against the target's penetration
    // chance (see the formula below). A crew struck through its vehicle is not foreseen yet.
    internal static Foresight Foresee(Actor actor, Skill skill, Tile tile)
    {
        var foresight = new Foresight { Target = tile?.GetEntity() };
        var from = actor?.GetTile();
        // Nothing is foreseen for an attack that cannot be made: out of range, no line of fire.
        if (actor == null || skill == null || tile == null || from == null || !skill.IsAttack() || !skill.IsUsableOn(tile, from))
            return null;
        var rng = TacticalManager.GetRandom();
        var saved = rng == null ? default : (rng.m_S0, rng.m_S1, rng.m_S2, rng.m_S3);
        _previewing = true;
        try
        {
            var fate = FateFor(skill, tile);
            var template = skill.GetTemplate()?.TryCast<SkillTemplate>();
            var repetitions = System.Math.Max(1, (int)(template?.Repetitions ?? 1));
            var elements = template == null ? 1 : System.Math.Max(1, skill.GetExecutingElementsAmount(template.ExecutingElement));
            foresight.Shots = repetitions * elements;

            var props = actor.GetSkills().BuildPropertiesForUse(skill, from, tile, null);
            var target = foresight.Target;
            var defence = target?.GetSkills()?.BuildPropertiesForDefense(actor, from, tile, skill);
            var chance = skill.GetHitchance(from, tile, props, defence, true, null, true);
            foresight.HitChance = chance.FinalValue;
            foresight.AlwaysHits = chance.AlwaysHits;
            for (var i = 0; i < foresight.Shots; i++)
                if (chance.AlwaysHits || fate.Hit(fate.HitsUsed + i) <= chance.FinalValue)
                    foresight.Hits++;

            if (target != null && foresight.Hits > 0)
            {
                // A hit is built twice, once forced normal and once forced critical, and each hit
                // takes whichever its fated critical roll gives. FillDamageInfo draws from the
                // mission generator, and its state is put back below, so the preview never moves
                // the real sequence.
                foresight.CritChance = props?.CriticalChance ?? 0;
                var normal = Build(skill, from, tile, false);
                var critical = foresight.CritChance > 0 ? Build(skill, from, tile, true) : normal;
                foresight.DamagePerHit = normal.Damage;
                var hit = target.GetSkills()?.BuildPropertiesForBeingHit(actor, skill, normal);
                var armour = hit?.GetArmorForDirection(normal.ArmorDirection) ?? 0;
                var normalChance = PenetrationChance(target, armour, normal.ArmorPenetration);
                var criticalChance = PenetrationChance(target, armour, critical.ArmorPenetration);
                foresight.PenetrationChance = normalChance;
                for (var j = 0; j < foresight.Hits; j++)
                {
                    // A critical hit check is drawn only when the chance is above zero.
                    var isCrit = foresight.CritChance > 0 && fate.Crit(fate.CritsUsed + j) <= foresight.CritChance;
                    if (isCrit)
                        foresight.Crits++;
                    if (fate.Penetration(fate.PenetrationsUsed + j) > (isCrit ? criticalChance : normalChance))
                        continue;
                    foresight.Penetrating++;
                    foresight.Damage += isCrit ? critical.Damage : normal.Damage;
                }
            }
        }
        finally
        {
            _previewing = false;
            if (rng != null)
            {
                rng.m_S0 = saved.Item1;
                rng.m_S1 = saved.Item2;
                rng.m_S2 = saved.Item3;
                rng.m_S3 = saved.Item4;
            }
        }
        return foresight;
    }

    private static DamageInfo Build(Skill skill, Tile from, Tile tile, bool critical)
    {
        _previewCrit = critical;
        var damage = new DamageInfo();
        skill.FillDamageInfo(damage, from, tile, false);
        return damage;
    }

    // Entity.OnDamageReceived: 100 - 3 * max(0, armour * durability - penetration), the
    // durability being the target's armour durability over its maximum.
    private static int PenetrationChance(Entity target, float armour, int penetration)
    {
        var durability = target.m_ArmorDurability / (float)System.Math.Max(1, target.m_ArmorDurabilityMax);
        var excess = System.Math.Max(0f, armour * durability - penetration);
        return System.Math.Max(0, (int)(100f - excess * 3f));
    }

    private static void OnFillDamagePre(PatchInfo info)
    {
        if (_previewing || _active == null || info.Instance is not Il2CppObjectBase skill || skill.Pointer != _activeSkill)
            return;
        var tile = info.Args?.Count > 2 ? info.Args[2] as Tile : null;
        if (tile?.GetEntity() is { } entity && entity.Pointer == _activeTarget)
            _critPending = true;
    }

    private static void OnFillDamagePost(PatchInfo info) => _critPending = false;

    private static void OnNextTry(PatchInfo info)
    {
        if (!_previewing && !_critPending)
            return;
        try
        {
            if (info.Instance is not Il2CppObjectBase rng || rng.Pointer != TacticalManager.GetRandom()?.Pointer)
                return;
            if (_previewing)
            {
                info.Result = _previewCrit;
                return;
            }
            var chance = info.Args?.Count > 0 && info.Args[0] is int c ? c : 0;
            var fated = _active.Crit(_active.CritsUsed++);
            info.Result = fated <= chance;
            _critPending = false;
        }
        catch (Exception ex) { _instance?.Context.Log.Warn($"divination: crit roll failed: {ex.Message}"); }
    }

    private static void OnUse(PatchInfo info)
    {
        try
        {
            ClearLabel();
            var skill = (info.Instance as Il2CppSystem.Object)?.TryCast<Skill>();
            var tile = info.Args?.Count > 0 ? info.Args[0] as Tile : null;
            var actor = (skill?.GetEntity() as Il2CppObjectBase)?.TryCast<Actor>();
            // Only her next attack replaces the one being resolved. Skills used in between, such
            // as reactions to her shot, leave its fate in place.
            if (skill == null || tile == null || !skill.IsAttack() || !IsSeer(actor))
                return;
            End();
            _active = FateFor(skill, tile);
            _activeSkill = skill.Pointer;
            _activeTarget = tile.GetEntity()?.Pointer ?? IntPtr.Zero;
        }
        catch (Exception ex) { _instance?.Context.Log.Warn($"divination: use failed: {ex.Message}"); }
    }

    private static void OnHitchance(PatchInfo info)
    {
        if (_previewing || _active == null || info.Instance is not Il2CppObjectBase skill || skill.Pointer != _activeSkill)
            return;
        // Only the reads that come right before a hit roll: both callers that roll
        // (Skill.ApplyToTile and Entity.OnDamageReceived) pass _forImmediateUse true, where the
        // targeting UI reads the chance for display. A shot that always hits takes no roll.
        if (info.Args is not { Count: > 6 } args || args[6] is not true)
            return;
        if (info.Result is HitChance chance && chance.AlwaysHits)
            return;
        // Only the check against the target she aimed at: the sixth argument names the entity
        // rolled against, and when it is empty the target tile's own entity is. Other tiles an
        // attack reaches roll freely.
        var target = (args[5] as Il2CppObjectBase)?.Pointer ?? (args[1] as Tile)?.GetEntity()?.Pointer ?? IntPtr.Zero;
        if (target != IntPtr.Zero && target == _activeTarget)
            _pending = Pending.Hit;
    }

    private static void OnDamageReceivedPre(PatchInfo info)
    {
        if (_active != null && info.Args?.Count > 1 && info.Args[1] is Il2CppObjectBase skill && skill.Pointer == _activeSkill
            && info.Instance is Il2CppObjectBase entity && entity.Pointer == _activeTarget)
            _pending = Pending.Penetration;
    }

    // A hit that never reached the penetration check leaves nothing pending behind it.
    private static void OnDamageReceivedPost(PatchInfo info)
    {
        if (_pending == Pending.Penetration)
            _pending = Pending.None;
    }

    private static void OnNextPercent(PatchInfo info)
    {
        if (_pending == Pending.None || _active == null || _previewing)
            return;
        // Draws from other generators pass through and leave the pending roll for the mission's.
        if (info.Instance is not Il2CppObjectBase rng || rng.Pointer != TacticalManager.GetRandom()?.Pointer)
            return;
        try
        {
            int fated;
            if (_pending == Pending.Hit)
                fated = _active.Hit(_active.HitsUsed++);
            else
                fated = _active.Penetration(_active.PenetrationsUsed++);
            info.Result = fated;
        }
        catch (Exception ex) { _instance?.Context.Log.Warn($"divination: roll failed: {ex.Message}"); }
        finally { _pending = Pending.None; }
    }

    private static void OnTurnEnd(PatchInfo info)
    {
        End();
        ClearLabel();
    }

    private const string PrefabAsset = "effects/vision_ghost/main";
    private static GameObject _label;

    private static void OnHideSkill(PatchInfo info) => ClearLabel();

    private static IntPtr _aimedTile;

    private static void OnAimTile(PatchInfo info)
    {
        try
        {
            var tile = info.Args?.Count > 1 ? info.Args[1] as Tile : null;
            if ((tile?.Pointer ?? IntPtr.Zero) == _aimedTile && _label != null && _label.activeSelf)
                return;
            _aimedTile = tile?.Pointer ?? IntPtr.Zero;
            var skill = (info.Instance as Il2CppSystem.Object)?.TryCast<Il2CppMenace.States.SkillAction>()?.GetSkill();
            var actor = (skill?.GetEntity() as Il2CppObjectBase)?.TryCast<Actor>();
            if (tile == null || skill == null || !skill.IsAttack() || !IsSeer(actor) || tile.GetEntity() == null)
            {
                ClearLabel();
                return;
            }
            var foresight = Foresee(actor, skill, tile);
            if (foresight == null)
            {
                ClearLabel();
                return;
            }
            ShowLabel(foresight, tile);
        }
        catch (Exception ex)
        {
            ClearLabel();
            _instance?.Context.Log.Warn($"divination: preview failed: {ex.Message}");
        }
    }

    // The foresight over the target, one line: MISS, or the damage the attack will deal, which is 0
    // when it hits but will not get through the target's armour.
    internal static string LabelText(Foresight f) => f.Hits == 0 ? "MISS" : $"{f.Damage} DMG";

    internal static void ShowLabel(Foresight f, Tile tile)
    {
        if (_label == null)
        {
            var template = _instance?.Context.Assets.Load<GameObject>(PrefabAsset)?.transform.Find("label")?.gameObject;
            if (template == null)
            {
                _instance?.Context.Log.Warn("divination: no label in the vision prefab");
                return;
            }
            _label = UnityEngine.Object.Instantiate(template);
            _label.name = "divination_label";
        }
        var filter = _label.GetComponent<MeshFilter>();
        if (filter == null)
        {
            _instance?.Context.Log.Warn("divination: label has no MeshFilter");
            return;
        }
        // Only the text mesh built here is destroyed, never one the prefab came with.
        if (filter.sharedMesh != null && filter.sharedMesh.name == TextMeshName)
            UnityEngine.Object.Destroy(filter.sharedMesh);
        filter.sharedMesh = TextMesh(LabelText(f), LetterSize);
        var anchor = f.Target?.GetPos() ?? tile.GetPos();
        _label.transform.position = anchor + Vector3.up * 3f;
        if (Camera.main is { } camera)
            _label.transform.rotation = camera.transform.rotation;
        _label.SetActive(true);
    }

    // The characters of the glyph atlas, in cell order (scripts/vfx/make_vision_glyphs.py).
    private const string Glyphs = "0123456789/ ABDEGHIMNOPRST%";
    private const int GlyphGrid = 6;
    private const float LetterSize = 1.4f;
    private const float Advance = 0.56f;
    private const float LineHeight = 1.05f;
    private const string TextMeshName = "divination_text";

    // Text as quads in the label's local XY plane, centred, one atlas cell per letter.
    private static Mesh TextMesh(string text, float size)
    {
        var lines = text.ToUpperInvariant().Split('\n');
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var colours = new List<Color>();
        var triangles = new List<int>();
        for (var l = 0; l < lines.Length; l++)
        {
            var line = lines[l];
            var y = ((lines.Length - 1) / 2f - l) * LineHeight * size;
            var x = -line.Length * Advance * size / 2f;
            foreach (var ch in line)
            {
                var index = Glyphs.IndexOf(ch);
                var cx = x + Advance * size / 2f;
                x += Advance * size;
                if (index < 0 || ch == ' ')
                    continue;
                var row = index / GlyphGrid;
                var col = index % GlyphGrid;
                float u0 = col / (float)GlyphGrid, u1 = (col + 1) / (float)GlyphGrid;
                float v1 = 1f - row / (float)GlyphGrid, v0 = 1f - (row + 1) / (float)GlyphGrid;
                var h = size / 2f;
                var start = vertices.Count;
                vertices.Add(new Vector3(cx - h, y - h, 0));
                vertices.Add(new Vector3(cx + h, y - h, 0));
                vertices.Add(new Vector3(cx + h, y + h, 0));
                vertices.Add(new Vector3(cx - h, y + h, 0));
                uvs.Add(new Vector2(u0, v0));
                uvs.Add(new Vector2(u1, v0));
                uvs.Add(new Vector2(u1, v1));
                uvs.Add(new Vector2(u0, v1));
                for (var k = 0; k < 4; k++)
                    colours.Add(Color.white);
                triangles.Add(start);
                triangles.Add(start + 2);
                triangles.Add(start + 1);
                triangles.Add(start);
                triangles.Add(start + 3);
                triangles.Add(start + 2);
            }
        }
        var mesh = new Mesh
        {
            name = TextMeshName,
            vertices = vertices.ToArray(),
            uv = uvs.ToArray(),
            colors = colours.ToArray(),
            triangles = triangles.ToArray(),
        };
        mesh.RecalculateBounds();
        return mesh;
    }

    internal static void ClearLabel()
    {
        _aimedTile = IntPtr.Zero;
        if (_label != null)
            _label.SetActive(false);
    }

    private static void End()
    {
        _active = null;
        _activeSkill = IntPtr.Zero;
        _activeTarget = IntPtr.Zero;
        _critPending = false;
        _pending = Pending.None;
    }
}
