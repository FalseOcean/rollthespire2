using System.Text.Json;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Semantics;

internal static class PartyInitialQuery
{
    internal const string OpeningPremiseId = "PartyOpening.UnselectedNeutralC.UnspecifiedCapsuleNoObtainEffects.v3";
    internal static IReadOnlyDictionary<ModelKey, IReadOnlySet<ModelKey>> CapsuleEffectPremise(SearchQuery query)
    {
        ModelKey[] sources = [BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule];
        return sources.ToDictionary(source => source, source => (IReadOnlySet<ModelKey>)query.StructuredOpeningEffects
            .Where(c => c.Scope == NeowStructuredEffectScope.NestedRelics &&
                (c.SourceRelicKey == source || c.SourceRelicKey == BaseGameModelKeys.Relics.NeowsBones))
            .SelectMany(c => c.OutputKeys)
            .Concat(query.LegacyNeow.CapsuleContainedRelics.Any)
            .Concat(query.LegacyNeow.CapsuleContainedRelics.All)
            .Concat(query.LegacyNeow.RequireWhetstone ? new[] { BaseGameModelKeys.OrdinaryRelics.Whetstone } : [])
            .Concat(query.LegacyNeow.RequireWarPaint ? new[] { BaseGameModelKeys.OrdinaryRelics.WarPaint } : [])
            .ToHashSet());
    }

