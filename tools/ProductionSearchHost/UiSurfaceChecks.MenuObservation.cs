using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;
using MegaCrit.Sts2.Core.Timeline.Epochs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots.RuntimeAuthority;

internal static partial class UiSurfaceChecks
{
    // Existing PCK/ModelDb/MockSave host only. No RunState is created. Native
    // mutations are controlled probes against actual inputs, restored in finally.
    private static async Task CheckMenuObservationAsync(Node root, ModRuntimeSnapshot runtime, Action<bool, string> check)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        int mainThread = System.Environment.CurrentManagedThreadId;
        var synchronizationContext = SynchronizationContext.Current;
        string version = runtime.Detection.DisplayVersion;
        string before = MenuNativeSignature();
        var originalEnvironment = RuntimeAuthorityEnvironment.Current;
        var profile = Beta111Profile.Instance;
        var character = CharacterIdentity.FromKey(BaseGameModelKeys.Characters.Ironclad);
        const string seed = "000000000000";

        // This verifies the new global synchronous wrapper against its sliced
        // route, not an independent historical implementation.
        RuntimeAuthoritySnapshot synchronous = RuntimeAuthoritySnapshotCapture.Capture(version);
        using var global = RuntimeAuthoritySnapshotCapture.BeginObservation(version);
        global.Advance(TimeSpan.FromTicks(1));
        check(!global.IsCompleted, "global observation yields before full capture");
        int globalFrames = 0;
        while (!global.IsCompleted)
        {
            global.Advance(TimeSpan.FromMilliseconds(2));
            check(System.Environment.CurrentManagedThreadId == mainThread &&
                ReferenceEquals(SynchronizationContext.Current, synchronizationContext), "global slice retains main thread/context");
            if (!global.IsCompleted) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (++globalFrames > 1000) throw new InvalidOperationException("MenuObservation: global cursor did not complete");
        }
        var globalInputs = global.Result;
        var workerGlobal = await Task.Run(() =>
        {
            bool offThread = System.Environment.CurrentManagedThreadId != mainThread;
            return (Snapshot: globalInputs.Derive(), OffThread: offThread);
        });
        check(workerGlobal.OffThread, "global derivation executes on worker");
        check(MenuSameValues(synchronous, workerGlobal.Snapshot), "global sliced worker derivation preserves all semantic fields, fingerprints, unlocks and reasons");
        using (var fresh = RuntimeAuthoritySnapshotCapture.BeginObservation(version))
            check(globalInputs.SemanticEquals(fresh.CompleteOnMainThread()), "global candidate matches fresh complete native observation");
        check(ReferenceEquals(originalEnvironment, RuntimeAuthorityEnvironment.Current), "candidate observation does not publish Current");

        var synchronousContext = RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(profile, seed, character, 10, version,
            predictionGameMode: WorldGameMode.Singleplayer, predictionGameModeAuthority: PredictionGameModeAuthority.ExplicitRequest);
        var directContext = MenuDirectAdapterContext(profile, seed, character, version);
        check(MenuSameValues(synchronousContext, directContext), "context preserves direct adapter composition values including provenance and reasons");
        var missingContext = RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(profile, "!invalid!", character, 10, version,
            predictionGameMode: WorldGameMode.Singleplayer, predictionGameModeAuthority: PredictionGameModeAuthority.ExplicitRequest);
        check(MenuSameValues(missingContext, MenuDirectAdapterContext(profile, "!invalid!", character, version)),
            "invalid-seed context preserves direct adapter missing values and original failure reasons");
        using var context = RuntimeContextAuthorityCapture.BeginObservation(profile, seed, character, 10, version,
            predictionGameMode: WorldGameMode.Singleplayer, predictionGameModeAuthority: PredictionGameModeAuthority.ExplicitRequest);
        context.Advance(TimeSpan.Zero);
        check(!context.IsCompleted, "context observation yields before full capture");
        check(ReferenceEquals(SynchronizationContext.Current, synchronizationContext), "context initial yield restores synchronization context");
        int contextFrames = 0;
        while (!context.IsCompleted)
        {
            context.Advance(TimeSpan.FromMilliseconds(2));
            check(System.Environment.CurrentManagedThreadId == mainThread &&
                ReferenceEquals(SynchronizationContext.Current, synchronizationContext), "context slice returns inline on original main thread/context");
            if (!context.IsCompleted) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (++contextFrames > 1000) throw new InvalidOperationException("MenuObservation: context cursor did not complete");
        }
        RuntimeContextObservedInputs contextInputs = context.Result;
        var workerContext = await Task.Run(() =>
        {
            bool offThread = System.Environment.CurrentManagedThreadId != mainThread;
            return (Snapshot: contextInputs.Derive(), OffThread: offThread);
        });
        check(workerContext.OffThread, "context derivation executes on worker");
        check(MenuSameValues(synchronousContext, workerContext.Snapshot), "context sliced worker derivation preserves full snapshot values");
        using (var fresh = NewContext())
            check(contextInputs.SemanticEquals(fresh.CompleteOnMainThread()), "context candidate matches fresh native comparer matrix and all other observations");
        check(before == MenuNativeSignature(), "observation and worker derivation preserve native models, account progress and any existing RunState/RNG");

