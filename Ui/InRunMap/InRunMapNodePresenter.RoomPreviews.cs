using System.Collections.Immutable;
using System.Text.Json;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.InRunOperations;

namespace RolltheSpire2.Ui.InRunMap;

internal sealed partial class InRunMapNodePresenter
{
    private sealed record RestAvailability(bool Enabled, bool Confirmed, ImmutableArray<string> Reasons);
    private readonly Dictionary<string, RestAvailability> _restAvailabilityCache = new(StringComparer.Ordinal);
    private (long Revision, long Localization) _restAvailabilityVersion = (-1, -1);
    private readonly Dictionary<string, OperationPreview?> _shopPreviewCache = new(StringComparer.Ordinal);
    private (long Revision, long Localization) _shopPreviewVersion = (-1, -1);

    private static PredictorPlannedNode RestActionPlan(Context c, PredictorRestAction action) => c.Plan with
    {
        RestActions = c.Plan.RestActions.Take(c.Node.AppliedRestActions).Append(new PredictorPlannedRestAction(action)).ToImmutableArray(),
        LeaveRest = false
    };

    private InRunMapDetailChoice RestOfferChoice(Context c, PredictorRestOffer offer,
        List<InRunMapDetailSection> explanations)
    {
        var availability = offer.Enabled && c.Node.AppliedRestActions > 0
            ? NextRestAvailability(c, offer.Action) : new RestAvailability(offer.Enabled, true, []);
        string label = R("rest." + offer.Action);
        if (!availability.Confirmed) label += " · " + M("rest_unverified");
        if (!availability.Reasons.IsEmpty)
        {
            string reason = string.Join("\n", availability.Reasons);
            int shared = explanations.FindIndex(section => section.Text == reason);
            if (shared < 0) explanations.Add(new(R("rest." + offer.Action), reason, [], []));
            else explanations[shared] = explanations[shared] with
                { Title = explanations[shared].Title + " · " + R("rest." + offer.Action) };
        }
        return Choice(c, label, context => Set(context, RestActionPlan(context, offer.Action)), enabled: availability.Enabled);
    }

    private RestAvailability NextRestAvailability(Context c, PredictorRestAction action)
    {
        if (!Valid(c, c.Node.Position.NodeId)) return new(false, false, [M("preview_unavailable")]);
        var version = (c.Revision, _localizationRevision);
        if (_restAvailabilityVersion != version)
        {
            _restAvailabilityCache.Clear();
            _restAvailabilityVersion = version;
        }
        var plan = RestActionPlan(c, action);
        string key = JsonSerializer.Serialize(plan);
        if (_restAvailabilityCache.TryGetValue(key, out var cached)) return cached;
        RestAvailability availability;
        try
        {
            var result = _session.PreviewPlan(c.Location.ActIndex, c.Location.Point, plan, c.Revision);
            int expected = plan.RestActions.Length;
            var local = NextRestActionCauses(c.Node, result);
            if (result?.AppliedRestActions >= expected)
                // This receipt confirms the additional operation, not its
                // target or the obligations of any earlier operation.
                availability = new(true, true, []);
            else if (local.Any(cause => cause.Code == "PlannedRestActionUnavailable"))
                availability = new(false, true, [M("rest_additional_unavailable")]);
            else if (result?.Phase == PredictorPhase.RestHealFact && local.Any(cause => cause.Code == "RestMaximumHp"))
                availability = new(true, true, PendingLines(local).Distinct().ToImmutableArray());
            else
            {
                // Missing Smith/Cook targets are checked before SelectRestAction.
                // They permit authoring that target, but do not prove that a
                // retained entry-time option remains executable after a use.
                var reasons = PendingLines(local).Concat(result == null ? [M("preview_unavailable")] : Array.Empty<string>())
                    .Distinct().ToImmutableArray();
                availability = new(true, false, reasons);
            }
        }
        catch (Exception error)
        {
            RuntimeLog.Fault("inRunMapOptionPreviewFailed=true;stage=detached-rest-availability;node=" +
                c.Node.Position.NodeId + ";action=" + action + ";revision=" + c.Revision, error);
            availability = new(true, false, [M("preview_unavailable")]);
        }
        _restAvailabilityCache[key] = availability;
        return availability;
    }

    // Node pending causes include inherited source obligations and effects of
    // already applied actions. Only a new rest execution blocker can qualify
    // this additional option; its parents retain the real dependency provenance.
    internal static ImmutableArray<PredictorCombatPendingCause> NextRestActionCauses(
        PredictorRouteNodeKnowledge current, PredictorRouteNodeKnowledge? preview)
    {
        var existing = current.PendingCauses.Select(cause => cause.Identity).ToHashSet(StringComparer.Ordinal);
        return preview?.PendingCauses.Where(cause => cause.NodeId == current.Position.NodeId &&
            (string.IsNullOrEmpty(cause.Identity) || !existing.Contains(cause.Identity)) &&
            cause.Code is "PlannedRestActionUnavailable" or "PlannedRestTargetPending" or "RestMaximumHp" or
                "RestDeckDependentAction" or "RestRelicRngPending" or "RestObtainDependent" or "RestChildChoice" or "RestRewardChoice")
            .ToImmutableArray() ?? [];
    }

