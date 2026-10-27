// Inspired by shion0528 (Egoistic今宵), v1.3.5-resource.17,
// da82f7f9023c3d3598d50b8642e93f2801c37ccc,
// Core/Prediction/Profiles/Beta111NeowOpeningProof.cs. Independent adaptation:
// only identity is reused; RT2 already reuses the opening effect choices.
// SPDX-License-Identifier: MPL-2.0
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Neow;

namespace RolltheSpire2.Core.Prediction.Profiles;

/// <summary>Owned by one synchronous Exact candidate, never a session/global cache.</summary>
internal sealed class Beta111NeowIdentityReuse
{
    private RuntimeContextAuthoritySnapshot? _authority;
    private TrustedRootHashInput _input;
    private ModernNeowIdentityResult? _identity;
    internal bool WasReused { get; private set; }

    internal void Capture(Beta111PredictionProfile owner, SeedPredictionRequest request,
        TrustedRootHashInput input, bool exactAuthority, ModernNeowIdentityResult identity)
    {
        // Injected delegates can be stateful; only the production pure predictor
        // and exact solo authority can supply this candidate-local proof.
        if (_identity is not null || !ReferenceEquals(owner, Beta111PredictionProfile.Instance) ||
            request.Authority.ProfileId != RuntimeProfileId.Beta111 || request.Authority.PlayersCount != 1 ||
            request.Authority.PlayerSlotIndex != 0 || request.PartyOpeningChoices is not null ||
            !request.Includes(SeedPredictionDomainSelection.Neow) ||
            request.Includes(SeedPredictionDomainSelection.NormalCombatRewards) ||
            !exactAuthority || !identity.ExactAuthority) return;
        _authority = request.Authority; _input = input; _identity = identity;
    }

    internal bool TryReuse(Beta111PredictionProfile owner, SeedPredictionRequest request,
        TrustedRootHashInput input, bool exactAuthority, out ModernNeowIdentityResult identity)
    {
        identity = null!;
        // Identity uses only root, fixed profile, captured authority and the
        // exactAuthority argument. Request validation and effect/diagnostic
        // generation retain their existing ownership in the profile.
        if (_identity is null || !ReferenceEquals(owner, Beta111PredictionProfile.Instance) ||
            input != _input || !ReferenceEquals(request.Authority, _authority) ||
            request.Authority.ProfileId != RuntimeProfileId.Beta111 || request.PartyOpeningChoices is not null ||
            exactAuthority != _identity.ExactAuthority) return false;
        identity = _identity;
        WasReused = true;
        return true;
    }
}