        // Worker access is rejected by the real thread guard, not a fake adapter.
        check(await Task.Run(() => ThrowsMainThread(() => RuntimeAuthoritySnapshotCapture.BeginObservation(version))),
            "global observation rejects a worker native entry");
        check(await Task.Run(() => ThrowsMainThread(() => RuntimeContextAuthorityCapture.BeginObservation(profile, seed, character, 10, version))),
            "context observation rejects a worker native entry");
        using (var retired = RuntimeAuthoritySnapshotCapture.BeginObservation(version))
        {
            retired.Advance(TimeSpan.FromTicks(1));
            retired.Dispose();
            bool rejected = false;
            try { retired.Advance(TimeSpan.FromMilliseconds(2)); } catch (ObjectDisposedException) { rejected = true; }
            check(rejected && !retired.IsCompleted, "retired global cursor cannot continue or publish a result");
        }
        using (var retired = NewContext())
        {
            retired.Advance(TimeSpan.Zero);
            retired.Dispose();
            bool rejected = false;
            try { retired.CompleteOnMainThread(); } catch (OperationCanceledException) { rejected = true; }
            check(rejected, "retired context cursor retains cancellation");
            check(ReferenceEquals(SynchronizationContext.Current, synchronizationContext), "retiring context restores caller synchronization context");
        }

