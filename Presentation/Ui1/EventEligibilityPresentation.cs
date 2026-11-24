using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.Localization;

namespace RolltheSpire2.Presentation.Ui1;

internal static class EventEligibilityPresentation
{
    internal sealed record OccurrenceDescription(string Text, bool HasAdditionalCondition);

    internal static OccurrenceDescription? Describe(
        IUiTextProvider text,
        RuntimeProfileId profile,
        RngSemanticProfileId semantics,
        ModelKey eventKey,
        int players,
        bool includeActScope = true)
    {
        if (semantics == RngSemanticProfileId.Modern113 && EventStaticEligibilityCatalog.IsAuditedModern113(eventKey))
        {
            string id = Beta111EventPresentationKnowledge.Normalize(eventKey.Entry);
            bool multiplayerOnlyCondition = id is "densevegetation" or "junglemazeadventure";
            string condition = string.Empty;
            bool hasCondition = (!multiplayerOnlyCondition || players > 1) &&
                text.TryGet("event.eligibility.modern113." + id, out condition) && !string.IsNullOrWhiteSpace(condition);
            IReadOnlyList<int> acts = EventStaticEligibilityCatalog.KnownModern113AllowedActs(eventKey);
            string scope = includeActScope && acts.Count is 1 or 2
                ? text.Format("event.eligibility.act_scope", string.Join(" / ", acts.Select(act => text.Format("query.event.act", act))))
                : string.Empty;
            return !hasCondition && scope.Length == 0 ? null : new OccurrenceDescription(scope + (hasCondition ? condition : ""), hasCondition);
        }

        string legacyKey = Beta111EventPresentationKnowledge.RuntimeConditionLocalizationKey(profile, eventKey, players, semantics);
        return !string.IsNullOrEmpty(legacyKey) && text.TryGet(legacyKey, out string legacyCondition) && !string.IsNullOrWhiteSpace(legacyCondition)
            ? new OccurrenceDescription(legacyCondition, true)
            : null;
    }
}
