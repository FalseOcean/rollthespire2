using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class CombatRewardEditorPrototype
{
    private void RefreshCardSupport(int ascension, int players, int seat)
    {
        _draft.Ascension = ascension;
        bool wheel = false, candy = false;
        bool known = false;
        try
        {
            // Only this seat's authored opening affects the card support model.
            // Event, Shop, potion and unrelated relic-queue targets never enter it.
            SearchQuery opening = players > 1 ? _pickerHost.ExportPartyConditions(seat) : _pickerHost.ExportQuery();
            known = _catalog?.CardSupportModelAvailable == true &&
                TryResolveCardSupportOpening(opening, out wheel, out candy);
        }
        catch (InvalidOperationException)
        {
            // The N editor can be awaiting its captured pool during restoration.
            // Missing authority leaves C editable and does not imply impossibility.
        }
        _draft.CardSupportKnown = known;
        _draft.Wheel = wheel;
        _draft.Candy = candy;
        _draft.SupportContext = $"{_draft.Context}/{ascension}/{known}/{wheel}/{candy}/{_catalog?.CardSupportProofAvailable}";
    }

    internal IReadOnlyList<ModelKey> ResidualCandidates(int index)
    {
        if (index < 0 || index >= _draft.BattleRange || _catalog is null) return [];
        // A best-effort Mod model can inform the player, but cannot prove that a
        // candidate is impossible. Do not interpret unknown probability as zero.
        if (!_draft.CardSupportKnown || !_catalog.CardSupportProofAvailable) return _catalog.CardCandidates;
        EnsureCardSupportCache();
        if (_draft.CandidateResults.TryGetValue(index, out var cached)) return cached;

        ModelKey?[] slots = _draft.Slots.Take(_draft.BattleRange).ToArray();
        slots[index] = null;
        var siblings = slots.Where(k => k.HasValue).Select(k => k!.Value).ToHashSet(ModelKeyComparer.Instance);
        var categories = new Dictionary<(EffectCardRarity Rarity, EffectCardType Type, ModelKey? Sibling), bool>();
        var candidates = new List<ModelKey>();
        foreach (ModelKey key in _catalog.CardCandidates)
        {
            if (!_catalog.CardPickerMetadata.TryGetValue(key, out var metadata))
            { candidates.Add(key); continue; }
            // Unselected identities of the same rarity/type are exchangeable in
            // this support DP. Evaluate each category once, not every card.
            var category = (metadata.Rarity, metadata.CardType, siblings.Contains(key) ? (ModelKey?)key : null);
            if (!categories.TryGetValue(category, out bool possible))
            {
                slots[index] = key;
                categories[category] = possible = SupportsCardTargets(slots);
            }
            if (possible) candidates.Add(key);
        }
        return _draft.CandidateResults[index] = candidates.ToArray();
    }

    internal bool HasImpossibleCardSelection => _draft.CardSupportKnown &&
        !SupportsCardTargets(_draft.Slots.Take(_draft.BattleRange).ToArray());

    internal string CardFeasibilityNotice
    {
        get
        {
            if (_catalog is null) return string.Empty;
            if (!_draft.CardSupportKnown)
                return _english ? "Feasibility is unverified for this opening. All captured candidates remain available."
                    : "当前开局的卡牌可行性尚未验证，保留全部已捕获候选。";
            if (HasImpossibleCardSelection)
                return _catalog.CardSupportProofAvailable
                    ? (_english ? "Card targets conflict at this ascension/opening. Targets are kept; adjust targets or combat range."
                        : "当前进阶与开局下，这组卡牌目标无法同时出现。已保留条件，请调整目标或战斗范围。")
                    : (_english ? "The current model predicts these targets are impossible. Mod rules are unverified; targets remain selectable."
                        : "按当前模型，这组目标预计不可行；模组可能改变规则，目标仍保留且可选择。");
            return _catalog.CardSupportProofAvailable ? string.Empty
                : (_english ? "Card targets checked against the current model; Mod reward rules remain unverified."
                    : "已按当前模型检查卡牌目标；模组奖励规则仍未验证。");
        }
    }

    private void EnsureCardSupportCache()
    {
        string context = $"{_draft.SupportContext}/{_draft.BattleRange}/{_draft.Unordered}/" +
            string.Join(",", _draft.Slots.Take(_draft.BattleRange).Select(k => k?.Serialized ?? "-"));
        if (_draft.CandidateContext == context) return;
        _draft.CandidateContext = context;
        _draft.SupportResults.Clear();
        _draft.CandidateResults.Clear();
    }

    private bool SupportsCardTargets(IReadOnlyList<ModelKey?> slots)
    {
        if (!_draft.CardSupportKnown || _catalog is null) return true;
        EnsureCardSupportCache();
        string key = string.Join(",", slots.Select(k => k?.Serialized ?? "-"));
        if (_draft.SupportResults.TryGetValue(key, out bool possible)) return possible;
        var pool = _catalog.CardCandidates.Where(_catalog.CardPickerMetadata.ContainsKey)
            .Select(k => (k, _catalog.CardPickerMetadata[k].Rarity, _catalog.CardPickerMetadata[k].CardType)).ToArray();
        // This finite support traversal reuses generation and distinct-battle
        // assignment. It never consumes a query probability estimate or changes
        // Search/Exact semantics, and never erases authored conditions.
        return _draft.SupportResults[key] = CombatRewardProbabilityEstimator.CanAuthorCards(
            _draft.Ascension, pool, slots, _draft.Unordered, _draft.Wheel, _draft.Candy);
    }

    private bool TryResolveCardSupportOpening(SearchQuery query, out bool wheel, out bool candy)
    {
        wheel = candy = false;
        if (query.OpeningRoute is not { IsValid: true } route) return true; // Unselected N uses the neutral premise.
        var sources = new List<ModelKey>();
        if (route.RouteRelicKey == BaseGameModelKeys.Relics.NeowsBones)
        {
            var children = query.OpeningRouteRelicRequirement?.RequiredRelicKeys;
            if (children is null || children.Count != 2) return false; // Unspecified companion can change rewards.
            sources.AddRange(children);
        }
        else sources.Add(route.RouteRelicKey);

        ModelKey[] capsules = sources.Where(IsCapsule).ToArray();
        if (capsules.Length > 0)
        {
            var grouped = query.StructuredOpeningEffects.FirstOrDefault(c =>
                c.Kind == NeowStructuredConditionKind.ExactGroupedCapsuleMultiset &&
                c.Scope == NeowStructuredEffectScope.NestedRelics && c.OutputKind == NeowStructuredOutputKind.Relic);
            if (grouped is not null)
            {
                if (grouped.OutputKeys.Count != capsules.Sum(CapsuleSize)) return false;
                sources.AddRange(grouped.OutputKeys);
            }
            else foreach (ModelKey capsule in capsules)
            {
                var targets = query.StructuredOpeningEffects.Where(c => c.SourceRelicKey == capsule &&
                        c.Scope == NeowStructuredEffectScope.NestedRelics && c.OutputKind == NeowStructuredOutputKind.Relic)
                    .SelectMany(c => c.OutputKeys).Distinct(ModelKeyComparer.Instance).ToArray();
                if (targets.Length != CapsuleSize(capsule)) return false; // Never guess hidden capsule contents.
                sources.AddRange(targets);
            }
        }

        foreach (ModelKey source in sources.Distinct(ModelKeyComparer.Instance))
        {
            if (!VanillaRelicRewardEffects.TryGet(_runtime.Profile.ProfileId, source, out var effect)) return false;
            if ((effect.OnObtainCapabilities & (RelicOnObtainRewardEffects.ChangesCardRewardPool | RelicOnObtainRewardEffects.Unknown)) != 0)
                return false;
            if ((effect.OnObtainCapabilities & RelicOnObtainRewardEffects.NestedRelicObtain) != 0 && !capsules.Contains(source))
                return false;
            if (OpeningCombatRewardImpactAdapterRegistry.TryResolve(_runtime.Profile.ProfileId, source, out var adapter))
            {
                wheel |= adapter.Operations.Any(o => o.Kind == OpeningCombatRewardImpactOperationKind.AddCardReward);
                candy |= adapter.Operations.Any(o => o.Kind == OpeningCombatRewardImpactOperationKind.AddPowerCardEveryOtherCombat);
                continue;
            }
            const HeldNormalCombatRewardEffects relevant = HeldNormalCombatRewardEffects.ChangesCardRewardPool |
                HeldNormalCombatRewardEffects.ChangesCardRewardCount | HeldNormalCombatRewardEffects.ChangesCardRewardOptions |
                HeldNormalCombatRewardEffects.AddsRewardEntries | HeldNormalCombatRewardEffects.RemovesRewardEntries |
                HeldNormalCombatRewardEffects.ReplacesRewardEntries | HeldNormalCombatRewardEffects.AddsCardRewardAlternative |
                HeldNormalCombatRewardEffects.UnknownModHook;
            if ((effect.HeldCapabilities & relevant) != 0) return false;
        }
        return true;
    }

    private static bool IsCapsule(ModelKey key) => key == BaseGameModelKeys.Relics.SmallCapsule || key == BaseGameModelKeys.Relics.LargeCapsule;
    private static int CapsuleSize(ModelKey key) => key == BaseGameModelKeys.Relics.SmallCapsule ? 1 : 2;
}
