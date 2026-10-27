using System.Globalization;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Tests.FastSearch;

internal enum ShopOrderedSemanticsOutcome : byte
{
    Pass = 0,
    Fail = 1,
    Unknown = 2
}

internal sealed record ShopOrderedSemanticsRow(
    string CaseId,
    string QueryShape,
    string ActualSequence,
    bool ExpectedMatch,
    bool RelicMatch,
    bool ColorlessMatch,
    double? RelicProbability,
    double? ColorlessProbability,
    ShopOrderedSemanticsOutcome Outcome,
    string FailureReason);

/// <summary>
/// Developer-only canonical behavior probes for Shop sequence order semantics.
/// This does not start Search or GPU work and has no capability, health, policy,
/// fallback, or packaging authority.
/// </summary>
internal static class ShopOrderedChecks
{
    internal static int Run()
    {
        var rows = BuildCases().Select(RunCase).ToArray();
        foreach (var row in rows)
            if (row.Outcome != ShopOrderedSemanticsOutcome.Pass)
                throw new InvalidOperationException(row.CaseId + ": " + row.FailureReason);
        CheckPresentationAttribution();
        Console.WriteLine($"ShopOrdered cases={rows.Length};relic+colorless+probability=PASS");
        return 0;
    }

    private static void CheckPresentationAttribution()
    {
        const string corpus = "tools/ProductionSearchHost/Corpus/";
        var context = ProductionSearchReplay.Read<ProductionSearchReplayContext>(corpus + "v1/context.json").Context;
        SearchQuery Query(string path) => ProductionSearchReplay.Read<ProductionSearchReplayWorkload>(corpus + path).Query;
        var shop = Query("family-pricing-matrix-v1/R-shop.json");
        var colorless = Query("family-pricing-matrix-v1/S-rare.json");
        var bags = Query("v1/cases/R-common.json");
        SearchProbabilityQuickView View(SearchQuery q) => SearchProbabilityPresentationBuilder.Build(SearchCompiler.Compile(q, context));
        double Row(SearchProbabilityQuickView view, SearchProbabilityRowKind kind) => view.Rows.Single(r => r.Kind == kind).Probability
            ?? throw new InvalidOperationException("Unpriced fixture row: " + kind);
        void Equal(double? actual, double expected, string issue)
        {
            if (actual is null || Math.Abs(actual.Value - expected) > Math.Abs(expected) * 1e-12)
                throw new InvalidOperationException("Shop presentation: " + issue);
        }
        var shopView = View(shop);
        if (shopView.Rows.Any(r => r.Kind == SearchProbabilityRowKind.Shop))
            throw new InvalidOperationException("Shop relics must not create an S row");
        Equal(shopView.TotalProbability, Row(shopView, SearchProbabilityRowKind.Relic), "shop-only belongs wholly to R");
        var colorlessView = View(colorless);
        var mixed = View(shop with { MerchantColorlessConditions = colorless.MerchantColorlessConditions,
            MerchantColorlessSequenceConditions = colorless.MerchantColorlessSequenceConditions });
        Equal(Row(mixed, SearchProbabilityRowKind.Relic), Row(shopView, SearchProbabilityRowKind.Relic), "S does not alter R attribution");
        Equal(Row(mixed, SearchProbabilityRowKind.Shop), Row(colorlessView, SearchProbabilityRowKind.Shop), "S includes only colorless cards");
        Equal(mixed.TotalProbability, Row(mixed, SearchProbabilityRowKind.Relic) * Row(mixed, SearchProbabilityRowKind.Shop), "mixed probability counted once");
        var allRelics = View(shop with { RelicSequenceConstraints = bags.RelicSequenceConstraints });
        Equal(Row(allRelics, SearchProbabilityRowKind.Relic), Row(shopView, SearchProbabilityRowKind.Relic) * Row(View(bags), SearchProbabilityRowKind.Relic), "bag and shop share one R row");
        Equal(allRelics.TotalProbability, Row(allRelics, SearchProbabilityRowKind.Relic), "R regrouping preserves full probability");
        Console.WriteLine("SHOP_PRESENTATION_PASS shopOnly=true;colorlessOnly=true;mixed=true;bagAndShop=true;search=false");
    }

    private sealed record CaseDefinition(
        string Id,
        string Shape,
        string[] Actual,
        string?[] Slots,
        CombatRewardSequenceOrderMode OrderMode,
        bool ExpectedMatch);

