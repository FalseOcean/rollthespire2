using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// N-private constants conditioned on authored Capsule results. R still verifies
// their presence. No search for an unrequested W/WP and no per-root deck state.
internal sealed record NeowAuthoredUpgradeContinuation(int[] Advances)
{
    internal const int PriorCount = 33;
    internal static int Draws(int eligible) => Math.Max(eligible - 1, 0);
    internal int Advance(byte source, byte prior) => source is Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule
        ? Advances[(source == Beta110FastRelicCatalog.SmallCapsule ? 0 : PriorCount) + (prior == 255 ? 32 : prior)] : 0;

    internal static NeowAuthoredUpgradeContinuation? Compile(ExactSearchExecutionRequest request)
    {
        var e = request.Evaluation;
        var requirements = new Dictionary<byte, (bool W, bool WP)>();
        bool unbound = false;
        foreach (var row in e.EffectOutputConditions.Where(c => !c.IsEmpty && NeowReplayPlan.IsCapsule(c.SourceRelicKey)))
        {
            bool w = row.OutputKeys.All.Contains(BaseGameModelKeys.OrdinaryRelics.Whetstone);
            bool wp = row.OutputKeys.All.Contains(BaseGameModelKeys.OrdinaryRelics.WarPaint);
            if (Beta110FastRelicCatalog.TryGetId(row.SourceRelicKey, out byte source) && (w || wp))
            {
                var old = requirements.GetValueOrDefault(source);
                requirements[source] = (old.W || w, old.WP || wp);
            }
            if (row.OutputKeys.Any.Contains(BaseGameModelKeys.OrdinaryRelics.Whetstone) ||
                row.OutputKeys.Any.Contains(BaseGameModelKeys.OrdinaryRelics.WarPaint)) unbound = true;
        }
        foreach (var row in e.StructuredNeowEffects.Where(c => !c.IsEmpty && NeowReplayPlan.IsCapsule(c)))
        {
            bool w = row.OutputKeys.Contains(BaseGameModelKeys.OrdinaryRelics.Whetstone);
            bool wp = row.OutputKeys.Contains(BaseGameModelKeys.OrdinaryRelics.WarPaint);
            if (!w && !wp) continue;
            if (!Beta110FastRelicCatalog.TryGetId(row.SourceRelicKey, out byte source) ||
                source is not (Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule)) { unbound = true; continue; }
            var old = requirements.GetValueOrDefault(source);
            requirements[source] = (old.W || w, old.WP || wp);
        }
        bool legacyW = e.RequireWhetstone || e.CapsuleContainedRelics.All.Contains(BaseGameModelKeys.OrdinaryRelics.Whetstone);
        bool legacyWP = e.RequireWarPaint || e.CapsuleContainedRelics.All.Contains(BaseGameModelKeys.OrdinaryRelics.WarPaint);
        if (legacyW || legacyWP)
        {
            var sources = e.RequiredBonesCombination.Concat(e.RequiredBonesAcquisitionOrder)
                .Concat(e.NeowRoute is null ? [] : new[] { e.NeowRoute.RouteRelicKey })
                .Concat(e.StructuredNeowEffects.Select(c => c.SourceRelicKey)).Where(NeowReplayPlan.IsCapsule).Distinct().ToArray();
            if (sources.Length == 1 && Beta110FastRelicCatalog.TryGetId(sources[0], out byte source))
            {
                var old = requirements.GetValueOrDefault(source);
                requirements[source] = (old.W || legacyW, old.WP || legacyWP);
            }
            else unbound = true;
        }
        if (e.CapsuleContainedRelics.Any.Contains(BaseGameModelKeys.OrdinaryRelics.Whetstone) ||
            e.CapsuleContainedRelics.Any.Contains(BaseGameModelKeys.OrdinaryRelics.WarPaint)) unbound = true;
        if (requirements.Count == 0 && !unbound) return null;
        var result = new int[2 * PriorCount];
        // An authored but source-ambiguous requirement cannot identify which
        // Capsule arrival to advance. Preserve the root for Exact instead.
        if (unbound) { Array.Fill(result, -1); return new(result); }
        var authority = request.Authority.EffectAuthority;
        bool valid = request.Authority.CanUseCurrentModel && request.Authority.NoRunModifiers == true &&
            request.Authority.IsAuditedBeta111SourceContext &&
            authority?.HasExactDeck == true;
        foreach (var (source, required) in requirements)
        for (int prior = 0; prior < PriorCount; prior++)
        {
            int at = (source == Beta110FastRelicCatalog.SmallCapsule ? 0 : PriorCount) + prior;
            result[at] = -1;
            if (!valid) continue;
            int attack = authority!.OrderedDeck!.Count(c => c.CardType == EffectCardType.Attack && c.CanUpgrade);
            int skill = authority.OrderedDeck!.Count(c => c.CardType == EffectCardType.Skill && c.CanUpgrade);
            if (!TryDelta((byte)(prior == 32 ? 255 : prior), e.StructuredNeowEffects, authority, ref attack, ref skill)) continue;
            result[at] = (required.W ? Draws(attack) : 0) + (required.WP ? Draws(skill) : 0);
        }
        return new(result);
    }

