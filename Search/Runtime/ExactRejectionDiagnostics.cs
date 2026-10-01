using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Runtime;

internal static class ExactRejectionDiagnostics
{
    // Called for only six automatic samples per session, or an explicit manual check.
    internal static void Log(ExactSearchExecutionRequest plan, string seed, ProductionExactSearchResult result)
    {
        try
        {
            object[] Groups(IReadOnlyList<PredictedEffectGroup> groups) => groups.Take(12).Select(g => (object)new
            {
                g.GroupId, g.SourceRelicKey, g.SelectionPolicy, g.Scope,
                totalItems = g.OrderedItems.Count, items = g.OrderedItems.Take(16).ToArray()
            }).ToArray();
            var observed = result.Document?.Sections.SelectMany(s => s.NeowChoices).Take(3).Select(c => new
            {
                c.SlotIndex, c.RelicKey, c.IdentityPrecision, c.EffectPrecision,
                groups = Groups(c.EffectGroups), bonesRelics = c.BonesOutcome?.OfferedRelics,
                routes = c.BonesOutcome?.OriginalRoutes.Take(8).Select(r => new
                {
                    r.RouteId, r.AcquisitionOrder, r.Precision,
                    r.SharedContinuation.FinalCurseKey,
                    relics = r.RelicScopedResults.Take(2).Select(s => new
                    { s.SourceRelicKey, s.Precision, groups = Groups(s.EffectGroups) }).ToArray()
                }).ToArray()
            }).ToArray();
            string json = RuntimeLog.SafeJson(new
            {
                seed, result.Evaluation.Disposition, result.Evaluation.FailureCode,
                result.Evaluation.RouteRejections,
                plan.SnapshotFingerprint, result.Authority.CatalogFingerprint,
                result.Authority.UnlockSnapshotFingerprint, result.Authority.EffectSnapshotFingerprint,
                result.Authority.WorldSnapshotFingerprint,
                requested = plan.CompiledSearch.NormalizedQuery,
                observedNeow = observed,
                observedRewards = result.Document?.Sections.Where(s => s.NormalCombatRewardSequencePrediction is not null)
                    .Select(s => s.NormalCombatRewardSequencePrediction!).Select(s => new
                    {
                        s.Status, s.IssueCode, s.AuthorityFingerprint,
                        routes = s.Routes.Take(8).Select(r => new
                        {
                            r.RouteGroupId, r.Status, r.Precision, r.IssueCode,
                            openings = r.EquivalentRoutes.Take(8).Select(o => new { o.RouteId, o.AcquisitionOrder }).ToArray(),
                            battles = r.Battles.Take(6).Select(b => new
                            {
                                b.BattleOrdinal, b.Precision, b.ReasonCode, b.Potion,
                                cards = b.Cards.Take(16).Select(c => new { c.CardKey, c.Rarity, c.Precision }).ToArray(),
                                cardRewards = b.CardRewards.Take(4).Select(c => new { c.RewardOrdinal,
                                    cards = c.Cards.Take(16).Select(x => new { x.CardKey, x.Rarity, x.Precision }).ToArray() }).ToArray()
                            }).ToArray()
                        }).ToArray()
                    }).ToArray(),
                observedParty = result.Document?.Party?.Transactions.Take(4).Select(t => new
                { t.Slot, t.Option, t.OpeningRouteId, t.Choices, offers = Groups(t.Offers), results = Groups(t.Results) }).ToArray(),
                documentAvailable = result.Document is not null,
                sampleBounds = "3 offers;8 routes;12 groups;16 items per group;6 battles;4 card rewards"
            });
            if (json.Length > 65536)
                json = RuntimeLog.SafeJson(new { seed, result.Evaluation.Disposition, result.Evaluation.FailureCode,
                    result.Evaluation.RouteRejections, plan.SnapshotFingerprint,
                    observationOmitted = "SampleExceeds64KCharacters", originalCharacters = json.Length });
            RuntimeLog.TryBackgroundInfo("searchExactRejectionSample=true;sampleJson=" + json);
        }
        catch (Exception ex)
        {
            // Diagnostics must never change a search outcome.
            RuntimeLog.TryBackgroundWarning("searchExactRejectionDiagnosticFailed=" + ex.GetType().Name);
        }
    }
}
