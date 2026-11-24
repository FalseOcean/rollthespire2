using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.World;

/// <summary>
/// Builds conditional inputs to replay the observed catalogs using the game's normal
/// initialization and room rules. A caller must preserve the assumption alongside
/// any output, including a mismatch. This input belongs only to the conditional pass.
/// </summary>
public static class WorldReplayAssumptions
{
    public static bool TryCreate(WorldAuthoritySnapshot? captured, out WorldAuthoritySnapshot modeled,
        out string issueCode)
    {
        modeled = null!;
        issueCode = string.Empty;
        if (captured?.Beta109Generation is not { Profile: RuntimeProfileId.Beta111, DirectSourceAudited: true } source)
            return false;

        string initial = source.InitialRelicReplayAssumption;
        string rooms = source.RoomReplayAssumption;
        if (source.AppliedReplayAssumptions) return false;
        string eventFilter = source.EventAuthority.LockedEventsAfterShuffle is null &&
            source.EventAuthority.HasExactFilteringAuthority && source.OrderedActCatalog.Any(a => a.EventRngConsumptionExact)
            ? "NoAdditionalEventRemovals" : string.Empty;
        var optionAssumptions = source.AncientEventContexts.Where(c => c.HookDecision == Beta109HookDecision.Unknown &&
            c.Catalog is { CatalogExact: true } && c.OptionReplayAssumption.Length != 0)
            .Select(c => c.OptionReplayAssumption).Distinct(StringComparer.Ordinal);
        issueCode = string.Join("|", new[] { initial, rooms, eventFilter }.Where(s => s.Length != 0).Concat(optionAssumptions));
        if (issueCode.Length == 0) return false;

        var generation = source;
        if (initial.Length != 0)
        {
            // Capture keeps the observed rows/count even if an outside callback may
            // change the bag or consume additional RNG after capture.
            bool orderedBags = source.SharedRelicPoolOrderExact && source.CharacterRelicPoolOrderExact &&
                source.PlayerRelicPoolCompositionExact && source.RelicRarityAuthorityExact &&
                source.SharedRelicBuckets.All(b => b.OrderExact) && source.PlayerRelicBuckets.All(b => b.OrderExact);
            generation = source with
            {
                RelicInitializationExact = orderedBags,
                InitialRelicShuffleCount = source.InitialRelicShuffleCount ?? source.ObservedInitialRelicShuffleCount
            };
            if (generation.UpFrontPrefix.Kind == Beta109UpFrontPrefixAuthorityKind.Missing &&
                generation.UpFrontPrefix.EvidenceCode is "MissingRelicInitialization" or "UnknownHookOrModifier" &&
                generation.CanReplayInitialRelicDraws && generation.ModeFactsExact && generation.RunSeedRootExact)
            {
                generation = generation with
                {
                    UpFrontPrefix = new(Beta109UpFrontPrefixAuthorityKind.ReplayFromRunStart,
                        Beta109UpFrontReplayOrigin.RunStartBeforeRelicInitialization, null, true, initial)
                };
            }
        }

        generation = generation with
        {
            AppliedReplayAssumptions = true,
            WorldGenerationHooksExact = source.WorldGenerationHooksExact || initial.Length != 0 || rooms.Length != 0,
            EventAuthority = eventFilter.Length == 0 ? source.EventAuthority :
                source.EventAuthority with { LockedEventsAfterShuffle = [] },
            EventSequenceReplayAssumption = eventFilter,
            AncientEventContexts = source.AncientEventContexts.Select(ModelOptionContext).ToArray(),
            SnapshotFingerprint = source.SnapshotFingerprint + "|Assuming:" + issueCode
        };
        // The outer snapshot's static authority also includes a run-start-prefix
        // requirement. Rebuild that part only when its actual catalog facts exist.
        bool foundation = initial.Length != 0 && generation.IsVanilla && generation.ActSelectionAuthorityExact &&
            generation.ModeFactsExact && generation.OrderedActCatalog.Count > 0 &&
            generation.SharedAncientCatalogExact && generation.UnlockFactsExact &&
            (generation.RelicInitializationExact || generation.HasCountedChildRelicShuffles);
        modeled = captured with
        {
            Beta109Generation = generation,
            EventAuthority = generation.EventAuthority,
            RelicInitializationExact = generation.RelicInitializationExact,
            SourceAuthority = foundation ? SourceAuthority.OfficialRuntimeExact : captured.SourceAuthority,
            Completeness = foundation ? SnapshotCompleteness.Complete : captured.Completeness,
            SnapshotFingerprint = generation.SnapshotFingerprint
        };
        return true;
    }

    private static Beta109AncientEventContextSnapshot ModelOptionContext(Beta109AncientEventContextSnapshot context)
    {
        if (context.HookDecision != Beta109HookDecision.Unknown) return context;
        bool conditionalGenerator = context.OptionReplayAssumption.Length != 0 && context.Catalog is { CatalogExact: true };
        if ((!conditionalGenerator && (context.ModifierFactsExact || !context.KnownGeneratorWithoutModifiers)) ||
            !context.EventIsSharedAuthorityExact || !context.EventRngRootExact ||
            !context.CharacterKey.IsValid || context.Catalog is null)
            return context;
        var facts = new Dictionary<string, bool>(context.BooleanFacts) { ["modifier.factsExact"] = true };
        return context with
        {
            ModifierFactsExact = true,
            BooleanFacts = facts,
            EventContextExact = true,
            HookDecision = Beta109HookDecision.Allow,
            ContextFingerprint = context.ContextFingerprint + "|Assuming:" +
                (context.ModifierFactsExact ? string.Empty : "UnmodifiedSetup|") + context.OptionReplayAssumption
        };
    }
}