    private static bool TryDelta(byte prior, IReadOnlyList<NeowStructuredEffectSearchCondition> conditions,
        NeowEffectAuthoritySnapshot a, ref int attack, ref int skill)
    {
        if (prior == 255) return true;
        ModelKey key = BaseGameModelKeys.Relics.AllNeow.FirstOrDefault(k =>
            Beta110FastRelicCatalog.TryGetId(k, out byte id) && id == prior);
        if (!key.IsValid) return false;
        ModelKey[] cards = NeowChoiceCommitment.Cards(conditions, key);
        if (NeowChoiceCommitment.IsOptionalCardOffer(key))
        {
            if (cards.Length > (prior == Beta110FastRelicCatalog.Kaleidoscope ? 2 : 1)) return false;
        }
        else if (prior == Beta110FastRelicCatalog.ArcaneScroll) { if (cards.Length != 1) return false; }
        else if (prior == Beta110FastRelicCatalog.LeafyPoultice)
        {
            if (cards.Length != 2) return false;
            var removed = a.OrderedDeck!.Where(c => c.IsBasic && (c.IsStrike || c.IsDefend))
                .GroupBy(c => c.IsStrike).Select(g => g.First()).ToArray();
            if (removed.Length != 2) return false;
            attack -= removed.Count(c => c.CardType == EffectCardType.Attack && c.CanUpgrade);
            skill -= removed.Count(c => c.CardType == EffectCardType.Skill && c.CanUpgrade);
        }
        else if (prior == Beta110FastRelicCatalog.ScrollBoxes)
        {
            if (cards.Length == 0 && a.ClawKey is ModelKey claw && conditions.Any(c => c.SourceRelicKey == key &&
                    c.Kind == NeowStructuredConditionKind.SpecialOffer && c.SpecialOffer == NeowSpecialOfferKind.ScrollBoxesTripleClaw))
                cards = [claw, claw, claw];
            if (cards.Length != 3) return false;
        }
        else if (prior == Beta110FastRelicCatalog.NeowsTalisman)
        {
            var s = a.OrderedDeck!.LastOrDefault(c => c.IsBasic && c.IsStrike);
            var d = a.OrderedDeck!.LastOrDefault(c => c.IsBasic && c.IsDefend);
            if (s?.CanUpgrade == true) attack--;
            if (d?.CanUpgrade == true) skill--;
            return true;
        }
        else if (prior == Beta110FastRelicCatalog.NeowsTorment)
        {
            // Fixed vanilla Neow's Fury: Attack, MaxUpgradeLevel=1; mandatory
            // add, not an optional pick. Its Ancient card is outside reward pools.
            attack++;
            return true;
        }
        else if (prior is Beta110FastRelicCatalog.NewLeaf or Beta110FastRelicCatalog.PreciseScissors or
                 Beta110FastRelicCatalog.PrecariousShears or Beta110FastRelicCatalog.Pomander or
                 Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule) return false;
        else return prior <= Beta110FastRelicCatalog.StoneHumidifier;
        var pool = (a.CharacterRewardPool ?? []).Concat(a.ColorlessRewardPool ?? []).Concat(a.TransformPool ?? [])
            .Concat((a.OtherCharacterPools ?? []).SelectMany(p => p.Cards)).ToArray();
        foreach (ModelKey target in cards)
        {
            var card = pool.FirstOrDefault(c => c.CardKey == target);
            if (card is null || !card.BehaviorMetadataExact) return false;
            if (card.CanUpgrade && card.CardType == EffectCardType.Attack) attack++;
            if (card.CanUpgrade && card.CardType == EffectCardType.Skill) skill++;
        }
        return attack >= 0 && skill >= 0;
    }
}
