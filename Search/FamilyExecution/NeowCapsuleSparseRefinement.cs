using System.Numerics;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Seed;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

internal readonly record struct NeowCapsuleSparseOutcome(bool Keep, string Reason, int Observations);
internal readonly record struct NeowCapsuleSparseObservation(bool Available, uint FirstMask, uint SecondMask, string Issue);

// Immutable, optional N observation. This first refinement only proves zero
// Capsule Niche consumption. Nonzero outcomes retain the validated union; no
// deck reconstruction, C held-fact premise, or result-confidence authority.
internal sealed class NeowCapsuleSparseRefinement
{
    internal const string Revision = ".CapsuleSparsePublicCpu.20261101.v1";
    internal const string PrivateRevision = ".SparseRefinementNotApplied.PrivateUnion.20261101.v1";
    private static readonly ulong UpFrontHash = XxHash64.Hash("up_front"u8,0);
    private static readonly ulong NicheHash = XxHash64.Hash("niche"u8,0);
    private readonly NeowReplayPlan _n, _arrival;
    private readonly int[] _lengths, _lanes;
    private readonly int _wBucket, _wpBucket, _wPosition, _wpPosition;
    private readonly uint _wRarity, _wpRarity;
    internal bool Ready { get; }
    internal string Issue { get; }

    private NeowCapsuleSparseRefinement(NeowReplayPlan n,string issue,int[] lengths,int[] lanes,
        int wBucket,int wpBucket,int wPosition,int wpPosition,uint wRarity,uint wpRarity)
    {
        _n=n; _arrival=n with { StructuredConditions=[], EnabledDomains=Beta110FastDomain.None };
        Issue=issue; Ready=issue.Length==0; _lengths=lengths; _lanes=lanes;
        _wBucket=wBucket; _wpBucket=wpBucket; _wPosition=wPosition; _wpPosition=wpPosition;
        _wRarity=wRarity; _wpRarity=wpRarity;
    }
    internal static string CaptureSelection() => (Environment.GetEnvironmentVariable("RT2_CAPSULE_SPARSE_REFINE") ?? "auto") switch {
        "" or "auto" => "auto", "off" => "off", "on" => "on",
        var value=>throw new InvalidOperationException("N.UnknownCapsuleSparseRefineMode:"+value)
    };
    internal static bool CaptureMode(NeowReplayPlan? plan=null) => ShouldApply(CaptureSelection(),plan);
    internal static bool ShouldApply(string mode,NeowReplayPlan? plan) => mode switch {
        "off"=>false, "on"=>true, "auto"=>plan is not null && AutoApplies(plan),
        _=>throw new InvalidOperationException("N.UnknownCapsuleSparseRefineMode:"+mode)
    };
    private static bool AutoApplies(NeowReplayPlan plan)
    {
        if(plan.Authority.PlayersCount!=1 || !plan.Bones || plan.CapsuleNicheContinuation is null) return false;
        ulong pair;
        if(plan.First!=255 && plan.Second!=255) {
            if(plan.First==plan.Second) return false;
            pair=Beta110FastRelicCatalog.Bit(plan.First)|Beta110FastRelicCatalog.Bit(plan.Second);
        } else pair=plan.BonesAll;
        if(BitOperations.PopCount(pair)!=2) return false;
        ulong small=Beta110FastRelicCatalog.Bit(Beta110FastRelicCatalog.SmallCapsule);
        return pair==(small|Beta110FastRelicCatalog.Bit(Beta110FastRelicCatalog.NewLeaf)) &&
                (plan.EnabledDomains&Beta110FastDomain.NewLeafTransform)!=0 &&
                plan.StructuredConditions.Any(c=>c.SourceRelicId==Beta110FastRelicCatalog.NewLeaf &&
                    c.Kind==Beta110FastStructuredConditionKind.NewLeafTransform) ||
            pair==(small|Beta110FastRelicCatalog.Bit(Beta110FastRelicCatalog.LargeCapsule)) && plan.HasFinalCurseFastProjection;
    }
    internal static NeowCapsuleSparseRefinement Compile(SearchModel request,NeowReplayPlan n)
    {
        NeowCapsuleSparseRefinement Unknown(string issue) => new(n,issue,[],[],0,0,0,0,0,0);
        if(n.Authority.PlayersCount!=1 || !n.DirectNestedVanilla111 || request.Authority.NoRunModifiers!=true)
            return Unknown("CapsuleObservationModelAuthorityUnavailable");
        if(n.ExactOnly.Length!=0) return Unknown("CapsuleObservationNPrerequisitesIncomplete");
        if(!NeowCapsuleNicheContinuation.TryInitialCounts(request,out _,out _))
            return Unknown("CapsuleObservationDeckAuthorityUnavailable");
        if(request.Authority.UsesBestEffortModel || request.Authority.WorldAuthority?.Beta109Generation?.NoUnknownHooksOrModifiers!=true)
            return Unknown("CapsuleObservationWorldAuthorityUnverified");
        if(!RelicFamilyPlanCompiler.TryCompilePool(request,out var pool,out var dense,out _,out string issue))
            return Unknown("CapsuleObservationPoolUnavailable:"+issue);
        if(!dense.TryGetValue(BaseGameModelKeys.OrdinaryRelics.Whetstone,out ushort w) ||
            !dense.TryGetValue(BaseGameModelKeys.OrdinaryRelics.WarPaint,out ushort wp))
            return Unknown("CapsuleObservationUpgradeIdentityMissing");
        int wb=-1,wpb=-1,wpos=-1,wppos=-1;
        uint wr=0,wpr=0;
        int[] lanes=new int[3];
        for(int b=0;b<pool.BucketCount;b++) {
            if(pool.BucketScopes[b]!=1 || pool.BucketKinds[b] is <1 or >3) continue;
            if(pool.DrawDirections[b]!=1) return Unknown("CapsuleObservationDrawDirectionUnsupported");
            lanes[pool.BucketKinds[b]-1]=pool.BucketLengths[b];
            int at=Array.IndexOf(pool.DenseRelicIds,w,pool.BucketOffsets[b],pool.BucketLengths[b]);
            if(at>=0) { wb=b; wpos=at-pool.BucketOffsets[b]; wr=pool.BucketKinds[b]; }
            at=Array.IndexOf(pool.DenseRelicIds,wp,pool.BucketOffsets[b],pool.BucketLengths[b]);
            if(at>=0) { wpb=b; wppos=at-pool.BucketOffsets[b]; wpr=pool.BucketKinds[b]; }
        }
        if(wb<0 || wpb<0) return Unknown("CapsuleObservationUpgradePositionMissing");
        return new(n,"",(int[])pool.BucketLengths.Clone(),lanes,wb,wpb,wpos,wppos,wr,wpr);
    }

