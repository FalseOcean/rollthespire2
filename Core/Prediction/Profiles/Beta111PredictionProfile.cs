using RolltheSpire2.Core.Authority;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Core.World;

namespace RolltheSpire2.Core.Prediction.Profiles;

/// <summary>
/// Beta111 version-bound prediction entry. Domain implementations are shared
/// with the source-audited Modern110 semantic family; game-version/audit/catalog
/// identity remains Beta111-specific. As an accepted A-class semantic-compatible
/// patch, Exact precision follows the normal live-authority gates.
/// </summary>
public sealed class Beta111PredictionProfile : ISeedPredictionProfile
{
    public static Beta111PredictionProfile Instance { get; } = new();
    private readonly Func<ulong, RuntimeContextAuthoritySnapshot, IRuntimeProfile, bool, ModernNeowIdentityResult> _identityPredictor;
    private Beta111PredictionProfile() : this(ModernNeowIdentityPredictor.PredictModernCore) { }
    internal Beta111PredictionProfile(Func<ulong, RuntimeContextAuthoritySnapshot, IRuntimeProfile, bool, ModernNeowIdentityResult> identityPredictor)
        => _identityPredictor = identityPredictor;

    public RuntimeProfileId ProfileId => RuntimeProfileId.Beta111;
    public string PredictorId => "Beta111AnalysisProfile";

    public SeedPredictionFacts Observe(SeedPredictionRequest request, GameVersionDetection detection)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(detection);
        if (!ContextMatches(request, detection))
            return Fail(request, detection, string.Empty, PredictionWarningCode.InvalidRequestContext, SeedPredictionInputStatus.Unknown);
        if (!ProfileSeedRules.TryCanonicalizePredictionSeed(Beta111Profile.Instance, request.OriginalSeed, out string canonicalSeed, out string issue))
            return SeedPredictionFacts.Invalid(detection.DisplayVersion, ProfileId, PredictorId, request, issue);
        var input = TrustedRootHashInput.FromCanonicalSeed(Beta111Profile.Instance, canonicalSeed);
        var observed = ObserveCore(request, detection, input, analysis: true,
            captureTransformationContinuation: request.PlayersCount == 1);
        return Supplement(observed, request.Domains);

