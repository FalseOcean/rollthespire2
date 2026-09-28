using RolltheSpire2.Core.Effects.Coverage;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Ui.Pages.Search.Neow;

/// <summary>
/// UI-shape registry audited from the production Neow projector. It contains no
/// RNG, no display-name inference, and no prediction logic.
/// </summary>
internal static class NeowEffectCardRegistry
{
    private static readonly IReadOnlyDictionary<ModelKey, NeowEffectCardDefinition> Definitions = Build();

    public static NeowEffectCardDefinition Get(ModelKey key) =>
        Definitions.TryGetValue(key, out NeowEffectCardDefinition? value)
            ? value
            : None(key, "Pending", "neow-ui-definition-pending");

    public static IReadOnlyList<NeowEffectCardDefinition> All => Definitions.Values.ToArray();

    private static IReadOnlyDictionary<ModelKey, NeowEffectCardDefinition> Build()
    {
        var values = BaseGameModelKeys.Relics.AllNeow.ToDictionary(
            key => key,
            DefaultDefinition,
            ModelKeyComparer.Instance);

        values[BaseGameModelKeys.Relics.ArcaneScroll] = Generic(
            BaseGameModelKeys.Relics.ArcaneScroll,
            One("generated-card", "随机稀有牌", NeowStructuredEffectScope.ProductRelevantEffects,
                NeowStructuredOutputKind.Card, NeowCandidatePoolKind.RareCharacterCards),
            "ProjectArcaneScroll");
        values[BaseGameModelKeys.Relics.HeftyTablet] = Generic(
            BaseGameModelKeys.Relics.HeftyTablet,
            One("chosen-card", "三选一结果", NeowStructuredEffectScope.SelectableOfferGroups,
                NeowStructuredOutputKind.Card, NeowCandidatePoolKind.RareCharacterCards),
            "ProjectHeftyTablet");
        values[BaseGameModelKeys.Relics.LeadPaperweight] = Generic(
            BaseGameModelKeys.Relics.LeadPaperweight,
            One("chosen-card", "无色牌选择结果", NeowStructuredEffectScope.SelectableOfferGroups,
                NeowStructuredOutputKind.Card, NeowCandidatePoolKind.ColorlessCards),
            "ProjectLeadPaperweight");
        values[BaseGameModelKeys.Relics.LostCoffer] = Generic(
            BaseGameModelKeys.Relics.LostCoffer,
            new[]
            {
                One("chosen-card", "卡牌结果", NeowStructuredEffectScope.SelectableOfferGroups,
                    NeowStructuredOutputKind.Card, NeowCandidatePoolKind.CharacterCards),
                One("potion", "药水结果", NeowStructuredEffectScope.SelectableOfferGroups,
                    NeowStructuredOutputKind.Potion, NeowCandidatePoolKind.Potions)
            },
            "ProjectLostCoffer");
        values[BaseGameModelKeys.Relics.MassiveScroll] = Generic(
            BaseGameModelKeys.Relics.MassiveScroll,
            One("chosen-card", "多人牌选择结果", NeowStructuredEffectScope.SelectableOfferGroups,
                NeowStructuredOutputKind.Card, NeowCandidatePoolKind.MultiplayerCards),
            "MassiveScroll.AfterObtained");
        values[BaseGameModelKeys.Relics.Kaleidoscope] = Generic(
            BaseGameModelKeys.Relics.Kaleidoscope,
            new[]
            {
                new NeowEffectComponentDefinition(
                    "independent-offers", "两组独立三选一", NeowStructuredConditionKind.IndependentOfferGroupTargets,
                    NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card,
                    NeowCandidatePoolKind.OtherCharacterCards, 2, Unordered: true, AllowDuplicateOutputs: true, SupportsOrderSelection: true)
            },
            "ProjectKaleidoscope");
        values[BaseGameModelKeys.Relics.ScrollBoxes] = new NeowEffectCardDefinition(
            BaseGameModelKeys.Relics.ScrollBoxes,
            NeowEffectCardTemplate.ScrollBoxes,
            Array.Empty<NeowEffectComponentDefinition>(),
            "DirectSourceConfirmed",
            "ProjectScrollBoxes");
        values[BaseGameModelKeys.Relics.PhialHolster] = Generic(
            BaseGameModelKeys.Relics.PhialHolster,
            new[]
            {
                new NeowEffectComponentDefinition(
                    "potions", "两瓶药水", NeowStructuredConditionKind.ExactUnorderedPair,
                    NeowStructuredEffectScope.GeneratedPotions, NeowStructuredOutputKind.Potion,
                    NeowCandidatePoolKind.Potions, 2, Unordered: true)
            },
            "ProjectPhialHolster");
        values[BaseGameModelKeys.Relics.LeafyPoultice] = Generic(
            BaseGameModelKeys.Relics.LeafyPoultice,
            new[]
            {
                new NeowEffectComponentDefinition(
                    "transforms", "两张变化结果", NeowStructuredConditionKind.ExactUnorderedPair,
                    NeowStructuredEffectScope.TransformResults, NeowStructuredOutputKind.Card,
                    NeowCandidatePoolKind.TransformCards, 2, Unordered: true, AllowDuplicateOutputs: true)
            },
            "ProjectLeafyPoultice");
        values[BaseGameModelKeys.Relics.NewLeaf] = Generic(
            BaseGameModelKeys.Relics.NewLeaf,
            One("transform", "变化结果（默认目标：基础打击）", NeowStructuredEffectScope.TransformResults,
                NeowStructuredOutputKind.Card, NeowCandidatePoolKind.NewLeafTransformCards),
            "ProjectNewLeaf");
        values[BaseGameModelKeys.Relics.SmallCapsule] = Generic(
            BaseGameModelKeys.Relics.SmallCapsule,
            One("nested-relic", "内部遗物", NeowStructuredEffectScope.NestedRelics,
                NeowStructuredOutputKind.Relic, NeowCandidatePoolKind.OrdinaryRelics),
            "ProjectCapsule(count=1)");
        values[BaseGameModelKeys.Relics.LargeCapsule] = Generic(
            BaseGameModelKeys.Relics.LargeCapsule,
            new[]
            {
                new NeowEffectComponentDefinition(
                    "nested-relics", "两个内部遗物", NeowStructuredConditionKind.ExactUnorderedPair,
                    NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic,
                    NeowCandidatePoolKind.OrdinaryRelics, 2, Unordered: true)
            },
            "ProjectCapsule(count=2)");
        values[BaseGameModelKeys.Relics.NeowsBones] = new NeowEffectCardDefinition(
            BaseGameModelKeys.Relics.NeowsBones,
            NeowEffectCardTemplate.Bones,
            Array.Empty<NeowEffectComponentDefinition>(),
            "DirectSourceConfirmed",
            "ProjectBones+PlayerChoiceRouteSet");

        return values;
    }


