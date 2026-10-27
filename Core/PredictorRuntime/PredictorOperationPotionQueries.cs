using System.Collections.Immutable;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.PredictorRuntime;

// Ordered unlocked native pools before the factory's player-count and combat
// filters. These copies carry no native models and invoke no content lifecycle.
internal sealed record PredictorOperationPotionCard(PredictorCard Card, string Rarity,
    bool CanGenerate, string MultiplayerConstraint);
internal sealed record PredictorOperationPotionPools(ImmutableArray<PredictorOperationPotionCard> CharacterCards,
    ImmutableArray<PredictorOperationPotionCard> ColorlessCards);
internal enum PredictorOperationPotionCardRole { ChoiceCandidate, GeneratedCard }
internal sealed record PredictorOperationPotionCardResult(int Group, int Ordinal, PredictorCard Card,
    PredictorOperationPotionCardRole Role, bool FreeForTurn, bool FreeIfSelected);
internal sealed record PredictorOperationPotionQueryResult(ModelKey Potion, ulong? TargetPlayerId,
    ImmutableArray<PredictorOperationPotionCardResult> Cards, bool RequiresCardChoice,
    PredictorOperationEventRng? RngAfter, ImmutableArray<string> Preconditions,
    ImmutableArray<string> MissingInputs)
{
    // This is a conditional identity query, never a request to use a potion.
    internal bool CanUseOutOfCombat => false;
    internal bool IdentitiesComputed => MissingInputs.IsEmpty;
}

internal static class PredictorOperationPotionQueries
{
    internal static bool Supports(ModelKey potion) => potion.Category == "POTION" && potion.Entry is
        "ATTACK_POTION" or "SKILL_POTION" or "POWER_POTION" or "COLORLESS_POTION" or "COSMIC_CONCOCTION" or "OROBIC_ACID";

    internal static PredictorOperationPotionQueryResult Query(PredictorContext context, ModelKey potion)
    {
        if (!Supports(potion)) throw new ArgumentException("PredictorOperationPotionQueryUnsupported", nameof(potion));
        bool choose = potion.Entry is "ATTACK_POTION" or "SKILL_POTION" or "POWER_POTION" or "COLORLESS_POTION";
        ImmutableArray<string> preconditions = ["LegalCombatUseOnCapturedTargetPlayer",
            "CapturedPoolsAndCombatCardGenerationCursorStillApply",
            "NoInterveningCombatCardGenerationBeforeUse",
            "CombatHandInsertionAndAfterCardEnteredCombatAreNotExecuted"];
        var missing = ImmutableArray.CreateBuilder<string>();
        var operation = context.Operation;
        if (operation == null) missing.Add("OperationTargetPlayerMissing");
        if (operation?.CombatCardRng == null) missing.Add("CombatCardGenerationStateMissing");
        bool colorless = potion.Entry is "COLORLESS_POTION" or "COSMIC_CONCOCTION";
        var pool = operation?.CombatCardPools is { } pools
            ? colorless ? pools.ColorlessCards : pools.CharacterCards : default;
        if (pool.IsDefault) missing.Add(colorless ? "OrderedUnlockedColorlessPoolMissing" : "OrderedUnlockedCharacterPoolMissing");
        if (missing.Count != 0)
            return new(potion, operation?.PlayerId, [], choose, null, preconditions, missing.ToImmutable());
        var cursor = operation!.CombatCardRng!;
        if (operation.PlayerCount < 1 || cursor.Calls < 0 || (cursor.S0 | cursor.S1 | cursor.S2 | cursor.S3) == 0 ||
            pool.Any(c => c == null || c.Card == null || c.Card.Key.Category != "CARD" || !c.Card.Key.IsValid ||
                c.Card.UpgradeLevel < 0 || c.Card.MaxUpgradeLevel < c.Card.UpgradeLevel ||
                c.Rarity == null || c.MultiplayerConstraint == null))
            throw new InvalidDataException("PredictorOperationPotionQueryInputInvalid");
        var rng = Xoshiro256StarStar.FromState(cursor.S0, cursor.S1, cursor.S2, cursor.S3, cursor.Calls);
        var result = ImmutableArray.CreateBuilder<PredictorOperationPotionCardResult>();

        // CardFactory.GetDistinctForCombat filters, deduplicates in incoming
        // order, shuffles the entire remaining pool, then takes the prefix.
        void Generate(EffectCardType? type, int count, int group)
        {
            var seen = new HashSet<ModelKey>();
            var candidates = pool.Where(c => type == null || c.Card.Type == type)
                .Where(c => operation.PlayerCount > 1 ? c.MultiplayerConstraint != "SingleplayerOnly" : c.MultiplayerConstraint != "MultiplayerOnly")
                .Where(c => c.CanGenerate && c.Rarity is not ("Basic" or "Ancient" or "Event"))
                .Where(c => seen.Add(c.Card.Key)).ToList();
            rng.UnstableShuffle(candidates);
            int ordinal = 0;
            foreach (var candidate in candidates.Take(count))
            {
                var card = candidate.Card with { Id = 0, Removed = false };
                if (potion.Entry == "COSMIC_CONCOCTION" && card.UpgradeLevel < card.MaxUpgradeLevel)
                {
                    card = card with { UpgradeLevel = card.UpgradeLevel + 1 };
                    // Vanilla level metadata remains authoritative; opaque
                    // content contributes no extra behavior in this product.
                    if (!operation.IgnoresBehavior(card.Key)) card = PredictorCardLevelEffects.Reconcile(context, card);
                }
                result.Add(new(group, ordinal++, card,
                    choose ? PredictorOperationPotionCardRole.ChoiceCandidate : PredictorOperationPotionCardRole.GeneratedCard,
                    FreeForTurn: potion.Entry == "OROBIC_ACID", FreeIfSelected: choose));
            }
        }

        if (potion.Entry == "OROBIC_ACID")
        {
            Generate(EffectCardType.Attack, 1, 0);
            Generate(EffectCardType.Skill, 1, 1);
            Generate(EffectCardType.Power, 1, 2);
        }
        else Generate(potion.Entry switch
        {
            "ATTACK_POTION" => EffectCardType.Attack,
            "SKILL_POTION" => EffectCardType.Skill,
            "POWER_POTION" => EffectCardType.Power,
            _ => null
        }, 3, 0);
        var after = rng.State;
        return new(potion, operation.PlayerId, result.ToImmutable(), choose,
            new(after.S0, after.S1, after.S2, after.S3, rng.CallCount), preconditions, []);
    }
}
