using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

internal static class VariantOpeningPreparationChecks
{
    internal static readonly ModelKey A = new("ACT", "V_PREPARATION_A"), B = new("ACT", "V_PREPARATION_B"),
        C = new("ACT", "V_PREPARATION_C"), X = new("EVENT", "V_CUSTOM_X"), Y = new("EVENT", "V_CUSTOM_Y"),
        Z = new("EVENT", "V_CUSTOM_Z"), Shared = new("EVENT", "V_CUSTOM_SHARED"), Filler = new("EVENT", "V_CUSTOM_FILLER");

    internal static int Run()
    {
        int checks = 0;
        void Check(bool condition, string name)
        { checks++; if (!condition) throw new InvalidDataException("VariantOpeningPreparation: " + name); }
        var context = CreateContext();
        SearchQuery Events(params EventSequenceSearchCondition[] rows) => SearchQuery.Empty with { EventSequenceConstraints = rows };
        EventSequenceSearchCondition Event(ModelKeySetFilter keys, EventPoolSourceKind? source = null) =>
            new(1, source, SearchSequenceRangeMode.FirstN, 3, keys);
        VariantOpeningPreparation Prepare(SearchQuery query, SearchContext? ctx = null) =>
            VariantOpeningPreparation.Prepare(SearchCompiler.Compile(query, ctx ?? context));
        void Allowed(VariantOpeningPreparation prepared, params ModelKey[] expected) => Check(
            new[] { A, B, C }.Where(key => prepared.Allows(1, key)).ToHashSet().SetEquals(expected),
            "allowed set " + string.Join(',', expected.Select(key => key.Entry)));

        var any = Prepare(Events(Event(new([X, Y], [], []))));
        Allowed(any, A, B);
        Allowed(Prepare(Events(Event(new([], [X, Y], [])))), A);
        Allowed(Prepare(Events(Event(new([X, Z], [], [])))), A, C);
        Allowed(Prepare(Events(Event(new([X, Y], [], [])), Event(new([Y, Z], [], [])))), A, B);
        var negative = Prepare(Events(Event(new([], [], [X]))));
        Check(!negative.HasVariantRestrictions, "negative event membership does not exclude its containing variants");
        Check(any.ExplicitUnknownPrefixCount == 0, "event FirstN does not create unknown-room requirements");
        var shared = Prepare(Events(Event(new([Shared], [], []))));
        Check(!shared.HasVariantRestrictions, "shared event belongs to all captured variants");
        Allowed(Prepare(Events(Event(new([Shared], [], []), EventPoolSourceKind.ActLocal))));
        var explicitQuery = Events(Event(new([X, Y], [], []))) with {
            VariantBossBranches = [new(1, B, ModelKeySetFilter.Empty, ModelKeySetFilter.Empty, false),
                new(1, C, ModelKeySetFilter.Empty, ModelKeySetFilter.Empty, false)] };
        Allowed(Prepare(explicitQuery), B);
        var compiled = SearchCompiler.Compile(explicitQuery, context);
        string before = System.Text.Json.JsonSerializer.Serialize(compiled);
        _ = VariantOpeningPreparation.Prepare(compiled);
        Check(before == System.Text.Json.JsonSerializer.Serialize(compiled), "preparation preserves authored and normalized query and boss branch relations");
        Check(any.Acts is IList<VariantOpeningActAllowance> { IsReadOnly: true } &&
            any.Acts[0].Allowed is IList<ModelKey> { IsReadOnly: true }, "prepared collections are frozen");

        var world = context.Authority.WorldAuthority!;
        var generation = world.Beta109Generation!;
        SearchContext With(Beta109WorldGenerationSnapshot changed) => context with {
            Authority = context.Authority.WithWorldAuthority(world with { Beta109Generation = changed }) };
        var missingC = generation with { OrderedActCatalog = generation.OrderedActCatalog.Where(row => row.ActKey != C).ToArray() };
        Allowed(Prepare(Events(Event(new([X], [], []))), With(missingC)), A, C);
        var partialC = generation with { OrderedActCatalog = generation.OrderedActCatalog.Select(row => row.ActKey == C
            ? row with { RawEventCatalogOrderExact = false } : row).ToArray() };
        Allowed(Prepare(Events(Event(new([X], [], []))), With(partialC)), A, C);
        Check(!Prepare(Events(Event(new([X], [], []))), With(generation with { NoUnknownHooksOrModifiers = false }))
            .HasVariantRestrictions, "unknown hook sources preserve every variant");
        Check(!Prepare(Events(Event(new([X], [], []))), With(generation with { SharedEventCatalogExact = false }))
            .HasVariantRestrictions, "incomplete shared catalog preserves every variant");
        var partyFactory = typeof(CombinationRegressionChecks).GetMethod("PartyContext",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var party = (SearchContext)partyFactory.Invoke(null, [context, 3, null, null])!;
        var prefix = Prepare(SearchQuery.Empty with { FirstActUnknownEventPrefixCount = 1,
            Players = Enumerable.Range(0, 3).Select(slot => new PlayerOfferQuery(slot, ModelKeySetFilter.Empty) {
                Conditions = SearchQuery.Empty with { FirstActUnknownEventPrefixCount = slot + 1 } }).ToArray() }, party);
        Check(prefix.ExplicitUnknownPrefixCount == 3, "shared prefix preserves global/player maximum once");
        Check(!Prepare(SearchQuery.Empty).HasConditions, "no empty V stage");
        Console.WriteLine("VariantOpeningPreparation: " + checks + " passed");
        return 0;
    }

    internal static SearchContext CreateContext()
    {
        var captured = ProductionSearchReplay.Read<ProductionSearchReplayContext>("tools/ProductionSearchHost/Corpus/v1/context.json").Context;
        var world = captured.Authority.WorldAuthority!;
        var generation = world.Beta109Generation!;
        var template = generation.OrderedActCatalog.First(row => row.Act == 1);
        Beta109ActGenerationSnapshot Variant(ModelKey key, params ModelKey[] local) => template with {
            ActKey = key, OrderedEvents = local.Concat([Shared]).ToArray(), OrderedRawEvents = local,
            OrderedEligibleEvents = local.Concat([Shared]).ToArray(), RawEventCatalogOrderExact = true,
            EventEpochMembershipExact = true, EventEpochRevealFactsExact = true, EligibleEventOrderExact = true };
        generation = generation with {
            Profile = RuntimeProfileId.Beta111, ActSelectionAuthorityExact = true, Act1OverrideExact = true,
            Act1OverrideResolvedKey = null, DirectSourceAudited = true, WorldGenerationHooksExact = true,
            NoUnknownHooksOrModifiers = true, SharedEventCatalogExact = true,
            ActSelectionGroups = [new(1, [A, B, C], true)],
            OrderedActCatalog = [Variant(A, X, Y, Filler), Variant(B, Y, Filler), Variant(C, Z, Filler)],
            SharedEvents = [Shared], EventAuthority = new([Shared], true,
                Enumerable.Range(1, 3).Select(i => new Beta109EventEpochSnapshot("V_EPOCH_" + i, [], true, true, true)).ToArray(),
                true, "V.Preparation.SyntheticClosedCatalog") };
        return captured with { ProfileId = RuntimeProfileId.Beta111,
            Authority = captured.Authority.WithWorldAuthority(world with { Beta109Generation = generation }) };
    }
}
