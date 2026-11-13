using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// A pristine numerical mismatch is a necessary-condition proof only when the
// real opening observation owns an applicable neutral continuation. No result,
// witness or mutable state is transported through ABI1 by this immutable input.
internal sealed class CombatRewardNeutralWitness
{
    internal const string Revision = ".RealNeutralWitness.20261102.v1";
    internal const string ShaderGuard = """


bool c_keep_root_proven(uint64_t root){
    // A real opening proof is needed only after a completed numerical mismatch.
    // Any original numerical failure still has its failure header and cause.
    if(c_numeric_keep_root(root))return true;
    if(plan_meta.values[51]==3u)return true;
    uint first,second,curse;
    if(!cr_replay_opening(root,first,second,curse))return cr_family_numeric_failure();
    if(first>=30u||second>=30u||curse>=30u)return cr_family_numeric_failure();
    uint offered=(1u<<first)|(1u<<second)|(1u<<curse),neutral=plan_meta.values[52];
    bool proven=plan_meta.values[51]==2u?(offered&~neutral)==0u:(offered&neutral)!=0u;
    return !proven;
}
""";
    private readonly NeowReplayPlan? _identity;
    internal uint NeutralMask { get; }
    internal bool RequiresAllOffers { get; }
    internal string Issue { get; } = "";
    private readonly bool _authoredOffersGuaranteeNeutral;
    internal string Remainder => Issue.Length != 0 ? Issue :
        Available && _authoredOffersGuaranteeNeutral ? "" : "UnprovenRealNeutralOpeningRetainedForExact";
    internal bool Available => _identity is not null && Issue.Length == 0;
    // Initial curse draw, up to three binary draws and at most sixteen swaps.
    // This is a work envelope on numerical failures, not a measured timing.
    internal const int MaximumIdentityDraws = 20;

    internal CombatRewardNeutralWitness(SearchModel request, Beta110FastEffectCatalog catalog)
    {
        RequiresAllOffers = request.Authority.PlayersCount > 1;
        var effects = request.Authority.EffectAuthority;
        if (effects is null || !effects.HasExactFoundation || effects.CapturedProfileId != request.ProfileId)
        { Issue = "RealNeutralOpeningEffectFoundationUnavailable"; return; }
        if (!SourceAuthorityRules.SupportsExactIdentity(effects.SourceAuthority) ||
            effects.Completeness != SnapshotCompleteness.Complete)
        { Issue = "RealNeutralOpeningEffectAuthorityNotExact"; return; }
        var e = request.Evaluation;
        // An existential neutral offer must not borrow a different route's N
        // obligations. These uncommon unpinned authored shapes stay with Exact.
        if (e.RequireNeowsBones || !e.BonesRelics.IsEmpty || e.RequiredBonesCombination.Count != 0 ||
            e.RequiredBonesAcquisitionOrder.Count != 0 || e.RequireSmallCapsule || e.RequireLargeCapsule ||
            !e.CapsuleContainedRelics.IsEmpty || e.RequireWhetstone || e.RequireWarPaint ||
            e.RequiredFinalCurse.HasValue || e.BannedFinalCurses.Count != 0 ||
            e.EffectOutputConditions.Any(c => !c.IsEmpty) || e.StructuredNeowEffects.Any(c => !c.IsEmpty) ||
            e.Preset != NeowSearchPreset.None)
        { Issue = "UnpinnedRouteScopedOpeningProofUnavailable"; return; }
        var authority = NeowNumericCompilation.BuildAuthority(request.CompiledSearch.Context, catalog, false);
        if (!authority.IdentityAuthorityExact)
        { Issue = "RealNeutralOpeningIdentityAuthorityUnavailable"; return; }
        // Reuse N's identity observer, without compiling or borrowing N's authored
        // predicates. Unknown N keys cannot turn optional C proof preparation into
        // a query-wide failure. This donor never observes a Bones pair or a bag.
        _identity = new(authority, NeowSearchFilter.Empty, Beta110FastDomain.None, [], [], [],
            0, 0, 0, 0, 0, 0, 255, 255, 255, false, false, false, []);
        for (byte id = 0; id < 30; id++)
            if (HasUnconditionalNeutralProducer(request.ProfileId, Beta110FastRelicCatalog.KeyOf(id)))
                NeutralMask |= 1u << id;
        _authoredOffersGuaranteeNeutral = WhitelistGuaranteesNeutral(request);
    }