    private static NeowEffectCardDefinition DefaultDefinition(ModelKey key)
    {
        if (!NeowEffectCoverageRegistry.TryGet(key, out NeowEffectCoverageEntry entry))
        {
            return None(key, "Pending", "neow-ui-definition-pending");
        }
        return entry.Family switch
        {
            NeowEffectFamily.MultiplayerOnly =>
                None(key, "SingleplayerNotApplicable", entry.Beta109EvidenceCode),
            NeowEffectFamily.FutureRuntime =>
                None(key, "IdentityOnlyFutureRuntime", entry.Beta109EvidenceCode),
            NeowEffectFamily.FinitePlayerChoice =>
                None(key, "PlayerChoiceCapabilityOnly", entry.Beta109EvidenceCode),
            _ => None(key, "ConfirmedNoConfigurableOutput", entry.Beta109EvidenceCode)
        };
    }

    private static NeowEffectCardDefinition Generic(
        ModelKey key,
        NeowEffectComponentDefinition component,
        string evidence) => Generic(key, new[] { component }, evidence);

    private static NeowEffectCardDefinition Generic(
        ModelKey key,
        IReadOnlyList<NeowEffectComponentDefinition> components,
        string evidence) => new(
            key,
            NeowEffectCardTemplate.Generic,
            components,
            "DirectSourceConfirmed",
            evidence);

    private static NeowEffectComponentDefinition One(
        string id,
        string description,
        NeowStructuredEffectScope scope,
        NeowStructuredOutputKind output,
        NeowCandidatePoolKind pool) => new(
            id,
            description,
            NeowStructuredConditionKind.ExactSingle,
            scope,
            output,
            pool,
            1);

    private static NeowEffectCardDefinition None(ModelKey key, string status, string evidence) => new(
        key,
        NeowEffectCardTemplate.None,
        Array.Empty<NeowEffectComponentDefinition>(),
        status,
        evidence);
}
