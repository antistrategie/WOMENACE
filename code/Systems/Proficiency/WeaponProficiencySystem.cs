using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.Items;
using Il2CppMenace.Strategy;
using Il2CppMenace.Tactical;
using Il2CppMenace.UI;
using Il2CppMenace.UI.Tactical;
using Jiangyu.Sdk;
using UnityEngine.UIElements;
using WeaponClass = WOMENACE.Code.Proficiency.WeaponClass;

namespace WOMENACE.Code;

// Weapon-type proficiency. A doll can equip any weapon, but she is trained on one class (Voymastina
// on assault rifles, Leva on SMGs, and so on). While she wields a weapon of her class she gains an
// accuracy bonus that grows with her affinity, so matching her to her weapon type is rewarded
// without ever forbidding the alternatives. Both the normal and the special weapon slot count; only
// the SSR weapons are excluded, their owner bonus being SsrImprintSystem's and kept separate.
//
// The class taxonomy and the affinity curve are the shared Proficiency model (read here and by the
// affinity badge popover). A doll's own signature weapon is recognised through its authored
// OnlyEquipableBy lock, and other weapons through the id-naming classifier below.
//
// The bonus lands on the leader's EntityProperties.Accuracy (rebuilt from base on each
// UpdatePropertiesBasedOnAttributes, so a flat add never accumulates), which is the accuracy the
// shot rolls against. The stat panels display base accuracy, so the armoury and in-mission panels
// are patched to add the bonus to the Accuracy row. The weapon tooltip carries a "<Doll> Weapon
// Proficiency" line, green when the weapon matches her class and greyed when it does not.
public sealed class WeaponProficiencySystem : JiangyuSystem
{
    // MENACE has no weapon-class field. A weapon's ShortName IS its category label ("Battle Rifle",
    // "SMG", "Light Machinegun", ...) shown as the tooltip subtitle, so classify by that first: it is
    // the reliable signal even when the id gives none (weapon.pirate_outcast_pipe_gun is a Battle
    // Rifle but its id says nothing). About twenty enemy/cut weapons carry no ShortName, so the id's
    // naming convention (weapon.generic_<class>_tier..., specialweapon.<class>_...) is the fallback.
    // Our own weapons match via OnlyEquipableBy, so this only sees weapons a doll picked up elsewhere.
    private static WeaponClass Classify(WeaponTemplate weapon)
    {
        if (weapon == null)
            return WeaponClass.None;
        var byShortName = ClassifyByShortName(weapon);
        return byShortName != WeaponClass.None ? byShortName : ClassifyById(weapon.GetID());
    }