    internal bool PrimaryMatches(ulong root)
    {
        var observation=NeowFamilyReplay.Observe(root,_n);
        return observation.IdentityPass && Primary(root,observation,out _);
    }
    private bool Primary(ulong root,NeowReplayObservation observation,out bool unknown)
    {
        unknown=false;
        foreach(var route in NeowFamilyReplay.Routes(_n,observation)) {
            if(_n.CapsuleNicheContinuation is not { } c) {
                if(NeowFamilyReplay.Route(root,_n,route.First,route.Second,observation.RewardsAfterBones)) return true;
                continue;
            }
            int da=NeowCapsuleNicheContinuation.ObservedAfterFirst(_n,route.Second) ? c.Advances(route.First,255)[0] : 0;
            int db=_n.HasFinalCurseFastProjection ? c.Advances(route.Second,route.First)[0] : 0;
            if(!NeowSingleplayerReplay.RouteAtArrival(root,_n,route.First,route.Second,observation.RewardsAfterBones,da,db)) continue;
            unknown=da<0 || db<0;
            return true;
        }
        return false;
    }
    internal NeowCapsuleSparseOutcome Evaluate(ulong root)
    {
        var observation=NeowFamilyReplay.Observe(root,_n);
        if(!observation.IdentityPass) return new(false,"Rejected",0);
        if(Primary(root,observation,out bool unknown)) return new(true,unknown ? "PrimaryUnknown" : "PrimaryPass",0);
        int checks=0;
        foreach(var route in NeowFamilyReplay.Routes(_n,observation)) {
            // A failed N route cannot protect a different route merely because
            // its unobserved Capsule happens to contain a nonzero upgrade relic.
            if(!NeowFamilyReplay.Route(root,_n,route.First,route.Second,observation.RewardsAfterBones)) continue;
            checks++;
            var actual=ObserveRoute(root,route.First,route.Second,observation.RewardsAfterBones);
            if(!actual.Available) return new(true,"ObservationUnavailable",checks);
            if((actual.FirstMask|actual.SecondMask)!=0) return new(true,"NonzeroRetained",checks);
            if(NeowSingleplayerReplay.RouteAtArrival(root,_n,route.First,route.Second,observation.RewardsAfterBones,0,0))
                return new(true,"ZeroPass",checks);
        }
        return new(false,"Rejected",checks);
    }

