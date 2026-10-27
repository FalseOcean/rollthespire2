using System.Collections.Immutable;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereItems;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.PredictorRuntime;
using static RolltheSpire2.Infrastructure.Snapshots.PredictorLiveRunCapture;
using static RolltheSpire2.Infrastructure.Snapshots.PredictorOperationCapture;

namespace RolltheSpire2.Infrastructure.Snapshots;

// Owns every native reward's detached reroll recipe. Scene captures retain
// event ancestry and scene facts; they never replace this concrete recipe.
internal static class PredictorOperationRewardRecipeCapture
{
    internal static PredictorRewardPlan? Capture(CardReward reward)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var options = Read<CardCreationOptions>(reward, "<RerollOptions>k__BackingField");
        // A missing recipe affects reroll only. Other actions keep their actual
        // generated content; no delegate is retained in the detached snapshot.
        bool personalRng = options.RngOverride == null || ReferenceEquals(options.RngOverride, reward.Player.PlayerRng.Rewards);
        bool eventRng = reward.Player.RunState.CurrentRoom is EventRoom eventRoom &&
            ReferenceEquals(options.RngOverride, eventRoom.LocalMutableEvent.Rng);
        if (!personalRng && !eventRng || options.CardPools.Count == 0 ||
            (options.Flags & (CardCreationFlags.NoHookUpgrades | CardCreationFlags.NoModifyHooks | CardCreationFlags.NoCardModelModifications)) != 0)
            return null;
        RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity? fixedRarity = null;
        if (options.CardPoolFilter is { } filter)
        {
            // Philosophers reads rarity; Future of Potions reads rarity and
            // its already initialized potion/type mapping. Both official
            // recipes forbid pool and rarity modification. Uniform alone
            // never authorizes evaluating an unknown filter or widening it.
            var philosophers = typeof(ColorfulPhilosophers);
            bool crystalFilter = filter.GetInvocationList().Length == 1 && eventRng &&
                filter.Target is CrystalSphereCardReward && DeclaredBy(filter, typeof(CrystalSphereCardReward));
            bool knownFilter = filter.GetInvocationList().Length == 1 &&
                (DeclaredBy(filter, philosophers) || DeclaredBy(filter, typeof(TheFutureOfPotions)));
            if ((!knownFilter && !crystalFilter) || options.Source != CardCreationSource.Other ||
                options.RarityOdds != CardRarityOddsType.Uniform ||
                !crystalFilter && !options.Flags.HasFlag(CardCreationFlags.NoCardPoolModifications | CardCreationFlags.NoRarityModification))
                return null;
            // Crystal permits vanilla pool additions. Preserve the predicate
            // after those additions too, rather than widening its reroll to
            // every rarity in Dingy Rug / Prismatic Gem's added pools.
            if (crystalFilter)
                fixedRarity = Enum.Parse<RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity>(
                    Read<CardRarity>(filter.Target!, "_rarity").ToString());
        }
        // Materialize the actual unlocked pool, in native order, on the main
        // thread. No live delegate or CardModel crosses the worker boundary.
        bool upgradeAfterGeneration = false;
        if (ReadNullable(reward, "AfterGenerated") is Delegate afterGenerated)
        {
            // Third-party additional hooks are outside the declared vanilla
            // model. Do not execute them or make their presence a new gate.
            var callbacks = afterGenerated.GetInvocationList().Where(callback =>
                callback.Method.DeclaringType?.Assembly == typeof(CardReward).Assembly).ToArray();
            if (callbacks.Length > 1 || callbacks.Length == 1 &&
                (!DeclaredBy(callbacks[0], typeof(TheFutureOfPotions)) ||
                 !callbacks[0].Method.Name.Contains("UpgradeCardsInReward", StringComparison.Ordinal)))
                return null;
            upgradeAfterGeneration = callbacks.Length == 1;
        }
        // Crystal's options object is mutated in-place by vanilla pool hooks
        // during initial population. Its authored base is exactly the owner's
        // character pool; replay the modeled additions once, in native order.
        // Reusing the expanded mutable list would add Dingy Rug twice.
        var pool = (fixedRarity == null ? options.GetPossibleCards(reward.Player) :
            reward.Player.Character.CardPool.GetUnlockedCards(reward.Player.UnlockState,
                reward.Player.RunState.CardMultiplayerConstraint).Where(options.CardPoolFilter!))
            .Select(Key).ToImmutableArray();
        var odds = options.RarityOdds switch
        {
            CardRarityOddsType.RegularEncounter => PredictorCardOdds.Regular,
            CardRarityOddsType.EliteEncounter => PredictorCardOdds.Elite,
            CardRarityOddsType.BossEncounter => PredictorCardOdds.Boss,
            CardRarityOddsType.Uniform => PredictorCardOdds.Uniform,
            _ => (PredictorCardOdds?)null
        };
        if (odds == null) return null;
        return new(PredictorRewardKind.Card, CardCount: Read<int>(reward, "<OptionCount>k__BackingField"), Odds: odds.Value,
            EncounterSource: options.Source == CardCreationSource.Encounter,
            IsFromCombat: options.Flags.HasFlag(CardCreationFlags.IsFromCombat),
            NoUpgradeRoll: options.Flags.HasFlag(CardCreationFlags.NoUpgradeRoll), CardPoolOverride: pool,
            NoCardPoolModifications: options.Flags.HasFlag(CardCreationFlags.NoCardPoolModifications),
            ForceRarityOddsChange: options.Flags.HasFlag(CardCreationFlags.ForceRarityOddsChange),
            NoRarityModification: options.Flags.HasFlag(CardCreationFlags.NoRarityModification),
            UseEventRng: eventRng, Rarity: fixedRarity) { UpgradeAfterGeneration = upgradeAfterGeneration };
    }

    private static bool DeclaredBy(Delegate callback, Type owner)
    {
        for (var type = callback.Method.DeclaringType; type != null; type = type.DeclaringType)
            if (type == owner) return true;
        return false;
    }
}
