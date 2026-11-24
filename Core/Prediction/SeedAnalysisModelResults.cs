using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.World;
using Domain = RolltheSpire2.Core.Prediction.SeedPredictionDomainSelection;

namespace RolltheSpire2.Core.Prediction;

internal static class SeedAnalysisModelResults
{
    // This merge is for seed analysis only. Search assesses a strict and a modeled candidate separately.
    internal static SeedPredictionFacts Combine(SeedPredictionFacts observed, SeedPredictionFacts estimate,
        IReadOnlyDictionary<Domain, string> reasons)
    {
        var displayed = new Dictionary<Domain, string>();
        var world = observed.World;
        if (reasons.TryGetValue(Domain.World, out string? roomReason) && estimate.World is { } projected)
        {
            world = CombineWorld(world, projected, out bool estimatedRooms);
            if (estimatedRooms)
            {
                string broadReason = string.Join("|", roomReason.Split('|').Where(c =>
                    c != "NoAdditionalEventRemovals" && !c.StartsWith("StandardAncientOptionGenerator:", StringComparison.Ordinal)));
                if (broadReason.Length != 0) displayed.Add(Domain.World, broadReason);
            }
        }

        var relics = observed.Relics;
        if (reasons.TryGetValue(Domain.RelicSequence, out string? relicReason) && estimate.Relics is { } r &&
            (r.Lanes.Count > 0 || r.TreasureRoomLanes.Count > 0) && relics?.Status != SeedDomainEvaluationStatus.Evaluated)
        {
            displayed.Add(Domain.RelicSequence, relicReason);
            relics = r with { Precision = Conditional(r.Precision), Authority = SourceAuthority.ModdedRuntimeBestEffort,
                Lanes = r.Lanes.Select(ConditionalLane).ToArray(), TreasureRoomLanes = r.TreasureRoomLanes.Select(ConditionalLane).ToArray() };
        }
        var neow = observed.Neow;
        if (reasons.TryGetValue(Domain.Neow, out string? openingReason) && estimate.Neow is { Choices.Count: > 0 } n &&
            neow?.Choices.Count is not > 0)
        {
            displayed.Add(Domain.Neow, openingReason);
            neow = n with { IdentityPrecision = Conditional(n.IdentityPrecision), HasIdentityAuthorityWarning = true,
                Choices = n.Choices.Select(c => c with { IdentityPrecision = Conditional(c.IdentityPrecision),
                    EffectPrecision = Conditional(c.EffectPrecision) }).ToArray() };
        }
        var rewards = observed.CombatRewards;
        if (reasons.TryGetValue(Domain.NormalCombatRewards, out string? rewardReason) && estimate.CombatRewards is { } cr &&
            (cr.Battles.Count > 0 || cr.Routes.Count > 0) && rewards?.Battles.Count is not > 0 && rewards?.Routes.Count is not > 0)
        {
            displayed.Add(Domain.NormalCombatRewards, rewardReason);
            rewards = cr with { Precision = Conditional(cr.Precision), Authority = SourceAuthority.ModdedRuntimeBestEffort };
        }

        if (displayed.Count == 0 && ReferenceEquals(world, observed.World) && !estimate.Warnings.Any(warning => warning.Code == PredictionWarningCode.AnalysisFailed))
            return observed;
        return observed with
        {
            World = world, Relics = relics, Neow = neow, CombatRewards = rewards,
            ModelAssumptions = observed.ModelAssumptions.Count == 0 ? displayed :
                observed.ModelAssumptions.Concat(displayed).ToDictionary(pair => pair.Key, pair => pair.Value),
            InputStatus = displayed.Count > 0 ? SeedPredictionInputStatus.Ready : observed.InputStatus,
            Warnings = observed.Warnings.Concat(estimate.Warnings).Distinct().ToArray(),
            Diagnostics = observed.Diagnostics.Concat(estimate.Diagnostics).Distinct().ToArray()
        };
    }

