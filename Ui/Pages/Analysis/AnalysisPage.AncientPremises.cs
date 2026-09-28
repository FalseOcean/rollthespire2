using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.Ui1;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed partial class AnalysisPage
{
    private sealed record AncientPremiseVariants(ModelKey[] Keys, AncientPredictionResult[] Results)
    {
        internal AncientPredictionResult Select(AncientOptionConditionProfile conditions)
        {
            int mask = 0;
            for (int i = 0; i < Keys.Length; i++)
                if (conditions.TryGetOptionEligibility(Keys[i], out bool eligible) && eligible) mask |= 1 << i;
            return Results[mask];
        }
    }

    private readonly Dictionary<(int Act, ModelKey Ancient), AncientPremiseVariants> _ancientPremiseVariants = [];

    private void PrecomputeAncientPremises(SeedPredictionRequest request, SeedPredictionDocument document)
    {
        _ancientPremiseVariants.Clear();
        // Each Ancient uses its own event-local generator: at most 8 Pael or 4
        // Orobas combinations, not 512 full seed predictions. Use the existing
        // World owner for every result, including precision and missing authority.
        var profile = request.Authority.ProfileId == RuntimeProfileId.Beta111 ? Beta111Profile.Instance : _runtime.Profile;
        foreach (var ancient in document.Sections.SelectMany(s => s.Ancients))
        {
            var keys = AncientOptionConditionProfile.AuthoredOptionKeys
                .Where(k => AncientForOption(k) == ancient.AncientKey.Entry).ToArray();
            if (keys.Length == 0) continue;
            try
            {
                var results = new AncientPredictionResult[1 << keys.Length];
                for (int mask = 0; mask < results.Length; mask++)
                {
                    var conditions = request.AncientOptionConditions;
                    for (int i = 0; i < keys.Length; i++)
                        conditions = conditions.WithOptionEligibility(keys[i], (mask & (1 << i)) != 0);
                    var world = request.TrustedRootHashInput is { } root
                        ? WorldPredictionEngine.PredictFromRootHash(profile, root.RootHash, root.SeedIdentity,
                            request.Ascension, request.PlayerSlotIndex, request.Character.CharacterKey,
                            request.Authority.WorldAuthority, request.Authority.EffectAuthority, conditions)
                        : WorldPredictionEngine.Predict(profile, document.CanonicalSeed,
                            request.Ascension, request.PlayerSlotIndex, request.Character.CharacterKey,
                            request.Authority.WorldAuthority, request.Authority.EffectAuthority, conditions);
                    results[mask] = world.Ancients.FirstOrDefault(a => a.Act == ancient.Act && a.AncientKey == ancient.AncientKey)
                        ?? throw new InvalidOperationException("AncientPremiseIdentityUnavailable");
                }
                _ancientPremiseVariants[(ancient.Act, ancient.AncientKey)] = new(keys, results);
            }
            catch (Exception ex)
            {
                // Keep the original observation visible, but explicitly disable
                // unsupported premise editing instead of triggering a full replay.
                RuntimeLog.Warn($"predictorAncientPremisesUnavailable={ancient.AncientKey.Serialized};issue={ex.Message}");
            }
        }
    }

    private void ApplyAncientPremises(AncientOptionConditionProfile conditions)
    {
        if (!_wbHasResult || LastRequest is not { } originalRequest || LastDocument is not { } originalDocument) return;
        foreach (var ancient in originalDocument.Sections.SelectMany(s => s.Ancients))
        {
            bool changed = AncientOptionConditionProfile.AuthoredOptionKeys.Where(k => AncientForOption(k) == ancient.AncientKey.Entry)
                .Any(k => conditions.TryGetOptionEligibility(k, out bool next) &&
                    originalRequest.AncientOptionConditions.TryGetOptionEligibility(k, out bool previous) && next != previous);
            if (changed && !_ancientPremiseVariants.ContainsKey((ancient.Act, ancient.AncientKey)))
            {
                _ancientGroupCard.SetConditions(originalRequest.AncientOptionConditions, notify: false);
                ReformatDocumentOnly();
                return;
            }
        }
        var request = originalRequest.WithAncientOptionConditions(conditions);
        LastRequest = request;
        LastDocument = new SeedPredictionDocument
        {
            Context = originalDocument.Context with { RequestId = request.RequestId },
            PredictorId = originalDocument.PredictorId,
            OriginalSeed = originalDocument.OriginalSeed,
            CanonicalSeed = originalDocument.CanonicalSeed,
            Sections = originalDocument.Sections.Select(section => section.Ancients.Count == 0 ? section : section with
            {
                Ancients = section.Ancients.Select(a => _ancientPremiseVariants.TryGetValue((a.Act, a.AncientKey), out var variants)
                    ? variants.Select(conditions) : a).ToArray()
            }).ToArray(),
            Warnings = originalDocument.Warnings,
            Diagnostics = originalDocument.Diagnostics.Select(d => d.Code == PredictionDiagnosticCodes.RequestId
                ? d with { Value = request.RequestId.Serialized } : d).ToArray(),
            OverallStatus = originalDocument.OverallStatus,
            ProductRelevantProjectionPrecision = originalDocument.ProductRelevantProjectionPrecision,
            ProductRelevantProjectionStatus = originalDocument.ProductRelevantProjectionStatus,
            FullEffectSemanticsCompleteness = originalDocument.FullEffectSemanticsCompleteness,
            Party = originalDocument.Party
        };
        // Do not call ShowDocument: maps, drawn routes, merchant/event projections
        // and the opening selection still belong to the same immutable inputs.
        _viewModel = SeedAnalysisPresentationBuilder.Build(LastDocument, _uiText!, _contentNames!, false);
        if (_ancientSurface is { } surface && Godot.GodotObject.IsInstanceValid(surface) && surface.IsInsideTree() &&
            _viewModel.AncientDomain.Items.FirstOrDefault(a => a.Act == _wbPage + 1) is { } visibleAncient)
            RenderAncientInspector(visibleAncient, surface);
        PredictorContextChanged?.Invoke(CurrentReactiveDraft, _preferredOpeningRouteId,
            _preferredOpeningChoiceSlotIndex, _preferredRewardRouteGroupId);
    }
}
