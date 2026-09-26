using HarmonyLib;
using Il2CppMenace.Tactical;
using Il2CppMenace.Tactical.Skills;
using Il2CppMenace.Tactical.Skills.Effects;
using WOMENACE.Code;

var passed = 0;
var failed = 0;
void Test(string name, Action<Fixture> test)
{
    try
    {
        using var fixture = new Fixture();
        test(fixture);
        passed++;
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

Test("direct melee deathrattle protects killer", f =>
    f.Hit(f.Wreck, f.Killer, f.Melee, () => f.Expect(f.Detonate(), f.Killer, true)));

Test("native critical has null attacker and retains melee through Source", f =>
{
    f.Critical.Source = new Actor { Pointer = f.Killer.Pointer };
    f.Hit(f.Wreck, f.Killer, f.Melee, () =>
        f.Hit(f.Wreck, null, f.Critical, () => f.Expect(f.Detonate(), f.Killer, true)));
});

Test("nested defect without Source clears melee even with matching attacker", f =>
{
    f.Critical.Source = null;
    f.Hit(f.Wreck, f.Killer, f.Melee, () =>
        f.Hit(f.Wreck, f.Killer, f.Critical, () => f.Expect(f.Detonate(), f.Killer, false)));
});

Test("nested defect from different Source clears melee", f =>
{
    f.Critical.Source = f.Other;
    f.Hit(f.Wreck, f.Killer, f.Melee, () =>
        f.Hit(f.Wreck, null, f.Critical, () => f.Expect(f.Detonate(), f.Killer, false)));
});

Test("ranged then nested critical has no melee protection", f =>
    f.Hit(f.Wreck, f.Killer, f.Ranged, () =>
        f.Hit(f.Wreck, null, f.Critical, () => f.Expect(f.Detonate(), f.Killer, false))));

Test("later ranged killing blow clears melee", f =>
{
    f.Hit(f.Wreck, f.Killer, f.Melee);
    f.Hit(f.Wreck, f.Killer, f.Ranged, () => f.Expect(f.Detonate(), f.Killer, false));
});

Test("standalone delayed damage effect clears melee", f =>
{
    f.Hit(f.Wreck, f.Killer, f.Melee);
    f.Hit(f.Wreck, null, f.Critical, () => f.Expect(f.Detonate(), f.Killer, false));
});

Test("nested different attacker clears melee", f =>
    f.Hit(f.Wreck, f.Killer, f.Melee, () =>
        f.Hit(f.Wreck, f.Other, f.Critical, () =>
        {
            var blast = f.Detonate();
            f.Expect(blast, f.Killer, false);
            f.Expect(blast, f.Other, false);
        })));

Test("depth belongs to victim rather than global damage stack", f =>
{
    f.Hit(f.Other, f.Killer, f.Melee);
    f.Hit(f.Wreck, f.Killer, f.Melee, () =>
        f.Hit(f.Other, null, f.Critical, () => f.Expect(f.Detonate(f.Other), f.Killer, false)));
});

Test("nested non-damage status effect clears melee", f =>
{
    var status = Fixture.Skill("status");
    status.Template.Type = SkillType.StatusEffect;
    f.Hit(f.Wreck, f.Killer, f.Melee, () =>
        f.Hit(f.Wreck, f.Killer, status, () => f.Expect(f.Detonate(), f.Killer, false)));
});

Test("blast and descendant shrapnel spare only killer hp and armour", f =>
    f.Hit(f.Wreck, f.Killer, f.Melee, () =>
    {
        var blast = f.Detonate();
        var shrapnel = Fixture.Skill("shrapnel", Fixture.Skill("delegate", blast));
        f.Expect(blast, f.Other, false);
        f.Expect(blast, f.Killer, true);
        f.Expect(shrapnel, f.Other, false);
        f.Expect(shrapnel, f.Killer, true);
    }));

Test("same-template unrelated explosion is not protected", f =>
    f.Hit(f.Wreck, f.Killer, f.Melee, () =>
    {
        var blast = f.Detonate();
        var unrelated = new Skill { Template = blast.Template, SourceSkill = Fixture.Skill("other-origin") };
        f.Use(unrelated);
        f.Expect(unrelated, f.Killer, false);
        f.Expect(Fixture.Skill("shrapnel", unrelated), f.Killer, false);
        f.Expect(blast, f.Killer, true);
    }));

Test("repeated impacts retain instance waiver", f =>
{
    Skill blast = null;
    f.Hit(f.Wreck, f.Killer, f.Melee, () => blast = f.Detonate());
    for (var i = 0; i < 3; i++)
    {
        f.Expect(blast, f.Killer, true);
        f.Hit(f.Other, f.Killer, f.Ranged);
        f.Use(blast);
    }
});

Test("non-attack deathrattle is not protected", f =>
{
    f.Payload.IsAttack = false;
    f.Hit(f.Wreck, f.Killer, f.Melee, () => f.Expect(f.Detonate(), f.Killer, false));
});

Test("scene reset clears ledger and blast origins and waivers", f =>
{
    Skill blast = null;
    f.Hit(f.Wreck, f.Killer, f.Melee, () => blast = f.Detonate());
    f.Reset();
    f.Expect(blast, f.Killer, false);
    var lateLaunch = new Skill { Template = blast.Template, SourceSkill = blast.SourceSkill };
    f.Use(lateLaunch);
    f.Expect(lateLaunch, f.Killer, false);
    f.Expect(f.Detonate(), f.Killer, false);
});

Test("scene reset clears active depth", f =>
    f.Hit(f.Wreck, f.Killer, f.Melee, () =>
    {
        f.Reset();
        f.Hit(f.Wreck, f.Killer, f.Melee);
        f.Hit(f.Wreck, null, f.Critical, () => f.Expect(f.Detonate(), f.Killer, false));
    }));

Test("multiple nested critical frames unwind to zero", f =>
{
    f.Hit(f.Wreck, f.Killer, f.Melee, () =>
        f.Hit(f.Wreck, null, f.Critical, () =>
            f.Hit(f.Wreck, null, f.Critical)));
    f.Hit(f.Wreck, null, f.Critical, () => f.Expect(f.Detonate(), f.Killer, false));
});

Test("native exception skips postfix and cannot leak damage depth", f =>
{
    var failure = new InvalidOperationException("native damage failed");
    var postfixRan = false;
    Fixture.ExpectException(failure, () => f.Hit(f.Wreck, f.Killer, f.Melee,
        () => throw failure, postfix: () => postfixRan = true));
    if (postfixRan)
        throw new Exception("An ordinary postfix ran after the original threw");
    f.Expect(f.Detonate(), f.Killer, false);
    // A fresh melee record must not hide an orphaned depth from the failed call.
    f.Hit(f.Wreck, f.Killer, f.Melee);
    f.Hit(f.Wreck, null, f.Critical, () => f.Expect(f.Detonate(), f.Killer, false));
});

Test("a later throwing prefix cannot leak damage depth", f =>
{
    var failure = new InvalidOperationException("another prefix failed");
    Fixture.ExpectException(failure, () => f.Hit(f.Wreck, f.Killer, f.Melee,
        afterPrefix: () => throw failure));
    f.Hit(f.Wreck, f.Killer, f.Melee);
    f.Hit(f.Wreck, null, f.Critical, () => f.Expect(f.Detonate(), f.Killer, false));
});

Test("a throwing postfix cannot leak damage depth", f =>
{
    var failure = new InvalidOperationException("another postfix failed");
    Fixture.ExpectException(failure, () => f.Hit(f.Wreck, f.Killer, f.Melee,
        postfix: () => throw failure));
    f.Hit(f.Wreck, f.Killer, f.Melee);
    f.Hit(f.Wreck, null, f.Critical, () => f.Expect(f.Detonate(), f.Killer, false));
});

Test("a prefix that never ran does not unwind the outer damage call", f =>
    f.Hit(f.Wreck, f.Killer, f.Melee, () =>
    {
        var failure = new InvalidOperationException("earlier prefix failed");
        Fixture.ExpectException(failure, () => f.Hit(f.Wreck, null, f.Critical,
            beforePrefix: () => throw failure));
        f.Hit(f.Wreck, null, f.Critical, () => f.Expect(f.Detonate(), f.Killer, true));
    }));

Test("skipping the original still unwinds its damage scope", f =>
{
    f.Hit(f.Wreck, f.Killer, f.Melee, () => throw new Exception("Skipped original ran"), skipOriginal: true);
    f.Hit(f.Wreck, null, f.Critical, () => f.Expect(f.Detonate(), f.Killer, false));
});

Test("nested exceptions unwind every entered damage scope", f =>
{
    var failure = new InvalidOperationException("nested native damage failed");
    Fixture.ExpectException(failure, () => f.Hit(f.Wreck, f.Killer, f.Melee, () =>
        f.Hit(f.Wreck, null, f.Critical, () => throw failure)));
    f.Hit(f.Wreck, f.Killer, f.Melee);
    f.Hit(f.Wreck, null, f.Critical, () => f.Expect(f.Detonate(), f.Killer, false));
});

Test("unload removes the owned damage patch and singleton", f =>
{
    f.System.OnUnload();
    if (Harmony.HasDamagePatch || MeleeCookOffSystem.Instance != null)
        throw new Exception("Damage hook survived unload");
});

#if JIANGYU_DEV
Test("native critical diagnostics count damage and inherited attribution", f =>
{
    f.Hit(f.Wreck, f.Killer, f.Melee, () =>
        f.Hit(f.Wreck, null, f.Critical, () => f.Expect(f.Detonate(), f.Killer, true)));
    var diagnostics = f.System.Diagnostics;
    if (diagnostics.DamageEffectHits != 1 || diagnostics.InheritedDefectHits != 1 || diagnostics.DamageZeroed != 1
        || !diagnostics.LastDamageEffectHit.Contains("depth=1")
        || !diagnostics.LastDamageEffectHit.Contains("attacker= source="))
        throw new Exception("Native critical diagnostic counters or provenance are incorrect");
});

Test("ranged and delayed defects do not increment inherited-defect diagnostics", f =>
{
    f.Hit(f.Wreck, f.Killer, f.Ranged, () => f.Hit(f.Wreck, null, f.Critical));
    f.Hit(f.Wreck, f.Killer, f.Melee);
    f.Hit(f.Wreck, null, f.Critical);
    var diagnostics = f.System.Diagnostics;
    if (diagnostics.DamageEffectHits != 2 || diagnostics.InheritedDefectHits != 0)
        throw new Exception("Independent defect damage inherited melee diagnostics");
});
#endif

Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

sealed class Fixture : IDisposable
{
    public readonly MeleeCookOffSystem System = new();
    public readonly Actor Wreck = new(), Killer = new(), Other = new();
    public readonly Skill Melee = Skill("melee"), Ranged = Skill("ranged"), Critical = Skill("critical");
    public readonly SkillTemplate Payload = new() { Id = "deathrattle-explosion" };
    private readonly Skill _origin = Skill("deathrattle");

    public Fixture()
    {
        Melee.Template.Tags.Add(new Tag { name = "wmgfl_melee" });
        Critical.Template.Type = SkillType.StatusEffect | SkillType.DamageEffect;
        Critical.Source = Killer;
        System.OnInit();
    }

    public static Skill Skill(string id, Skill source = null) =>
        new() { Template = new SkillTemplate { Id = id }, SourceSkill = source };

    public DamageInfo Hit(Actor victim, Actor attacker, Skill skill, Action nativeBody = null,
        Action beforePrefix = null, Action afterPrefix = null, Action postfix = null, bool skipOriginal = false)
    {
        var damage = new DamageInfo();
        Harmony.Receive(victim, attacker, skill, damage, nativeBody, beforePrefix, afterPrefix, postfix, skipOriginal);
        return damage;
    }

    public static void ExpectException(Exception expected, Action body)
    {
        try { body(); }
        catch (Exception actual) when (ReferenceEquals(actual, expected)) { return; }
        throw new Exception("The original exception was swallowed or replaced");
    }

    public void Use(Skill blast) => System.Context.Patches.Invoke(blast, "Use", 2, false, Wreck, null);

    public Skill Detonate(Actor wreck = null)
    {
        var handler = new DeathrattleHandler
        {
            Actor = wreck ?? Wreck,
            ParentSkill = _origin,
            m_Template = new DeathrattleTemplate { Skill = Payload },
        };
        System.Context.Patches.Invoke(handler, "TriggerSkill", -1, false);
        var blast = new Skill { Template = Payload, SourceSkill = _origin };
        Use(blast);
        return blast;
    }

    public void Expect(Skill blast, Actor target, bool protectedTarget)
    {
        var damage = Hit(target, Wreck, blast);
        var hp = protectedTarget ? 0 : 80;
        var armour = protectedTarget ? 0 : 30;
        if (damage.Damage != hp || damage.ArmorDamage != armour || damage.Suppression != 17 || !damage.IsAoE)
            throw new Exception($"Expected hp={hp}, armour={armour}, unchanged effects, got hp={damage.Damage}, armour={damage.ArmorDamage}");
    }

    public void Reset() => System.OnSceneLoaded(1, "Tactical");
    public void Dispose() => System.OnUnload();
}
