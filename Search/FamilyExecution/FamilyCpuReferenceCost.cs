using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// Reference evidence only. Shape admission lives in each Family. No CPU local
// ratio, core-count extrapolation, observed-population learning or GPU store.
internal static class FamilyCpuReferenceCost
{
    private const string LegacyProvenance = "CPU_REFERENCE_COST_ANCHORS_20260912.json;review=0c96ec6;donor=e5fe715;reference=i9-13900HX;Release-net9;root=ByteDenseCarry;window=65536;metric=ns/ActualStageInput;boundary=CanonicalAbi1Ready;includes=HashMatcherPartitionJoinOrderedAbi1;quote=RoundedSlowEnd;LocalCpuRatio=ResolvedPerInvocation";
    internal const string Provenance = "CPU_REFERENCE_COST_ANCHORS_20261013.json;baseline=c390dea;reference=i9-13900HX;Release-net9;root=ByteDenseCarryOrCompactFullDecode;window=65536;metric=ns/ActualStageInput;boundary=CanonicalAbi1Ready;includes=HashMatcherPartitionJoinOrderedAbi1;sample=2x10000ms;quote=RoundedSlowEnd;LocalCpuRatio=ResolvedPerInvocation";
    internal static double? ReferenceNs(string shape) => shape switch {
        "S.ThreeUncommonSlots.P1"=>24, "S.ThreeUncommonSlots.P16"=>6,
        "C.ThreeOrderedCommonPotions.P1"=>171, "C.ThreeOrderedCommonPotions.P16"=>38,
        "R.CommonFirst3All3.P1"=>258, "N.BonesIdentityTwoSource.P1"=>29,
        "R.DirectSmallC.P1"=>268, "R.DirectLargeUR.P1"=>130, "R.DirectLargeUR.P8"=>56,
        "R.ShopOrdered3.P1"=>233, "R.ShopOrdered3.P8"=>54, _=>null };
    internal static string? Revision(string shape) => shape.Split(".P")[0] switch {
        "N.BonesIdentityTwoSource" => "N.Neow.Cpu.IdentityPair.20260912.v1",
        "S.ThreeUncommonSlots" => "S.MerchantShopColorless.Cpu.Slot.20261013.v2",
        "C.ThreeOrderedCommonPotions" => "C.CombatReward.Cpu.AuthoredPrefix.20261013.v2",
        "R.CommonFirst3All3" => "R.Relic.Cpu.TrackedPositions.20261013.v2",
        "R.ShopOrdered3" => "R.Relic.Cpu.ShopBackPrefix.20260912.v1",
        "R.DirectSmallC" or "R.DirectLargeUR" => "R.Relic.Cpu.CapsuleTargets.20261013.v2", _=>null };
    internal static bool Authority(ExactSearchExecutionRequest r, bool equivalentIdentityWork) => r.ProfileId == RuntimeProfileId.Beta111 &&
        r.Authority.CanUseCurrentModel && r.Authority.PlayersCount == 1 && r.Ascension == 10 &&
        (equivalentIdentityWork || r.Authority.Character.CharacterKey == BaseGameModelKeys.Characters.Ironclad) &&
        r.Authority.AllCharacterCardPoolsUnlocked == true;
    // Old measured/model R envelopes belong to these physical bodies only.
    // A new matcher or scheduler window stays executable but needs new pricing.
    internal static bool RelicIdentitySupported(string? physicalRevision, int capacity, int workers, bool compact)
    {
        if (capacity != 65536 || workers is < 1 or > 32 || physicalRevision is null) return false;
        string suffix = $".RootMode2.P{workers}.B65536.{(compact ? "CompactAbi1" : "Dense")}.CanonicalAbi1Ready";
        return compact
            ? physicalRevision == "R.Relic.Cpu.CapsuleTargets.20261013.v2" + suffix
            : physicalRevision == "R.Relic.Cpu.TrackedPositions.20261013.v2" + suffix ||
              physicalRevision == "R.Relic.Cpu.ShopBackPrefix.20260912.v1" + suffix;
    }
    internal static FamilyPhysicalQuote? Quote(ExactSearchExecutionRequest r, FamilyPhysicalQuoteRequest g,
        string family, string shape, int workers, string range, bool compact = false, bool equivalentIdentityWork = false,
        string? cpuPhysicalRevision = null, int cpuWindowCapacity = 0)
    {
        if (!Authority(r,equivalentIdentityWork) || !FamilyPhysicalQuote.AdmittedRequest(g) || g.PrivateInput || g.PrivateOutput ||
            g.CompactInput != compact) return null;
        if (family == "R.Relic" && (!RelicIdentitySupported(cpuPhysicalRevision, cpuWindowCapacity, workers, compact) ||
            cpuPhysicalRevision != Revision(shape) + $".RootMode2.P{workers}.B65536.{(compact ? "CompactAbi1" : "Dense")}.CanonicalAbi1Ready"))
            return null;
        // Dense anchors own full 65536-root invocations. Compact anchors are
        // further bounded by the owning Family and its passed Coverage check.
        if(ReferenceNs(shape+".P"+workers) is not double ns)return null;
        double fixedMs=0;
        if (!compact && g.MeanInputPopulation < 65536)
        {
            // Targeted 1K/4K/16K/32K/64K canonical measurements, not worker scaling.
            bool linearP1=workers==1 && shape is "N.BonesIdentityTwoSource" or "S.ThreeUncommonSlots" or "R.CommonFirst3All3" or "R.ShopOrdered3";
            bool measuredS16=workers==16 && shape=="S.ThreeUncommonSlots";
            if(g.MeanInputPopulation<1024 || !(linearP1||measuredS16))return null;
            if(measuredS16)fixedMs=.04;
        }
        return new(family, shape + ".P" + workers, ns, 65536, null,
            (shape is "N.BonesIdentityTwoSource" or "R.ShopOrdered3" ? LegacyProvenance : Provenance) +
                ";workers=" + workers + ";observedRange=" + range +
                (family == "R.Relic" ? ";CpuPhysicalRevision=" + cpuPhysicalRevision : ""),
            OutputElementBytes: 8, OutputAlreadyOrdered: true, PublicTransportClass: "CpuOrderedAbi1") { FixedWindowMilliseconds=fixedMs };
    }
}
