using System.Runtime.CompilerServices;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.Compilation;

// An unpinned aggregate Capsule route can remove a treasure candidate. The
// standalone chest baseline cannot decide that route's acquisition predicate.
internal static class SearchRuleRemainders
{
    private static readonly ConditionalWeakTable<CompiledSearch, Projection> Projections = new();

    internal sealed record Projection(CompiledSearch Compiled, string[] Causes);

    internal static Projection For(CompiledSearch compiled) =>
        compiled.Context.Authority.VersionResolution.SemanticProfileId == RngSemanticProfileId.Modern113
            ? Projections.GetValue(compiled, Build) : new(compiled, []);

    internal static SearchModel ForExecution(SearchModel model, Projection projection) => projection.Causes.Length == 0
        ? model
        : SearchPreparationCompiler.ForPlayer(model, projection.Compiled) with
        {
            SnapshotFingerprint = model.SnapshotFingerprint,
            ExactOptions = model.ExactOptions,
            PartyOpeningChoices = model.PartyOpeningChoices
        };

    private static Projection Build(CompiledSearch compiled)
    {
        var causes = new List<string>();
        SearchQuery query = compiled.NormalizedQuery;
        var projected = Strip(query, "");
        if (query.Players.Count != 0)
            projected = projected with { Players = query.Players.Select(p =>
                p with { Conditions = Strip(p.Conditions, $":slot={p.Slot}") }).ToArray() };
        if (causes.Count == 0) return new(compiled, []);
        var executable = compiled.Context.Party is not null ? SearchCompiler.Compile(projected, compiled.Context)
            : SearchCompiler.CompilePlayer(projected, compiled.Context);
        return new(executable, causes.Distinct(StringComparer.Ordinal).ToArray());

        SearchQuery Strip(SearchQuery q, string slot)
        {
            void Record(bool applies, string domain, string cause)
            {
                if (applies) causes.Add(domain + slot + ":0.113.0." + cause);
            }

            bool unresolvedTreasure = q.TreasureRelics is { IsEmpty: false } && q.OpeningRoute is null &&
                q.TransformationAggregate is { Opening: TransformationOpening.BonesLeafyOther, CompanionRelic: { } companion } &&
                (companion == BaseGameModelKeys.Relics.SmallCapsule || companion == BaseGameModelKeys.Relics.LargeCapsule);
            Record(unresolvedTreasure, "TreasureRelics", "TransformationDependentCapsuleAcquisitions");
            return unresolvedTreasure ? q with { TreasureRelics = null } : q;
        }
    }
}
