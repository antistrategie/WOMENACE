using System.Collections;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Makes the mounted bay weapons fireable. Two jobs, both keyed off the same
// registry of skills this system granted:
//
// 1. Granting: when her tactical element spawns, every slotted weapon's
//    SkillsGranted actives are instantiated into her actor's skill container,
//    exactly as if the weapon were equipped. The vanilla deploy step
//    (active.infantry_deploy_heavy_weapon) and carry passives are skipped:
//    the bay has no set-up phase and the arms do the carrying. Tripod fire
//    skills gate on IsDeploymentRequired / IsWeaponSetupRequired, which live
//    on the SHARED template, so gated skills are granted from a runtime
//    clone with the gates switched off. Every reader of the gate (skill bar,
//    selection, use validation, AI) then agrees the skill is live, and no
//    usability method needs patching.
//
// 2. Fire routing: projectiles and muzzle flashes resolve their spawn point
//    through the element's cached muzzle. While a bay skill resolves its
//    muzzle, the cache is swapped to that arm's weapon muzzle, so shots
//    leave the weapon in the fist instead of her rifle.
public sealed class BaySkillSystem : JiangyuSystem
{
    internal sealed class BaySkillInfo
    {
        public int Slot;
        public IntPtr ElementPtr;
        public IntPtr ActorPtr;
        public string SkillId;
        public Skill Skill;
        public Item Item;
        // Position in the weapon's SkillsGranted list. A linked pair matches
        // its two arms BY INDEX rather than by skill id, because a calibrated
        // weapon's actives carry rank-suffixed ids: the arms hold the same
        // weapon, so their lists line up even when the ids do not.
        public int Index;
    }

    // Skill pointer -> which bay slot granted it. Il2Cpp's GC never moves
    // objects, so the pointer is a stable identity for the skill's lifetime.
    private static readonly Dictionary<IntPtr, BaySkillInfo> Registry = new();

    // Vanilla template pointer -> its bay runtime clone. Templates are
    // assets that live for the whole session, so the cache does too.
    private static readonly Dictionary<IntPtr, SkillTemplate> BayClones = new();

    // Whether this skill came out of the bay AT ALL, single or linked.
    // The gates that ask "is the bay carrier wielding this weapon" must use
    // this rather than the grant registry alone: a linked skill lives in its
    // own table, so registry-only checks answered no and silently dropped the
    // SSR imprint from every linked shot.
    internal static bool IsBaySkill(IntPtr skillPtr)
        => skillPtr != IntPtr.Zero
            && (Registry.ContainsKey(skillPtr) || TryGetLinked(skillPtr, out _, out _));

    // How many times a linked shot multiplies the skill's repetitions, or 1
    // for a skill that is not linked. Anything that REWRITES Repetitions has
    // to scale by this or it flattens the link back to a single volley.
    internal static int RepetitionFactor(IntPtr skillPtr)
        => TryGetLinked(skillPtr, out var group, out _) ? BayLink.Groups[group].Length : 1;

    // Whether a group's linked variants exist this mission. The toggle keys
    // on this so a failed grant leaves the link button inert.
    internal static bool HasLinkedSkills(int pair)
        => LinkedSkills.TryGetValue(pair, out var skills) && skills.Count > 0;

    internal static bool TryGetGranted(IntPtr skillPtr, out BaySkillInfo info)
        => Registry.TryGetValue(skillPtr, out info);

    // Whether this tactical actor is the one carrying the bay this mission.
    internal static bool IsBayActor(IntPtr actorPtr)
    {
        if (actorPtr == IntPtr.Zero)
            return false;
        foreach (var info in Registry.Values)
            if (info.ActorPtr == actorPtr)
                return true;
        return false;
    }

    // Whether this actor's bay granted the given skill this mission. Owner
    // gates outside the imprint system (Cheyanne's aim trainer) key on this
    // so the imprint follows the weapon into the bay.
    internal static bool HasGrantedSkill(IntPtr actorPtr, string skillId)
    {
        if (actorPtr == IntPtr.Zero || skillId == null)
            return false;
        foreach (var info in Registry.Values)
            if (info.ActorPtr == actorPtr && info.SkillId == skillId)
                return true;
        return false;
    }

    // The item whose skills slot `slot` granted THIS mission. The skill bar
    // reads this rather than the live BayState so a loadout edited after her
    // element spawned (which only takes effect next spawn) never shows
    // weapon boxes whose skills and mounts do not exist yet.
    internal static Item GrantedItemFor(IntPtr actorPtr, int slot)
    {
        foreach (var info in Registry.Values)
            if (info.ActorPtr == actorPtr && info.Slot == slot)
                return info.Item;
        return null;
    }

    // How many shots slot `slot` has left on the active at position `index`,
    // or -1 when that skill does not meter uses.
    //
    // PER SKILL, not per slot: a weapon's actives carry SEPARATE pools (the
    // light mortar's frag shells and its smoke are different ammo), so a slot
    // has no single "uses left" to speak of. Collapsing them to one number is
    // what made a linked 5/5 skill display 3 and left the fuller skill
    // unusable, because the emptier sibling's count was written over it.
    internal static int SlotSkillUses(IntPtr actorPtr, int slot, int index)
    {
        foreach (var info in Registry.Values)
        {
            if (info.ActorPtr != actorPtr || info.Slot != slot || info.Index != index || info.Skill == null)
                continue;
            try
            {
                if (info.Skill.GetTemplate()?.IsLimitedUses != true)
                    return -1;
                return Math.Max(0, RemainingOf(info.Skill));
            }
            catch
            {
                return -1;
            }
        }
        return -1;
    }

    // Whether an arm can no longer fire at all: it meters its shots and every
    // one of its actives is empty. This is what gates the link toggle,
    // rather than any single skill running out, so a mortar out of frag shells
    // but still holding smoke is not treated as a dead arm.
    internal static bool IsSlotDry(IntPtr actorPtr, int slot)
    {
        var metered = false;
        foreach (var info in Registry.Values)
        {
            if (info.ActorPtr != actorPtr || info.Slot != slot || info.Skill == null)
                continue;
            try
            {
                if (info.Skill.GetTemplate()?.IsLimitedUses != true)
                    return false; // an unmetered active keeps the arm alive
                metered = true;
                if (RemainingOf(info.Skill) > 0)
                    return false;
            }
            catch
            {
                return false;
            }
        }
        return metered;
    }

