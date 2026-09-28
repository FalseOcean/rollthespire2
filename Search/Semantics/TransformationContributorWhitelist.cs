namespace RolltheSpire2.Search.Semantics;

/// <summary>Owner approvals 2026-09-14 / 2026-09-22. Documentation of explicit source switches,
/// not discovery metadata and not a registration/reflection framework.</summary>
internal static class TransformationContributorWhitelist
{
    internal sealed record Entry(string Identity, int Opportunities, string Commitment,
        string Predictor, string NumericalDonor);
    internal static IReadOnlyList<Entry> Entries { get; } = Array.AsReadOnly(new[] {
        new Entry("N.LeafyPoultice", 2, "Offer/pick Leafy; first available Basic Strike and Defend",
            "NeowEffectProjectionEngine.ProjectLeafyPoultice", "NeowLocalOperators.DrawLeafyTransforms"),
        new Entry("N.NewLeaf", 1, "Offer/pick New Leaf; normalized first remaining Basic Strike",
            "NeowEffectProjectionEngine.ProjectNewLeaf", "NeowLocalOperators.DrawNewLeafTransform"),
        new Entry("N.BonesLeafyOther", 2, "Fixed Bones pair; only Leafy outputs are owned, companion and pickup order retained",
            "NeowEffectProjectionEngine.ProjectBones", "TransformationAggregateNumericalPlan.LeafyWithCompanion"),
        new Entry("E.MorphicGrove", 2, "Group; two legal initial Basics at authored entry",
            "Beta111MorphicGroveProjector", "TransformationAggregateNumericalPlan.MORPHIC_GROVE"),
        new Entry("E.AromaOfChaos", 1, "LetGo; one legal initial Basic at authored entry",
            "Beta111SingleBasicTransformProjector.AromaOfChaos", "TransformationAggregateNumericalPlan.AROMA_OF_CHAOS"),
        new Entry("E.WhisperingHollow", 1, "Hug; one legal initial Basic; replay CalculateVars gold draw",
            "Beta111SingleBasicTransformProjector.WhisperingHollow", "TransformationAggregateNumericalPlan.WHISPERING_HOLLOW"),
        new Entry("E.Symbiote", 1, "One legal initial Basic at authored entry", "EventResultTransformSemantics.Project", "TransformationAggregateNumericalPlan.SYMBIOTE"),
        new Entry("E.TrialNondescript", 2, "Require Nondescript case, then transform two initial Basics", "EventResultTransformSemantics.Project", "TransformationAggregateNumericalPlan.TRIAL")
    });
    // N openings are alternatives. Bones admits exactly Leafy + NewLeaf, with
    // either or pinned pickup order. BonesLeafyOther owns Leafy alone; companion
    // effects stay in full Exact replay. No Conveyor contributor.
}
