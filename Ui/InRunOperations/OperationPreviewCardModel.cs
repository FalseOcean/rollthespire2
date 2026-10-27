using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Core.Effects.Snapshots;

namespace RolltheSpire2.Ui.InRunOperations;

// Constructs only unattached display models. Never clone or attach a live card.
internal static class OperationPreviewCardModel
{
    internal static CardModel Create(PredictorCard snapshot)
    {
        var key = snapshot.Key;
        if (snapshot.IncreasedBlock != 0 || snapshot.IncreasedDamage != 0 || snapshot.PermanentCostOverride != null ||
            snapshot.RoomsEntered != 0 || snapshot.CombatsSeen != 0 ||
            snapshot.SpoilsActIndex >= 0 || snapshot.SpoilsCoord != null)
            throw new InvalidOperationException("Detached native card display cannot reconstruct this card's persistent modifiers");
        var canonicalCard = ModelDb.GetById<CardModel>(new ModelId(key.Category, key.Entry));
        // Mod cards use the same detached native display path. Constructing
        // their face (including its recorded upgrade level) does not add their
        // gameplay hooks to the predictor or attach this model to the run.
        var model = canonicalCard.ToMutable();
        if (model is MadScience science)
        {
            science.TinkerTimeType = snapshot.TinkerTimeType switch
            {
                EffectCardType.Attack => CardType.Attack, EffectCardType.Power => CardType.Power,
                _ => CardType.Skill
            };
            science.TinkerTimeRider = Enum.Parse<TinkerTime.RiderEffect>(snapshot.TinkerTimeRider.ToString());
        }
        else if (snapshot.TinkerTimeRider != PredictorMadScienceRider.None)
            throw new InvalidOperationException("Mad Science rider belongs to a different display card");
        for (int level = 0; level < snapshot.UpgradeLevel; level++)
        {
            model.UpgradeInternal();
            model.FinalizeUpgradeInternal();
        }
        if (snapshot.Enchantment is { } enchantmentKey)
        {
            var canonical = ModelDb.GetById<EnchantmentModel>(new ModelId(enchantmentKey.Category, enchantmentKey.Entry));
            if (canonical.GetType().Assembly != typeof(CardModel).Assembly)
                throw new InvalidOperationException("Cannot execute third-party enchantment behavior for a display preview");
            var clone = canonical.ToMutable();
            model.EnchantInternal(clone, snapshot.EnchantmentAmount);
            clone.ModifyCard();
        }
        // These flags describe the actual persistent keyword state, including
        // native canonical Exhaust/Innate. Reconcile after upgrade/enchantment
        // so ordinary keyword-bearing cards remain genuine native previews.
        SetKeyword(model, CardKeyword.Eternal, snapshot.HasEternalKeyword);
        SetKeyword(model, CardKeyword.Exhaust, snapshot.HasExhaustKeyword);
        SetKeyword(model, CardKeyword.Innate, snapshot.HasInnateKeyword);
        SetKeyword(model, CardKeyword.Retain, snapshot.HasRetainKeyword);
        // Never attach the display model to a player/run or use a live deck card here.
        if (model.RunState != null || model.CombatState != null)
            throw new InvalidOperationException("Operation preview card must be detached");
        return model;
    }

    private static void SetKeyword(CardModel model, CardKeyword keyword, bool enabled)
    {
        if (model.Keywords.Contains(keyword) == enabled) return;
        if (enabled) model.AddKeyword(keyword);
        else model.RemoveKeyword(keyword);
    }
}
