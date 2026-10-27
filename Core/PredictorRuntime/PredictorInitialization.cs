using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Beta109;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record PredictorActState(int Act, ModelKey Key, ModelKey Ancient,
    ImmutableArray<ModelKey> Bosses, ImmutableArray<ModelKey> NormalEncounters,
    ImmutableArray<ModelKey> EliteEncounters, ImmutableArray<ModelKey> RawEvents,
    int NormalCursor = 0, int EliteCursor = 0, int EventCursor = 0, int BossCursor = 0);

internal static class PredictorInitialization
{
    // Source metadata is copied by the main-thread adapter; this path only runs
    // RT2 numerical donors and never constructs/hosts a vanilla RunManager.
    internal static PredictorRun FromSeed(PredictorContext context, RuntimeContextAuthoritySnapshot authority,
        ImmutableArray<PredictorCard> startingDeck, ImmutableArray<ModelKey> startingRelics, int startingGold)
    {
        if (context.Crystal is not null || authority.Character.CharacterKey != context.Character || authority.Ascension != context.Ascension ||
            authority.CatalogFingerprint != context.CatalogFingerprint || authority.UnlockSnapshotFingerprint != context.UnlockFingerprint ||
            authority.PlayerSlotIndex != 0 || authority.PlayersCount != 1 || authority.NoRunModifiers != true)
            throw new InvalidOperationException("PredictorInitializationContextMismatch");
        var effects = authority.EffectAuthority ?? throw new InvalidOperationException("PredictorEffectCatalogMissing");
        var world = authority.WorldAuthority ?? throw new InvalidOperationException("PredictorWorldCatalogMissing");
        var worldSource = world.Beta109Generation ?? throw new InvalidOperationException("PredictorAncientCatalogMissing");
        if (context.DiscoveredActs.IsDefault) throw new InvalidOperationException("PredictorDiscoveredActsAuthorityMismatch");
        var declaredActs = context.DiscoveredActs.ToHashSet();
        var capturedActs = worldSource.ActSelectionGroups.SelectMany(g => g.Candidates)
            .Where(c => c.DiscoveredInSingleplayer).Select(c => c.ActKey).ToHashSet();
        if (declaredActs.Count != context.DiscoveredActs.Length ||
            !declaredActs.SetEquals(capturedActs))
            throw new InvalidOperationException("PredictorDiscoveredActsAuthorityMismatch");
        if (startingDeck.IsDefaultOrEmpty || effects.CharacterRewardPool == null || effects.ColorlessRewardPool == null ||
            effects.SharedRelicPoolSource == null || effects.CharacterRelicPoolSource == null || effects.PotionPool == null)
            throw new InvalidOperationException("PredictorSourceCatalogIncomplete");
        context = context with
        {
            AncientAuthority = Beta109WorldSnapshotProjector.ProjectForSeed(worldSource, context.Seed),
            Catalog = new(effects.CharacterRewardPool.ToImmutableArray(), effects.ColorlessRewardPool.ToImmutableArray()),
            PotionPool = effects.PotionPool.Select(p => new PredictorPotionOption(p.PotionKey,
                p.Rarity switch { EffectPotionRarity.Common => PredictorPotionRarity.Common,
                    EffectPotionRarity.Uncommon => PredictorPotionRarity.Uncommon,
                    EffectPotionRarity.Rare => PredictorPotionRarity.Rare, _ => PredictorPotionRarity.Event })).ToImmutableArray()
        };
        var deck = startingDeck.ToList();
        if (context.Modifiers.Any(m => m.Key.Entry is "DRAFT" or "INSANITY" or "SEALED_DECK")) deck.Clear();
        // AscensionManager.ApplyEffectsTo, after starting inventory, not during reload.
        if (context.Ascension >= 5) deck.Add(new(0, new("CARD", "ASCENDERS_BANE"), EffectCardType.Curse, 0, 0, false, 1));
        var state = PredictorSeedState.Create(context, deck, startingRelics, startingGold,
            context.Ascension >= 4 ? 2 : 3, new(-.05f, .4f));
        var upFront = state.Streams.Single(s => s.Stream == PredictorStream.UpFront).Restore();
        static PredictorBagEntry Entry(NeowEffectRelicSnapshot r) => new(r.RelicKey,
            string.IsNullOrWhiteSpace(r.RarityCode) ? r.Rarity.ToString() : r.RarityCode);
        var shared = PredictorRelicBag.Populate(effects.SharedRelicPoolSource.Select(Entry), false, upFront);
        var personal = PredictorRelicBag.Populate(effects.SharedRelicPoolSource.Concat(effects.CharacterRelicPoolSource).Select(Entry), true, upFront);
        var runtimeWorld = PredictorWorldInitialization.Generate(context, world);
        var final = runtimeWorld.FinalUpFrontState;
        ImmutableArray<ModelKey> neowOptions = [];
        if (runtimeWorld.StartedWithNeow && context.Modifiers.IsEmpty)
        {
            var neow = ModernNeowIdentityPredictor.PredictModernCore(context.Seed, authority, Beta111Profile.Instance, true);
            if (neow.UnknownEligibilityAffectedPool) throw new InvalidOperationException("PredictorOpeningEligibilityMissing");
            neowOptions = neow.RelicKeys.ToImmutableArray();
        }
        state = state with { PersonalBag = personal, SharedBag = shared, Acts = runtimeWorld.Acts,
            StartedWithNeow = runtimeWorld.StartedWithNeow, OpeningCompleted = !runtimeWorld.StartedWithNeow,
            OpeningOptions = neowOptions, Position = new(1, 1, "opening-entry") };
        if (context.HasRunModifier("DEADLY_EVENTS"))
        {
            var juzu = new ModelKey("RELIC", "JUZU_BRACELET");
            state = state with { PersonalBag = state.PersonalBag.Remove(juzu), SharedBag = state.SharedBag.Remove(juzu), UnknownOdds = new(Elite: .1f) };
        }
        state = state.WithRng(PredictorStream.UpFront, RolltheSpire2.Core.Rng.Xoshiro256StarStar.FromState(final.S0, final.S1, final.S2, final.S3, final.CallCount));
        // FinalizeStartingRelics is distinct from AddRelicInternal; current supported
        // starters have no future-relevant obtain hook. Unknown starters are not no-ops.
        foreach (var relic in state.Relics) PredictorSettlementEffects.RequireImplementedRelic(relic.Key);
        state.Validate();
        return new(context, state);
    }

    internal static PredictorCard ToCard(NeowEffectCardSnapshot card) => new(0, card.CardKey, card.CardType,
        card.UpgradeLevel, card.MaxUpgradeLevel, card.CanRemove, 1);
}