    internal NeowCapsuleSparseObservation ObserveRoute(ulong root,byte first,byte second,Beta110FastRng rewardsAfterBones)
    {
        if(!Ready) return new(false,0,0,Issue);
        int w=_wPosition,wp=_wpPosition;
        var upfront=new Beta110FastRng(unchecked(root+UpFrontHash));
        // Every shared/player bucket preceding the observed positions consumes
        // its full shuffle. No separately initialized per-Capsule bag exists.
        for(int b=0;b<=Math.Max(_wBucket,_wpBucket);b++)
        for(int remaining=_lengths[b];remaining>1;remaining--) {
            int selected=upfront.NextInt(remaining),tail=remaining-1;
            if(b==_wBucket) Swap(ref w,selected,tail);
            if(b==_wpBucket) Swap(ref wp,selected,tail);
        }
        var rewards=_n.Bones ? rewardsAfterBones : new Beta110FastRng(unchecked(root+NeowFamilyReplay.RewardsHash));
        var niche=new Beta110FastRng(unchecked(root+NicheHash));
        Beta110FastRng transformations=default,potions=default;
        Span<int> consumed=stackalloc int[3]; consumed.Clear();
        uint firstMask=0,secondMask=0;
        int remainingCapsules=(IsCapsule(first)?1:0)+(IsCapsule(second)?1:0);
        for(int pickup=0;pickup<2;pickup++) {
            byte source=pickup==0 ? first : second;
            if(IsCapsule(source)) {
                uint mask=0;
                int draws=source==Beta110FastRelicCatalog.SmallCapsule ? 1 : 2;
                for(int i=0;i<draws;i++) {
                    float roll=rewards.NextFloat();
                    int lane=roll<.5f ? 0 : roll<.83f ? 1 : 2;
                    while(lane<3 && consumed[lane]>=_lanes[lane]) lane++;
                    if(lane==3) continue; // Vanilla Circlet fallback has no Niche hook.
                    int ordinal=consumed[lane]++;
                    if((uint)(lane+1)==_wRarity && ordinal==w) mask|=1;
                    if((uint)(lane+1)==_wpRarity && ordinal==wp) mask|=2;
                }
                if(pickup==0) firstMask=mask; else secondMask=mask;
                if(--remainingCapsules==0) break;
            }
            else if(source!=255 && remainingCapsules>0 && !NeowLocalOperators.ExecuteRelic(source,true,_arrival,
                _n.Authority.EffectCatalog,true,false,false,false,ref rewards,ref niche,ref transformations,ref potions,Span<byte>.Empty))
                return new(false,0,0,"CapsuleRewardsArrivalUnavailable:Source"+source);
        }
        return new(true,firstMask,secondMask,"");
    }
    private static bool IsCapsule(byte source) => source is Beta110FastRelicCatalog.SmallCapsule or Beta110FastRelicCatalog.LargeCapsule;
    private static void Swap(ref int position,int selected,int tail)
    { if(position==selected) position=tail; else if(position==tail) position=selected; }
}
