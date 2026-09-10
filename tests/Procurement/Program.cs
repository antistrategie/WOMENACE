using System.Text.Json;
using WOMENACE.Code;

var checks = 0;
void Assert(bool value, string message)
{
    if (!value) throw new Exception(message);
    checks++;
}

Assert(SoundIds.FromName("hello") == 0x4F9F2CAB, "Sound identifiers must match the FNV-1a-32 hello vector");

var catalogue = new List<ProcurementReward>
{
    new("part", ProcurementSection.Parts),
    new("material", ProcurementSection.Materials),
    new("equipment", ProcurementSection.Equipment),
    new("special", ProcurementSection.Special, 1),
    new("dossier-a", ProcurementSection.Dossiers, 1),
    new("dossier-b", ProcurementSection.Dossiers, 1),
    new("outfit.wmgfl_kalina_perlica", ProcurementSection.Curios, 1),
};
var seedZero = Procurement.Plan(new ProcurementState(), catalogue, 1, 0);
var seedOne = Procurement.Plan(new ProcurementState(), catalogue, 1, 1);
Assert(seedZero.State.RandomState != seedOne.State.RandomState,
    "Adjacent campaign seeds must not collapse to the same random sequence");
var zeroMixedSeed = Procurement.Plan(new ProcurementState(), catalogue, 1, unchecked((int)0x9E3779B9u));
Assert(zeroMixedSeed.State.RandomState != 0, "A campaign seed that mixes to zero must still initialise a usable RNG");
var state = new ProcurementState();
foreach (var section in Procurement.Sections.Where(section => Procurement.Pity(section) > 0))
    state.Counters[section] = Procurement.Pity(section) - 1;
var before = JsonSerializer.Serialize(state);
var plan = Procurement.Plan(state, catalogue, 10, 123);
Assert(plan.Rewards.Count == 10, "A ten-pull must contain exactly ten rewards");
Assert(JsonSerializer.Serialize(state) == before, "Planning must not mutate the transaction's original state");
Assert(plan.Rewards.Take(3).Select(reward => reward.Section).SequenceEqual(new[]
{
    ProcurementSection.Dossiers, ProcurementSection.Special, ProcurementSection.Equipment,
}), "Simultaneous pity must use separate normal slots in rarity order");
Assert(!plan.State.Counters.ContainsKey(ProcurementSection.Parts), "Weapon parts must not gain a pity counter");
Assert(!plan.State.Counters.ContainsKey(ProcurementSection.Materials), "Core materials must not gain a pity counter");
Assert(!plan.State.Counters.ContainsKey(ProcurementSection.Curios), "Curios must not gain a pity counter");
Assert(Procurement.Sections.Where(section => Procurement.Pity(section) > 0)
    .All(section => plan.State.Counter(section) < Procurement.Pity(section)), "Awarded pity sections must leave their capped counters");
Assert(plan.Rewards.Where(reward => reward.Limit > 0).GroupBy(reward => reward.Id).All(group => group.Count() == 1), "A batch must not duplicate a limited reward");

var beforeNaturalHit = new ProcurementState();
beforeNaturalHit.Counters[ProcurementSection.Equipment] = 10;
var naturalHit = Procurement.Plan(beforeNaturalHit, catalogue, 1, 10240);
Assert(naturalHit.Rewards.Single().Section == ProcurementSection.Equipment, "The seeded equipment hit must occur before pity is due");
Assert(naturalHit.State.Counter(ProcurementSection.Equipment) == 0, "A natural hit must reset its section's pity counter");

var materialCounter = new ProcurementState();
materialCounter.Counters[ProcurementSection.Materials] = 20;
var withCounter = Procurement.Plan(materialCounter, catalogue, 10, 123);
var withoutCounter = Procurement.Plan(new ProcurementState(), catalogue, 10, 123);
Assert(withCounter.Rewards.Select(reward => reward.Id).SequenceEqual(withoutCounter.Rewards.Select(reward => reward.Id))
    && withCounter.State.RandomState == withoutCounter.State.RandomState, "A saved material counter must not affect rewards or the random sequence");

var singles = state.Copy();
var sequence = new List<string>();
for (var i = 0; i < 10; i++)
{
    var single = Procurement.Plan(singles, catalogue, 1, 123);
    Assert(single.Rewards.Count == 1, "A single pull must contain one reward");
    singles.Apply(single.State);
    sequence.Add(single.Rewards[0].Id);
}
Assert(sequence.SequenceEqual(plan.Rewards.Select(reward => reward.Id)), "Ten-pull convenience must preserve the sequence of ten single pulls");
Assert(JsonSerializer.Serialize(singles) == JsonSerializer.Serialize(plan.State), "Single pulls and batches must leave identical ledgers");

var saved = JsonSerializer.Serialize(plan.State);
using (var json = JsonDocument.Parse(saved))
    Assert(json.RootElement.GetProperty("Counters").TryGetProperty("Equipment", out _),
        "Saved counter keys must use stable section names");
