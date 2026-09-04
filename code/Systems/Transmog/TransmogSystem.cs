using Il2CppMenace.Items;
using Il2CppMenace.Tactical;
using Jiangyu.Game;
using Jiangyu.Sdk;
using UnityEngine;

namespace WOMENACE.Code;

// Renders every doll in her selected transmog outfit while she wears ordinary vanilla armour
// for stats.
//
// EntityVisuals.DetermineArmorPrefab is THE body-prefab pick (the tactical spawn and the
// armoury preview are its only callers), dispatching on the EQUIPPED armour. The postfix
// replaces its result with the selected outfit's model for our characters, on every element of
// the squad, so a doll never renders as a vanilla soldier body whatever armour she wears.
public sealed class TransmogSystem : JiangyuSystem
{
    private readonly Dictionary<string, ArmorTemplate> _armorCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GameObject> _bodyCache = new(StringComparer.Ordinal);

    public override void OnInit()
    {
        Context.Patches.Postfix("Il2CppMenace.Tactical.EntityVisuals", "DetermineArmorPrefab", OnDetermineArmorPrefab);
    }

    // Args: EntityTemplate, Squaddie, elementIndex, Gender, ItemContainer, UnitLeaderTemplate,
    // PseudoRandom. The EntityTemplate carries the doll's identity tag, so nothing else is read.
    private void OnDetermineArmorPrefab(PatchInfo info)
    {
        try
        {
            var characterTag = CharacterTagOf(info.Args.Count > 0 ? info.Args[0] as EntityTemplate : null);
            if (characterTag == null)
                return;
            var armorId = Transmog.SelectionFor(Context, characterTag);
            var model = OutfitModel(characterTag, armorId);
            if (model != null)
            {
                info.Result = model;
                Context.Log.Debug($"transmog: '{characterTag}' renders '{armorId}'");
            }
        }
        catch (Exception ex) { Context.Log.Warn($"transmog: body-prefab swap failed: {ex.Message}"); }
    }

    // The "wmgfl_<name>" identity tag on a doll's EntityTemplate, or null for any other unit.
    // Only tags that name a registered character count, so unrelated wmgfl_* tags never match.
    private static string CharacterTagOf(EntityTemplate template)
    {
        var tags = template != null && template.IsAlive() ? template.Tags : null;
        if (tags == null)
            return null;
        for (var i = 0; i < tags.Count; i++)
        {
            var name = tags[i]?.name;
            if (name != null && Transmog.DefaultFor(name) != null)
                return name;
        }
        return null;
    }

    // The body prefab an outfit renders, loaded from the mod's bundles the first time a doll
    // wears it, so a doll nobody fields in a session costs no memory. The outfit templates
    // carry no body of their own; an outfit without a baked body falls back to whatever its
    // template's model lists hold.
    private GameObject OutfitModel(string characterTag, string armorId)
    {
        if (armorId == null)
            return null;
        if (_bodyCache.TryGetValue(armorId, out var cached))
            return cached;

        GameObject model = null;
        var asset = Transmog.BodyAssetFor(characterTag, armorId);
        if (asset != null)
            model = Context.Assets.Load<GameObject>(asset);

        if (model == null)
        {
            var template = Templates.Resolve<ArmorTemplate>(armorId, _armorCache, msg => Context.Log.Warn($"transmog: {msg}"));
            var models = template?.FemaleModels;
            if (models == null || models.Length == 0)
                models = template?.MaleModels;
            model = models != null && models.Length > 0 ? models[0] : null;
        }

        if (model != null)
            _bodyCache[armorId] = model;
        return model;
    }
}
