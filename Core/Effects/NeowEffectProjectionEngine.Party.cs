using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects.Shadow;
using RolltheSpire2.Core.Prediction;

namespace RolltheSpire2.Core.Effects;

internal sealed partial class NeowEffectProjectionEngine
{
    // Initial, vanilla, one selected transaction per owner. Does not attach C continuation.
    internal NeowEffectProjection ProjectPartyOption(ModelKey option, NeowEffectRngContext rng)
    {
        var authority = _analysisAuthority.EffectAuthority ?? throw new InvalidOperationException("Party.N.EffectAuthorityMissing");
        if (!authority.HasExactFoundation || authority.UsesBestEffortModel || !_analysisAuthority.IsBeta111NeowIdentityAuthorityExact ||
            _analysisAuthority.NoRunModifiers != true || !_analysisAuthority.Character.IsKnownVanilla)
            throw new InvalidOperationException("Party.N.InitialAuthorityIncomplete");
        // Beta111 AscensionLevel.TightBelt is A4. Each admitted owner begins with no potions.
        authority = authority with { CurrentPotionCount = 0, PotionCapacity = _analysisAuthority.Ascension >= 4 ? 2 : 3 };
        var state = new NeowEffectWorkingState(authority,
            authority.OrderedDeck is null ? null : new NeowShadowDeck(authority.OrderedDeck), null, rng);
        var result = ProjectInternal(option, state, 0);
        if (result.ProductRelevantProjectionPrecision != PredictionPrecision.Exact || result.FullEffectSemanticsCompleteness != FullEffectSemanticsCompleteness.Complete ||
            result.EffectGroups.Any(g => g.OrderedItems.Any(e => e.Precision != PredictionPrecision.Exact)))
            throw new InvalidOperationException("Party.N.TransactionUnclosed:" + option.Serialized + ":" + result.EvidenceCode);
        return result;
    }
}