var restored = JsonSerializer.Deserialize<ProcurementState>(saved)!;
var next = Procurement.Plan(plan.State, catalogue, 10, 123);
var reload = Procurement.Plan(restored, catalogue, 10, 123);
Assert(next.Rewards.Select(reward => reward.Id).SequenceEqual(reload.Rewards.Select(reward => reward.Id)), "Save reload must preserve the next random rewards");
Assert(JsonSerializer.Serialize(next.State) == JsonSerializer.Serialize(reload.State), "Save reload must preserve pity and claims");
var existingSequence = Procurement.Plan(restored, catalogue, 10, 456);
Assert(JsonSerializer.Serialize(existingSequence.State) == JsonSerializer.Serialize(next.State),
    "A stored RNG sequence must not be reinitialised from the campaign seed");

var exhausted = new ProcurementState();
foreach (var reward in catalogue.Where(reward => reward.Limit > 0)) exhausted.Claims[reward.Id] = reward.Limit;
exhausted.Counters[ProcurementSection.Dossiers] = 250;
exhausted.Counters[ProcurementSection.Special] = 95;
var empty = Procurement.Plan(exhausted, catalogue, 10, 42);
Assert(empty.Rewards.All(reward => reward.Limit == 0), "Spent claims must stay out of the pool without relying on inventory");
Assert(empty.State.Counter(ProcurementSection.Dossiers) == 250 && empty.State.Counter(ProcurementSection.Special) == 95, "Empty sections must pause their counters");
bool Eligible(ProcurementSection section) => catalogue.Any(reward => reward.Section == section && exhausted.Available(reward));
Assert(Procurement.EffectiveWeight(ProcurementSection.Parts, Eligible) * 100d / Procurement.TotalWeight == 86.5,
    "Exhausting all limited sections must increase the base part chance to 86.5 percent");
Assert(Procurement.EffectiveWeight(ProcurementSection.Special, Eligible) == 0
    && Procurement.EffectiveWeight(ProcurementSection.Dossiers, Eligible) == 0
    && Procurement.EffectiveWeight(ProcurementSection.Curios, Eligible) == 0,
    "Claimed sections must advertise and sample zero weight");
Assert(Procurement.Sections.Sum(section => Procurement.EffectiveWeight(section, Eligible)) == Procurement.TotalWeight,
    "Depleted pools must preserve the total sampling weight");
Assert(Procurement.EffectiveWeight(ProcurementSection.Parts, section => section == ProcurementSection.Parts) == Procurement.TotalWeight,
    "A parts-only catalogue must advertise and sample a 100 percent part chance");
var expanded = catalogue.Append(new ProcurementReward("dossier-c", ProcurementSection.Dossiers, 1)).ToList();
exhausted.Counters[ProcurementSection.Dossiers] = 299;
var added = Procurement.Plan(exhausted, expanded, 1, 42);
Assert(added.Rewards.Single().Id == "dossier-c", "A newly added dossier must become eligible without resetting previous claims");

var curiosity = Procurement.Plan(new ProcurementState(), catalogue, 10, 10976);
Assert(curiosity.Rewards.Count(reward => reward.Section == ProcurementSection.Curios) == 1,
    "A naturally drawn Curio must use exactly one slot and exhaust its claim within the batch");
var curiositySaved = JsonSerializer.Deserialize<ProcurementState>(JsonSerializer.Serialize(curiosity.State))!;
Assert(!curiositySaved.Available(catalogue.Single(reward => reward.Section == ProcurementSection.Curios)),
    "The Curio unlock must survive saving without an inventory item");

var special = catalogue.Single(reward => reward.Section == ProcurementSection.Special);
var equipmentState = new ProcurementState();
equipmentState.Counters[ProcurementSection.Special] = Procurement.Pity(ProcurementSection.Special) - 1;
var equipmentPull = Procurement.Plan(equipmentState, catalogue, 1, 42);
Assert(equipmentPull.Rewards.Single() == special, "A due special guarantee must award the available equipment");
equipmentPull.State.TrackEquipment(special, "procurement-railgun");
Assert(equipmentState.EquipmentItems.Count == 0 && equipmentState.Available(special),
    "Recording a pending award must not commit its item origin or claim");
equipmentState.Apply(equipmentPull.State);
var owned = new HashSet<string> { "procurement-railgun", "affinity-railgun" };
equipmentState.RefreshEquipmentClaims(owned.Contains);
Assert(equipmentState.Claimed(special.Id) == 1 && !equipmentState.Available(special),
    "Owning both copies must consume only the Procurement copy's limit");
