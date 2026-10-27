using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;

namespace RolltheSpire2.Core.Authority;

public enum RequestSupportScopeState { Unknown, WithinDeclaredBaseline, OutsideDeclaredBaseline }
public enum RequestFactSource { Unknown, ExplicitRequest, CurrentAccount }

public sealed record LoadedModEvidence(string Id, string Version);

/// <summary>Copied facts of the actual request/seat, never the global account interpretation.</summary>
public sealed record RequestUnlockEvidence(
    RequestFactSource Source,
    ImmutableArray<string> RevealedEpochs,
    bool Complete,
    ImmutableArray<ModelKey> SeenEncounters,
    int? NumberOfRuns,
    string CaptureEvidence)
{
    public static RequestUnlockEvidence Unknown { get; } = new(RequestFactSource.Unknown, [], false, [], null, "RequestUnlockNotCaptured");
}

public sealed record RequestDiscoveryEvidence(
    RequestFactSource Source,
    ImmutableArray<ModelKey> Acts,
    bool Complete,
    string CaptureEvidence)
{
    public static RequestDiscoveryEvidence Unknown { get; } = new(RequestFactSource.Unknown, [], false, "RequestDiscoveryNotCaptured");
}

/// <summary>Descriptive request evidence; neither model precision nor admission authority.</summary>
public sealed record RequestSupportEvidence(
    bool LoadedModsInspected,
    ImmutableArray<LoadedModEvidence> LoadedOtherMods,
    string ModInspectionEvidence,
    RequestUnlockEvidence Unlock,
    RequestDiscoveryEvidence Discovery)
{
    public static RequestSupportEvidence Unknown { get; } = new(false, [], "LoadedModsNotInspected", RequestUnlockEvidence.Unknown, RequestDiscoveryEvidence.Unknown);
}

/// <summary>
/// Membership in the Owner's declared real-game baseline, conditional on the player's
/// declared operations. It is not a guarantee of a model result and must never gate execution.
/// </summary>
public sealed record RequestSupportAssessment(
    RequestSupportScopeState State,
    ImmutableArray<string> Reasons,
    RequestSupportEvidence Evidence,
    string BaselineGameVersion,
    string PlayerOperationCondition)
{
    public const string CurrentBaselineGameVersion = "0.111.0";
    public const string DeclaredPlayerOperationCondition = "UnderDeclaredPlayerOperations";

    public static RequestSupportAssessment Assess(RuntimeContextAuthoritySnapshot context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var reasons = ImmutableArray.CreateBuilder<string>();
        bool outside = false;
        bool unknown = false;
        void Outside(string reason) { outside = true; reasons.Add(reason); }
        void Unknown(string reason) { unknown = true; reasons.Add(reason); }
        if (!context.ExactGameVersionIdentity.IsExact) Unknown("GameVersionUnknown");
        else if (context.ExactGameVersionIdentity != GameVersionIdentity.V0_111_0) Outside("GameVersionOutsideDeclaredBaseline");
        if (!context.Character.IsValid) Unknown("CharacterUnknown");
        else if (!context.Character.IsKnownVanilla) Outside("CharacterOutsideDeclaredBaseline");
        if (context.PlayersCount < 1 || context.PlayerSlotIndex < 0 || context.PlayerSlotIndex >= context.PlayersCount) Unknown("PlayerContextUnknown");
        else if (context.PlayersCount != 1 || context.PlayerSlotIndex != 0 || context.PredictionGameMode == WorldGameMode.Multiplayer) Outside("PartyOutsideDeclaredBaseline");
        if (context.NoRunModifiers == false) Outside("RunModifiersOutsideDeclaredBaseline");
        else if (context.NoRunModifiers is null) Unknown("RunModifiersUnknown");
        RequestSupportEvidence evidence = context.SupportEvidence;
        if (!evidence.LoadedOtherMods.IsDefaultOrEmpty) Outside("LoadedOtherModsOutsideDeclaredBaseline");
        if (!evidence.LoadedModsInspected) Unknown("LoadedModsUnknown");
        if (evidence.Unlock.Source == RequestFactSource.Unknown || !evidence.Unlock.Complete || evidence.Unlock.RevealedEpochs.IsDefault)
            Unknown("RequestEpochUnlockUnknown");
        else
        {
            var unlocked = evidence.Unlock.RevealedEpochs.ToHashSet(StringComparer.Ordinal);
            foreach (string epoch in Vanilla111EpochBaseline.EpochIds)
                if (!unlocked.Contains(epoch)) Outside("RequestEpochNotUnlocked:" + epoch);
        }
        if (!outside && !unknown) reasons.Add("WithinDeclaredBaselineUnderDeclaredPlayerOperations");
        return new(outside ? RequestSupportScopeState.OutsideDeclaredBaseline : unknown ? RequestSupportScopeState.Unknown : RequestSupportScopeState.WithinDeclaredBaseline,
            reasons.ToImmutable(), evidence, CurrentBaselineGameVersion, DeclaredPlayerOperationCondition);
    }
}
