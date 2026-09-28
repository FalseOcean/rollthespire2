using System.Reflection;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class ReflectionNeowEffectSnapshotAdapter
{
    private static Beta109EventCatalogAuthoritySnapshot CaptureBeta109EventAuthority(
        Assembly assembly,
        object? unlockState,
        ModelListCapture sharedEventsRaw)
    {
        string[] epochTypeNames =
        {
            "MegaCrit.Sts2.Core.Timeline.Epochs.Event1Epoch",
            "MegaCrit.Sts2.Core.Timeline.Epochs.Event2Epoch",
            "MegaCrit.Sts2.Core.Timeline.Epochs.Event3Epoch"
        };
        string[] epochIds = { "EVENT1_EPOCH", "EVENT2_EPOCH", "EVENT3_EPOCH" };
        var epochs = new List<Beta109EventEpochSnapshot>(epochTypeNames.Length);
        // Event1 -> Event2 -> Event3 is a direct-source versioned rule.
        // Membership and reveal facts have independent exactness flags and must
        // not collapse the known filter-order authority.
        const bool filterOrderExact = true;

        for (int index = 0; index < epochTypeNames.Length; index++)
        {
            Type? epochType = FindType(assembly, epochTypeNames[index], epochTypeNames[index].Split('.').Last());
            if (epochType is null || epochType.Assembly != assembly)
            {
                epochs.Add(new Beta109EventEpochSnapshot(
                    epochIds[index],
                    Array.Empty<ModelKey>(),
                    MembershipExact: false,
                    IsRevealed: false,
                    RevealFactExact: false));
                continue;
            }

            ModelListCapture membership = CapturePossiblyEmptyStaticModelsWorld(epochType, "Events");
            (bool revealed, bool revealExact) = CaptureBeta109EpochRevealFact(unlockState, epochType);
            epochs.Add(new Beta109EventEpochSnapshot(
                epochIds[index],
                membership.Keys,
                membership.Exact,
                revealed,
                revealExact));
        }

        bool epochFactsExact = epochs.Count == epochTypeNames.Length &&
                               epochs.All(epoch => epoch.MembershipExact && epoch.RevealFactExact);
        string evidence = epochFactsExact
            ? "ActModel.GenerateRooms:Event1>Event2>Event3"
            : string.Join("|", epochs.Select(epoch =>
                $"{epoch.EpochId}:membership={epoch.MembershipExact}:reveal={epoch.RevealFactExact}"));
        (int characterCardPoolCount, bool characterCardPoolCountExact) =
            CaptureCharacterCardPoolCount(unlockState);
        return new Beta109EventCatalogAuthoritySnapshot(
            sharedEventsRaw.Keys,
            sharedEventsRaw.Exact,
            epochs,
            filterOrderExact,
            evidence)
        {
            CharacterCardPoolCount = characterCardPoolCount,
            CharacterCardPoolCountExact = characterCardPoolCountExact
        };
    }


    private static (int Count, bool Exact) CaptureCharacterCardPoolCount(object? unlockState)
    {
        if (unlockState is null) return (0, false);
        object? pools = ReadProperty(unlockState, "CharacterCardPools");
        if (pools is null) return (0, false);
        try
        {
            return (Enumerate(pools).Count(), true);
        }
        catch
        {
            return (0, false);
        }
    }

    private static (bool Value, bool Exact) CaptureBeta109EpochRevealFact(
        object? unlockState,
        Type epochType)
    {
        if (unlockState is null) return (false, false);
        MethodInfo? method = unlockState.GetType()
            .GetMethods(PublicInstance | BindingFlags.NonPublic)
            .Where(candidate => string.Equals(candidate.Name, "IsEpochRevealed", StringComparison.Ordinal))
            .Where(candidate => candidate.IsGenericMethodDefinition)
            .FirstOrDefault(candidate =>
                candidate.GetGenericArguments().Length == 1 &&
                candidate.GetParameters().Length == 0 &&
                candidate.ReturnType == typeof(bool));
        if (method is null) return (false, false);
        try
        {
            MethodInfo closed = method.MakeGenericMethod(epochType);
            return closed.Invoke(unlockState, null) is bool result
                ? (result, true)
                : (false, false);
        }
        catch
        {
            return (false, false);
        }
    }

    private static IReadOnlyList<ModelKey> FilterBeta109Events(
        IEnumerable<ModelKey> rawActEvents,
        IEnumerable<ModelKey> rawSharedEvents,
        Beta109EventCatalogAuthoritySnapshot authority)
    {
        var result = rawActEvents.Concat(rawSharedEvents).ToList();
        foreach (Beta109EventEpochSnapshot epoch in authority.EpochsInFilterOrder)
        {
            if (epoch.IsRevealed) continue;
            HashSet<ModelKey> hidden = epoch.OrderedMemberKeys.ToHashSet(ModelKeyComparer.Instance);
            result.RemoveAll(key => hidden.Contains(key));
        }
        return result;
    }
}
