using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.FamilyExecution;

// Reference evidence only. Shape admission lives in each Family. No CPU local
// ratio, core-count extrapolation, observed-population learning or GPU store.
internal static class FamilyCpuReferenceCost
{
    internal const string Provenance = "CPU_REFERENCE_COST_ANCHORS_20260912.json;review=0c96ec6;donor=e5fe715;reference=i9-13900HX;Release-net9;root=ByteDenseCarry;window=65536;metric=ns/ActualStageInput;boundary=CanonicalAbi1Ready;includes=HashMatcherPartitionJoinOrderedAbi1;quote=RoundedSlowEnd;LocalCpuRatio=ResolvedPerInvocation";
    internal static double? ReferenceNs(string shape) => shape switch {
        "S.ThreeUncommonSlots.P1"=>55, "S.ThreeUncommonSlots.P16"=>27,
        "C.ThreeOrderedCommonPotions.P1"=>1305, "C.ThreeOrderedCommonPotions.P16"=>153,
        "R.CommonFirst3All3.P1"=>511, "N.BonesIdentityTwoSource.P1"=>29,
        "R.DirectSmallC.P1"=>605, "R.DirectLargeUR.P1"=>259, "R.DirectLargeUR.P8"=>106,
        "R.ShopOrdered3.P1"=>233, "R.ShopOrdered3.P8"=>54, _=>null };
    internal static string? Revision(string shape) => shape.Split(".P")[0] switch {
        "N.BonesIdentityTwoSource" => "N.Neow.Cpu.IdentityPair.20260912.v1",
        "S.ThreeUncommonSlots" => "S.MerchantShopColorless.Cpu.Slot.20260912.v1",
        "C.ThreeOrderedCommonPotions" => "C.CombatReward.Cpu.AuthoredPrefix.20260912.v1",
        "R.CommonFirst3All3" => "R.Relic.Cpu.TrackedPositions.20260912.v1",
        "R.ShopOrdered3" => "R.Relic.Cpu.ShopBackPrefix.20260912.v1",
        "R.DirectSmallC" or "R.DirectLargeUR" => "R.Relic.Cpu.CapsuleTargets.20260912.v1", _=>null };
    internal static bool Authority(ExactSearchExecutionRequest r, bool equivalentIdentityWork) => r.ProfileId == RuntimeProfileId.Beta111 &&
        r.Authority.CanUseCurrentModel && r.Authority.PlayersCount == 1 && r.Ascension == 10 &&
        (equivalentIdentityWork || r.Authority.Character.CharacterKey == BaseGameModelKeys.Characters.Ironclad) &&
        r.Authority.AllCharacterCardPoolsUnlocked == true;
    internal static FamilyPhysicalQuote? Quote(ExactSearchExecutionRequest r, FamilyPhysicalQuoteRequest g,
        string family, string shape, int workers, string range, bool compact = false, bool equivalentIdentityWork = false)
    {
        if (!Authority(r,equivalentIdentityWork) || !FamilyPhysicalQuote.AdmittedRequest(g) || g.PrivateInput || g.PrivateOutput ||
            g.CompactInput != compact) return null;
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
            Provenance + ";workers=" + workers + ";observedRange=" + range,
            OutputElementBytes: 8, OutputAlreadyOrdered: true, PublicTransportClass: "CpuOrderedAbi1") { FixedWindowMilliseconds=fixedMs };
    }
}
