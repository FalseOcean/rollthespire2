namespace RolltheSpire2.Core.PredictorRuntime;

// Per-level card facts are captured from detached mutable vanilla cards. Runtime
// upgrades only select a captured level and replay persistent local enchantments.
internal static class PredictorCardLevelEffects
{
    // Native Dingy Dagger can gain hundreds of millions of levels. OnUpgrade
    // changes damage only, so the captured cost and keyword facts are constant.
    internal static bool HasInvariantLevelMetadata(RolltheSpire2.Core.Identity.ModelKey key) =>
        key.Category == "CARD" && key.Entry == "DINGY_DAGGER";

    internal static PredictorCardLevelMetadata ResolveLevel(PredictorCardDefinition definition, int upgradeLevel)
    {
        if (definition.Levels.IsDefaultOrEmpty)
            throw new InvalidOperationException("PredictorCardLevelsMissing:" + definition.Prototype.Key);
        int capturedLevel = HasInvariantLevelMetadata(definition.Prototype.Key) ? 0 : upgradeLevel;
        var level = definition.Levels.FirstOrDefault(l => l.UpgradeLevel == capturedLevel)
            ?? throw new InvalidOperationException("PredictorCardLevelMissing:" + definition.Prototype.Key + ":" + upgradeLevel);
        return level with { UpgradeLevel = upgradeLevel };
    }

    internal static PredictorCardLevelMetadata Resolve(PredictorContext context, PredictorCard card)
    {
        var definition = PredictorCardChanges.Definition(context, card.Key);
        var level = ResolveLevel(definition, card.UpgradeLevel);
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
