using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.FamilyExecution;

// Shared by the managed check and the existing ProductionSearchHost probe.
// The unchanged solo replay constructs and shuffles the complete offer array.
internal static class NeowEntryIdentityFixtures
{
    internal sealed record Fixture(string Name, NeowReplayPlan Replay, bool Track, bool Positive = true,
        bool AdditionalReject = false);
    internal static readonly string[] Modes = ["off", "reject", "track", "all"];
    internal const string Variable = "RT2_N_ENTRY_EXPERIMENT";

    internal static IReadOnlyList<Fixture> Build(NeowReplayPlan source)
    {
        if (source.Authority.PlayersCount != 1) throw new InvalidOperationException("NEntry.RequiresSoloFixture");
        var neutral = source with {
            Authority = source.Authority with { AllCharacterCardPoolsUnlocked = true, ScrollBoxesAllowed = true },
            EnabledDomains = Beta110FastDomain.None, StructuredConditions = [], RequiredFinalCurseIds = [], BannedFinalCurseIds = [],
            TopAny = 0, TopAll = 0, TopBan = 0, BonesAny = 0, BonesAll = 0, BonesBan = 0,
            Selected = 255, First = 255, Second = 255, Bones = false, RequireBones = false,
            AuthoredUpgrades = null, DirectNestedVanilla111 = false, ExactOnly = []
        };
        var result = new List<Fixture>();
        static ulong Bit(byte id) => Beta110FastRelicCatalog.Bit(id);
        // Every fixed position and every outcome of all three authentic flips.
        foreach (byte id in NeowIdentityOfferFacts.For(1, true, true).AdmittedFixedCandidates.ToArray().Concat(new byte[] {
            Beta110FastRelicCatalog.LavaRock, Beta110FastRelicCatalog.SmallCapsule,
            Beta110FastRelicCatalog.NutritiousOyster, Beta110FastRelicCatalog.StoneHumidifier,
            Beta110FastRelicCatalog.NeowsTalisman, Beta110FastRelicCatalog.Pomander }))
            result.Add(new("Single." + id, neutral with { Selected = id }, true));

        var pearl = neutral with { Selected = Beta110FastRelicCatalog.GoldenPearl };
        result.Add(new("AnySingle", neutral with { TopAny = Bit(Beta110FastRelicCatalog.ArcaneScroll) }, true));
        result.Add(new("AllSingleWithCurseBan", neutral with { TopAll = Bit(Beta110FastRelicCatalog.NewLeaf),
            TopBan = Bit(Beta110FastRelicCatalog.NeowsBones) }, true));
        result.Add(new("AnyUnionFallback", neutral with { TopAny = Bit(Beta110FastRelicCatalog.GoldenPearl) |
            Bit(Beta110FastRelicCatalog.ArcaneScroll) }, false));
        result.Add(new("AllPairFallback", neutral with { TopAll = Bit(Beta110FastRelicCatalog.GoldenPearl) |
            Bit(Beta110FastRelicCatalog.ArcaneScroll) }, false));
        result.Add(new("PositiveBanFallback", pearl with { TopBan = Bit(Beta110FastRelicCatalog.LavaRock) }, false));
        result.Add(new("MixedAnySatisfiedByCurse", neutral with { TopAny = Bit(Beta110FastRelicCatalog.CursedPearl) |
            Bit(Beta110FastRelicCatalog.GoldenPearl) }, true));

        // Each real exclusion is isolated so every root must reject, instead of
        // relying on a rare curse appearing in a short population by chance.
        foreach (var pair in new (byte Curse, byte Target)[] {
            (Beta110FastRelicCatalog.CursedPearl, Beta110FastRelicCatalog.GoldenPearl),
            (Beta110FastRelicCatalog.HeftyTablet, Beta110FastRelicCatalog.ArcaneScroll),
            (Beta110FastRelicCatalog.LeafyPoultice, Beta110FastRelicCatalog.NewLeaf),
            (Beta110FastRelicCatalog.PrecariousShears, Beta110FastRelicCatalog.PreciseScissors),
            (Beta110FastRelicCatalog.NeowsSacrifice, Beta110FastRelicCatalog.LostCoffer),
            (Beta110FastRelicCatalog.NeowsSacrifice, Beta110FastRelicCatalog.PhialHolster),
            (Beta110FastRelicCatalog.LargeCapsule, Beta110FastRelicCatalog.SmallCapsule) })
            result.Add(new($"Excluded.{pair.Curse}.{pair.Target}", neutral with {
                Selected = pair.Target, Authority = neutral.Authority with { EligibleCurseRelicIds = [pair.Curse] }
            }, true, Positive: false, AdditionalReject: true));
        result.Add(new("LargeCapsuleSkipsFirstFlip", neutral with { Selected = Beta110FastRelicCatalog.NutritiousOyster,
            Authority = neutral.Authority with { EligibleCurseRelicIds = [Beta110FastRelicCatalog.LargeCapsule] } }, true));
        result.Add(new("LockedKaleidoscope", neutral with { Selected = Beta110FastRelicCatalog.Kaleidoscope,
            Authority = neutral.Authority with { AllCharacterCardPoolsUnlocked = false } }, true, false, true));
        result.Add(new("ScrollBoxesDisallowed", neutral with { Selected = Beta110FastRelicCatalog.ScrollBoxes,
            Authority = neutral.Authority with { ScrollBoxesAllowed = false } }, true, false, true));
        result.Add(new("ChangedPoolPositions", pearl with { Authority = neutral.Authority with {
            AllCharacterCardPoolsUnlocked = false, ScrollBoxesAllowed = false } }, true));
        result.Add(new("UnavailableAnyUnion", neutral with { TopAny = Bit(Beta110FastRelicCatalog.Kaleidoscope) |
            Bit(Beta110FastRelicCatalog.ScrollBoxes), Authority = neutral.Authority with {
                AllCharacterCardPoolsUnlocked = false, ScrollBoxesAllowed = false } }, false, false, true));
        result.Add(new("UnavailableAnyStillHasCurse", neutral with { TopAny = Bit(Beta110FastRelicCatalog.Kaleidoscope) |
            Bit(Beta110FastRelicCatalog.CursedPearl), Authority = neutral.Authority with {
                AllCharacterCardPoolsUnlocked = false, EligibleCurseRelicIds = [Beta110FastRelicCatalog.CursedPearl] } }, true));

        var catalog = neutral.Authority.EffectCatalog;
        if (catalog.CharacterRewardPool.Rare.Length != 0)
            result.Add(new("ArcaneRouteProjection", neutral with { Selected = Beta110FastRelicCatalog.ArcaneScroll,
                EnabledDomains = Beta110FastDomain.ArcaneScrollOffer,
                StructuredConditions = [new(Beta110FastRelicCatalog.ArcaneScroll,
                    Beta110FastStructuredConditionKind.ArcaneScrollGeneratedCard,
                    catalog.CharacterRewardPool.Rare[0], ushort.MaxValue, ushort.MaxValue, 1, false)] }, true));
        if (catalog.GeneratedCurseIds.Length != 0)
            result.Add(new("CapsuleAuthoredUpgradeFinalCurse", neutral with { Selected = Beta110FastRelicCatalog.SmallCapsule,
                EnabledDomains = Beta110FastDomain.FinalCurse, RequiredFinalCurseIds = [catalog.GeneratedCurseIds[0]],
                AuthoredUpgrades = new(Enumerable.Repeat(3, 66).ToArray()) }, true));

        // Real captured route projections exercise post-identity continuation.
        // The caller's request may add one more representative projection below.
        if (source.StructuredConditions.Length != 0 || source.HasFinalCurseFastProjection)
        {
            ulong positiveUnion = source.TopAny | source.TopAll |
                (source.Selected == 255 ? 0 : Bit(source.Selected));
            ulong positiveMask = 0;
            foreach (byte id in NeowIdentityOfferFacts.For(1, true, true).AdmittedFixedCandidates.ToArray().Concat(new byte[] {
                Beta110FastRelicCatalog.LavaRock, Beta110FastRelicCatalog.SmallCapsule,
                Beta110FastRelicCatalog.NutritiousOyster, Beta110FastRelicCatalog.StoneHumidifier,
                Beta110FastRelicCatalog.NeowsTalisman, Beta110FastRelicCatalog.Pomander })) positiveMask |= Bit(id);
            bool track = System.Numerics.BitOperations.PopCount(positiveUnion & positiveMask) == 1 &&
                (source.TopBan & positiveMask) == 0;
            result.Add(new("AuthoredRouteProjection", source, track));
        }
        return result;
    }

    internal static NeowFamilyGpuPlan Compile(NeowReplayPlan replay, string? mode)
    {
        string? previous = Environment.GetEnvironmentVariable(Variable);
        string? direct = Environment.GetEnvironmentVariable("RT2_N_DIRECT_EXPERIMENT");
        try
        {
            Environment.SetEnvironmentVariable(Variable, mode);
            Environment.SetEnvironmentVariable("RT2_N_DIRECT_EXPERIMENT", "baseline");
            return NeowFamilyGpuPlan.TryCreate(replay, out var plan, out var issue) && plan is not null
                ? plan : throw new InvalidDataException("NEntry.GpuFixtureAdmission:" + issue);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, previous);
            Environment.SetEnvironmentVariable("RT2_N_DIRECT_EXPERIMENT", direct);
        }
    }
}
