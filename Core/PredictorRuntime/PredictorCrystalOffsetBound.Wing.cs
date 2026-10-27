using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorCrystalOffsetBound
{
    // CompileLanguage holds _sync. The bound already owns the frozen Rewards
    // stream, eligibility, custom-source policy and interned board-result bags.
    private readonly Dictionary<(int Bag,int Count),ImmutableHashSet<ModelKey>> _wingGiftAvailability=[];
    private ImmutableHashSet<ModelKey> WingGiftAvailability(int bag,int count)
    {
        var key=(bag,count);
        if(_wingGiftAvailability.TryGetValue(key,out var known)) return known;
        var result=PredictorCrystalWing.GiftAvailability(_source.Context,_source.State with {PersonalBag=_bags[bag]},count);
        if(_wingGiftAvailability.Count<16_384) _wingGiftAvailability.Add(key,result);
        return result;
    }
}