        // Use the same actual pool field seam as OperationFreshness. Identity and
        // ModManager stay constant while the authoritative pool order changes.
        var pool = ModelDb.Character<Ironclad>().CardPool;
        FieldInfo poolField = typeof(CardPoolModel).GetField("_allCards", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object? originalPool = poolField.GetValue(pool);
        var poolCards = pool.AllCards.ToArray();
        var card = pool.GetUnlockedCards(SaveManager.Instance.GenerateUnlockStateFromProgress(), CardMultiplayerConstraint.SingleplayerOnly)
            .First(card => card.Rarity == CardRarity.Common);
        var originalCardId = card.Id;
        FieldInfo rarityField = typeof(CardModel).GetField("<Rarity>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object? originalRarity = rarityField.GetValue(card);
        var starter = ModelDb.Character<Ironclad>().StartingDeck.First();
        FieldInfo upgradeField = typeof(CardModel).GetField("_currentUpgradeLevel", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object? originalUpgrade = upgradeField.GetValue(starter);
        ProgressState originalProgress = SaveManager.Instance.Progress;
        try
        {
            poolField.SetValue(pool, poolCards.AsEnumerable().Reverse().ToArray());
            using (var changed = RuntimeAuthoritySnapshotCapture.BeginObservation(version))
                check(!globalInputs.SemanticEquals(changed.CompleteOnMainThread()), "same pool identity with changed native ordered contents rejects global candidate");
            using (var changed = NewContext())
                check(!contextInputs.SemanticEquals(changed.CompleteOnMainThread()), "changed actual pool order rejects context candidate");
            poolField.SetValue(pool, originalPool);

            rarityField.SetValue(card, CardRarity.Rare);
            check(card.Id == originalCardId && card.Rarity == CardRarity.Rare,
                "property probe changes actual model value while retaining native identity");
            using (var changed = RuntimeAuthoritySnapshotCapture.BeginObservation(version))
                check(!globalInputs.SemanticEquals(changed.CompleteOnMainThread()), "same model identity with changed native rarity rejects global candidate");
            using (var changed = NewContext())
                check(!contextInputs.SemanticEquals(changed.CompleteOnMainThread()), "same model identity with changed native rarity rejects context candidate");
            rarityField.SetValue(card, originalRarity);

            // Mutate only the isolated mock profile, through its real public
            // progress API; restore the original profile object in finally.
            SaveManager.Instance.Progress = new ProgressState();
            using var emptyGlobal = RuntimeAuthoritySnapshotCapture.BeginObservation(version);
            var emptyGlobalInputs = emptyGlobal.CompleteOnMainThread();
            using var emptyContext = NewContext();
            var emptyContextInputs = emptyContext.CompleteOnMainThread();
            SaveManager.Instance.Progress.ObtainEpochOverride(EpochModel.GetId<Ironclad2Epoch>(), EpochState.Revealed);
            using (var changed = RuntimeAuthoritySnapshotCapture.BeginObservation(version))
                check(!emptyGlobalInputs.SemanticEquals(changed.CompleteOnMainThread()), "revealed native card epoch rejects global current-unlock candidate");
            using (var changed = NewContext())
                check(!emptyContextInputs.SemanticEquals(changed.CompleteOnMainThread()), "revealed native card epoch rejects context account candidate");
            SaveManager.Instance.Progress = originalProgress;

            upgradeField.SetValue(starter, 1);
            check(ModelDb.Character<Ironclad>().StartingDeck.First().CurrentUpgradeLevel == 1,
                "starter probe changes actual StartingDeck model observation");
            using (var changed = NewContext())
            {
                var inputs = changed.CompleteOnMainThread();
                check(!contextInputs.SemanticEquals(inputs), "native starting deck state change rejects context candidate");
                check(!MenuSameValues(contextInputs.Effect.Deck, inputs.Effect.Deck), "starting deck invalidation includes actual copied deck values");
            }
        }
        finally
        {
            poolField.SetValue(pool, originalPool);
            rarityField.SetValue(card, originalRarity);
            upgradeField.SetValue(starter, originalUpgrade);
            SaveManager.Instance.Progress = originalProgress;
        }
        check(before == MenuNativeSignature(), "all native observation probes restore the exact original fixture signature");
        using (var restored = RuntimeAuthoritySnapshotCapture.BeginObservation(version))
            check(globalInputs.SemanticEquals(restored.CompleteOnMainThread()), "restored native fixture matches original global inputs");
        using (var restored = NewContext())
            check(contextInputs.SemanticEquals(restored.CompleteOnMainThread()), "restored native fixture matches original context inputs");
        GD.Print($"MENU_OBSERVATION_PASS globalFrames={globalFrames};contextFrames={contextFrames};directAdapterCompositionParity=true;nativeRestored=true;scope=isolated-menu-modeldb-mocksave");

        RuntimeContextObservation NewContext() => RuntimeContextAuthorityCapture.BeginObservation(profile, seed, character, 10, version,
            predictionGameMode: WorldGameMode.Singleplayer, predictionGameModeAuthority: PredictionGameModeAuthority.ExplicitRequest);
    }

    private static bool ThrowsMainThread(Action action)
    {
        try { action(); return false; }
        catch (InvalidOperationException ex) when (ex.Message == "RuntimeSnapshotCaptureRequiresGodotMainThread") { return true; }
    }

    // Only capture timestamps are diagnostic. All fingerprints, missing reasons,
    // authority flags, primitive values, native comparer results and order remain.
    private static bool MenuSameValues(object? left, object? right)
    {
        JsonNode? Freeze(object? value)
        {
            var node = JsonSerializer.SerializeToNode(value, value?.GetType() ?? typeof(object));
            void RemoveTimes(JsonNode? current)
            {
                if (current is JsonObject obj)
                {
                    obj.Remove("CapturedAtUtc");
                    foreach (var entry in obj) RemoveTimes(entry.Value);
                }
                else if (current is JsonArray array) foreach (var item in array) RemoveTimes(item);
            }
            RemoveTimes(node);
            return node;
        }
        return JsonNode.DeepEquals(Freeze(left), Freeze(right));
    }

    private static RuntimeContextAuthoritySnapshot MenuDirectAdapterContext(IRuntimeProfile profile, string seed,
        CharacterIdentity character, string version)
    {
        var request = RequestSupportEvidenceCapture.FreezeInputs(null, null);
        var captured = ReflectionNeowEffectSnapshotAdapter.Capture(profile, seed, character, 10, 1, 0,
            profile.ProfileId, version, new(true, true), request.UnlockState);
        var world = ReflectionNeowEffectSnapshotAdapter.CaptureWorld(profile, seed, character, 10, 1, 0, true,
            captured.EffectAuthority, WorldGameMode.Singleplayer, PredictionGameModeAuthority.ExplicitRequest, version,
            explicitUnlockState: request.UnlockState, explicitDiscoveredActs: request.DiscoveredActs);
        bool unlockCaptured = !string.IsNullOrWhiteSpace(captured.UnlockFingerprint);
        bool exact = character.IsKnownVanilla && unlockCaptured && captured.CharacterResolution == IdentityResolutionStatus.Exact &&
            SourceAuthorityRules.SupportsExactIdentity(captured.UnlockAuthority);
        SourceAuthority source = exact ? SourceAuthority.OfficialRuntimeExact : character.IsKnownVanilla ? SourceAuthority.Incomplete :
            captured.UnlockAuthority == SourceAuthority.Unknown ? SourceAuthority.ModdedRuntimeBestEffort : captured.UnlockAuthority;
        return new(character, 10, 0, 1, true, ReflectionNeowEffectSnapshotAdapter.CaptureVanillaNeowCatalogExact(out _),
            captured.AllCharacterCardPoolsUnlocked, captured.UnlockedCommonCards, captured.UnlockedUncommonCards,
            version, profile.ProfileId, character.IsKnownVanilla, character.IsKnownVanilla ? null : "runtime-character",
            character.IsKnownVanilla ? "sts2" : null, source, unlockCaptured ? captured.UnlockCompleteness : SnapshotCompleteness.Missing,
            captured.CharacterResolution, captured.EffectAuthority, world, WorldGameMode.Singleplayer,
            PredictionGameModeAuthority.ExplicitRequest, supportEvidence: RequestSupportEvidenceCapture.Capture(request));
    }

    private static string MenuNativeSignature()
    {
        // Read any pre-existing real run without constructing one for this check.
        var run = typeof(RunManager).GetProperty("State", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(RunManager.Instance) as RunState;
        return JsonSerializer.Serialize(new
        {
            Account = SaveManager.Instance.Progress.ToSerializable(),
            Models = ModelDb.All.Select(model => model.Id),
            Cards = ModelDb.AllCards.Select(card => new { card.Id, card.Rarity, card.Type, card.CurrentUpgradeLevel, card.IsUpgradable }),
            Pools = ModelDb.AllCardPools.Select(pool => new { pool.Id, Cards = pool.AllCards.Select(card => card.Id) }),
            Deck = ModelDb.Character<Ironclad>().StartingDeck.Select(card => new { card.Id, card.CurrentUpgradeLevel }),
            RunRng = run?.Rng.ToSerializable(), Players = run?.Players.Select(player => player.ToSerializable()),
            SharedBag = run is null ? null : PredictorLiveRunCapture.Bag(run.SharedRelicGrabBag),
            PersonalBags = run?.Players.Select(player => PredictorLiveRunCapture.Bag(player.RelicGrabBag))
        });
    }
}
