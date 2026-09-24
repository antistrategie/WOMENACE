using WOMENACE.Code;

var tests = new (string Name, Action Run)[]
{
    ("successful loan is removed once", SuccessfulLoan),
    ("pre-existing skill is preserved", ExistingSkill),
    ("pre-existing non-Skill entry is preserved", ExistingUnfindableSkill),
    ("successful add with null lookup is removed", NullLookup),
    ("add throwing after insertion is removed", PartialGrant),
    ("lookup throwing after insertion is removed", ThrowingLookup),
    ("rejected add is harmless", RejectedGrant),
    ("spawn exception preserves original and cleans loan", () => BodyException("spawn")),
    ("native damage exception preserves original and cleans loan", () => BodyException("native damage")),
    ("report exception preserves original and cleans loan", () => BodyException("report inspection")),
    ("successful weight restoration precedes loan removal", RestoreWeights),
    ("failed restorations and removal warn without masking original", FailedCleanup),
    ("native removal exception warns and preserves original", ThrowingRemoval),
    ("weight forcing exception restores partial writes", FailedWeightForcing),
    ("removal attempt mismatch warns", RemovalMismatch),
    ("already removed skill needs no further removal", AlreadyRemoved),
    ("throwing warning sink does not interrupt cleanup", ThrowingWarningSink),
};
foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}
Console.WriteLine($"{tests.Length} probe cleanup tests passed");

static void Check(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
}

static void SuccessfulLoan()
{
    var loan = new Loan();
    var cleanup = new ProbeCleanup(loan.Warnings.Add);
    using (cleanup)
        Check(ReferenceEquals(loan.Borrow(cleanup), loan.Skill), "find must return granted instance");
    cleanup.Dispose();
    Check(loan.Count == 0 && loan.Removals == 1 && loan.Warnings.Count == 0, "remove exactly once");
}

static void ExistingSkill()
{
    var loan = new Loan { Count = 1 };
    using (var cleanup = new ProbeCleanup(loan.Warnings.Add))
        Check(ReferenceEquals(loan.Borrow(cleanup), loan.Skill), "return original instance");
    Check(loan.Count == 1 && loan.Adds == 0 && loan.Removals == 0, "original skill must not be touched");
}

static void ExistingUnfindableSkill()
{
    var loan = new Loan { Count = 1, NullLookup = true };
    using (var cleanup = new ProbeCleanup(loan.Warnings.Add))
        Check(loan.Borrow(cleanup) == null, "a non-Skill match has no usable instance");
    Check(loan.Count == 1 && loan.Adds == 0 && loan.Removals == 0, "preserve all existing template entries");
}

static void NullLookup()
{
    var loan = new Loan { NullLookup = true };
    using (var cleanup = new ProbeCleanup(loan.Warnings.Add))
        Check(loan.Borrow(cleanup) == null && loan.Count == 1, "add succeeded before null lookup");
    Check(loan.Count == 0 && loan.Removals == 1, "null lookup must still revoke");
}

static void PartialGrant()
{
    var original = new Exception("native Add after insertion");
    var loan = new Loan { AddError = original };
    try
    {
        using var cleanup = new ProbeCleanup(loan.Warnings.Add);
        loan.Borrow(cleanup);
        throw new Exception("expected Add exception");
    }
    catch (Exception error) { Check(ReferenceEquals(error, original), "keep Add exception"); }
    Check(loan.Count == 0 && loan.Removals == 1, "partial add must revoke");
}

static void ThrowingLookup()
{
    var original = new Exception("lookup after insertion");
    var loan = new Loan { FindError = original };
    try
    {
        using var cleanup = new ProbeCleanup(loan.Warnings.Add);
        loan.Borrow(cleanup);
        throw new Exception("expected lookup exception");
    }
    catch (Exception error) { Check(ReferenceEquals(error, original), "keep lookup exception"); }
    Check(loan.Count == 0 && loan.Removals == 1, "failed lookup must revoke");
}

static void RejectedGrant()
{
    var loan = new Loan { RejectAdd = true };
    using (var cleanup = new ProbeCleanup(loan.Warnings.Add))
        Check(loan.Borrow(cleanup) == null, "rejected add has no instance");
    Check(loan.Count == 0 && loan.Warnings.Count == 0, "no false removal failure on rejected add");
}

static void BodyException(string stage)
{
    var original = new Exception(stage);
    var loan = new Loan();
    try
    {
        using var cleanup = new ProbeCleanup(loan.Warnings.Add);
        loan.Borrow(cleanup);
        throw original;
    }
    catch (Exception error) { Check(ReferenceEquals(error, original), $"keep {stage} exception"); }
    Check(loan.Count == 0 && loan.Removals == 1, $"cleanup after {stage}");
}

static void RestoreWeights()
{
    var loan = new Loan();
    var weights = new[] { 12, 34, 56 };
    using (var cleanup = new ProbeCleanup(loan.Warnings.Add))
    {
        loan.Borrow(cleanup);
        using (var restore = new ProbeCleanup(loan.Warnings.Add))
        {
            foreach (var index in Enumerable.Range(0, weights.Length))
            {
                var saved = weights[index];
                restore.Defer($"defect {index} Chance", () => weights[index] = saved);
            }
            Array.Fill(weights, 0);
            weights[1] = 100;
        }
        Check(weights.SequenceEqual(new[] { 12, 34, 56 }), "restore every weight before reporting");
        Check(loan.Count == 1, "loan must span report inspection");
    }
    Check(loan.Count == 0 && loan.Warnings.Count == 0, "restore then revoke");
}

