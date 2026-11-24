using RolltheSpire2.Core.Merchant;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Search.FamilyExecution;

// S owns only its immutable Shops stream inputs and numerical plans. The GPU
// buffers and shader variant are prepared here, before any device is acquired.
internal sealed record MerchantShopColorlessPreparation(
    ExactSearchEvaluationProjection Evaluation,
    Beta111MerchantColorlessAuthority Authority,
    FilterPricingAuthority PricingAuthority,
    MerchantShopColorlessCpuPlan? CpuPlan,
    MerchantShopColorlessGpuPreparation? GpuPlan,
    string GpuPreparationFailure,
    Func<ulong, bool> Reference)
{
    internal RolltheSpire2.Compatibility.RuntimeProfileId ProfileId => PricingAuthority.ProfileId;
    internal static MerchantShopColorlessPreparation Prepare(SearchModel model, bool prepareGpu)
    {
        var evaluation = ExactSearchEvaluationProjection.Empty with {
            MerchantColorlessConditions = model.Evaluation.MerchantColorlessConditions,
            MerchantColorlessSequenceConditions = model.Evaluation.MerchantColorlessSequenceConditions
        };
        var authority = Beta111MerchantColorlessAuthority.From(model.Authority);
        var referenceAuthority = model.Authority;
        MerchantShopColorlessGpuPreparation? gpu = null;
        string gpuFailure = "";
        if (prepareGpu)
        {
            try { gpu = MerchantShopColorlessGpuExecutor.PrepareInputs(evaluation, authority, model.SnapshotFingerprint); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error) { gpuFailure = error.ToString(); }
        }
        return new(evaluation, authority, FilterPricingAuthority.From(model),
            MerchantShopColorlessCpuPlan.TryCompile(evaluation, authority),
            gpu, gpuFailure,
            root => MerchantShopColorlessQueryEvaluator.Evaluate(evaluation, root, referenceAuthority).Disposition != SearchDisposition.NoMatch);
    }
}
