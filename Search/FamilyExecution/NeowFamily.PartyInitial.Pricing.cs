namespace RolltheSpire2.Search.FamilyExecution;

internal sealed partial class NeowFamily
{
    internal sealed partial class PartyInitial
    {
        private FamilyUnitCost? QuoteCoarseUnit(double inputs, bool compact, double finalOutputRate,
            bool computationOnly = false, GpuCostSnapshot? costs = null)
        {
            var geometry = new FamilyPhysicalQuoteRequest(compact, false, inputs)
                { ComputationOnly = computationOnly, CostSnapshot = costs };
            if (!FamilyPhysicalQuote.AdmittedRequest(geometry)) return null;
            if (!double.IsFinite(finalOutputRate) || finalOutputRate is < 0 or > 1) finalOutputRate = 1;
            const string basis = "N.PartyInitial.WorkEstimate.20261028.v1;SameFamilyOnly;PriceQuality=Estimate;" +
                "NoIndependentPlayerProbabilityProduct;SharedArrivalWitnessUnchanged;FullStageInputEnvelope;" +
                "FinalOutput=ExistingWholeTableOfferUpperEnvelopeNotResultJointProbability;" +
                "SharedAndConnectionBoundaryUnmeasured;NotCurrentWrapperMeasurement";
            if (_gpuStages.Count == 0)
            {
                // All selected slots' initial observations precede local effects.
                // Bound each authored arrival separately rather than silently
                // dropping unknown correlation or inventing a joint witness.
                double ns = 25 + 210d * _cpuPlayers.Count;
                int arrivals = 0;
                foreach (var plan in _cpuPlans)
                {
                    var work = NeowWorkEstimate.Describe(plan);
                    int count = Math.Max(1, plan.SharedArrivals.Length);
                    arrivals += count;
                    double burns = plan.SharedArrivals.Length == 0 ? 0 :
                        plan.SharedArrivals.Max(a => (double)Math.Max(0, a.Niche) + Math.Max(0, a.Potions));
                    ns += (!_optimize ? 210 : 0) + (plan.Bones ? 3d * plan.Authority.BonesEligibleRelicIds.Length : 0) +
                        count * Math.Max(1, work.Routes) * (12 * (work.Draws + burns) + .3 * work.PoolVisits + 2 * work.Comparisons);
                }
                var quote = FamilyCoarsePricing.Cpu(FamilyId, "N.PartyInitial.Cpu", ns,
                    FamilyCpuExecution.ConfiguredWorkers(_request), geometry,
                    basis + $";OfferSlots={_cpuPlayers.Count};PredicateParts={_cpuPlans.Count};ArrivalAlternatives={arrivals};" +
                    "CpuOwnReplayAnchors=25,210ns;WorkerRatioIsFamilyReferenceNotPartyMeasurement", _cpu.WindowCapacity);
                if (quote is null) return null;
                // Historical wrapper floor is a conservative scheduling reference,
                // not a newly measured v6/B65536 worker/arrival combination.
                quote = quote with { FixedWindowMilliseconds = .35 };
                double? milliseconds = computationOnly ? FamilyCoarsePricing.ComputationMilliseconds(quote, geometry)
                    : FamilyCoarsePricing.PublicMilliseconds(quote, geometry, finalOutputRate);
                return milliseconds is double ms
                    ? new(ms, 0, quote.Evidence + ";CpuWrapperFloorReference=.35ms;" +
                        (computationOnly ? "ComputationOnly;CpuOwnedCanonicalWork" : "CompleteCanonicalAbi1")) : null;
            }

            bool privateEdges = UsesPrivate || UsesFused;
            double numericalNs = 0, publicMs = 0, setup = 0;
            var evidence = new List<string>();
            for (int i = 0; i < _gpuStages.Count; i++)
            {
                var stage = _gpuStages[i].Family;
                bool stageCompact = compact || !UsesFused && i > 0;
                var stageGeometry = new FamilyPhysicalQuoteRequest(stageCompact,
                    privateEdges && (!computationOnly || i < _gpuStages.Count - 1), inputs,
                    PrivateInput: privateEdges && stageCompact) { ComputationOnly = computationOnly, CostSnapshot = costs };
                var quote = computationOnly ? FamilyPhysicalQuote.Computation(stage, stageGeometry, costs) : stage.QuotePhysicalWork(stageGeometry);
                if (quote is null) return null;
                if (computationOnly && costs is null) quote = FamilyCoarsePricing.LocalComputation(stage, quote, stageGeometry);
                setup += quote.SetupMilliseconds ?? 0;
                evidence.Add(quote.Evidence);
                if (computationOnly)
                {
                    if (FamilyCoarsePricing.ComputationMilliseconds(quote, stageGeometry) is not double ms) return null;
                    publicMs += ms;
                }
                else if (privateEdges) numericalNs += quote.NanosecondsPerInput;
                else
                {
                    // Intermediate populations are unknown jointly. Retain full
                    // input/output for them, use only the existing whole-N offer
                    // envelope for final public materialization. Never multiply
                    // personal N probabilities to discount later stages.
                    double output = i == _gpuStages.Count - 1 ? finalOutputRate : 1;
                    if (FamilyCoarsePricing.PublicMilliseconds(quote, stageGeometry, output) is not double ms) return null;
                    publicMs += ms;
                }
            }
            string mode = UsesFused ? "Fused" : privateEdges ? "PrivateOrdinal" : "PublicOrMixed";
            if (!computationOnly && privateEdges)
            {
                var quote = FamilyCoarsePricing.Gpu(FamilyId, "N.PartyInitial." + mode, numericalNs,
                    NeowFamilyGpuExecutor.Capacity, geometry, basis + ";NoAssumedFusedSpeedDiscount", setup,
                    publicTransportClass: "LargeResidentAppend32");
                if (quote is null) return null;
                quote = quote with { FixedWindowMilliseconds = UsesFused ? 0 : .15 * Math.Max(0, _gpuStages.Count - 1) };
                if (FamilyCoarsePricing.PublicMilliseconds(quote, geometry, finalOutputRate) is not double ms) return null;
                publicMs = ms;
            }
            return new(publicMs, setup, basis + $";Mode={mode};Stages={_gpuStages.Count};" +
                (computationOnly ? "ComputationOnly;NumericalAndDeviceEmission;NoSubmitOrHostReadback;NoCrossFamilyHandoffCost;"
                    : "SubmitEnvelope=.15msPerPrivateStageWindow;IntermediateOutputUnknown=Full;CompleteCanonicalAbi1;") + "StageReferences=" +
                string.Join('|', evidence));
        }
    }
}