    internal static WorldPredictionResult? CombineWorld(WorldPredictionResult? observed, WorldPredictionResult projected,
        out bool estimatedRooms)
    {
        var bosses = observed?.Bosses.ToList() ?? [];
        var ancients = observed?.Ancients.ToList() ?? [];
        var encounters = observed?.EncounterSequences.ToList() ?? [];
        bool changed = false;
        estimatedRooms = false;
        foreach (var boss in projected.Bosses)
        {
            if (bosses.Any(b => b.Act == boss.Act && b.Ordinal == boss.Ordinal)) continue;
            bosses.Add(boss with { IdentityPrecision = Conditional(boss.IdentityPrecision), Authority = SourceAuthority.ModdedRuntimeBestEffort });
            estimatedRooms = true;
        }
        foreach (var ancient in projected.Ancients)
        {
            int index = ancients.FindIndex(a => a.Act == ancient.Act);
            if (index < 0)
            {
                ancients.Add(ConditionalOptions(ancient) with { IdentityPrecision = Conditional(ancient.IdentityPrecision),
                    Authority = SourceAuthority.ModdedRuntimeBestEffort });
                estimatedRooms = true;
            }
            else if (ancient.OptionReplayAssumption.Length > 0 && ancients[index].AncientKey == ancient.AncientKey &&
                ancients[index].OptionsEvaluationStatus is AncientOptionsEvaluationStatus.UnknownMissingAuthority or AncientOptionsEvaluationStatus.Unsupported &&
                ancient.OptionsEvaluationStatus is AncientOptionsEvaluationStatus.EvaluatedNonEmpty or AncientOptionsEvaluationStatus.EvaluatedKnownEmpty
                    or AncientOptionsEvaluationStatus.EvaluatedLocked or AncientOptionsEvaluationStatus.EvaluatedPartial)
            {
                var options = ConditionalOptions(ancient);
                ancients[index] = ancients[index] with { Options = options.Options, OptionPrecision = options.OptionPrecision,
                    OptionReplayAssumption = options.OptionReplayAssumption, OptionRngTrace = options.OptionRngTrace,
                    OptionEvidenceCode = options.OptionEvidenceCode, OptionRngCallCount = options.OptionRngCallCount,
                    OptionsEvaluationStatus = options.OptionsEvaluationStatus, OptionIssueCode = options.OptionIssueCode };
                changed = true;
            }
        }
        foreach (var row in projected.EncounterSequences)
        {
            if (encounters.Any(e => e.Act == row.Act)) continue;
            encounters.Add(row with { Precision = Conditional(row.Precision) });
            estimatedRooms = true;
        }

        var events = observed?.EventPoolSequencePrediction;
        string eventReason = string.Empty;
        if (projected.EventPoolSequencePrediction is { Acts.Count: > 0 } queues)
        {
            var acts = events?.Acts.ToList() ?? [];
            foreach (var act in queues.Acts)
            {
                if (acts.Any(a => a.Act == act.Act)) continue;
                acts.Add(act with { Precision = Conditional(act.Precision), Authority = SourceAuthority.ModdedRuntimeBestEffort,
                    Entries = act.Entries.Select(e => e with { Precision = Conditional(e.Precision) }).ToArray(),
                    RawEntries = act.RawEntries.Select(e => e with { Precision = Conditional(e.Precision) }).ToArray() });
            }
            if (acts.Count > (events?.Acts.Count ?? 0))
            {
                events = queues with { Precision = Conditional(queues.Precision), RawEventOrderPrecision = Conditional(queues.RawEventOrderPrecision),
                    StaticCandidateOrdinalPrecision = Conditional(queues.StaticCandidateOrdinalPrecision),
                    Authority = SourceAuthority.ModdedRuntimeBestEffort, Acts = acts.OrderBy(a => a.Act).ToArray() };
                eventReason = projected.EventSequenceReplayAssumption;
                if (eventReason.Length == 0) estimatedRooms = true;
                changed = true;
            }
        }
        if (projected.SelectedActs.Count > 0 && observed?.SelectedActs.Count is not > 0) estimatedRooms = true;
        if (!changed && !estimatedRooms) return observed;
        return projected with
        {
            SelectedActs = observed?.SelectedActs.Count > 0 ? observed.SelectedActs : projected.SelectedActs,
            Bosses = bosses.OrderBy(b => b.Act).ThenBy(b => b.Ordinal).ToArray(), Ancients = ancients.OrderBy(a => a.Act).ToArray(),
            EncounterSequences = encounters.OrderBy(e => e.Act).ToArray(), EventPoolSequencePrediction = events ?? projected.EventPoolSequencePrediction,
            EventSequenceReplayAssumption = eventReason
        };
    }

    private static AncientPredictionResult ConditionalOptions(AncientPredictionResult result) => result with
    {
        OptionPrecision = Conditional(result.OptionPrecision),
        Options = result.Options.Select(o => o with { OptionPrecision = Conditional(o.OptionPrecision),
            SelectabilityPrecision = Conditional(o.SelectabilityPrecision), AppearancePrecision = Conditional(o.AppearancePrecision),
            CharacterTarget = o.CharacterTarget is { } target ? target with { Precision = Conditional(target.Precision) } : null }).ToArray()
    };

    private static PredictionPrecision Conditional(PredictionPrecision precision) =>
        precision == PredictionPrecision.Exact ? PredictionPrecision.Partial : precision;

    private static RelicSequenceLaneResult ConditionalLane(RelicSequenceLaneResult lane) =>
        lane with { Precision = Conditional(lane.Precision), Authority = SourceAuthority.ModdedRuntimeBestEffort };
}