    internal static bool HasWorld(SearchQuery q) => q.VariantBossBranches.Count > 0 || q.LegacyWorld.BossFilters.Count > 0 || q.LegacyWorld.BossOrdinalFilters.Count > 0;
    internal static CompiledSearch Compile(SearchQuery query, SearchContext context)
    {
        var authored = query;
        var party = context.Party ?? throw new ArgumentException("Party.AuthorityMissing");
        if (query.Players.Count != party.Players.Count || context.Authority.PlayerSlotIndex != 0 ||
            context.Authority.PlayersCount != party.Players.Count || context.CharacterKey != party.Players[0].Character.CharacterKey ||
            context.Authority.UnlockSnapshotFingerprint != party.Players[0].UnlockSnapshotFingerprint ||
            context.Authority.CatalogFingerprint != party.Players[0].CatalogFingerprint ||
            context.Authority.WorldSnapshotFingerprint != party.World.SnapshotFingerprint ||
            context.Ascension != party.Players[0].Ascension || context.Detection.NormalizedVersion != party.Players[0].GameVersion)
            throw new ArgumentException("Party.ContextMismatch");
        if (query.TransformationAggregate is not null || query.Players.Any(p => p.Conditions.TransformationAggregate is not null))
            throw new ArgumentException("Party.TransformationAggregateNotInScope");
        if (query.Players.Where((p, index) => p.Slot != index || p.Conditions.Players.Count > 0).Any())
            throw new ArgumentException("Party.InvalidOrderedSlotPredicate");
        query = BindSharedConditions(query);
        string empty = JsonSerializer.Serialize(SearchQuery.Empty);
        var personalAtSharedLevel = query with { Players = [], VariantBossBranches = [], StandardMaps = [], EventSequenceConstraints = [],
            LegacyWorld = query.LegacyWorld with { BossFilters = [], BossOrdinalFilters = [], AncientIdentityFilters = [] } };
        if (JsonSerializer.Serialize(personalAtSharedLevel) != empty)
            throw new ArgumentException("Party.PersonalConditionsRequirePlayerSlot");
        bool familyProjection = query.Players.Any(p => JsonSerializer.Serialize(p.Conditions) != empty) ||
            JsonSerializer.Serialize(query with { Players = [], VariantBossBranches = [],
                LegacyWorld = query.LegacyWorld with { BossFilters = [], BossOrdinalFilters = [] } }) != empty;
        if (familyProjection && query.Players.Any(p => p.SelectedOption is not null))
            throw new ArgumentException("Party.LegacySelectedOptionMustBeMigratedToPlayerConditions");
        var personal = query.Players.Select(p => SearchCompiler.CompilePlayer(p.Conditions,
            context with { Party = null, Authority = party.Players[p.Slot], CharacterKey = party.Players[p.Slot].Character.CharacterKey,
                EvaluationAssumptions = new(p.AncientPremises) })).ToArray();
        var shared = SearchCompiler.CompilePlayer(query with { Players = [] }, context with { Party = null });
        for (int slot = 0; slot < query.Players.Count; slot++)
        {
            var p = query.Players[slot];
            if (p.Slot != slot || p.Offers.Any.Concat(p.Offers.All).Concat(p.Offers.Ban).Any(k => !k.IsValid || k.Category != BaseGameModelKeys.Categories.Relic))
                throw new ArgumentException("Party.InvalidOrderedSlotPredicate");
            PartyNeowQuery.Validate(p);
        }
        if (query.VariantBossBranches.Any(b => !b.IsValid)) throw new ArgumentException("Party.InvalidWorldPredicate");
        if (query.LegacyWorld.BossFilters.Any(b => b.Act is < 1 or > 3) ||
            query.LegacyWorld.BossOrdinalFilters.Any(b => b.Act is < 1 or > 3 || b.Ordinal is < 1 or > 2))
            throw new ArgumentException("Party.InvalidBossObservationOrdinal");
        static ModelKeySetFilter Canonical(ModelKeySetFilter keys) => new(
            keys.Any.Distinct().OrderBy(k => k.Serialized).ToArray(),
            keys.All.Distinct().OrderBy(k => k.Serialized).ToArray(),
            keys.Ban.Distinct().OrderBy(k => k.Serialized).ToArray());
        var normalized = query with
        {
            Players = query.Players.Select(p => p with { Offers = Canonical(p.Offers),
                SelectedOption = p.SelectedOption is { } plan ? plan with { Choices = plan.Choices.OrderBy(c => c.GroupId, StringComparer.Ordinal).ToArray() } : null,
                Results = p.Results.DistinctBy(c => JsonSerializer.Serialize(c)).OrderBy(c => JsonSerializer.Serialize(c), StringComparer.Ordinal).ToArray() }).ToArray(),
            VariantBossBranches = query.VariantBossBranches.Select(b => b with { FirstBoss = Canonical(b.FirstBoss), SecondBoss = Canonical(b.SecondBoss) })
                .DistinctBy(b => JsonSerializer.Serialize(b)).OrderBy(b => b.Act).ThenBy(b => JsonSerializer.Serialize(b), StringComparer.Ordinal).ToArray(),
            LegacyWorld = query.LegacyWorld with
            {
                BossFilters = query.LegacyWorld.BossFilters.Where(b => !b.IsEmpty).Select(b => b with { Keys = Canonical(b.Keys) }).DistinctBy(b => JsonSerializer.Serialize(b)).OrderBy(b => JsonSerializer.Serialize(b)).ToArray(),
                BossOrdinalFilters = query.LegacyWorld.BossOrdinalFilters.Where(b => !b.IsEmpty).Select(b => b with { Keys = Canonical(b.Keys) }).DistinctBy(b => JsonSerializer.Serialize(b)).OrderBy(b => JsonSerializer.Serialize(b)).ToArray()
            }
        };
        if (normalized.VariantBossBranches.Any(b => !b.IncludesSecondBoss && !b.SecondBoss.IsEmpty))
            throw new ArgumentException("Party.SecondBossPredicateWithoutOrdinal");
        var acts = normalized.VariantBossBranches.Select(b => b.Act).Concat(normalized.LegacyWorld.BossFilters.Select(b => b.Act))
            .Concat(normalized.LegacyWorld.BossOrdinalFilters.Select(b => b.Act)).Distinct();
        bool impossible = acts.Any(number => !party.World.Beta109Generation!.OrderedActCatalog.Where(a => a.Act == number).Any(act =>
        {
            var branches = normalized.VariantBossBranches.Where(b => b.Act == number).ToArray();
            var variant = branches.Where(b => b.VariantKey == act.ActKey).ToArray();
            if (branches.Length > 0 && variant.Length == 0) return false;
            IEnumerable<ModelKey> first = party.World.Beta109Generation.PartyBossDiscoveryOverrides.TryGetValue(act.ActKey.Serialized, out var replacement)
                ? new[] { replacement } : act.Bosses;
            return first.Any(boss =>
            {
                IEnumerable<ModelKey?> seconds = number == 3 && context.Ascension >= 10
                    ? act.Bosses.Where(k => k != boss).Select(k => (ModelKey?)k) : new ModelKey?[] { null };
                return seconds.Any(second =>
                {
                    ModelKey[] all = second is { } key ? [boss, key] : [boss];
                    return variant.All(b => Matches(b.FirstBoss, [boss]) && (!b.IncludesSecondBoss || second is { } s && Matches(b.SecondBoss, [s]))) &&
                        normalized.LegacyWorld.BossFilters.Where(b => b.Act == number).All(b => Matches(b.Keys, all)) &&
                        normalized.LegacyWorld.BossOrdinalFilters.Where(b => b.Act == number).All(b =>
                            b.Ordinal == 1 ? Matches(b.Keys, [boss]) : second is { } s && Matches(b.Keys, [s]));
                });
            });
        }));
        impossible |= personal.Any(p => p.Status == QueryNormalizationStatus.Impossible) || shared.Status == QueryNormalizationStatus.Impossible;
        return new CompiledSearch(authored, context, new(normalized, impossible ? QueryNormalizationStatus.Impossible : QueryNormalizationStatus.Legal, [], [], [], []),
            new(new(ResolvedCombatRewardRouteKind.NotApplicable, null, false, OrderedPartyAuthority.ObservationVersion)), false,
            OrderedPartyAuthority.Hash(party.Fingerprint + "|" + OpeningPremiseId + "|" + (PartyNeowQuery.HasTransactions(normalized) ? Core.Effects.PartyNeowAdmission.ObservationVersion : "") + "|" + JsonSerializer.Serialize(normalized)))
            { PlayerSearches = personal.Append(shared).ToArray(), UsesPartyFamilyProjection = familyProjection };
    }

