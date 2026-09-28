using RolltheSpire2.Core.Effects.Snapshots;

namespace RolltheSpire2.Core.Effects.Shadow;

internal sealed class NeowEffectWorkingState
{
    public NeowEffectWorkingState(
        NeowEffectAuthoritySnapshot authority,
        NeowShadowDeck? deck,
        List<NeowEffectRelicSnapshot>? relicBag,
        NeowEffectRngContext rng)
    {
        Authority = authority;
        Deck = deck;
        RelicBag = relicBag;
        Rng = rng;
    }

    public NeowEffectAuthoritySnapshot Authority { get; }
    public NeowShadowDeck? Deck { get; }
    public List<NeowEffectRelicSnapshot>? RelicBag { get; }
    public NeowEffectRngContext Rng { get; }

    // Only the opening-local Tress / Coffer / Kaleidoscope interaction is tracked.
    public bool HasSilkenTress { get; set; }
    public bool SilkenTressConsumed { get; set; }

    public NeowEffectWorkingState Clone() => new(
        Authority,
        Deck?.Clone(),
        RelicBag is null ? null : new List<NeowEffectRelicSnapshot>(RelicBag),
        Rng.Clone())
    {
        HasSilkenTress = HasSilkenTress,
        SilkenTressConsumed = SilkenTressConsumed
    };
}