owned.Remove("affinity-railgun");
equipmentState.RefreshEquipmentClaims(owned.Contains);
Assert(!equipmentState.Available(special), "Losing the affinity copy must not reopen Procurement");
owned.Add("affinity-railgun");
owned.Remove("procurement-railgun");
var equipmentCounter = equipmentState.Counter(ProcurementSection.Special);
var equipmentRandomState = equipmentState.RandomState;
equipmentState.RefreshEquipmentClaims(owned.Contains);
Assert(equipmentState.Available(special) && equipmentState.Claimed(special.Id) == 0,
    "Losing the Procurement copy must reopen its reward while the affinity copy remains owned");
Assert(equipmentState.Counter(ProcurementSection.Special) == equipmentCounter && equipmentState.RandomState == equipmentRandomState,
    "Returning lost equipment must not advance pity or the random stream");
equipmentState.Counters[ProcurementSection.Special] = Procurement.Pity(ProcurementSection.Special) - 1;
var replacement = Procurement.Plan(equipmentState, catalogue, 10, 42);
Assert(replacement.Rewards.Count(reward => reward == special) == 1,
    "A reopened reward must be drawable once within a ten-pull");
replacement.State.TrackEquipment(special, "replacement-railgun");
Assert(!equipmentState.EquipmentItems.ContainsKey("replacement-railgun") && equipmentState.Available(special),
    "A failed replacement exchange must leave the original ledger and eligibility intact");
equipmentState.Apply(replacement.State);
owned.Add("replacement-railgun");
var equipmentReload = JsonSerializer.Deserialize<ProcurementState>(JsonSerializer.Serialize(equipmentState))!;
equipmentReload.RefreshEquipmentClaims(owned.Contains);
Assert(equipmentReload.EquipmentItems["replacement-railgun"] == special.Id && !equipmentReload.Available(special),
    "Saving and loading must preserve the replacement copy's origin and limit");
owned.Add("procurement-railgun");
equipmentReload.RefreshEquipmentClaims(owned.Contains);
Assert(equipmentReload.Claimed(special.Id) == 2 && !equipmentReload.Available(special),
    "Buying back a previously sold Procurement copy must restore its claim alongside any replacement");
owned.Remove("replacement-railgun");
equipmentReload.RefreshEquipmentClaims(owned.Contains);
Assert(equipmentReload.Claimed(special.Id) == 1 && !equipmentReload.Available(special),
    "Losing a replacement must not reopen the pool while another Procurement copy is owned");
owned.Remove("procurement-railgun");
equipmentReload.RefreshEquipmentClaims(owned.Contains);
Assert(equipmentReload.Available(special), "Repeated Procurement losses must reopen the reward");

var vehicle = new ProcurementReward("vehicle", ProcurementSection.Special, 1);
var separateEquipment = equipmentState.Copy();
separateEquipment.TrackEquipment(vehicle, "procurement-vehicle");
separateEquipment.Claims[vehicle.Id] = 1;
separateEquipment.RefreshEquipmentClaims(guid => guid == "procurement-vehicle");
Assert(separateEquipment.Available(special) && !separateEquipment.Available(vehicle),
    "Losing a weapon must not reset a different equipment reward's limit");
separateEquipment.RefreshEquipmentClaims(guid => guid != "procurement-railgun");
var beforeUnreadableInventory = JsonSerializer.Serialize(separateEquipment);
var unreadableInventoryRejected = false;
try
{
    separateEquipment.RefreshEquipmentClaims(guid => guid == "procurement-vehicle"
        ? throw new InvalidOperationException("Inventory unavailable") : false);
}
catch (InvalidOperationException) { unreadableInventoryRejected = true; }
Assert(unreadableInventoryRejected && JsonSerializer.Serialize(separateEquipment) == beforeUnreadableInventory,
    "A failed inventory read must leave every equipment claim intact");

var permanentClaims = exhausted.Copy();
foreach (var reward in catalogue.Where(reward => !reward.ReturnsOnLoss))
    permanentClaims.TrackEquipment(reward, "non-equipment");
permanentClaims.RefreshEquipmentClaims(_ => false);
Assert(permanentClaims.EquipmentItems.Count == 0 && catalogue.Where(reward => reward.Limit > 0)
    .All(reward => !permanentClaims.Available(reward)),
    "Permanent unlocks and claims without a tracked item must remain spent");

var partsOnly = Procurement.Plan(new ProcurementState(), new[] { catalogue[0] }, 10, 42);
Assert(partsOnly.Rewards.All(reward => reward.Id == "part"), "All empty section weights must return to parts");
Assert(Procurement.Sections.Sum(Procurement.Weight) == 10000, "Base probabilities must total 100 percent");
Assert(Procurement.PiecePrice * Procurement.PiecesPerPull == 25, "A pull must cost the equivalent of 25 Sardis");
var rejected = false;
try { Procurement.Plan(state, catalogue, 2, 123); } catch (ArgumentOutOfRangeException) { rejected = true; }
Assert(rejected, "Unsupported batch sizes must be rejected");

if (args.Contains("--rates"))
    checks += RateAudit.Run();
Console.WriteLine($"Procurement: {checks} checks passed");
