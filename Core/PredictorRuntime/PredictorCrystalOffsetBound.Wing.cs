using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed partial class PredictorCrystalOffsetBound
{
    // CompileLanguage holds _sync. The bound already owns the frozen Rewards
    // stream, eligibility, custom-source policy and interned board-result bags.
    private readonly Dictionary<(int Bag,int Count),ImmutableHashSet<ModelKey>> _modeledWingGiftAvailability=[];
    private ImmutableHashSet<ModelKey> WingGiftAvailability(int bag,int count)
    {
        var key=(bag,count);
        if(_modeledWingGiftAvailability.TryGetValue(key,out var known)) return known;
        var state = _source.State with { PersonalBag = _bags[bag] };
        // Card rewards can advance the personal Rewards stream before any
        // sacrifice. The exact cursor belongs to endpoint replay.
        var result = PredictorCardRewardRelicHooks.WingCharmStream(_source.Context) == PredictorStream.Rewards &&
            CrystalRewardOption.UsesWingCharmRng(state)
            ? PredictorCrystalWing.GiftUpper(_source.Context, state, count)
            : PredictorCrystalWing.ModeledGiftAvailability(_source.Context, state, count);
        if(_modeledWingGiftAvailability.Count<16_384) _modeledWingGiftAvailability.Add(key,result);
        return result;
    }
}
