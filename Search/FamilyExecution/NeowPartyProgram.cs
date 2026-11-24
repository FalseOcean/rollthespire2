using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

internal sealed record NeowPartyPlayer(RuntimeContextAuthoritySnapshot Authority,
    ModelKeySetFilter[] RichGates, ModelKey[] RequiredOffers, NeowReplayPlan Offers,
    NeowReplayPlan[] Predicates, (ulong Any, ulong All, ulong Ban)[]? Gates);

internal sealed class NeowPartyProgram(NeowPartyPlayer[] players, NeowReplayPlan[] plans, bool optimize)
{
    private readonly NeowPartyPlayer[] _cpuPlayers = players;
    private readonly NeowReplayPlan[] _cpuPlans = plans;
    private readonly bool _optimize = optimize;
    private static bool MatchesRichOffers(ulong root, NeowPartyPlayer player)
    {
        var offers = ModernNeowIdentityPredictor.PredictModernCore(root, player.Authority, Beta111Profile.Instance, true).RelicKeys;
        return player.RichGates.All(gate => PartyInitialQuery.Matches(gate, offers)) && player.RequiredOffers.All(offers.Contains);
    }
        private bool MatchesOffers(ulong root, NeowPartyPlayer player, out NeowReplayObservation observation)
        {
            observation = NeowPartyReplay.Observe(root, player.Offers);
            if (player.Gates is null) return MatchesRichOffers(root, player);
            ulong top = Beta110FastRelicCatalog.Bit(observation.First) | Beta110FastRelicCatalog.Bit(observation.Second) |
                Beta110FastRelicCatalog.Bit(observation.Curse);
            // Each Any row is an independent gate. Combining them into one OR
            // would weaken the whole authored conjunction.
            foreach (var gate in player.Gates)
                if (gate.Any != 0 && (top & gate.Any) == 0 || (top & gate.All) != gate.All || (top & gate.Ban) != 0) return false;
            return true;
        }
        internal bool MatchesInitialOffers(ulong root)
        {
            foreach (var player in _cpuPlayers)
                if (!MatchesOffers(root, player, out _)) return false;
            return true;
        }
        internal bool MatchesInitialOffersReference(ulong root) => _cpuPlayers.All(p => MatchesRichOffers(root, p));
        internal bool Matches(ulong root)
        {
            if (!_optimize) return MatchesInitialOffersReference(root) && _cpuPlans.All(plan => NeowPartyReplay.Matches(root, plan));
            Span<NeowReplayObservation> offers = stackalloc NeowReplayObservation[_cpuPlayers.Length];
            // All slots' initial gates still precede every local predicate/fault.
            for (int i = 0; i < _cpuPlayers.Length; i++)
                if (!MatchesOffers(root, _cpuPlayers[i], out offers[i])) return false;
            for (int i = 0; i < _cpuPlayers.Length; i++)
            {
                var player = _cpuPlayers[i];
                if (player.Predicates.Length == 0) continue;
                var observation = NeowPartyReplay.CompleteObservation(root, player.Predicates[0], offers[i]);
                foreach (var plan in player.Predicates)
                    if (!NeowPartyReplay.Matches(root, plan, observation)) return false;
            }
            return true;
        }
}
