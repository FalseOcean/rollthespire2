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
        var document = Stable107PredictionProfile.Instance.Predict(request!, detection);
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
            document.CanonicalSeed, document.ProfileId, character.CharacterKey, 10, "fixture", request!, document, authority, [], [], []);
        var candidateJson = System.Text.Json.JsonSerializer.SerializeToElement(candidate, options);
        Check(candidateJson.TryGetProperty("AnalysisRequest", out _), "Candidate request wire name");
        Check(!candidateJson.TryGetProperty("PredictionRequest", out _), "Internal rename leaked into candidate schema");
        Console.WriteLine($"CompatibilityReference checks={checks};PASS");
        return 0;
    }
}
