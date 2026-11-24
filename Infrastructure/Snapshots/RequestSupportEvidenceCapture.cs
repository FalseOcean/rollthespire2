using System.Collections.Immutable;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Infrastructure.Snapshots;

/// <summary>Main-thread copies only. Missing observational evidence is descriptive, never an execution fault.</summary>
internal static class RequestSupportEvidenceCapture
{
    internal sealed record RequestInputs(object? UnlockState, IReadOnlyList<ModelKey>? DiscoveredActs,
        RequestFactSource UnlockSource, RequestFactSource DiscoverySource, string UnlockIssue, string DiscoveryIssue);

    internal static RequestInputs FreezeInputs(object? explicitUnlockState, IReadOnlyList<ModelKey>? explicitDiscoveredActs)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        object? unlock = explicitUnlockState;
        IReadOnlyList<ModelKey>? acts = explicitDiscoveredActs?.ToImmutableArray();
        var unlockSource = unlock is null ? RequestFactSource.Unknown : RequestFactSource.ExplicitRequest;
        var discoverySource = acts is null ? RequestFactSource.Unknown : RequestFactSource.ExplicitRequest;
        string unlockIssue = string.Empty, discoveryIssue = string.Empty;
        if (unlock is null)
        {
            try
            {
                var save = SaveManager.Instance;
                if (save.IsProfileInitialized)
                {
                    unlock = save.GenerateUnlockStateFromProgress();
                    if (unlock is not null) unlockSource = RequestFactSource.CurrentAccount;
                }
                else unlockIssue = "SaveProfileNotInitialized";
            }
            catch (Exception ex) { unlockIssue = "RequestUnlockCaptureUnavailable:" + ex; }
        }
        if (acts is null)
        {
            try
            {
                var save = SaveManager.Instance;
                if (save.IsProfileInitialized)
                {
                    acts = save.Progress.DiscoveredActs.Select(k => new ModelKey(k.Category, k.Entry)).ToImmutableArray();
                    discoverySource = RequestFactSource.CurrentAccount;
                }
                else discoveryIssue = "SaveProfileNotInitialized";
            }
            catch (Exception ex) { discoveryIssue = "RequestDiscoveryCaptureUnavailable:" + ex; }
        }
        return new(unlock, acts, unlockSource, discoverySource, unlockIssue, discoveryIssue);
    }

    internal static RequestSupportEvidence Capture(RequestInputs inputs)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        var mods = ImmutableArray.CreateBuilder<LoadedModEvidence>();
        bool inspected = false;
        string modEvidence;
        try
        {
            foreach (var mod in ModManager.Mods.Where(m => m.state == ModLoadState.Loaded))
            {
                if (string.Equals(mod.manifest?.id, ModEntry.ModId, StringComparison.OrdinalIgnoreCase)) continue;
                mods.Add(new(mod.manifest?.id ?? "UnknownLoadedMod", mod.manifest?.version ?? string.Empty));
            }
            inspected = ModManager.State == ModManagerState.Initialized;
            modEvidence = inspected ? "ModManager.Mods:Loaded:ExcludingRolltheSpire2" : "LoadedModInspectionNotInitialized:" + ModManager.State;
        }
        catch (Exception ex) { modEvidence = "LoadedModInspectionUnavailable:" + ex; }

        RequestUnlockEvidence unlock = CaptureUnlock(inputs);
        var acts = inputs.DiscoveredActs?.ToImmutableArray() ?? [];
        bool discoveryComplete = false;
        string discoveryEvidence = inputs.DiscoveredActs is null ? inputs.DiscoveryIssue : "RequestDiscoveredActs:" + inputs.DiscoverySource;
        try
        {
            var knownActs = ModelDb.ActsByIndex.SelectMany(group => group)
                .Select(act => new ModelKey(act.Id.Category, act.Id.Entry)).ToHashSet();
            discoveryComplete = inputs.DiscoveredActs is not null && acts.All(k => k.IsValid && knownActs.Contains(k)) && acts.Distinct().Count() == acts.Length;
            if (inputs.DiscoveredActs is not null && !discoveryComplete)
                discoveryEvidence += ";InvalidOrUnresolvedActIdentity";
        }
        catch (Exception ex) { discoveryEvidence = "RequestDiscoveryCatalogUnavailable:" + ex; }
        var discovery = new RequestDiscoveryEvidence(inputs.DiscoverySource, acts, discoveryComplete, discoveryEvidence);
        return new(inspected, mods.ToImmutable(), modEvidence, unlock, discovery);
    }

    private static RequestUnlockEvidence CaptureUnlock(RequestInputs inputs)
    {
        if (inputs.UnlockState is not UnlockState state)
            return RequestUnlockEvidence.Unknown with { CaptureEvidence = inputs.UnlockIssue.Length > 0 ? inputs.UnlockIssue : "RequestUnlockStateUnavailable" };
        try
        {
            var serialized = state.ToSerializable();
            return new(inputs.UnlockSource, serialized.UnlockedEpochs.ToImmutableArray(), true,
                serialized.EncountersSeen.Select(k => new ModelKey(k.Category, k.Entry)).ToImmutableArray(), serialized.NumberOfRuns,
                "RequestUnlockState:" + inputs.UnlockSource);
        }
        catch (Exception ex)
        {
            return RequestUnlockEvidence.Unknown with { Source = inputs.UnlockSource, CaptureEvidence = "RequestUnlockSerializationUnavailable:" + ex };
        }
    }
}
