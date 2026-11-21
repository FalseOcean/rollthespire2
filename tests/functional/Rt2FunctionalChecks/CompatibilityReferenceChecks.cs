using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Profiles;

namespace RolltheSpire2.Tests.FastSearch;

internal static class CompatibilityReferenceChecks
{
    // Preserve the distinct archived-profile identity/uncertainty fixture from R1;
    // current profile/version selection vectors remain in tests/seed-vectors.
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string issue) { checks++; if (!value) throw new Exception(issue); }
        var character = CharacterIdentity.FromKey(BaseGameModelKeys.Characters.Silent);
        var authority = RuntimeContextAuthorityCapture.CaptureAuditedVanillaAllUnlocks(character, 10, "0.107.1", RuntimeProfileId.Stable107);
        var detection = new GameVersionDetection("v0.107.1", "0.107.1", "v0.107.1", "CompatibilityReference", true, "");
        Check(SeedPredictionRequest.TryCreate(" H0T0Z0S23C ", character, 10, 1, 0, authority, true, out var request, out var error), error.ToString());
        var facts = Stable107PredictionProfile.Instance.Observe(request!, detection);
        var document = SeedPredictionDocument.FromFacts(facts);
        var choices = document.Sections.SelectMany(s => s.NeowChoices).ToArray();
        Check(RuntimeProfileRegistry.Select(detection).ProfileId == RuntimeProfileId.Unsupported, "Archived profile became Production selectable");
        Check(document.ProfileId == RuntimeProfileId.Stable107 && document.PredictorId == "Stable107AnalysisProfile", "Version binding");
        Check(choices.Select(c => c.RelicKey).SequenceEqual(new[] { BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.WingedBoots, BaseGameModelKeys.Relics.NeowsBones }), "Stable107 golden identity/order");
        Check(choices.All(c => c.IdentityPrecision == PredictionPrecision.Exact), "Identity precision");
        Check(choices[0].EffectPrecision == PredictionPrecision.Unknown && choices[2].EffectPrecision == PredictionPrecision.Unknown, "Missing effect authority became exact");
        Check(choices[1].EffectPrecision == PredictionPrecision.Partial, "Future route precision");
        Check(document.Context.ProfileId == RuntimeProfileId.Stable107 && document.Context.CharacterKey == character.CharacterKey && document.Context.Ascension == 10, "Immutable context");
        var options = RolltheSpire2.Infrastructure.Snapshots.ProductionSearchReplay.JsonOptions;
        var serialized = System.Text.Json.JsonSerializer.SerializeToElement(document, options);
        Check(serialized.GetProperty("AnalyzerId").GetString() == "Stable107AnalysisProfile", "Historical predictor wire identity");
        Check(!serialized.TryGetProperty("PredictorId", out _), "Internal rename leaked into document schema");
        var candidate = new RolltheSpire2.Search.Contracts.SearchCandidate(
            document.CanonicalSeed, document.ProfileId, character.CharacterKey, 10, "fixture", authority,
            RolltheSpire2.Search.Contracts.SearchQueryEvaluation.Match()) { Facts = facts };
        var candidateJson = System.Text.Json.JsonSerializer.SerializeToElement(candidate, options);
        Check(candidateJson.TryGetProperty("Evaluation", out _) && !candidateJson.TryGetProperty("AnalysisRequest", out _), "Candidate carries final evaluation independently of analysis request");
        Check(!candidateJson.TryGetProperty("PredictionRequest", out _), "Internal rename leaked into candidate schema");
        CheckModernPatchPrediction(Check);
        Console.WriteLine($"CompatibilityReference checks={checks};PASS");
        return 0;
    }

    private static void CheckModernPatchPrediction(Action<bool, string> check)
    {
        const string seed = "000000000001"; // Native 0.113.0 opening includes Phial Holster.
        var captured = ProductionSearchReplay.Read<ProductionSearchReplayContext>(
            "tools/ProductionSearchHost/Corpus/v1/context.json").Context.Authority;
        // Hold the item pools fixed so the combined Neow and reward report exercises both evidence sources.
        var world = captured.WorldAuthority!;
        world = world with { Beta109Generation = world.Beta109Generation! with { RngSemantics = RngSemanticProfileId.Modern113 } };
        SeedPredictionFacts Observe(string version)
        {
            var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(captured.Character.CharacterKey, 10, 0, 1, version)
                .WithEffectAuthority(captured.EffectAuthority).WithWorldAuthority(world);
            var detection = new GameVersionDetection(version, version, "beta", "CompatibilityReference", true, "");
            if (!SeedPredictionRequest.TryCreate(seed, authority.Character, 10, 1, 0, authority, null,
                SeedPredictionDomainSelection.Neow | SeedPredictionDomainSelection.NormalCombatRewards, true, out var request, out var error))
                throw new InvalidOperationException(error.ToString());
            return RuntimeProfileRegistry.Observe(detection, request!);
        }

        var reference = Observe("0.113.0");
        var patch = Observe("0.113.1");
        var offers = patch.Neow?.Choices.Select(choice => choice.RelicKey).ToArray() ?? [];
        check(patch.Neow?.IdentityPrecision == PredictionPrecision.Exact && offers.Length == 3 &&
            offers.Contains(BaseGameModelKeys.Relics.PhialHolster) &&
            reference.Neow is { } opening && offers.SequenceEqual(opening.Choices.Select(choice => choice.RelicKey)),
            "The accepted patch preserves the Modern113 opening and its ordered choices");
        check(patch.Warnings.All(w => w.Code != PredictionWarningCode.UnverifiedVersionFallback) &&
            Observe("0.113.9").Warnings.Any(w => w.Code == PredictionWarningCode.UnverifiedVersionFallback),
            "Only the unknown later patch warns about provisional reuse");
        check(patch.Diagnostics.Single(d => d.Code == PredictionDiagnosticCodes.RuntimeAuditFingerprint).Value ==
            RuntimeVersionCompatibility.Resolve("0.113.1").EvidenceCode,
            "The accepted patch records its own version evidence under the shared adapter");
        check(patch.Diagnostics.Single(d => d.Code == "reward-adapter-audit-fingerprint").Value ==
            RuntimeProfilePolicies.Beta111AuditFingerprint, "The combined report identifies the reward adapter's separate audit");
    }
}
