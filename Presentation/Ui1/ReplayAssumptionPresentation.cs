using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Presentation.Localization;

namespace RolltheSpire2.Presentation.Ui1;

internal static class ReplayAssumptionPresentation
{
    internal static string Describe(IUiTextProvider text, string causes)
    {
        var labels = causes.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(cause => cause.Split(':', 2)[0] switch
            {
                "NoAdditionalEventRemovals" => "event_removals",
                "StandardAncientOptionGenerator" => "ancient_options",
                "RoomGenerationMethodPatched" => "rooms",
                "RelicInitializationMethodPatched" => "bags",
                "StandardNeowOfferPool" => "offer_pool",
                "StandardNeowOfferRules" => "offer_rules",
                Beta111MerchantColorlessAuthority.StandardRulesAssumption => "shop_colorless_rules",
                Beta111MerchantColorlessAuthority.StandardLayoutAssumption => "shop_colorless_layout",
                Beta111MerchantColorlessAuthority.CapturedOrderAssumption => "shop_colorless_order",
                "UnresolvedEligibilityIncluded" => "eligibility",
                "UnknownHookOrModifier" or "StandardRunRules" => "setup",
                _ => "model"
            }).Distinct().Select(key => text.Get("predictor.analysis.premise." + key));
        return string.Join(text.Get("predictor.analysis.separator"), labels);
    }
}