    private static IReadOnlyList<CaseDefinition> BuildCases() => new[]
    {
        new CaseDefinition("O1", "[A, null, null] Ordered -> #1=A", new[] { "A", "X", "B" }, new string?[] { "A", null, null }, CombatRewardSequenceOrderMode.Ordered, true),
        new CaseDefinition("O2", "[null, A, null] Ordered -> #2=A", new[] { "X", "A", "B" }, new string?[] { null, "A", null }, CombatRewardSequenceOrderMode.Ordered, true),
        new CaseDefinition("O3", "[A, null, B] Ordered -> #1=A,#3=B", new[] { "A", "X", "B" }, new string?[] { "A", null, "B" }, CombatRewardSequenceOrderMode.Ordered, true),
        new CaseDefinition("U1", "[A, null, B] Unordered -> A+B in first3", new[] { "X", "A", "B" }, new string?[] { "A", null, "B" }, CombatRewardSequenceOrderMode.Unordered, true),
        new CaseDefinition("U2", "[null, A, null] Unordered -> A anywhere first3", new[] { "X", "B", "A" }, new string?[] { null, "A", null }, CombatRewardSequenceOrderMode.Unordered, true),
        new CaseDefinition("OX", "counterexample [A, null, B] Ordered", new[] { "X", "A", "B" }, new string?[] { "A", null, "B" }, CombatRewardSequenceOrderMode.Ordered, false)
    };

    private static ShopOrderedSemanticsRow RunCase(CaseDefinition definition)
    {
        ModelKey[] relicPool = Keys(BaseGameModelKeys.Categories.Relic, "A", "B", "X", "Y", "Z");
        ModelKey[] cardPool = Keys(BaseGameModelKeys.Categories.Card, "A", "B", "X", "Y", "Z");
        ModelKey[] relicActual = definition.Actual.Select(entry => Key(BaseGameModelKeys.Categories.Relic, entry)).ToArray();
        ModelKey[] cardActual = definition.Actual.Select(entry => Key(BaseGameModelKeys.Categories.Card, entry)).ToArray();
        ModelKey?[] relicSlots = definition.Slots.Select(entry => entry is null ? (ModelKey?)null : Key(BaseGameModelKeys.Categories.Relic, entry)).ToArray();
        ModelKey?[] cardSlots = definition.Slots.Select(entry => entry is null ? (ModelKey?)null : Key(BaseGameModelKeys.Categories.Card, entry)).ToArray();

        bool relicMatch = ShopSequenceSemantics.Matches(relicActual, definition.Slots.Length, definition.OrderMode, relicSlots);
        bool colorlessMatch = ShopSequenceSemantics.Matches(cardActual, definition.Slots.Length, definition.OrderMode, cardSlots);
        var relicCondition = new RelicShopSequenceSearchCondition(definition.Slots.Length, definition.OrderMode, relicSlots);
        var colorlessCondition = new MerchantColorlessSequenceSearchCondition(definition.Slots.Length, definition.OrderMode, MerchantColorlessSlot.Uncommon, cardSlots);
        bool relicPriced = ShopSequenceProbabilityEstimator.TrySolveRelic(relicPool, relicCondition, out double relicProbability, out string relicIssue);
        bool colorlessPriced = ShopSequenceProbabilityEstimator.TrySolveColorless(cardPool, colorlessCondition, out double colorlessProbability, out string colorlessIssue);

        var failures = new List<string>();
        if (relicMatch != definition.ExpectedMatch) failures.Add("RelicMatchMismatch");
        if (colorlessMatch != definition.ExpectedMatch) failures.Add("ColorlessMatchMismatch");
        if (!relicPriced) failures.Add("RelicProbability:" + relicIssue);
        if (!colorlessPriced) failures.Add("ColorlessProbability:" + colorlessIssue);

        ShopOrderedSemanticsOutcome outcome = failures.Count != 0
            ? relicPriced && colorlessPriced
                ? ShopOrderedSemanticsOutcome.Fail
                : ShopOrderedSemanticsOutcome.Unknown
            : ShopOrderedSemanticsOutcome.Pass;

        return new ShopOrderedSemanticsRow(
            definition.Id,
            definition.Shape,
            string.Join(",", definition.Actual),
            definition.ExpectedMatch,
            relicMatch,
            colorlessMatch,
            relicPriced ? relicProbability : null,
            colorlessPriced ? colorlessProbability : null,
            outcome,
            string.Join(" | ", failures));
    }

    private static ModelKey Key(string category, string entry) => new(category, "SHOP_DIAG_" + entry);

    private static ModelKey[] Keys(string category, params string[] entries) =>
        entries.Select(entry => Key(category, entry)).ToArray();
}