    // Older per-seat editors may still submit shared facts. Bind them once here
    // without weakening their conjunction; UI migration can follow separately.
    internal static SearchQuery BindSharedConditions(SearchQuery query)
    {
        var sources = new[] { query }.Concat(query.Players.Select(p => p.Conditions)).ToArray();
        static T[] Unique<T>(IEnumerable<T> values) => values.DistinctBy(v => JsonSerializer.Serialize(v)).ToArray();
        var variants = new List<VariantScopedBossBranch>();
        foreach (int act in sources.SelectMany(q => q.VariantBossBranches).Select(b => b.Act).Distinct())
        {
            var groups = sources.Select(q => q.VariantBossBranches.Where(b => b.Act == act).ToArray()).Where(g => g.Length > 0).ToArray();
            var allowed = groups[0].Select(b => b.VariantKey).ToHashSet();
            foreach (var group in groups.Skip(1)) allowed.IntersectWith(group.Select(b => b.VariantKey));
            if (allowed.Count == 0) throw new ArgumentException("Party.ConflictingSharedVariants:Act" + act);
            variants.AddRange(groups.SelectMany(g => g).Where(b => allowed.Contains(b.VariantKey)));
        }
        var identities = sources.SelectMany(q => q.LegacyWorld.AncientIdentityFilters).ToList();
        // Options remain attached to their player and their exact Ancient branch.
        foreach (var p in query.Players)
            foreach (var group in p.Conditions.AncientBranches.GroupBy(b => b.Act))
                identities.Add(new(group.Key, new(group.Select(b => b.AncientKey).Distinct().ToArray(), [], [])));
        return query with
        {
            VariantBossBranches = Unique(variants),
            StandardMaps = Unique(sources.SelectMany(q => q.StandardMaps)),
            EventSequenceConstraints = Unique(sources.SelectMany(q => q.EventSequenceConstraints)),
            LegacyWorld = query.LegacyWorld with
            {
                BossFilters = Unique(sources.SelectMany(q => q.LegacyWorld.BossFilters)),
                BossOrdinalFilters = Unique(sources.SelectMany(q => q.LegacyWorld.BossOrdinalFilters)),
                AncientIdentityFilters = Unique(identities)
            },
            Players = query.Players.Select(p => p with { Conditions = p.Conditions with
            {
                VariantBossBranches = [], StandardMaps = [], EventSequenceConstraints = [],
                AncientBranches = p.Conditions.AncientBranches.GroupBy(b => b.Act)
                    .Where(g => g.Any(b => b.OptionAny.Count > 0 || b.SeaGlassTargetAny.Count > 0)).SelectMany(g => g).ToArray(),
                LegacyWorld = p.Conditions.LegacyWorld with { BossFilters = [], BossOrdinalFilters = [], AncientIdentityFilters = [] }
            } }).ToArray()
        };
    }
    internal static bool Matches(ModelKeySetFilter condition, IEnumerable<ModelKey> values)
    {
        var set = values.ToHashSet();
        return (condition.Any.Count == 0 || condition.Any.Any(set.Contains)) && condition.All.All(set.Contains) && !condition.Ban.Any(set.Contains);
    }
    internal static bool MatchesWorld(SearchQuery query, PartySeedInformation observation) =>
        query.LegacyWorld.BossFilters.All(c => Matches(c.Keys, observation.Bosses.Where(b => b.Act == c.Act).Select(b => b.BossKey))) &&
        query.LegacyWorld.BossOrdinalFilters.All(c => observation.Bosses.Any(b => b.Act == c.Act && b.Ordinal == c.Ordinal) &&
            Matches(c.Keys, observation.Bosses.Where(b => b.Act == c.Act && b.Ordinal == c.Ordinal).Select(b => b.BossKey))) &&
        query.VariantBossBranches.GroupBy(b => b.Act).All(act =>
        observation.Acts.Count >= act.Key && act.GroupBy(b => b.VariantKey).Any(variant => observation.Acts[act.Key - 1] == variant.Key &&
            variant.All(b => Matches(b.FirstBoss, observation.Bosses.Where(x => x.Act == b.Act && x.Ordinal == 1).Select(x => x.BossKey)) &&
                (!b.IncludesSecondBoss || observation.Bosses.Any(x => x.Act == b.Act && x.Ordinal == 2) &&
                    Matches(b.SecondBoss, observation.Bosses.Where(x => x.Act == b.Act && x.Ordinal == 2).Select(x => x.BossKey))))));
    internal static bool MatchesOffers(SearchQuery query, PartySeedInformation observation) => query.Players.All(p =>
        Matches(p.Offers, observation.Players.Single(o => o.Slot == p.Slot).Offers));
}