    private static string Safe(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            return "!" + ex.GetType().Name;
        }
    }

    // Every counter the link touches, per group and per active, so the
    // arms and the linked skill can be compared directly instead of inferred
    // from what the bar happens to draw.
    private static readonly Dictionary<string, SkillTemplate> DiagCache = new(StringComparer.Ordinal);

    internal static List<Dictionary<string, object>> DescribeLinks(IntPtr actorPtr)
    {
        var rows = new List<Dictionary<string, object>>();
        // The BAY carrier, not whoever is selected: the dump is useless if
        // having another unit active drops every arm row from it.
        if (!IsBayActor(actorPtr))
            foreach (var info in Registry.Values)
            {
                actorPtr = info.ActorPtr;
                break;
            }
        foreach (var (pair, skills) in LinkedSkills)
        {
            var members = BayLink.Groups[pair];
            foreach (var (index, linked) in skills.OrderBy(k => k.Key))
            {
                Dictionary<string, object> Describe(string who, Skill skill)
                {
                    if (skill == null)
                        return new Dictionary<string, object> { ["who"] = who, ["skill"] = "(none)" };
                    var heat = HeatOf(skill);
                    return new Dictionary<string, object>
                    {
                        ["who"] = who,
                        ["skill"] = skill.GetTemplate()?.GetID(),
                        ["limited"] = skill.GetTemplate()?.IsLimitedUses,
                        ["left"] = RemainingOf(skill),
                        ["cap"] = skill.GetUses(),
                        ["max"] = skill.GetMaxUses(),
                        ["consumed"] = skill.GetUsesConsumed(),
                        ["reps"] = skill.GetTemplate()?.Repetitions,
                        // Which template object the fire path will read, and
                        // what the template DB resolves for the same id. A
                        // linked row whose tmpl equals db means the clone was
                        // swapped out from under the skill; distinct pointers
                        // with reps 1 mean the clone's doubling was clobbered.
                        ["tmpl"] = Safe(() => ((skill.GetTemplate() as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)?.Pointer ?? IntPtr.Zero).ToString("x")),
                        ["db"] = Safe(() =>
                        {
                            var id = skill.GetTemplate()?.GetID();
                            var db = id == null ? null : Templates.Resolve<SkillTemplate>(id, DiagCache, _ => { });
                            return db == null ? "(none)" : $"{db.Pointer.ToString("x")} reps={db.Repetitions}";
                        }),
                        ["linkhit"] = TryGetLinked(skill.Pointer, out _, out _),
                        ["shared"] = SharesItemUses(skill.GetTemplate()),
                        ["heat"] = heat == null ? "-" : $"{heat.GetHeat()}/{heat.GetMaxHeat()}",
                        ["usable"] = Safe(() => skill.IsUsable().ToString()),
                        ["ap"] = Safe(() => skill.GetActionPointCost().ToString()),
                    };
                }
                foreach (var info in Registry.Values)
                    if (info.ActorPtr == actorPtr && info.Index == index && Array.IndexOf(members, info.Slot) >= 0)
                        rows.Add(Describe($"group{pair}.idx{index}.arm{info.Slot}", info.Skill));
                rows.Add(Describe($"group{pair}.idx{index}.LINKED", linked));
            }
        }
        return rows;
    }

    internal static List<Dictionary<string, object>> DescribeGranted()
        => Registry.Values.OrderBy(i => i.Slot)
            .Select(i => new Dictionary<string, object> { ["slot"] = i.Slot, ["skill"] = i.SkillId })
            .ToList();

    private Element _swapElement;
    private GameObject _swapSaved;
    private bool _swapActive;
    private readonly HashSet<IntPtr> _pendingGrants = new();

    public override void OnInit()
    {
        Instance = this;
        // A linked shot spends from both arms and then re-checks the pair, so
        // an arm running dry takes the mode down with it. SynchronizeUses is
        // the game's own per-turn re-derivation of every skill's counter,
        // which is where the linked skills get their ceiling put back.
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "Use", 2, OnSkillFired);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "SynchronizeUses", OnSynchronizeUses);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Element", "CreateAttachments", OnCreateAttachments);
        Context.Patches.Prefix("Il2CppMenace.Tactical.Skills.Skill", "GetMuzzle", 7, OnMuzzleEnter);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Skills.Skill", "GetMuzzle", 7, OnMuzzleExit);
        // Shot origins resolve through the element's NAMED spawn points
        // (Config.MUZZLE_TYPE_NAME lookups), not just the cached m_Muzzle,
        // so both search paths get overridden while a bay skill executes.
        Context.Patches.Postfix("Il2CppMenace.Tactical.Element", "GetSpawnpoint", 3, OnGetSpawnpoint);
        Context.Patches.Postfix("Il2CppMenace.Tactical.Element", "GetSpawnpoints", 4, OnGetSpawnpoints);
        // Bay weapons weigh on the mission supply bill like equipped gear:
        // their DeployCosts are folded into her leader's total (boxed
        // value-typed Result override, honoured by the dispatcher).
        Context.Patches.Postfix("Il2CppMenace.Strategy.BaseUnitLeader", "GetDeployCosts", OnLeaderDeployCosts);
    }

    private void OnLeaderDeployCosts(PatchInfo info)
    {
        try
        {
            var leader = (info.Instance as Il2CppObjectBase)?.TryCast<Il2CppMenace.Strategy.BaseUnitLeader>();
            if (leader == null || Affinity.CharacterTag(leader) != Bay.CharacterTag)
                return;
            if (info.Result is not Il2CppMenace.Strategy.OperationResources costs)
                return;
            var extra = 0;
            var slots = Bay.Loadout(Context);
            for (var i = 0; i < Bay.SlotCount; i++)
            {
                var weapon = Bay.WeaponOf(Bay.ResolveItem(slots[i]));
                if (weapon != null)
                    extra += weapon.GetDeployCosts().GetAmount();
            }
            if (extra <= 0)
                return;
            costs.SetAmount(costs.GetAmount() + extra);
            info.Result = costs;
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay: deploy cost fold failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public override void OnSceneLoaded(int buildIndex, string sceneName)
    {
        // The items and the leader's skill container are persistent strategy
        // objects, so what the mission granted is unpicked on the way out:
        // stripped from the item skill lists (fed for the skill bar),
        // flagged garbage, and the container's garbage collected so nothing
        // reaches the next strategy save.
        SkillContainer container = null;
        foreach (var info in Registry.Values)
        {
            try
            {
                var itemSkills = info.Item?.GetSkills();
                for (var s = 0; itemSkills != null && s < itemSkills.Count; s++)
                    if (itemSkills[s]?.Pointer == info.Skill.Pointer)
                    {
                        itemSkills.RemoveAt(s);
                        break;
                    }
                info.Skill.m_IsGarbage = true;
                container ??= info.Skill.GetContainer();
            }
            catch
            {
                // the mission tore these down already
            }
        }
        foreach (var (_, skills) in LinkedSkills)
            foreach (var (_, linked) in skills)
                try
                {
                    var itemSkills = linked?.GetItem()?.GetSkills();
                    for (var s = 0; itemSkills != null && s < itemSkills.Count; s++)
                        if (itemSkills[s]?.Pointer == linked.Pointer)
                        {
                            itemSkills.RemoveAt(s);
                            break;
                        }
                    if (linked != null)
                    {
                        linked.m_IsGarbage = true;
                        container ??= linked.GetContainer();
                    }
                }
                catch
                {
                    // the mission tore these down already
                }
        foreach (var carrier in _carriers)
            try
            {
                carrier.m_IsGarbage = true;
                container ??= carrier.GetContainer();
            }
            catch
            {
                // the mission tore it down already
            }
        _carriers.Clear();
        try
        {
            container?.CollectGarbage();
        }
        catch
        {
            // the container died with the mission
        }
        Registry.Clear();
        LinkedSkills.Clear();
        BayLink.ClearMissionState();
        _pendingGrants.Clear();
        _kicks.Clear(); // the coroutines died with the scene
        _swapElement = null;
        _swapSaved = null;
        _swapActive = false;
    }

    private void OnCreateAttachments(PatchInfo info)
    {
        try
        {
            var element = (info.Instance as Il2CppObjectBase)?.TryCast<Element>();
            if (element == null || info.Args == null || info.Args.Count < 2)
                return;
            if (info.Args[0] is not int elementIndex || elementIndex != 0)
                return;
            var items = (info.Args[1] as Il2CppObjectBase)?.TryCast<ItemContainer>();
            if (!Bay.IsHerContainer(items))
                return;
            // The element's entity is wired up AFTER attachments are created,
            // so the grant cannot run inline here: wait for the actor and its
            // skill container to appear. The armoury preview never grows one,
            // and its wait just times out silently.
            if (_pendingGrants.Add(element.Pointer))
                Context.Coroutines.Start(GrantWhenReady(element));
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay skills failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private IEnumerator GrantWhenReady(Element element)
    {
        var elementPtr = element.Pointer;
        try
        {
            for (var frame = 0; frame < 600; frame++)
            {
                Actor actor = null;
                SkillContainer container = null;
                var dead = false;
                try
                {
                    if (element == null || element.WasCollected)
                        dead = true;
                    else
                    {
                        actor = (element.GetEntity() as Il2CppObjectBase)?.TryCast<Actor>();
                        container = actor?.GetSkills();
                    }
                }
                catch
                {
                    dead = true; // element died while waiting
                }
                if (dead)
                    yield break;
                if (container != null)
                {
                    GrantAll(element, actor, container);
                    yield break;
                }
                yield return null;
            }
            Context.Log.Debug("bay: no actor appeared behind her element, skills not granted (armoury preview?)");
        }
        finally
        {
            _pendingGrants.Remove(elementPtr);
        }
    }

    private void GrantAll(Element element, Actor actor, SkillContainer container)
    {
        try
        {
            // Evict stale slots before granting: an item sold or equipped on
            // a doll since the last armoury visit must not arm the bay.
            var dropped = Bay.Prune(Context);
            if (dropped > 0)
                Context.Log.Debug($"bay: pruned {dropped} slot(s) at mission grant (sold or equipped elsewhere)");
            var slots = Bay.Loadout(Context);
            var granted = 0;
            for (var i = 0; i < Bay.SlotCount; i++)
            {
                var item = Bay.ResolveItem(slots[i]);
                var weapon = Bay.WeaponOf(item);
                var templates = weapon?.SkillsGranted;
                if (templates == null)
                    continue;
                for (var k = 0; k < templates.Count; k++)
                {
                    // One skill that refuses to grant must not empty the
                    // whole bay: isolate each, and log which one with the
                    // full il2cpp stack.
                    var id = "?";
                    try
                    {
                        var template = templates[k];
                        if (template == null || !template.IsActive)
                            continue;
                        id = template.GetID();
                        if (id == Bay.DeploySkillId)
                            continue;
                        var grantable = BayTemplateFor(template);
                        var skill = FindGranted(container, id, slots[i]) ?? Grant(container, grantable, item);
                        if (skill == null)
                            continue;
                        skill.m_Template = grantable;
                        // The skill bar's weapon slots draw their buttons from
                        // the ITEM's own skill list, so the grant registers
                        // there too (vanilla Item.AddSkills does the same for
                        // equipped weapons).
                        var itemSkills = item.GetSkills();
                        var listed = false;
                        for (var s = 0; itemSkills != null && s < itemSkills.Count; s++)
                            if (itemSkills[s]?.Pointer == skill.Pointer)
                            {
                                listed = true;
                                break;
                            }
                        if (!listed)
                            itemSkills?.Add(skill);
                        Registry[skill.Pointer] = new BaySkillInfo
                        {
                            Slot = i,
                            ElementPtr = element.Pointer,
                            ActorPtr = actor?.Pointer ?? IntPtr.Zero,
                            SkillId = id,
                            Skill = skill,
                            Item = item,
                            Index = k,
                        };
                        granted++;
                    }
                    catch (Exception ex)
                    {
                        Context.Log.Warn($"bay: granting '{id}' (slot {i}) failed: {ex}");
                    }
                }
                // The item's STAT CARRIER: a vanilla equip adds an ItemSkill
                // to the container whose OnBeforeAnySkillUsed injects the
                // weapon's Damage/AP/ranges into the properties built for a
                // use of that item's skills. Without it every bay shot fires
                // stat-less and deals nothing. Built on its own inert hidden
                // template: sharing a granted fire skill's template made the
                // container's GetSkillByTemplate<Skill> lookup find the
                // carrier and die casting ItemSkill to Skill (the mining
                // laser's heat handler, 185 log errors).
                GrantItemCarrier(container, item, slots[i]);
            }
            GrantLinkedGroups(element, actor, container);
            if (granted > 0)
                Context.Log.Debug($"bay: granted {granted} skill(s) to her actor");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay skills failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // The template a bay grant instantiates from: the original template
    // unless it carries a deployment gate, in which case a cached runtime
    // clone with the gates switched off (the bay has no set-up phase).
    // Cloning is reserved for that case alone: Instantiate on an
    // Odin-serialised template re-deserialises the clone from its bytes,
    // which wipes runtime-injected state - Cheyanne's ricochet carries a
    // managed custom AoE shape, and its clone NRE'd the AI agent's
    // GetAoeRadius at container add. Gated templates (tripods, mortars) are
    // plain data and clone safely. Mission-scoping of granted skills is
    // handled by this system's own scene cleanup, never by template flags.
    // The clone keeps the original's name: GetID derives from the Unity
    // object name, and a "(Clone)" suffix would break duplicate matching
    // and leak into tooltips.

    // ----- link ------------------------------------------------------------

    // Pair -> the linked skills granted for it this mission, by the index of
    // the active they were cloned from.
    private static readonly Dictionary<int, Dictionary<int, Skill>> LinkedSkills = new();

    internal static Item LinkedItemFor(IntPtr actorPtr, int pair)
        => IsBayActor(actorPtr) && LinkedSkills.ContainsKey(pair)
            ? BayLink.LinkedItemFor(Instance?.Context, pair)
            : null;

    private static BaySkillSystem Instance;

    // A paired pair gets its linked variants granted UP FRONT, alongside the
    // singles, and the toggle only decides which are shown. Granting on the
    // toggle instead would mean adding and removing skills from a live
    // container mid-mission, which is what softlocked the OS Tuning kit: the
    // engine sits on a spinner if a skill is destroyed while its use is
    // resolving.
    private void GrantLinkedGroups(Element element, Actor actor, SkillContainer container)
    {
        for (var pair = 0; pair < BayLink.Groups.Length; pair++)
        {
            try
            {
                if (!BayLink.IsGrouped(Context, pair))
                    continue;
                var linkedItem = BayLink.LinkedItemFor(Context, pair);
                if (linkedItem == null)
                    continue;
                var a = BayLink.Groups[pair][0];
                var slots = Bay.Loadout(Context);
                var sourceWeapon = Bay.WeaponOf(Bay.ResolveItem(slots[a]));
                var templates = sourceWeapon?.SkillsGranted;
                if (templates == null)
                    continue;
                var granted = new Dictionary<int, Skill>();
                for (var k = 0; k < templates.Count; k++)
                {
                    var template = templates[k];
                    if (template == null || !template.IsActive || template.GetID() == Bay.DeploySkillId)
                        continue;
                    // EVERY active gets a linked variant, matching vanilla:
                    // the twinfire family covers a weapon's whole kit
                    // (active.mod.twinfire.atgm.direct_fire AND
                    // .indirect_fire, .fire_auto_laser AND .auto_laser.vent).
                    // The surviving tile has to carry the entire weapon or
                    // linking would silently drop abilities.
                    var linkedTemplate = BayLink.LinkedSkillFor(template, BayLink.Groups[pair].Length);
                    if (linkedTemplate == null)
                        continue;
                    // Reuse before granting: element re-creation mid-mission
                    // (a transport ride) and a mid-mission save both leave an
                    // instance already in the container, and granting again
                    // would strand it as an orphan that fires outside every
                    // linked rule.
                    var skill = FindGranted(container, template.GetID(), linkedItem.GetGuid())
                        ?? Grant(container, linkedTemplate, linkedItem);
                    if (skill == null)
                        continue;
                    // Always repointed: a save rebuilds the skill off the
                    // VANILLA template (the clone is not in the DB), which
                    // would drop the multiplied repetitions.
                    skill.m_Template = linkedTemplate;
                    skill.m_Item = linkedItem;
                    var itemSkills = linkedItem.GetSkills();
                    var listed = false;
                    for (var q = 0; itemSkills != null && q < itemSkills.Count; q++)
                        if (itemSkills[q]?.Pointer == skill.Pointer)
                        {
                            listed = true;
                            break;
                        }
                    if (!listed)
                        itemSkills?.Add(skill);
                    granted[k] = skill;
                }
                if (granted.Count > 0)
                {
                    LinkedSkills[pair] = granted;
                    GrantItemCarrier(container, linkedItem, linkedItem.GetGuid());
                    Context.Log.Debug($"bay: group {pair} can link ({granted.Count} skill(s))");
                }
            }
            catch (Exception ex)
            {
                Context.Log.Warn($"bay: link grant failed for group {pair}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        SweepLinkedStrays(container);
    }

    // Anything still riding a linked guid but not re-adopted by the grant
    // above is a stray from an earlier grant (a save taken under a different
    // grouping, or a torn-down element). Left alone it would fire outside
    // every linked rule and follow the leader into the next save.
    private void SweepLinkedStrays(SkillContainer container)
    {
        try
        {
            var live = new HashSet<IntPtr>();
            foreach (var (_, skills) in LinkedSkills)
                foreach (var (_, sk) in skills)
                    if (sk != null)
                        live.Add(sk.Pointer);
            // A currently linked item's STAT CARRIER is as live as its
            // actives: BuildPropertiesForUse folds the weapon's stats into a
            // shot through the carrier, so sweeping it left the linked item
            // carrierless and every linked volley landed for zero damage.
            var carrierGuids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var pair in LinkedSkills.Keys)
            {
                var linkedItem = BayLink.LinkedItemFor(Context, pair);
                var linkedGuid = linkedItem?.GetGuid();
                if (!string.IsNullOrEmpty(linkedGuid))
                    carrierGuids.Add(linkedGuid);
            }
            var removed = 0;
            void Sweep(Il2CppSystem.Collections.Generic.List<BaseSkill> list)
            {
                for (var i = 0; list != null && i < list.Count; i++)
                {
                    var candidate = list[i];
                    var guid = candidate?.GetItem()?.GetGuid();
                    if (guid == null || !guid.StartsWith(BayLink.LinkedGuidPrefix, StringComparison.Ordinal))
                        continue;
                    if (live.Contains(candidate.Pointer))
                        continue;
                    if (candidate.TryCast<ItemSkill>() != null && carrierGuids.Contains(guid))
                        continue;
                    candidate.m_IsGarbage = true;
                    removed++;
                }
            }
            Sweep(container.GetAllSkills());
            Sweep(container.GetSkillsInAddQueue());
            if (removed > 0)
            {
                container.CollectGarbage();
                Context.Log.Warn($"bay: swept {removed} stray linked skill(s) from an earlier grant");
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay: linked stray sweep failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // How many shots a skill has left, read the way the engine keeps it
    // (verified against the disassembly of Skill.Use, ConsumeUse and
    // IsOutOfUses):
    //
    //   GetUses          the remaining count, the number the bar draws
    //   GetMaxUses       the "/N" half of the display
    //   GetUsesConsumed  the cost of ONE shot, set to 1 in the constructor
    //
    // Skill.Use spends uses -= usesConsumed exactly once per activation, so
    // repetitions never re-spend. IsOutOfUses greys the button when
    // uses < usesConsumed. ChangeUsesConsumed changes the per-shot COST, so
    // it must never be used to spend or refund ammo: doing so both drains
    // arms at double rate and greys buttons that still show shots.
    internal static int RemainingOf(Skill skill)
    {
        try
        {
            return skill.GetUses();
        }
        catch
        {
            return -1;
        }
    }

    // Hold a skill at exactly `left` shots, the same way the engine does.
    private static void SetRemaining(Skill skill, int left)
    {
        try
        {
            if (left >= 0 && skill.GetUses() != left)
                skill.SetUses(left);
        }
        catch
        {
            // a skill that will not meter is left as it is
        }
    }

    // Whether a skill draws from a magazine SHARED with the rest of its
    // item's skills, which vanilla marks with a SynchronizeItemUses handler.
    // Rifles and SMGs use it so burst, tap and sustained fire eat one pool;
    // in the bay only specialweapon.tripod_rocketlauncher_atgm and
    // specialweapon.tripod_minigun do. Everything else keeps separate pools,
    // like the light mortar's 8 frag shells beside its 4 smoke rounds.
    internal static bool SharesItemUses(SkillTemplate template)
    {
        var handlers = template?.EventHandlers;
        for (var i = 0; handlers != null && i < handlers.Count; i++)
            if (handlers[i]?.TryCast<Il2CppMenace.Tactical.Skills.Effects.SynchronizeItemUses>() != null)
                return true;
        return false;
    }

    // A linked shot spends one use from EVERY arm in the group, so linking
    // costs exactly what firing them separately would have cost. The linked
    // skill's own counter is then re-derived from the arms rather than
    // tracked, which is what keeps unlinking honest: whatever you spent while
    // linked is already gone from the arms.
    internal static void SpendLinked(IntPtr actorPtr, int pair, int index)
    {
        if (!LinkedSkills.ContainsKey(pair))
            return;
        // A vent is one of the weapon's actives, so it gets a linked variant
        // like any other. Firing it must COOL the arms rather than heat them:
        // left alone it would vent the synthetic item's own pool and leave
        // the real arms as hot as they were.
        var venting = LinkedSkills[pair].TryGetValue(index, out var linkedSkill) && IsVent(linkedSkill);
        foreach (var slot in BayLink.Groups[pair])
        {
            Skill charged = null;
            foreach (var info in Registry.Values)
            {
                if (info.ActorPtr != actorPtr || info.Slot != slot || info.Index != index || info.Skill == null)
                    continue;
                try
                {
                    if (info.Skill.GetTemplate()?.IsLimitedUses == true)
                    {
                        info.Skill.SetUses(Math.Max(0, info.Skill.GetUses() - 1));
                        charged = info.Skill;
                    }
                }
                catch
                {
                    // an arm that cannot be metered simply is not charged
                }
            }
            // The engine's own propagation: SynchronizeUses runs the skill's
            // SynchronizeItemUses handler when it has one, copying the new
            // count onto the item's other pool-sharing skills, and no-ops
            // when it does not.
            if (charged != null)
                charged.SynchronizeUses();
            // Heat is charged whether or not the skill meters ammo: a laser
            // runs on heat alone and would otherwise pay nothing at all. A
            // VENT does the opposite, so it cools both arms instead.
            if (venting)
                VentArm(actorPtr, slot, index);
            else
                SpendLinkedHeat(actorPtr, slot, index);
        }
    }

    private static bool IsVent(Skill skill)
    {
        var handlers = skill?.m_EventHandlers;
        for (var i = 0; handlers != null && i < handlers.Length; i++)
            if (handlers[i]?.TryCast<Il2CppMenace.Tactical.Skills.Effects.VentHeatHandler>() != null)
                return true;
        return false;
    }

    // Dump the heat on one arm, through the vent's own link to the pool it
    // cools rather than by guessing which skill holds it.
    private static void VentArm(IntPtr actorPtr, int slot, int index)
    {
        foreach (var info in Registry.Values)
        {
            if (info.ActorPtr != actorPtr || info.Slot != slot || info.Index != index || info.Skill == null)
                continue;
            try
            {
                var handlers = info.Skill.m_EventHandlers;
                for (var i = 0; handlers != null && i < handlers.Length; i++)
                {
                    var vent = handlers[i]?.TryCast<Il2CppMenace.Tactical.Skills.Effects.VentHeatHandler>();
                    var pool = vent?.GetHeatCapacity();
                    if (pool == null)
                        continue;
                    pool.ResetHeat();
                    pool.Synchronize();
                }
            }
            catch
            {
                // an arm whose vent cannot be read stays hot
            }
        }
    }

    // A skill's live heat, or null when it does not run hot. Heat is a SECOND
    // resource beside ammo, held on a HeatCapacityHandler on the skill
    // instance: the mining laser and the plasma rifle build it and carry a
    // vent skill to dump it.
    internal static Il2CppMenace.Tactical.Skills.Effects.HeatCapacityHandler HeatOf(Skill skill)
    {
        var handlers = skill?.m_EventHandlers;
        for (var i = 0; handlers != null && i < handlers.Length; i++)
        {
            var heat = handlers[i]?.TryCast<Il2CppMenace.Tactical.Skills.Effects.HeatCapacityHandler>();
            if (heat != null)
                return heat;
        }
        return null;
    }

    // Charge both arms the heat a linked shot just made.
    //
    // The linked skill fires from a SYNTHETIC item with its own heat pool, so
    // without this a linked pair of lasers would never overheat while the same
    // two weapons fired singly would: the exploit is worse than the ammo one
    // was, because heat has no counter on the button to give it away.
    //
    // Synchronize() afterwards is what handles a weapon whose skills SHARE one
    // heat pool, which vanilla marks with HeatCapacity's
    // IsSynchronizedWithOtherSkillsOfTheSameItem. That is a heat flag, not an
    // ammo one: shared AMMO is marked by a SynchronizeItemUses handler
    // instead, and the two are unrelated.
    private static void SpendLinkedHeat(IntPtr actorPtr, int slot, int index)
    {
        foreach (var info in Registry.Values)
        {
            if (info.ActorPtr != actorPtr || info.Slot != slot || info.Index != index || info.Skill == null)
                continue;
            try
            {
                var heat = HeatOf(info.Skill);
                if (heat == null)
                    continue;
                var next = heat.GetHeat() + heat.GetHeatPerUse();
                var max = heat.GetMaxHeat();
                heat.SetHeat(max > 0 ? Math.Min(next, max) : next);
                heat.Synchronize();
            }
            catch
            {
                // an arm whose heat cannot be read is left cool
            }
        }
    }

    // Show the linked skill the HOTTER of its two arms, so it greys out as
    // soon as either would have overheated rather than running on a pool of
    // its own.
    private static void MirrorLinkedHeat(IntPtr actorPtr, int pair, int index, Skill linked)
    {
        var target = HeatOf(linked);
        if (target == null)
            return;
        var hottest = -1;
        foreach (var slot in BayLink.Groups[pair])
            foreach (var info in Registry.Values)
            {
                if (info.ActorPtr != actorPtr || info.Slot != slot || info.Index != index || info.Skill == null)
                    continue;
                var heat = HeatOf(info.Skill);
                if (heat != null)
                    hottest = Math.Max(hottest, heat.GetHeat());
            }
        if (hottest >= 0 && target.GetHeat() != hottest)
            target.SetHeat(hottest);
    }

    // Hold each linked skill's remaining uses at ITS OWN emptiest arm's
    // count. Matched by index so a weapon whose actives have separate pools
    // keeps them separate: the linked frag shell tracks the arms' frag pools
    // and the linked smoke tracks their smoke pools.
    internal static void SyncLinkedUses(IntPtr actorPtr)
    {
        foreach (var (pair, skills) in LinkedSkills)
        {
            var members = BayLink.Groups[pair];
            foreach (var (index, skill) in skills)
            {
                try
                {
                    if (skill == null)
                        continue;
                    MirrorLinkedHeat(actorPtr, pair, index, skill);
                    if (skill.GetTemplate()?.IsLimitedUses != true)
                        continue;
                    var least = int.MaxValue;
                    var leastMax = int.MaxValue;
                    foreach (var slot in members)
                        foreach (var info in Registry.Values)
                        {
                            if (info.ActorPtr != actorPtr || info.Slot != slot
                                || info.Index != index || info.Skill == null)
                                continue;
                            if (info.Skill.GetTemplate()?.IsLimitedUses != true)
                            {
                                least = -1;
                                break;
                            }
                            least = Math.Min(least, Math.Max(0, RemainingOf(info.Skill)));
                            leastMax = Math.Min(leastMax, info.Skill.GetMaxUses());
                        }
                    if (least < 0 || least == int.MaxValue)
                        continue;
                    // The max mirrors too: an ammo-pouch boost lands on the
                    // ARMS, and without this the linked counter read over its
                    // own denominator (10/8) and flickered against resyncs.
                    if (leastMax > 0 && leastMax != int.MaxValue && skill.GetMaxUses() != leastMax)
                        skill.SetMaxUses(leastMax);
                    SetRemaining(skill, least);
                }
                catch
                {
                    // leave a skill that will not meter alone
                }
            }
        }
    }

    // Which pair and active a linked skill belongs to, or false.
    internal static bool TryGetLinked(IntPtr skillPtr, out int pair, out int index)
    {
        foreach (var (p, skills) in LinkedSkills)
            foreach (var (k, skill) in skills)
                if (skill != null && skill.Pointer == skillPtr)
                {
                    pair = p;
                    index = k;
                    return true;
                }
        pair = -1;
        index = -1;
        return false;
    }


    // The bay actor the granted skills belong to. There is one bay doll, so
    // the first registry entry names her.
    private static IntPtr BayActorPtr()
    {
        foreach (var info in Registry.Values)
            if (info.ActorPtr != IntPtr.Zero)
                return info.ActorPtr;
        return IntPtr.Zero;
    }

    // Skill.Use ran: it is called once per activation and spends the used
    // skill itself inline, so this is the one place the mirror has to hold.
    //
    // A LINKED skill was engine-spent, so both arms are charged for the shot
    // and every linked counter is re-derived from its arms. An ARM was
    // engine-spent on its own, so only the re-derive runs. Either way the
    // bar is asked to redraw, because nothing else rebuilds it after our
    // writes land.
    private void OnSkillFired(PatchInfo info)
    {
        try
        {
            if (info.Result is bool fired && !fired)
                return; // the activation was refused, nothing was spent
            var skillPtr = (info.Instance as Il2CppObjectBase)?.Pointer ?? IntPtr.Zero;
            if (skillPtr == IntPtr.Zero)
                return;
            if (TryGetLinked(skillPtr, out var pair, out var index))
            {
                var actorPtr = BayActorPtr();
                if (actorPtr == IntPtr.Zero)
                    return;
                SpendLinked(actorPtr, pair, index);
                SyncLinkedUses(actorPtr);
                if (BayLink.DropSpentGroups(actorPtr) > 0)
                    Context.Log.Debug($"bay: group {pair} unlinked, an arm is out of ammo");
                Ots14BayBarSystem.RequestRefresh?.Invoke();
                return;
            }
            foreach (var granted in Registry.Values)
            {
                if (granted.Skill == null || granted.Skill.Pointer != skillPtr)
                    continue;
                SyncLinkedUses(granted.ActorPtr);
                if (BayLink.DropSpentGroups(granted.ActorPtr) > 0)
                    Context.Log.Debug("bay: group unlinked, an arm is out of ammo");
                Ots14BayBarSystem.RequestRefresh?.Invoke();
                return;
            }
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay: link spend failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void OnSynchronizeUses(PatchInfo info)
    {
        try
        {
            // A global postfix, so the cheap identity check comes first:
            // every skill in the game synchronises through here each turn,
            // and only bay skills warrant the full mirror.
            var skillPtr = (info.Instance as Il2CppObjectBase)?.Pointer ?? IntPtr.Zero;
            if (skillPtr == IntPtr.Zero
                || (!Registry.ContainsKey(skillPtr) && !TryGetLinked(skillPtr, out _, out _)))
                return;
            var actorPtr = BayActorPtr();
            if (actorPtr != IntPtr.Zero)
                SyncLinkedUses(actorPtr);
        }
        catch
        {
            // the ceiling is re-applied on the next synchronise
        }
    }

    private SkillTemplate BayTemplateFor(SkillTemplate template)
    {
        if (!template.IsDeploymentRequired && !template.IsWeaponSetupRequired)
            return template;
        if (BayClones.TryGetValue(template.Pointer, out var cached) && cached != null)
            return cached;
        var clone = UnityEngine.Object.Instantiate(template);
        clone.name = template.name;
        // GetID caches into m_ID during Instantiate, under the "X(Clone)"
        // name. Renaming does not refresh it, so the cache is stamped too.
        clone.m_ID = template.GetID();
        clone.hideFlags = HideFlags.HideAndDontSave;
        clone.IsDeploymentRequired = false;
        clone.IsWeaponSetupRequired = false;
        clone.IsRemovedAfterCombat = true;
        BayClones[template.Pointer] = clone;
        return clone;
    }

    // Mission-granted ItemSkill stat carriers, for the scene-end cleanup.
    private readonly List<BaseSkill> _carriers = new();

    private const string CarrierTemplateId = "passive.wmgfl_bay_carrier";
    private readonly Dictionary<string, SkillTemplate> _carrierCache = new(StringComparer.Ordinal);

    private void GrantItemCarrier(SkillContainer container, Item item, string itemGuid)
    {
        try
        {
            var template = Templates.Resolve<SkillTemplate>(
                CarrierTemplateId, _carrierCache, msg => Context.Log.Warn($"bay: {msg}"));
            if (template == null)
                return;
            var all = container.GetAllSkills();
            for (var i = 0; all != null && i < all.Count; i++)
                if (all[i]?.TryCast<ItemSkill>() != null && all[i].GetItem()?.GetGuid() == itemGuid)
                    return; // already carried (a re-grant in the same mission)
            var queued = container.GetSkillsInAddQueue();
            for (var i = 0; queued != null && i < queued.Count; i++)
                if (queued[i]?.TryCast<ItemSkill>() != null && queued[i].GetItem()?.GetGuid() == itemGuid)
                    return;
            var carrier = new ItemSkill(template, item);
            if (container.Add(carrier))
                _carriers.Add(carrier);
            else
                Context.Log.Warn($"bay: container rejected the stat carrier for '{item.GetTemplate()?.GetID()}'");
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"bay: stat carrier failed for '{item?.GetTemplate()?.GetID()}': {ex.GetType().Name}: {ex.Message}");
        }
    }

    private Skill Grant(SkillContainer container, SkillTemplate template, Item item)
    {
        // a boxed EMPTY nullable, not managed null: null for an
        // Il2CppSystem.Nullable proxy misbehaves in the interop marshalling
        var skill = template.CreateSkill(new Il2CppSystem.Nullable<Il2CppMenace.Strategy.Origin>());
        if (skill == null)
        {
            Context.Log.Warn($"bay: CreateSkill returned null for '{template.GetID()}'");
            return null;
        }
        // The item link ties the skill back to its bay weapon for the player
        // (skill bar grouping, icon, tooltip source).
        skill.m_Item = item;
        if (!container.Add(skill))
        {
            Context.Log.Warn($"bay: container rejected '{template.GetID()}'");
            return null;
        }
        return skill;
    }

    // The already-granted instance for this template id AND item, or null.
    // Matched by id rather than template pointer so instances rebuilt from
    // the vanilla template by a mid-mission reload are found (and healed by
    // the caller) instead of duplicated. Queue-aware like
    // SkillEffects.FindInstance: a skill added this frame is still in the
    // add queue.
    private static Skill FindGranted(SkillContainer container, string skillId, string itemGuid)
    {
        Skill Match(Il2CppSystem.Collections.Generic.List<BaseSkill> list)
        {
            for (var i = 0; list != null && i < list.Count; i++)
            {
                var candidate = list[i];
                if (candidate?.GetTemplate()?.GetID() != skillId)
                    continue;
                if (candidate.GetItem()?.GetGuid() != itemGuid)
                    continue;
                if (candidate.TryCast<Skill>() is { } skill)
                    return skill;
            }
            return null;
        }
        return Match(container.GetAllSkills()) ?? Match(container.GetSkillsInAddQueue());
    }

    // -- fire routing -------------------------------------------------------

    // The bay weapon muzzle that should stand in for the element's own, or
    // null when this call is not a bay skill resolving a muzzle-ish spawn
    // point on her element.
    private Transform BayMuzzleFor(PatchInfo info)
    {
        if (info.Args is not { Count: > 0 } args || args[0] is not string name
            || !name.Contains("muzzle", StringComparison.OrdinalIgnoreCase))
            return null;
        var element = (info.Instance as Il2CppObjectBase)?.TryCast<Element>();
        if (element == null || !BayMountSystem.Mounts.TryGetValue(element.Pointer, out var set))
            return null;
        var actor = (element.GetEntity() as Il2CppObjectBase)?.TryCast<Actor>();
        var active = actor?.GetActiveSkill();
        if (active == null)
            return null;
        if (Registry.TryGetValue(active.Pointer, out var granted))
            return set.Slots[granted.Slot]?.Muzzle?.transform;
        // A linked shot belongs to BOTH arms, so its shots leave them in turn.
        // Vanilla does the same for a twin mount through GetTwinFireSlot: twin
        // fire is not one simultaneous shot but the pair's shots interleaved
        // at half the delay, which is what makes both barrels look live.
        // Without this a linked skill matched no bay slot at all and fired
        // from her rifle.
        if (TryGetLinked(active.Pointer, out var pair, out _))
        {
            var members = BayLink.Groups[pair];
            for (var step = 0; step < members.Length; step++)
            {
                _linkedMuzzleCycle++;
                var slot = members[_linkedMuzzleCycle % members.Length];
                var muzzle = set.Slots[slot]?.Muzzle?.transform;
                if (muzzle != null)
                    return muzzle;
            }
        }
        return null;
    }

    // Rotates the arm a linked shot leaves from, one spawn point at a time,
    // so a pair alternates and a quad walks all four barrels. The recoil
    // kicks rotate on their own counter, never on this one.
    private static int _linkedMuzzleCycle;
    private static int _linkedKickCycle;

    private void OnGetSpawnpoint(PatchInfo info)
    {
        try
        {
            var muzzle = BayMuzzleFor(info);
            if (muzzle != null)
                info.Result = muzzle;
        }
        catch
        {
            // never let a spawn-point lookup die on our account
        }
    }

    private void OnGetSpawnpoints(PatchInfo info)
    {
        try
        {
            var muzzle = BayMuzzleFor(info);
            if (muzzle == null)
                return;
            // The out parameter is List<Spawnpoint>, NOT List<Transform>: the
            // wrong cast returned null every time and left this whole override
            // dead, so a bay skill resolving its origin through the plural
            // path fired from her equipped rifle.
            var list = (info.Args is { Count: > 3 } args ? args[3] as Il2CppObjectBase : null)
                ?.TryCast<Il2CppSystem.Collections.Generic.List<Il2CppMenace.Tactical.Spawnpoint>>();
            if (list == null)
                return;
            // Retarget what vanilla found rather than clearing and rebuilding.
            // The list belongs to the caller and this is a postfix, so a Clear
            // would discard spawn points collected before the call, and each
            // entry's Angle and Distance stay meaningful once its Transform
            // points at the arm. BayMuzzleFor already narrowed this to OTs-14
            // executing a bay-granted skill, where every shot does come from
            // that one muzzle.
            for (var i = 0; i < list.Count; i++)
            {
                var point = list[i];
                if (point != null)
                    point.Transform = muzzle;
            }
        }
        catch
        {
            // never let a spawn-point lookup die on our account
        }
    }

    private void OnMuzzleEnter(PatchInfo info)
    {
        var skillPtr = (info.Instance as Il2CppObjectBase)?.Pointer ?? IntPtr.Zero;
        if (skillPtr == IntPtr.Zero)
            return;
        int slot;
        if (Registry.TryGetValue(skillPtr, out var granted))
        {
            slot = granted.Slot;
        }
        else if (TryGetLinked(skillPtr, out var pair, out _))
        {
            // A linked volley's rounds walk the group's arms, so the kicks
            // and the swapped muzzle rotate with them. On its OWN counter:
            // each planned round calls GetMuzzle here AND GetSpawnpoint in
            // BayMuzzleFor, and sharing one counter double-stepped it, which
            // parked every projectile on the same arm while the kicks took
            // the other.
            var members = BayLink.Groups[pair];
            _linkedKickCycle++;
            slot = members[(_linkedKickCycle & int.MaxValue) % members.Length];
        }
        else
        {
            return;
        }
        try
        {
            // A stale swap from an original that threw (the postfix is
            // skipped then) is restored before saving over it, so the
            // element's own muzzle reference can never be lost.
            if (_swapActive && _swapElement != null)
            {
                try { _swapElement.m_Muzzle = _swapSaved; } catch { }
                _swapActive = false;
            }
            var element = (info.Args is { Count: > 1 } args ? args[1] as Il2CppObjectBase : null)?.TryCast<Element>();
            if (element == null || !BayMountSystem.Mounts.TryGetValue(element.Pointer, out var set))
                return;
            // One queued kick per planned round (fires even for a weapon
            // with no model: the bare arm still sells the shot).
            var skill = (info.Instance as Il2CppObjectBase)?.TryCast<Skill>();
            if (skill != null)
                QueueKick(element.Pointer, set, slot, skill);
            var muzzle = set.Slots[slot]?.Muzzle;
            if (muzzle == null)
                return;
            _swapElement = element;
            _swapSaved = element.m_Muzzle;
            element.m_Muzzle = muzzle;
            _swapActive = true;
        }
        catch
        {
            _swapActive = false;
        }
    }

    // -- recoil kicks -------------------------------------------------------
    //
    // The engine resolves a whole burst's shots in ONE frame (planning pass:
    // ten GetMuzzle calls in three milliseconds on the MMG; SpawnMuzzle is
    // never called at all), so no engine hook fires per visible shot. The
    // planning calls COUNT the rounds instead, and a drain coroutine plays
    // one kick per round at the skill's own repetition cadence.

    private sealed class KickQueue
    {
        public int Pending;
        public bool Running;
        public float Interval;
        // How long the FIRST kick waits: the skill's authored delays put
        // the visible shot well after the planning pass (the Particle
        // Cannon's beam lands ~0.6s later), and a kick before the bang
        // reads as a misfire.
        public float Lead;
    }

    private readonly Dictionary<long, KickQueue> _kicks = new();

    private void QueueKick(IntPtr elementPtr, BayMountSet set, int slot, Skill skill)
    {
        var record = set.Slots[slot];
        if (record?.Scapula == null || set.ArmsAnimator == null)
            return;
        var key = (elementPtr.ToInt64() << 2) | (uint)slot;
        if (!_kicks.TryGetValue(key, out var queue))
            _kicks[key] = queue = new KickQueue();
        try
        {
            var template = (skill.GetTemplate() as Il2CppObjectBase)?.TryCast<SkillTemplate>();
            queue.Interval = template?.RepetitionDelay ?? 0.12f;
            queue.Lead = template == null
                ? 0f
                : template.MinDelayBeforeSkillUse + template.DelayAfterAnimationTrigger + template.MinElementDelay;
            // A beam fades in rather than appearing: hold the kick a touch
            // longer so it lands on the visible flash.
            if ((template?.ProjectileData as Il2CppObjectBase)?.TryCast<LaserProjectileData>() != null)
                queue.Lead += 0.2f;
        }
        catch
        {
            queue.Interval = 0.12f;
            queue.Lead = 0f;
        }
        queue.Pending++;
        if (!queue.Running)
        {
            queue.Running = true;
            Context.Coroutines.Start(DrainKicks(set, record, queue));
        }
    }

    private System.Collections.IEnumerator DrainKicks(BayMountSet set, BayMountRecord record, KickQueue queue)
    {
        try
        {
            if (queue.Lead > 0f)
            {
                var start = UnityEngine.Time.time + queue.Lead;
                while (UnityEngine.Time.time < start)
                    yield return null;
            }
            while (queue.Pending > 0)
            {
                queue.Pending--;
                try
                {
                    set.ArmsAnimator.SetTrigger("Fire_" + record.Scapula.name);
                    Context.Log.Debug($"bay: recoil kick Fire_{record.Scapula.name} ({queue.Pending} queued)");
                }
                catch
                {
                    queue.Pending = 0; // the animator died: drop the rest
                }
                var until = UnityEngine.Time.time + Mathf.Max(queue.Interval, 0.08f);
                while (UnityEngine.Time.time < until)
                    yield return null;
            }
        }
        finally
        {
            queue.Running = false;
        }
    }

    private void OnMuzzleExit(PatchInfo info)
    {
        if (!_swapActive)
            return;
        _swapActive = false;
        try
        {
            _swapElement.m_Muzzle = _swapSaved;
        }
        catch
        {
            // element died mid-shot: the cache dies with it
        }
        _swapElement = null;
        _swapSaved = null;
    }

}
