using Il2CppInterop.Runtime.InteropTypes;
using Il2CppMenace.States;
using Il2CppMenace.Strategy;
using Il2CppMenace.UI.Strategy;
using Jiangyu.Sdk;
using UnityEngine.UIElements;

namespace WOMENACE.Code;

// A Doll's dummy links are empty chassis of her own body that she drives, so they go by her body
// model and a serial, not by a soldier's name. Vanilla squaddies are lasting people drawn from one
// pool and moved between leaders, so the name follows the leader a squaddie joins: a Doll's squad
// gives "<body model> #<serial>", and a vanilla leader's gives back the name the squaddie came
// with, which is kept in the save until then. A squaddie back in the pool keeps the name of the
// squad it left. The serial is the squaddie's id, which no other squaddie shares and which never
// changes, so a chassis keeps its number when it moves to another Doll and only the model changes.
//
// The name is written into the squaddie's record, so every screen that shows it needs nothing
// further. Joining a squad is TryAddSquaddie, whoever calls it. Squads read from a save never go
// through it, so the hired roster is brought in line once a save has read it in. The scene a save
// opens loads before its roster, and the save reads its squaddies before its roster, so that is
// after Roster.ProcessSaveState. Roster has no subclass, so its override is safe to detour.
//
// Which chassis fill a Doll's squad means nothing, so the unit window's button that opens the
// squaddie picker does nothing for her: the squad's size is still set beside it. The button and
// what is drawn in it stop taking the pointer, so no press reaches vanilla's handler, and take it
// again for a vanilla leader.
public sealed class DummyLinkNameSystem : JiangyuSystem
{
    private DummyLinkNameState State => Context.State.Get<DummyLinkNameState>();

    // Marks the elements of the picker's button this system stopped taking the pointer. A class
    // goes with its element when the window is rebuilt, where a remembered pointer would not.
    private const string DeafenedClass = "wmgfl-deafened";

    public override void OnInit()
    {
        // Non-virtual on the base, where it lives.
        Context.Patches.Postfix("Il2CppMenace.Strategy.BaseUnitLeader", "TryAddSquaddie", OnTryAddSquaddie);
        Context.Patches.Postfix("Il2CppMenace.Strategy.Roster", "ProcessSaveState", OnRosterRead);
        // Runs whenever the window shows a leader's squaddie row.
        Context.Patches.Postfix("Il2CppMenace.UI.Strategy.UnitWindowEquipment", "UpdateSquaddiesElements", OnUpdateSquaddiesElements);
    }

    private void OnUpdateSquaddiesElements(PatchInfo info)
    {
        try
        {
            var equipment = (info.Instance as Il2CppObjectBase)?.TryCast<UnitWindowEquipment>();
            var button = equipment?.m_SquaddiesEditButton;
            if (button == null)
                return;
            Deafen(button, BodyModels.For(Affinity.CharacterTag(equipment.m_Leader)) != null);
        }
        catch (Exception ex) { Context.Log.Warn($"dummy link names: picker button failed: {ex.Message}"); }
    }

    private void Deafen(VisualElement element, bool deaf)
    {
        if (deaf && element.pickingMode == PickingMode.Position)
        {
            element.pickingMode = PickingMode.Ignore;
            element.AddToClassList(DeafenedClass);
        }
        else if (!deaf && element.ClassListContains(DeafenedClass))
        {
            element.RemoveFromClassList(DeafenedClass);
            element.pickingMode = PickingMode.Position;
        }
        for (var i = 0; i < element.childCount; i++)
            Deafen(element.ElementAt(i), deaf);
    }

    private void OnRosterRead(PatchInfo info)
    {
        try
        {
            if (info.Args is not { Count: > 0 } || (info.Args[0] as Il2CppObjectBase)?.TryCast<SaveState>()?.IsLoading() != true)
                return;
            var squaddies = StrategyState.Get()?.Squaddies;
            var roster = (info.Instance as Il2CppObjectBase)?.TryCast<Roster>();
            var leaders = roster?.m_HiredLeaders;
            if (squaddies == null || leaders == null)
                return;
            foreach (var leader in leaders)
            {
                var ids = leader?.m_SquaddieIds;
                if (ids == null)
                    continue;
                foreach (var id in ids)
                    Name(squaddies.GetById(id), leader);
            }
        }
        catch (Exception ex) { Context.Log.Warn($"dummy link names: naming the loaded roster failed: {ex}"); }
    }

    private void OnTryAddSquaddie(PatchInfo info)
    {
        try
        {
            if (info.Result is not true || info.Args is not { Count: > 0 } || info.Args[0] is not int id)
                return;
            var leader = (info.Instance as Il2CppObjectBase)?.TryCast<BaseUnitLeader>();
            Name(StrategyState.Get()?.Squaddies?.GetById(id), leader);
        }
        catch (Exception ex) { Context.Log.Warn($"dummy link names: naming failed: {ex.Message}"); }
    }

    private void Name(Squaddie squaddie, BaseUnitLeader leader)
    {
        if (squaddie == null || leader == null)
            return;
        var id = squaddie.GetId();
        var model = BodyModels.For(Affinity.CharacterTag(leader));
        var originals = State.Originals;
        if (model == null)
        {
            if (!originals.Remove(id, out var original))
                return;
            squaddie.Name = original.Name;
            squaddie.Nickname = original.Nickname;
            return;
        }
        var serial = $"#{id:D4}";
        // A record already carrying its serial was named before, under this model or another, so
        // what it holds is not a name to give back.
        if (!originals.ContainsKey(id) && squaddie.Nickname?.EndsWith(serial, StringComparison.Ordinal) != true)
            originals[id] = new DummyLinkNameState.Original { Name = squaddie.Name, Nickname = squaddie.Nickname };
        // The callsign is the whole name too: a unit's tooltip lists its squad by callsign alone.
        var name = $"{model} {serial}";
        squaddie.Name = name;
        squaddie.Nickname = name;
    }
}

public sealed class DummyLinkNameState
{
    // The name and callsign each squaddie had before a Doll's squad took it, by squaddie id.
    public Dictionary<int, Original> Originals { get; set; } = [];

    public sealed class Original
    {
        public string Name { get; set; }
        public string Nickname { get; set; }
    }
}
