using WOMENACE.Code;

internal static class RateAudit
{
    public static int Run()
    {
        var catalogue = new ProcurementReward[]
        {
            new("part", ProcurementSection.Parts),
            new("material", ProcurementSection.Materials),
            new("equipment", ProcurementSection.Equipment),
            new("special-a", ProcurementSection.Special, 1),
            new("special-b", ProcurementSection.Special, 1),
            new("dossier-a", ProcurementSection.Dossiers, 1),
            new("dossier-b", ProcurementSection.Dossiers, 1),
            new("perlica", ProcurementSection.Curios, 1),
        };
        const int basePulls = 250000;
        var counts = new int[Procurement.Sections.Length];
        var state = new ProcurementState();
        for (var i = 0; i < basePulls; i++)
        {
            // Preserve the random stream while keeping every draw clear of pity and claims.
            state.Counters.Clear();
            state.Claims.Clear();
            var plan = Procurement.Plan(state, catalogue, 1, 783491);
            state.Apply(plan.State);
            counts[(int)plan.Rewards[0].Section]++;
        }
        Console.WriteLine($"Base-rate audit: {basePulls:N0} draws through Procurement.Plan");
        foreach (var section in Procurement.Sections)
        {
            var expected = Procurement.Weight(section) / (double)Procurement.TotalWeight;
            var observed = counts[(int)section] / (double)basePulls;
            var tolerance = 6 * Math.Sqrt(expected * (1 - expected) / basePulls);
            if (Math.Abs(observed - expected) > tolerance)
                throw new Exception($"{section}: observed frequency does not match its configured base chance");
            Console.WriteLine($"  {section}: {observed:P3}, configured {expected:P3}");
        }

        const int campaigns = 20000;
        var seeds = new Random(32607);
        var materialCounts = new long[11];
        var bothSpecials = 0;
        for (var campaign = 0; campaign < campaigns; campaign++)
        {
            state = new ProcurementState();
            var seed = seeds.Next();
            for (var batch = 0; batch < 5; batch++)
            {
                var plan = Procurement.Plan(state, catalogue, 10, seed);
                state.Apply(plan.State);
                var materials = plan.Rewards.Count(reward => reward.Section == ProcurementSection.Materials);
                materialCounts[materials]++;
                if (plan.Rewards.Count != 10)
                    throw new Exception("A ten-pull produced an incorrect reward count");
            }
            if (state.Claimed("special-a") == 1 && state.Claimed("special-b") == 1)
                bothSpecials++;
        }
        var average = materialCounts.Select((count, materials) => count * materials).Sum() / (campaigns * 5d);
        Console.WriteLine($"Fresh-campaign audit: {campaigns:N0} campaigns, 50 pulls each, normal pity and limits");
        Console.WriteLine($"  Core materials per ten-pull: {average:F3}");
        Console.WriteLine($"  Exactly two materials in a ten-pull: {materialCounts[2] / (campaigns * 5d):P2}");
        Console.WriteLine($"  Both special rewards within 50 pulls: {bothSpecials / (double)campaigns:P2}");
        Console.WriteLine($"  Material count histogram: {string.Join(", ", materialCounts.Select((count, i) => $"{i}={count}"))}");
        if (average is < .95 or > 1.05 || materialCounts[0] == 0 || materialCounts[1] == 0)
            throw new Exception("Material rewards have an unexpected distribution");
        if (bothSpecials / (double)campaigns is < .075 or > .105)
            throw new Exception("Special rewards have an unexpected distribution before pity is due");
        return Procurement.Sections.Length + 2;
    }
}