    // Hover reuses the very plan that clicking would author. The existing
    // detached replay owns purchase success and the replacement stock quote.
    private OperationPreview? ShopPurchasePreview(Context c, PredictorShopOfferSnapshot expected)
    {
        if (!Valid(c, c.Node.Position.NodeId)) return null;
        var version = (c.Revision, _localizationRevision);
        if (_shopPreviewVersion != version)
        {
            _shopPreviewCache.Clear();
            _shopPreviewVersion = version;
        }
        var plan = ShopActionPlan(c, new(PredictorShopActionKind.Buy) { ExpectedOffer = expected });
        string cacheKey = JsonSerializer.Serialize(plan);
        if (_shopPreviewCache.TryGetValue(cacheKey, out var cached)) return cached;
        OperationPreview? preview;
        try
        {
            var result = _session.PreviewPlan(c.Location.ActIndex, c.Location.Point, plan, c.Revision);
            var shop = result?.Shop;
            int purchase = plan.Shop!.Actions.Length - 1;
            var receipt = shop?.ResolvedActions.ElementAtOrDefault(purchase);
            bool applied = shop != null && shop.AppliedActions > purchase && receipt is
                { Action: PredictorShopActionKind.Buy, ExpectedOffer: { } bound } &&
                bound.Slot == expected.Slot && bound.StockRevision == expected.StockRevision &&
                bound.StockRevisionIsOccurrence == expected.StockRevisionIsOccurrence &&
                bound.Kind == expected.Kind && bound.Key == expected.Key;
            var notices = ImmutableArray.CreateBuilder<string>();
            var pending = ImmutableArray.CreateBuilder<string>();
            var sections = ImmutableArray.CreateBuilder<OperationPreviewSection>();
            var source = expected.Key ?? default;
            // Inspect the actual quoted stock; this is not an inferred obtain
            // effect or a comparison of final inventories.
            if (expected.Card is { } offeredCard)
                sections.Add(new(source, [new(OperationPreviewAction.Choose, [], [offeredCard], false)], []));
            else if (expected.Key is { } offeredKey)
                sections.Add(new(source, [new(OperationPreviewAction.Choose, [offeredKey], [], false)], []));
            string stockName = expected.Card is { } namedCard ? _names.Resolve(namedCard.Key, GameContentKind.Card) :
                expected.Key is { } namedKey ? _names.Resolve(namedKey, namedKey.Category == "POTION" ? GameContentKind.Potion : GameContentKind.Relic) : "";
            notices.Add(stockName + " · " + expected.Cost + "G");
            if (!applied)
            {
                pending.AddRange(PendingLines(shop?.PendingCauses ?? result?.PendingCauses ?? []));
                if (pending.Count == 0) pending.Add(M("preview_unavailable"));
            }
            else
            {
                var replacement = shop!.Offers.FirstOrDefault(offer => offer.Slot == expected.Slot);
                // A changed stock occurrence is the replay's positive receipt
                // of restocking, including a replacement with the same model.
                if (replacement is { IsKnown: true, Value: { Stocked: true } value } &&
                    value.StockRevisionIsOccurrence && value.StockRevision > expected.StockRevision)
                {
                    OperationPreviewRow? row = value.Card is { } card
                        ? new(OperationPreviewAction.Restock, [], [card], false)
                        : value.Key is { } key ? new(OperationPreviewAction.Restock, [key], [], false) : null;
                    if (row != null)
                        sections.Add(new(new ModelKey("RELIC", "THE_COURIER"), [row with { Price = value.Cost }], []));
                }
                else
                {
                    pending.AddRange(PendingLines(replacement?.PendingCauses ?? []));
                    if (!shop.RequiredChoices.IsEmpty &&
                        (expected.Key?.Entry == "THE_COURIER" || c.Node.Inventory?.Relics.Any(relic =>
                            relic.IsKnown && !relic.Value.Melted && relic.Value.Key.Entry == "THE_COURIER") == true))
                        pending.Add(M("shop_restock_pending"));
                    pending.AddRange(PendingLines(shop.PendingCauses));
                }
            }
            var unresolved = pending.Distinct().ToImmutableArray();
            notices.AddRange(unresolved);
            var displayNotices = notices.Distinct().ToImmutableArray();
            // Localized display facts are not raw pending reason codes. Keep
            // real uncertainty on the branch and show its explanation once.
            preview = sections.Count == 0 && displayNotices.IsEmpty ? null : new(source, [], [], false,
                [new([], sections.ToImmutable(), [], 1, applied && unresolved.IsEmpty)], [])
                { OptionKey = "SHOP:" + expected.Slot + ":" + expected.StockRevision,
                    Notices = displayNotices, NoticesAreLocalized = true,
                    Style = new(OperationTextMode.GroupRows, OperationCardLayout.Parallel) };
        }
        catch (Exception error)
        {
            RuntimeLog.Fault("inRunMapOptionPreviewFailed=true;stage=detached-shop-plan;node=" +
                c.Node.Position.NodeId + ";slot=" + expected.Slot + ";stockRevision=" + expected.StockRevision +
                ";revision=" + c.Revision, error);
            preview = new(expected.Key ?? default, [], [], false, [], [])
            { OptionKey = "SHOP:" + expected.Slot + ":" + expected.StockRevision,
                Notices = [M("preview_unavailable")], NoticesAreLocalized = true,
                Faults = [new("MapShopPlanPreview", [], error)] };
        }
        _shopPreviewCache[cacheKey] = preview;
        return preview;
    }
}