static void FailedCleanup()
{
    var original = new Exception("native damage");
    var loan = new Loan { RefuseRemoval = true };
    var restored = new List<int>();
    try
    {
        using var cleanup = new ProbeCleanup(loan.Warnings.Add);
        cleanup.Defer("remaining cleanup", () => restored.Add(9));
        loan.Borrow(cleanup);
        using var weights = new ProbeCleanup(loan.Warnings.Add);
        weights.Defer("defect first Chance", () => restored.Add(1));
        weights.Defer("defect broken Chance", () => throw new Exception("weight write failed"));
        weights.Defer("defect last Chance", () => restored.Add(3));
        throw original;
    }
    catch (Exception error) { Check(ReferenceEquals(error, original), "cleanup must not replace native exception"); }
    Check(restored.SequenceEqual(new[] { 3, 1, 9 }), "all other restorations must run");
    Check(loan.Removals == 1, "bounded removal attempted despite weight failure");
    Check(loan.Warnings.Count == 2, "warn for weight and removal failures");
    Check(loan.Warnings[0].Contains("defect broken Chance") && loan.Warnings[0].Contains("weight write failed"),
        "weight failure must identify defect");
    Check(loan.Warnings[1].Contains(Loan.Description) && loan.Warnings[1].Contains("still live 1"),
        "removal failure must identify actor and skill");
}

static void RemovalMismatch()
{
    var warnings = new List<string>();
    var count = 1;
    using (var cleanup = new ProbeCleanup(warnings.Add))
        cleanup.Defer(Loan.Description, () => ProbeCleanup.RemoveBorrowed(() => count, () => { count = 0; return 0; }));
    Check(warnings.Count == 1 && warnings[0].Contains("attempted 0"), "surface unexpected removal result");
}

static void ThrowingRemoval()
{
    var original = new Exception("native damage");
    var warnings = new List<string>();
    var restored = false;
    try
    {
        using var cleanup = new ProbeCleanup(warnings.Add);
        cleanup.Defer("remaining cleanup", () => restored = true);
        cleanup.Defer(Loan.Description, () => ProbeCleanup.RemoveBorrowed(() => 1,
            () => throw new Exception("native removal failed")));
        throw original;
    }
    catch (Exception error) { Check(ReferenceEquals(error, original), "keep body exception on throwing removal"); }
    Check(restored && warnings.Count == 1 && warnings[0].Contains(Loan.Description)
        && warnings[0].Contains("native removal failed"), "warn and continue after throwing removal");
}

static void FailedWeightForcing()
{
    var original = new Exception("weight forcing failed");
    var loan = new Loan();
    var weights = new[] { 12, 34, 56 };
    try
    {
        using var cleanup = new ProbeCleanup(loan.Warnings.Add);
        loan.Borrow(cleanup);
        using var restore = new ProbeCleanup(loan.Warnings.Add);
        foreach (var index in Enumerable.Range(0, weights.Length))
        {
            var saved = weights[index];
            restore.Defer($"defect {index} Chance", () => weights[index] = saved);
        }
        weights[0] = 100;
        throw original;
    }
    catch (Exception error) { Check(ReferenceEquals(error, original), "keep weight setter exception"); }
    Check(weights.SequenceEqual(new[] { 12, 34, 56 }) && loan.Count == 0,
        "partial forcing must restore all weights and revoke loan");
}

static void AlreadyRemoved()
{
    var loan = new Loan();
    using (var cleanup = new ProbeCleanup(loan.Warnings.Add))
    {
        loan.Borrow(cleanup);
        loan.Count = 0;
    }
    Check(loan.Warnings.Count == 0, "a skill removed by native damage is not a cleanup failure");
}

static void ThrowingWarningSink()
{
    var restored = false;
    using var output = new StringWriter();
    var previous = Console.Error;
    Console.SetError(output);
    try
    {
        using var cleanup = new ProbeCleanup(_ => throw new Exception("logger unavailable"));
        cleanup.Defer("remaining cleanup", () => restored = true);
        cleanup.Defer("broken cleanup", () => throw new Exception("restore unavailable"));
    }
    finally { Console.SetError(previous); }
    Check(restored && output.ToString().Contains("restore unavailable"), "fallback diagnostic and remaining cleanup");
}

sealed class Loan
{
    internal const string Description = "actor 0x1234, skill 'active.test'";
    internal readonly object Skill = new();
    internal readonly List<string> Warnings = new();
    internal int Count;
    internal int Adds;
    internal int Removals;
    internal bool NullLookup;
    internal bool RejectAdd;
    internal bool RefuseRemoval;
    internal Exception AddError;
    internal Exception FindError;

    internal object Borrow(ProbeCleanup cleanup) => cleanup.Borrow(Description,
        () => Count > 0,
        () => FindError != null ? throw FindError : NullLookup ? null : Skill,
        () =>
        {
            Adds++;
            if (RejectAdd)
                return false;
            Count++;
            if (AddError != null)
                throw AddError;
            return true;
        },
        () => ProbeCleanup.RemoveBorrowed(() => Count, () =>
        {
            Removals++;
            var attempted = Count;
            if (!RefuseRemoval)
                Count = 0;
            return attempted;
        }));
}