        SeedPredictionFacts Supplement(SeedPredictionFacts facts, SeedPredictionDomainSelection group)
        {
            var requested = request.Domains & group;
            if (requested == SeedPredictionDomainSelection.None) return facts;
            try
            {
                if (!SeedModelAssumptions.TryCreate(request.Authority, out var modeled, out var assumptions, requested)) return facts;
                var domains = SeedPredictionDomainSelection.None;
                foreach (var domain in assumptions.Keys) domains |= requested & domain;
                if (domains == SeedPredictionDomainSelection.None) return facts;
                var estimate = ObserveCore(request.ForModelReplay(modeled, domains), detection, input, analysis: true,
                    captureTransformationContinuation: request.PlayersCount == 1);
                return SeedAnalysisModelResults.Combine(facts, estimate, assumptions);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                string stage = "AnalysisModelReplay:" + requested;
                var retained = facts with
                {
                    Warnings = [.. facts.Warnings, new(PredictionWarningCode.AnalysisFailed, EvidenceCode: stage)],
                    Diagnostics = [.. facts.Diagnostics, new(PredictionDiagnosticCodes.Exception, stage + ": " + ex)]
                };
                // Retry separately only when a joint optional replay failed. The normal pass keeps
                // the shared initialization inputs available to effects that need them.
                var rooms = requested & (SeedPredictionDomainSelection.World | SeedPredictionDomainSelection.RelicSequence);
                var opening = requested & (SeedPredictionDomainSelection.Neow | SeedPredictionDomainSelection.NormalCombatRewards);
                return rooms != 0 && opening != 0 ? Supplement(Supplement(retained, rooms), opening) : retained;
            }
        }
    }

    internal SeedPredictionFacts ObserveFromRootHash(SeedPredictionRequest request, GameVersionDetection detection, TrustedRootHashInput input,
        IReadOnlyList<NeowChoiceResult>? preparedOpeningChoices = null, Beta111NeowIdentityReuse? identityReuse = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(detection);
        if (request.TrustedRootHashInput is not { } requestInput || requestInput != input)
            return Fail(request, detection, input.SeedIdentity, PredictionWarningCode.InvalidRequestContext, SeedPredictionInputStatus.Unknown,
                new PredictionDiagnostic("trusted-root-hash-request-mismatch", "true"));
        if (input.HasCanonicalSeed && Beta111Profile.Instance.ComputeRootSeed(input.CanonicalSeed) != input.RootHash)
            return Fail(request, detection, input.SeedIdentity, PredictionWarningCode.InvalidRequestContext, SeedPredictionInputStatus.Unknown,
                new PredictionDiagnostic("trusted-root-hash-forward-validation", "failed"));
        return ObserveCore(request, detection, input, preparedOpeningChoices, identityReuse);
    }

    private SeedPredictionFacts ObserveCore(SeedPredictionRequest request, GameVersionDetection detection, TrustedRootHashInput input,
        IReadOnlyList<NeowChoiceResult>? preparedOpeningChoices = null, Beta111NeowIdentityReuse? identityReuse = null,
        bool captureTransformationContinuation = false, bool analysis = false)
    {
        if (!ContextMatches(request, detection))
            return Fail(request, detection, input.SeedIdentity, PredictionWarningCode.InvalidRequestContext, SeedPredictionInputStatus.Unknown);
        if (request.Authority.NoRunModifiers == false)
            return Fail(request, detection, input.SeedIdentity, PredictionWarningCode.NonVanillaCatalogUnsupported, SeedPredictionInputStatus.Unsupported);

        RuntimeVersionResolution compatibility = RuntimeProfileRegistry.Resolve(detection);
        bool requiresNeowIdentity = request.Includes(SeedPredictionDomainSelection.Neow) ||
                                    request.Includes(SeedPredictionDomainSelection.NormalCombatRewards);

        ModernNeowIdentityResult? result = null;
        PredictionPrecision identityPrecision = PredictionPrecision.Unknown;
        NeowChoiceResult[] choices = Array.Empty<NeowChoiceResult>();
        var warnings = new List<PredictionWarning>();
        SeedDomainEvaluationStatus neowStatus = SeedDomainEvaluationStatus.Evaluated;
        string neowIssueCode = string.Empty;

        List<PredictionDiagnostic>? errors = null;
        void Report(string stage, Exception error)
        {
            (errors ??= []).Add(new(PredictionDiagnosticCodes.Exception, stage + ": " + error));
            warnings.Add(new(PredictionWarningCode.AnalysisFailed, EvidenceCode: "AnalysisDomainFailed:" + stage));
        }

        // A missing Neow premise does not erase independent requested facts.
        // Rewards depend on Neow and inherit this status; World/Relic do not.
        if (requiresNeowIdentity && request.Authority.VanillaNeowCatalogExact == false && request.Authority.CapturedNeowOffers is null)
        {
            neowStatus = SeedDomainEvaluationStatus.Unsupported;
            neowIssueCode = "NonVanillaCatalogUnsupported";
            warnings.Add(new PredictionWarning(PredictionWarningCode.NonVanillaCatalogUnsupported));
        }
        else if (requiresNeowIdentity && request.Authority.UsesStandardOpeningAssumption && request.Authority.NeowPreparation.EligibleCurses.IsEmpty)
        {
            neowStatus = SeedDomainEvaluationStatus.Unknown;
            neowIssueCode = "EmptyNeowCursePool";
            warnings.Add(new PredictionWarning(PredictionWarningCode.SnapshotAuthorityIncomplete, EvidenceCode: neowIssueCode));
        }
        else if (requiresNeowIdentity && !request.Authority.HasExactModernNeowIdentityInputs && !request.Authority.UsesStandardOpeningAssumption)
        {
            neowStatus = SeedDomainEvaluationStatus.Unknown;
            neowIssueCode = "SnapshotAuthorityIncomplete";
            warnings.Add(new PredictionWarning(PredictionWarningCode.SnapshotAuthorityIncomplete));
        }

        if (requiresNeowIdentity && neowStatus == SeedDomainEvaluationStatus.Evaluated)
        {
            bool exactAuthority;
            try
            {
                exactAuthority = request.Authority.IsBeta111NeowIdentityAuthorityExact || request.Authority.UsesStandardOpeningAssumption;
                result = identityReuse is not null && identityReuse.TryReuse(this, request, input, exactAuthority, out var identity)
                    ? identity
                    : _identityPredictor(input.RootHash, request.Authority, Beta111Profile.Instance, exactAuthority);
                identityPrecision = !result.ExactAuthority
                    ? PredictionPrecision.Unknown
                    // Exact under the selected ruleset and captured inputs does not
                    // claim that an unverified game version has been runtime-accepted.
                    : Beta111ValidationAuthority.RuntimeAccepted && compatibility.AllowsProductionSearch &&
                      !compatibility.Blocks(CompatibilityDomainMask.Neow)
                        ? PredictionPrecision.Exact
                        : PredictionPrecision.Partial;

                identityReuse?.Capture(this, request, input, exactAuthority, result);

                choices = (preparedOpeningChoices ?? request.PartyOpeningChoices)?.ToArray() ??
                    result.RelicKeys.Select((key, index) =>
                    {
                        var projection = NeowChoiceFacts.UnrequestedNeowEffects;
                        if (request.ShouldProjectNeowEffects(key))
                        {
                            try
                            {
                                projection = Beta111NeowEffectProjector.ProjectFromRootHash(key, identityPrecision,
                                    request.Authority, input.RootHash, request.EnableComplexBonesDeckInteractions,
                                    captureTransformationContinuation);
                            }
                            catch (Exception ex) when (analysis && ex is not OperationCanceledException)
                            {
                                Report("NeowEffect:" + key.Serialized, ex);
                                projection = Core.Effects.NeowEffectProjection.Unknown(key, PredictionWarningCode.AnalysisFailed,
                                    "AnalysisDomainFailed:NeowEffect");
                            }
                        }
                        return NeowChoiceFacts.Choice(index + 1, key, request, identityPrecision,
                            "beta111.shared-modern-neow-identity", projection);
                    }).ToArray();

                warnings.AddRange(choices.SelectMany(choice => choice.Warnings));
                if (result.UnknownEligibilityAffectedPool)
                    warnings.Add(new PredictionWarning(PredictionWarningCode.ModernEligibilityIncomplete, EvidenceCode: "beta111-eligibility"));
                else if (!result.ExactAuthority)
                    warnings.Add(new PredictionWarning(PredictionWarningCode.SnapshotAuthorityIncomplete, EvidenceCode: "beta111-identity-authority"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (!analysis) return Fail(request, detection, input.SeedIdentity, PredictionWarningCode.AnalysisFailed,
                    SeedPredictionInputStatus.Unknown, new PredictionDiagnostic(PredictionDiagnosticCodes.Exception, ex.ToString()));
                neowStatus = SeedDomainEvaluationStatus.Unknown;
                neowIssueCode = "AnalysisDomainFailed:Neow";
                Report("Neow", ex);
            }
        }

        if (compatibility.IsFallback)
            warnings.Add(new PredictionWarning(PredictionWarningCode.UnverifiedVersionFallback, EvidenceCode: compatibility.EvidenceCode));
        WorldPredictionResult? worldPrediction = null;
        if (request.Includes(SeedPredictionDomainSelection.World))
        {
            try
            {
                worldPrediction = WorldPredictionEngine.PredictFromRootHash(Beta111Profile.Instance, input.RootHash,
                    input.SeedIdentity, request.Ascension, request.Authority.PlayerSlotIndex, request.Character.CharacterKey,
                    request.Authority.WorldAuthority, request.Authority.EffectAuthority, request.AncientOptionConditions);
            }
            catch (Exception ex) when (analysis && ex is not OperationCanceledException)
            {
                Report("World", ex);
                worldPrediction = WorldPredictionResult.Unknown("AnalysisDomainFailed:World");
            }
        }

        RelicSequencePredictionResult? relicPrediction = null;
        if (request.Includes(SeedPredictionDomainSelection.RelicSequence))
        {
            try
            {
                relicPrediction = RelicSequencePredictor.PredictFromRootHash(Beta111Profile.Instance, input.RootHash,
                    input.SeedIdentity, request.Authority.WorldAuthority, request.RelicSequencePreviewCount);
            }
            catch (Exception ex) when (analysis && ex is not OperationCanceledException)
            {
                Report("RelicSequence", ex);
                relicPrediction = RelicSequencePredictionResult.Unknown(ProfileId, "AnalysisDomainFailed:RelicSequence");
            }
        }

        NormalCombatRewardSequencePredictionResult? rewardPrediction = null;
        if (request.Includes(SeedPredictionDomainSelection.NormalCombatRewards))
        {
            try
            {
                rewardPrediction = neowStatus == SeedDomainEvaluationStatus.Unsupported
                    ? NormalCombatRewardSequencePredictionResult.Unsupported(ProfileId, "NeowPrerequisite:" + neowIssueCode, request.Authority.EffectSnapshotFingerprint)
                    : neowStatus != SeedDomainEvaluationStatus.Evaluated
                        ? NormalCombatRewardSequencePredictionResult.Unknown(ProfileId, "NeowPrerequisite:" + neowIssueCode, request.Authority.EffectSnapshotFingerprint)
                        : NormalCombatRewardSequencePredictor.PredictFromRootHash(Beta111Profile.Instance, input.RootHash,
                            input.SeedIdentity, request.Authority.PlayerSlotIndex, request.Ascension, choices,
                            request.Authority.EffectAuthority, request.Authority.WorldAuthority,
                            request.CombatRewardProjectionRequest, request.RewardPoolPreparation);
            }
            catch (Exception ex) when (analysis && ex is not OperationCanceledException)
            {
                Report("NormalCombatRewards", ex);
                rewardPrediction = NormalCombatRewardSequencePredictionResult.Unknown(ProfileId, "AnalysisDomainFailed:NormalCombatRewards");
            }
        }

        PredictionWarning[] distinctWarnings = warnings.Distinct().ToArray();
        var diagnostics = new List<PredictionDiagnostic>
        {
            new(PredictionDiagnosticCodes.Analyzer, PredictorId),
            new(PredictionDiagnosticCodes.RuntimeCompatibilityMode, compatibility.SupportKind.ToString()),
            new(PredictionDiagnosticCodes.GameVersionIdentity, compatibility.GameVersionIdentity.ToString()),
            new(PredictionDiagnosticCodes.RngSemanticProfile, compatibility.SemanticProfileId.ToString()),
            new(PredictionDiagnosticCodes.CompatibilityConfidence, compatibility.Confidence.ToString()),
            new(PredictionDiagnosticCodes.RuntimeAccepted, compatibility.RuntimeAccepted.ToString().ToLowerInvariant()),
            new(PredictionDiagnosticCodes.RuntimeCompatibilityReferenceVersion, compatibility.ReferenceVersion),
            new(PredictionDiagnosticCodes.RuntimeAuthorityId, request.Authority.RuntimeAuthorityId),
            new(PredictionDiagnosticCodes.RuntimeAuditFingerprint, request.Authority.RuntimeAuditFingerprint),
            new(PredictionDiagnosticCodes.RuntimeCardCatalogFingerprint, request.Authority.RuntimeCardCatalogFingerprint),
            new(PredictionDiagnosticCodes.CardBaseOddsPolicy, RuntimeProfilePolicies.BaseOddsPolicy(ProfileId).ToString()),
            new(PredictionDiagnosticCodes.Beta111ValidationStatus, Beta111ValidationAuthority.RuntimeSupportStatus),
            new(PredictionDiagnosticCodes.RuntimeValidationStatus, compatibility.Confidence.ToString()),
            new("domain-local-neow-required", requiresNeowIdentity.ToString().ToLowerInvariant())
        };
        if (errors is not null) diagnostics.AddRange(errors);
        if (result is not null)
        {
            diagnostics.Add(new PredictionDiagnostic(PredictionDiagnosticCodes.RngCalls, result.RngCalls.ToString()));
            diagnostics.Add(new PredictionDiagnostic(PredictionDiagnosticCodes.EventEntry, "NEOW"));
            diagnostics.Add(new PredictionDiagnostic(PredictionDiagnosticCodes.EligibilityExact, result.ExactAuthority.ToString().ToLowerInvariant()));
        }
        if (!input.HasCanonicalSeed)
        {
            diagnostics.Add(new PredictionDiagnostic("trusted-root-hash", input.RootHash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture)));
            diagnostics.Add(new PredictionDiagnostic("trusted-root-hash-visible-seed-bound", "false"));
        }

        bool neowAuthorityWarning = result is not null &&
                                    (result.UnknownEligibilityAffectedPool || !result.ExactAuthority);
        return SeedPredictionFactsFactory.Create(
            request, detection, ProfileId, PredictorId, input.SeedIdentity, choices, distinctWarnings, diagnostics,
            SeedPredictionInputStatus.Ready,
            worldPrediction, relicPrediction, rewardPrediction,
            requiresNeowIdentity ? neowStatus : null, neowIssueCode, identityPrecision: identityPrecision,
            identityAuthorityWarning: neowAuthorityWarning || compatibility.IsFallback || !Beta111ValidationAuthority.RuntimeAccepted);
    }

    private bool ContextMatches(SeedPredictionRequest request, GameVersionDetection detection)
    {
        RuntimeVersionResolution compatibility = RuntimeProfileRegistry.Resolve(detection);
        return request.Authority.ProfileId == ProfileId &&
               compatibility.ProfileId == ProfileId &&
               RuntimeVersionCompatibility.UsesBeta111Profile(request.Authority.GameVersion) &&
               string.Equals(request.Authority.GameVersion, detection.NormalizedVersion, StringComparison.Ordinal);
    }

    private SeedPredictionFacts Fail(SeedPredictionRequest request, GameVersionDetection detection, string canonicalSeed,
        PredictionWarningCode warning, SeedPredictionInputStatus status, PredictionDiagnostic? diagnostic = null) =>
        SeedPredictionFactsFactory.Create(request, detection, ProfileId, PredictorId, canonicalSeed, Array.Empty<NeowChoiceResult>(),
            new[] { new PredictionWarning(warning) }, diagnostic is null ? Array.Empty<PredictionDiagnostic>() : new[] { diagnostic }, status, inputIssue: warning.ToString());
}
