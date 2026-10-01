namespace RolltheSpire2.Core.PredictorRuntime;

// Per-level card facts are captured from detached mutable vanilla cards. Runtime
// upgrades only select a captured level and replay persistent local enchantments.
internal static class PredictorCardLevelEffects
{
    internal static PredictorCardLevelMetadata Resolve(PredictorContext context, PredictorCard card)
    {
        var definition = PredictorCardChanges.Definition(context, card.Key);
        if (definition.Levels.IsDefaultOrEmpty)
            throw new InvalidOperationException("PredictorCardLevelsMissing:" + card.Key);
        var level = definition.Levels.FirstOrDefault(l => l.UpgradeLevel == card.UpgradeLevel)
            ?? throw new InvalidOperationException("PredictorCardLevelMissing:" + card.Key + ":" + card.UpgradeLevel);
        string? enchantment = card.Enchantment?.Entry;
        return level with
        {
            LocalCost = card.PermanentCostOverride ?? level.LocalCost,
            LocalExhaust = enchantment switch
            {
                "SOULS_POWER" => false,
                "GOOPY" => true,
                _ => level.LocalExhaust
            },
            Innate = level.Innate || enchantment == "ROYALLY_APPROVED",
            Retain = level.Retain || enchantment is "STEADY" or "ROYALLY_APPROVED",
            Eternal = level.Eternal || enchantment == "TEZCATARAS_EMBER"
        };
    }

    internal static PredictorCard Reconcile(PredictorContext context, PredictorCard card)
    {
        var level = Resolve(context, card);
        return card with
        {
            HasExhaustKeyword = level.LocalExhaust,
            HasInnateKeyword = level.Innate,
            HasRetainKeyword = level.Retain,
            HasEternalKeyword = level.Eternal,
            Removable = !level.Eternal
        };
    }
}