    // The ShortName category as it reads in the armoury/tooltip subtitle, mapped to a proficiency
    // class. Anything not here (Laser Rifle, Plasma, launchers, mortars, ...) is left unclassified.
    private static readonly Dictionary<string, WeaponClass> ShortNameClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Assault Rifle"] = WeaponClass.AssaultRifle,
        ["Heavy Assault Rifle"] = WeaponClass.AssaultRifle,
        ["Enhanced AR"] = WeaponClass.AssaultRifle,
        ["Carbine"] = WeaponClass.AssaultRifle,
        ["Automatic Rifle"] = WeaponClass.AssaultRifle,
        ["SMG"] = WeaponClass.Smg,
        ["Heavy SMG"] = WeaponClass.Smg,
        ["PDW"] = WeaponClass.Smg,
        ["Battle Rifle"] = WeaponClass.Rifle,
        ["Sniper Rifle"] = WeaponClass.Rifle,
        ["Sniper"] = WeaponClass.Rifle,
        ["Bolt-Action Rifle"] = WeaponClass.Rifle,
        ["DMR"] = WeaponClass.Rifle,
        ["AT Rifle"] = WeaponClass.Rifle,
        ["Light Machinegun"] = WeaponClass.MachineGun,
        ["Medium MG"] = WeaponClass.MachineGun,
        ["MMG"] = WeaponClass.MachineGun,
        ["HMG"] = WeaponClass.MachineGun,
        ["Minigun"] = WeaponClass.MachineGun,
        ["Shotgun"] = WeaponClass.Shotgun,
        ["Sweeper"] = WeaponClass.Shotgun,
        ["Sword"] = WeaponClass.Blade,
    };

    private static WeaponClass ClassifyByShortName(WeaponTemplate weapon)
    {
        try
        {
            var line = weapon.ShortName;
            var text = line != null ? line.GetTranslated(null) : null;
            return !string.IsNullOrEmpty(text) && ShortNameClasses.TryGetValue(text.Trim(), out var wc)
                ? wc
                : WeaponClass.None;
        }
        catch { return WeaponClass.None; }
    }

    // Fallback for weapons with no ShortName (enemy constructs, cut tier3 guns). Ordered so a
    // battle-rifle marksman variant is a rifle before the carbine (assault-rifle) rule, and the
    // sniper/marksman family classifies as rifle. A bare "rifle" is deliberately not matched, so an
    // energy "laser_rifle"/"plasma_rifle" stays unclassified.
    private static WeaponClass ClassifyById(string id)
    {
        if (string.IsNullOrEmpty(id))
            return WeaponClass.None;
        if (id.Contains("sword") || id.Contains("blade") || id.Contains("melee")) return WeaponClass.Blade;
        if (id.Contains("battle_rifle")) return WeaponClass.Rifle;
        if (id.Contains("sniper") || id.Contains("marksman") || id.Contains("dmr") || id.Contains("anti_materiel")) return WeaponClass.Rifle;
        if (id.Contains("assault_rifle") || id.Contains("carbine")) return WeaponClass.AssaultRifle;
        if (id.Contains("shotgun") || id.Contains("sweeper")) return WeaponClass.Shotgun;
        if (id.Contains("smg") || id.Contains("pdw")) return WeaponClass.Smg;
        if (id.Contains("machinegun") || id.Contains("chaingun") || id.Contains("minigun") || id.Contains("repeater")) return WeaponClass.MachineGun;
        return WeaponClass.None;
    }

    // A friendly plural for the class, for the tooltip text.
    private static string ClassNoun(WeaponClass wc) => wc switch
    {
        WeaponClass.AssaultRifle => "assault rifles",
        WeaponClass.Smg => "SMGs",
        WeaponClass.Rifle => "rifles",
        WeaponClass.MachineGun => "machine guns",
        WeaponClass.Shotgun => "shotguns",
        WeaponClass.Blade => "blades",
        _ => "weapons",
    };

    // The doll's display name from her character tag ("wmgfl_voymastina" -> "Voymastina").
    private static string NameFromTag(string characterTag)
    {
        var seg = characterTag != null && characterTag.StartsWith(Affinity.Tag + "_", StringComparison.Ordinal)
            ? characterTag.Substring(Affinity.Tag.Length + 1)
            : characterTag;
        return string.IsNullOrEmpty(seg) ? seg : char.ToUpperInvariant(seg[0]) + seg.Substring(1);
    }

    // The current unit-window leader's identity and class, tracked so the weapon tooltip in the
    // armoury/loadout (where the hovered item has no combat wielder) knows whose proficiency to show.
    private string _viewerTag;
    private WeaponClass _viewerClass;

    // The bonus to fold into the armoury stats panel's Accuracy row, set only for the span of that
    // panel's Update so the GetAccuracy postfix knows to add it. 0 at all other times.
    private int _armouryStatsBonus;

    public override void OnInit()
    {
        // Add the bonus to the leader's freshly recomputed properties. Both concrete leaders run
        // this: SquadLeader for a normal doll, Pilot for Voymastina's mech form. Recomputed from
        // base each call, so a flat add here never stacks.
        Context.Patches.Postfix("Il2CppMenace.Strategy.SquadLeader", "UpdatePropertiesBasedOnAttributes", OnLeaderPropsRebuilt);
        Context.Patches.Postfix("Il2CppMenace.Strategy.Pilot", "UpdatePropertiesBasedOnAttributes", OnLeaderPropsRebuilt);

        // The visible incentive: a proficiency line on the weapon tooltip, plus the bonus folded into
        // the unit-window Accuracy stat (which otherwise shows only base accuracy).
        Context.Patches.Postfix("Il2CppMenace.Items.ItemTemplate", "AppendTooltipData", OnWeaponTooltip);
        // The armoury stats panel reads base accuracy, and its rows carry no locale-independent id.
        // Rather than match the Accuracy row by its (localised) label, bracket the panel's ShowPanel
        // with a bonus flag and add the bonus inside GetAccuracy, which the panel calls once for that
        // row. Works in every language.
        Context.Patches.Prefix("Il2CppMenace.UI.Strategy.UnitStatsAndAttributesPanel", "ShowPanel", OnArmouryStatsPre);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitStatsAndAttributesPanel", "ShowPanel", OnArmouryStatsPost);
        Context.Patches.Postfix("Il2CppMenace.Tactical.EntityProperties", "GetAccuracy", OnGetAccuracy);
        // The in-mission selected-unit panel is a separate class whose rows carry the property config,
        // so its Accuracy row is matched by that directly.
        Context.Patches.Postfix("Il2CppMenace.UI.Tactical.SelectedUnitPanel", "UpdateStats", OnTacticalStats);

        // Track the current unit-window leader for the tooltip's whose-proficiency gate.
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "SetLeader", OnWindowChanged);
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindow", "Refresh", OnWindowChanged);
    }

    private void OnLeaderPropsRebuilt(PatchInfo info)
    {
        try
        {
            var leader = (info.Instance as Il2CppSystem.Object)?.TryCast<BaseUnitLeader>();
            var bonus = BonusForLeader(leader);
            if (bonus <= 0)
                return;
            var props = leader.GetCurrentProperties();
            if (props != null)
                props.Accuracy += bonus;
        }
        catch (Exception ex) { Context.Log.Warn($"proficiency: leader props failed: {ex.Message}"); }
    }

    // The accuracy bonus a leader currently earns: her affinity-scaled amount if she is a proficiency
    // doll whose loadout matches her class, else 0. Shared by the properties hook (which adds it to
    // combat accuracy) and the stats-panel patches (which show it). Resolves the speaker Tags once.
    private int BonusForLeader(BaseUnitLeader leader)
    {
        if (leader == null)
            return 0;
        var tags = Affinity.OurSpeakerTags(leader);
        var dollClass = Proficiency.ClassFromSpeakerTags(tags);
        if (dollClass == WeaponClass.None)
            return 0;
        var tag = Affinity.ParseCharacterTag(tags);
        if (tag == null)
            return 0;
        if (!MatchesLoadout(leader, tag, dollClass))
            return 0;
        return Proficiency.AccuracyBonusForLevel(Affinity.LevelFor(Context, Affinity.KeyForTag(tag)));
    }

    // The unit-window stats panel reads the leader's BASE properties for the Accuracy row, so the
    // combat bonus (which lives on current properties) is invisible there. Arm the bonus for the span
    // of the panel's ShowPanel; OnGetAccuracy adds it to the one accuracy read it makes, so the shown
    // number matches the accuracy she fights with. No label match, so it holds in every language.
    private void OnArmouryStatsPre(PatchInfo info)
    {
        try
        {
            var leader = info.Args != null && info.Args.Count > 0
                ? (info.Args[0] as Il2CppSystem.Object)?.TryCast<BaseUnitLeader>()
                : null;
            _armouryStatsBonus = BonusForLeader(leader);
        }
        catch (Exception ex)
        {
            Context.Log.Warn($"proficiency: stats panel failed: {ex.Message}");
            _armouryStatsBonus = 0;
        }
    }

    private void OnArmouryStatsPost(PatchInfo info) => _armouryStatsBonus = 0;

    // While the armoury stats panel is filling its Accuracy row (and only then), add the doll's bonus
    // to the accuracy it reads. GetAccuracy has few call sites and the flag is live only for one panel
    // Update, so combat and every other reader are untouched.
    private void OnGetAccuracy(PatchInfo info)
    {
        if (_armouryStatsBonus != 0 && info.Result is float accuracy)
            info.Result = accuracy + _armouryStatsBonus;
    }

    // The in-mission selected-unit panel reads base accuracy too, from its own tactical stat rows.
    // Bump the Accuracy row for the selected doll so the number matches her combat accuracy. The row
    // is matched by its PropertyDisplayConfig (locale-independent), and the doll is reached through
    // the actor's leader.
    private void OnTacticalStats(PatchInfo info)
    {
        try
        {
            var panel = (info.Instance as Il2CppSystem.Object)?.TryCast<SelectedUnitPanel>();
            var leader = panel?.m_Actor?.TryCast<UnitActor>()?.GetLeader();
            var bonus = BonusForLeader(leader);
            if (bonus <= 0)
                return;

            var stats = panel.m_Stats;
            for (var i = 0; stats != null && i < stats.Count; i++)
            {
                var stat = stats[i];
                if (stat?.m_PropertyConfig == null || stat.m_PropertyConfig.Type != PropertyDisplayConfig.Accuracy)
                    continue;
                BumpValue(stat.m_ValueLabel, bonus, stat.ShowValue);
                break;
            }
        }
        catch (Exception ex) { Context.Log.Warn($"proficiency: tactical stats failed: {ex.Message}"); }
    }

    // Add the bonus to a stat row's shown integer value, if it parses. Shared by both stat panels.
    private static void BumpValue(Label valueLabel, int bonus, Action<string> show)
    {
        if (int.TryParse(valueLabel?.text, out var shown))
            show((shown + bonus).ToString());
    }

    // Whether the doll's loadout earns the bonus. Both the normal and the special weapon slot count,
    // so a doll wielding a special weapon of her class (a machine-gun doll on a vanilla LMG) is
    // rewarded too. A doll with no equippable weapon at all (Voymastina's mech carries a baked-in
    // assault rifle, not an item, and is weapon restricted) is deemed to use her class.
    private static bool MatchesLoadout(BaseUnitLeader leader, string dollTag, WeaponClass dollClass)
    {
        // No readable item container: do not auto-grant. A real leader always has one (m_Items is a
        // readonly ItemContainer), so this only guards the degenerate case, never the mech.
        var items = leader.GetItems();
        if (items == null)
            return false;
        var normal = WeaponAt(items.GetItemAtSlot(ItemSlot.InfantryWeapon));
        var special = WeaponAt(items.GetItemAtSlot(ItemSlot.InfantrySpecial));
        // Both weapon slots empty: a baked-weapon form (Voymastina's mech carries a built-in assault
        // rifle, not an item, and is weapon restricted) counts as her class.
        if (normal == null && special == null)
            return true;
        return WeaponMatches(normal, dollTag, dollClass) || WeaponMatches(special, dollTag, dollClass);
    }

    // Whether one equipped weapon counts as the doll's type: her own signature weapon (locked to her
    // via OnlyEquipableBy, authored to be her class) or a weapon that classifies to her class. SSR
    // weapons never count here: their owner bonus is SsrImprintSystem's, kept separate on purpose.
    private static bool WeaponMatches(WeaponTemplate weapon, string dollTag, WeaponClass dollClass)
    {
        if (weapon == null)
            return false;
        var id = weapon.GetID();
        if (SsrImprintSystem.IsImprintWeapon(id))
            return false;
        if (LockedTo(weapon, dollTag))
            return true;
        return Classify(weapon) == dollClass;
    }

    private static bool LockedTo(WeaponTemplate weapon, string dollTag)
    {
        var only = weapon.OnlyEquipableBy;
        if (only == null || dollTag == null)
            return false;
        for (var i = 0; i < only.Count; i++)
            if (only[i]?.name == dollTag)
                return true;
        return false;
    }

    private static WeaponTemplate WeaponAt(Item item)
        => (item?.GetTemplate() as Il2CppObjectBase)?.TryCast<WeaponTemplate>();

    private void OnWeaponTooltip(PatchInfo info)
    {
        try
        {
            var weapon = (info.Instance as Il2CppSystem.Object)?.TryCast<WeaponTemplate>();
            if (weapon == null)
                return;
            // Both weapon slots are in scope, but never the SSR weapons: those carry their own
            // Imprint section (SsrImprintSystem), so a proficiency line there would double up.
            if (weapon.SlotType != ItemSlot.InfantryWeapon && weapon.SlotType != ItemSlot.InfantrySpecial)
                return;
            if (SsrImprintSystem.IsImprintWeapon(weapon.GetID()))
                return;

            var (viewerTag, viewerClass) = TooltipViewer(info);
            if (viewerTag == null || viewerClass == WeaponClass.None)
                return;

            var data = info.Args != null && info.Args.Count > 0
                ? (info.Args[0] as Il2CppSystem.Object)?.TryCast<TooltipData>()
                : null;
            if (data == null)
                return;

            var matches = WeaponMatches(weapon, viewerTag, viewerClass);

            var heading = data.AddSubheading($"{NameFromTag(viewerTag)} Weapon Proficiency", null, NoIconSize, NoIconColour, true);
            heading?.SetBorderBottom(true);
            heading?.SetMarginTop(6);

            var text = matches
                ? $"Bonus accuracy for wielding her weapon type ({ClassNoun(viewerClass)})."
                : $"Wield {ClassNoun(viewerClass)} for bonus accuracy.";
            var para = data.AddParagraph(
                text, matches ? ParagraphStyle.Positive : ParagraphStyle.Default, null, NoIconSize, NoIconColour, true, false);
            if (!matches)
            {
                var grey = new UnityEngine.Color(0.45f, 0.45f, 0.45f, 1f);
                heading?.SetColor(grey);
                para?.SetColor(grey);
            }
        }
        catch (Exception ex) { Context.Log.Warn($"proficiency: tooltip failed: {ex.Message}"); }
    }

    // Whose proficiency the hovered weapon's tooltip should reflect. The combat wielder is
    // authoritative when it exists (in a mission), else the tracked unit-window leader (the armoury
    // or loadout, where the item is owned by the non-Entity leader we cannot read off the item).
    private (string tag, WeaponClass cls) TooltipViewer(PatchInfo info)
    {
        var item = info.Args != null && info.Args.Count > 1
            ? (info.Args[1] as Il2CppSystem.Object)?.TryCast<BaseItem>()
            : null;
        // A combat wielder is authoritative: read the doll from it and never fall back to the
        // tracked leader, or a non-doll unit's weapon would be mislabelled with the last doll viewed.
        var wielder = (item?.GetContainer()?.GetOwner() as Il2CppObjectBase)?.TryCast<Entity>();
        if (wielder != null)
        {
            var tags = Affinity.OurSpeakerTags(wielder);
            return (Affinity.ParseCharacterTag(tags), Proficiency.ClassFromSpeakerTags(tags));
        }
        // No combat wielder: the item is owned by the non-Entity unit-window leader, so use the
        // tracked one (the armoury/loadout case).
        return (_viewerTag, _viewerClass);
    }

    private void OnWindowChanged(PatchInfo info)
    {
        try
        {
            if (info.Instance is VisualElement window)
            {
                var tags = Affinity.OurSpeakerTags(Affinity.LeaderOf(window));
                _viewerTag = Affinity.ParseCharacterTag(tags);
                _viewerClass = Proficiency.ClassFromSpeakerTags(tags);
            }
        }
        catch (Exception ex) { Context.Log.Warn($"proficiency: window track failed: {ex.Message}"); }
    }

    // Boxed-empty Il2Cpp nullable: a C# null default throws in the tooltip's nullable marshalling.
    private static readonly Il2CppSystem.Nullable<int> NoIconSize = new();
    private static readonly Il2CppSystem.Nullable<UnityEngine.Color> NoIconColour = new();
}