    private bool WhitelistGuaranteesNeutral(SearchModel request)
    {
        if (request.Evaluation.NeowOfferWhitelist is not { AllowedKeys.Count: > 0 } whitelist ||
            whitelist.AllowedKeys.Any(key => !Beta110FastRelicCatalog.TryGetId(key, out _))) return false;
        bool hasMenu = false, everyMenuProven = true;
        // Visit the complete bounded menu space once during request preparation.
        // Shared identity rules own eligibility, curse conflicts, binary exclusions
        // and the two distinct positive offers. A mixed whitelist can still force
        // a neutral choice in every solo menu; party requires every choice neutral.
        // Do not infer coverage from an empty space or discard unknown authored keys.
        bool complete = ModernNeowIdentityRules.TryVisitOfferSpace(request.Authority, (first, second, curse, _) =>
        {
            if (!NeowOfferWhitelist.Matches(whitelist, first, second, curse)) return;
            hasMenu = true;
            everyMenuProven &= Beta110FastRelicCatalog.TryGetId(first, out byte a) &&
                Beta110FastRelicCatalog.TryGetId(second, out byte b) &&
                Beta110FastRelicCatalog.TryGetId(curse, out byte c) && AllowsRejection(a, b, c);
        });
        // Coverage only changes the declared remainder. CPU/GPU per-root proof,
        // numerical replay and Exact authority remain unchanged.
        return complete && hasMenu && everyMenuProven;
    }

    // These specific producer branches in NeowEffectProjectionEngine execute
    // with no deck/pool/choice dependency: direct deterministic results (including
    // Sacrifice's explicit generated-potion release contract), or its explicit
    // ProductExactNoVisibleEffects branch. Do not widen this from a generic
    // capability/implementation label; other neutral relics may lack observations.
    internal static bool HasUnconditionalNeutralProducer(Compatibility.RuntimeProfileId profile, ModelKey key)
    {
        bool producer = key == BaseGameModelKeys.Relics.CursedPearl ||
            key == BaseGameModelKeys.Relics.DowsingRod || key == BaseGameModelKeys.Relics.GoldenPearl ||
            key == BaseGameModelKeys.Relics.NeowsTorment || key == BaseGameModelKeys.Relics.NutritiousOyster ||
            key == BaseGameModelKeys.Relics.NeowsSacrifice || key == BaseGameModelKeys.Relics.BoomingConch ||
            key == BaseGameModelKeys.Relics.FishingRod || key == BaseGameModelKeys.Relics.WingedBoots ||
            key == BaseGameModelKeys.Relics.StoneHumidifier || key == BaseGameModelKeys.Relics.LavaRock;
        return producer && VanillaRelicRewardEffects.TryGet(profile, key, out var facts) &&
            facts.IsHeldNeutralForNormalCombatReward && facts.NestedOnObtainPreservesRewardContinuation;
    }

    internal bool AllowsRejection(ulong root)
    {
        if (!Available) return false;
        var offered = NeowFamilyReplay.Observe(root, _identity!);
        return AllowsRejection(offered.First, offered.Second, offered.Curse);
    }

    internal bool AllowsRejection(byte first, byte second, byte curse)
    {
        if (!Available || first >= 30 || second >= 30 || curse >= 30) return false;
        uint offered = (1u << first) | (1u << second) | (1u << curse);
        // Solo Exact selects neutral continuations from the whole offer. Party
        // Visit evaluates each choice separately and retains unresolved choices;
        // a neutral mismatch cannot erase an excluded choice's original Unknown.
        return RequiresAllOffers ? (offered & ~NeutralMask) == 0 : (offered & NeutralMask) != 0;
    }
}
