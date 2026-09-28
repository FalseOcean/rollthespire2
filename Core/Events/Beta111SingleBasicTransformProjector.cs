using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World.Beta109;

namespace RolltheSpire2.Core.Events;

// Explicit source whitelist. Neither event proves occurrence or continuation of a
// real deck. Aroma/LetGo and Whisper/Hug commit one legal initial Basic at entry.
public enum SingleBasicTransformEvent { AromaOfChaos, WhisperingHollow }

public static class Beta111SingleBasicTransformProjector
{
    public static MorphicGroveTransform Project(ulong rootHash, int playerSlot,
        SingleBasicTransformEvent source, MorphicGrovePremises premises, MorphicGroveTarget target)
    {
        if (!Enum.IsDefined(source) || playerSlot != 0 || string.IsNullOrWhiteSpace(premises.EventOccurrenceBasis) ||
            !target.Original.CanTransform || target.Original.CardType == "Quest")
            throw new ArgumentException("SingleBasicTransform.InvalidCommittedEntry");
        string entry = source == SingleBasicTransformEvent.AromaOfChaos ? "AROMA_OF_CHAOS" : "WHISPERING_HOLLOW";
        var rng = Beta109WorldRng.CreateEventLocal(rootHash, playerSlot, false, entry);
        // Whisper.CalculateVars always precedes GenerateInitialOptions/Hug.
        if (source == SingleBasicTransformEvent.WhisperingHollow) rng.NextInt(19, "CalculateVars:gold-minus-nine");
        MorphicGroveCard? raw = null;
        if (premises.VanillaLocalDependenciesBound && target.OrderedSourceCandidates is { } pool)
        {
            if (pool.Count == 0) throw new InvalidOperationException("SingleBasicTransform.EmptySourcePool");
            raw = pool[rng.NextInt(pool.Count, source + ":transform")];
        }
        bool finalKnown = raw is not null && premises.NoDeckMutationHooks;
        return new(target.InstanceId, raw, finalKnown ? raw : null, finalKnown ? premises.EntryFloor : null,
            raw is null ? PredictionPrecision.Unknown : PredictionPrecision.Exact,
            finalKnown ? PredictionPrecision.Exact : PredictionPrecision.Unknown);
    }
}
