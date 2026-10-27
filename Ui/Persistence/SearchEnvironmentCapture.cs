using RolltheSpire2.Core.World.Snapshots;
using System.Text.Json;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Infrastructure.Snapshots.RuntimeAuthority;
using MegaCrit.Sts2.Core.Unlocks;

namespace RolltheSpire2.Ui.Persistence;

internal static partial class SearchEnvironmentSignatureBuilder
{
    public static SearchEnvironmentSignature Capture(ModRuntimeSnapshot runtime)
    {
        if (!ProfileSeedGenerator.TryCreateProbeSeed(runtime.Profile, out string seed, out string issue))
            return new SearchEnvironmentSignature
            {
                GameVersionIdentity = runtime.Detection.DisplayVersion,
                CaptureComplete = false,
                CaptureIssue = issue + ";versionEvidence=" + runtime.Detection.Evidence
            };
        try
        {
            RuntimeContextAuthoritySnapshot authority = RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(
                runtime.Profile,
                seed,
                CharacterIdentity.FromKey(BaseGameModelKeys.Characters.Silent),
                SeedPredictionInputLimits.MaximumAscension,
                runtime.Detection.DisplayVersion,
                playersCount: 1,
                playerSlotIndex: 0,
                predictionGameMode: WorldGameMode.Singleplayer,
                predictionGameModeAuthority: PredictionGameModeAuthority.ExplicitRequest);

            return FromObservedInputs(authority, RuntimeAuthorityEnvironment.Current.Authority,
                MegaCrit.Sts2.Core.Saves.SaveManager.Instance.GenerateUnlockStateFromProgress().ToSerializable(),
                RuntimeAuthoritySnapshotCapture.CaptureCharacterStartingDecks(), CaptureModuleDiagnostics());
        }
        catch (Exception ex)
        {
            try { RuntimeLog.Warn($"searchEnvironmentSignatureCaptureFailed=true;failSoft=true;exception={ex}"); } catch { }
            return CaptureFailure(runtime, ex);
        }
    }

    internal static SearchEnvironmentSignature CaptureFailure(ModRuntimeSnapshot runtime, Exception ex) => new()
    {
        GameVersionIdentity = GameVersionIdentity.From(runtime.Detection.DisplayVersion).ToString(),
        CaptureComplete = false, CaptureIssue = ex.ToString(), Fingerprint = string.Empty
    };

    // All arguments are copied facts. This method performs no native observation.
    internal static SearchEnvironmentSignature FromObservedInputs(RuntimeContextAuthoritySnapshot authority,
        RuntimeAuthoritySnapshot global, SerializableUnlockState unlocks,
        IReadOnlyList<CharacterStartingDeckSnapshot>? startingDecks, string moduleDiagnostics)
    {
            var effect = authority.EffectAuthority;
            var world = authority.WorldAuthority?.Beta109Generation;
            ModelKey[] characters = (world?.AllCharactersInSourceOrder ?? BaseGameModelKeys.Characters.All)
                .Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance).OrderBy(key => key.Serialized, StringComparer.Ordinal).ToArray();
            ModelKey[] cards = EnumerateCards(effect)
                .Distinct(ModelKeyComparer.Instance).OrderBy(key => key.Serialized, StringComparer.Ordinal).ToArray();
            ModelKey[] relics = EnumerateRelics(effect, world)
                .Distinct(ModelKeyComparer.Instance).OrderBy(key => key.Serialized, StringComparer.Ordinal).ToArray();
            ModelKey[] potions = (effect?.PotionPool ?? Array.Empty<RolltheSpire2.Core.Effects.Snapshots.NeowEffectPotionSnapshot>())
                .Select(item => item.PotionKey).Where(key => key.IsValid)
                .Distinct(ModelKeyComparer.Instance).OrderBy(key => key.Serialized, StringComparer.Ordinal).ToArray();
            ModelKey[] ancients = (world?.OrderedActCatalog ?? Array.Empty<RolltheSpire2.Core.World.Snapshots.Beta109ActGenerationSnapshot>())
                .SelectMany(act => act.OrderedAncients)
                .Concat(world?.AllSharedAncients ?? Array.Empty<ModelKey>())
                .Where(key => key.IsValid).Distinct(ModelKeyComparer.Instance).OrderBy(key => key.Serialized, StringComparer.Ordinal).ToArray();

            string gameVersion = authority.ExactGameVersionIdentity.ToString();
            var signature = FromCapturedInputs(gameVersion, global, effect, world, unlocks, startingDecks: startingDecks);
            // Loaded code identities remain useful diagnostic evidence. They do
            // not change the environment unless captured content/unlocks change.
            signature.RuntimeModuleDiagnostics = moduleDiagnostics;
            signature.CharacterCount = characters.Length;
            signature.CardCount = cards.Length;
            signature.RelicCount = relics.Length;
            signature.PotionCount = potions.Length;
            signature.AncientCount = ancients.Length;
            return signature;
    }

    internal static string CaptureModuleDiagnostics()
    {
            RuntimeSnapshotThreadGuard.RequireMainThread();
            try { return JsonSerializer.Serialize(new
            {
                GameModule = typeof(MegaCrit.Sts2.Core.Models.ModelDb).Assembly.ManifestModule.ModuleVersionId,
                Mods = MegaCrit.Sts2.Core.Modding.ModManager.Mods
                    .Where(m => m.state == MegaCrit.Sts2.Core.Modding.ModLoadState.Loaded)
                    .Select(m => new { m.manifest?.id, m.manifest?.version,
                        Modules = m.assemblies.Select(a => a.ManifestModule.ModuleVersionId).ToArray() }).ToArray()
            }); }
            catch (Exception diagnosticsFailure) { return diagnosticsFailure.ToString(); }
    }

    private static IEnumerable<ModelKey> EnumerateCards(RolltheSpire2.Core.Effects.Snapshots.NeowEffectAuthoritySnapshot? effect)
    {
        if (effect is null) yield break;
        foreach (var card in effect.CharacterRewardPool ?? Array.Empty<RolltheSpire2.Core.Effects.Snapshots.NeowEffectCardSnapshot>()) yield return card.CardKey;
        foreach (var card in effect.ColorlessRewardPool ?? Array.Empty<RolltheSpire2.Core.Effects.Snapshots.NeowEffectCardSnapshot>()) yield return card.CardKey;
        foreach (var pool in effect.OtherCharacterPools ?? Array.Empty<RolltheSpire2.Core.Effects.Snapshots.CharacterCardPoolSnapshot>())
        foreach (var card in pool.Cards) yield return card.CardKey;
        foreach (var card in effect.TransformPool ?? Array.Empty<RolltheSpire2.Core.Effects.Snapshots.NeowEffectCardSnapshot>()) yield return card.CardKey;
        foreach (ModelKey curse in effect.GeneratedCursePool ?? Array.Empty<ModelKey>()) yield return curse;
    }

    private static IEnumerable<ModelKey> EnumerateRelics(
        RolltheSpire2.Core.Effects.Snapshots.NeowEffectAuthoritySnapshot? effect,
        RolltheSpire2.Core.World.Snapshots.Beta109WorldGenerationSnapshot? world)
    {
        foreach (var relic in effect?.OrderedRelicBag ?? Array.Empty<RolltheSpire2.Core.Effects.Snapshots.NeowEffectRelicSnapshot>()) yield return relic.RelicKey;
        foreach (var relic in effect?.SharedRelicPoolSource ?? Array.Empty<RolltheSpire2.Core.Effects.Snapshots.NeowEffectRelicSnapshot>()) yield return relic.RelicKey;
        foreach (var relic in effect?.CharacterRelicPoolSource ?? Array.Empty<RolltheSpire2.Core.Effects.Snapshots.NeowEffectRelicSnapshot>()) yield return relic.RelicKey;
        foreach (var bucket in world?.SharedRelicBuckets ?? Array.Empty<RolltheSpire2.Core.World.Snapshots.Beta109RelicBucketSnapshot>())
        foreach (ModelKey relic in bucket.OrderedRelics) yield return relic;
        foreach (var bucket in world?.PlayerRelicBuckets ?? Array.Empty<RolltheSpire2.Core.World.Snapshots.Beta109RelicBucketSnapshot>())
        foreach (ModelKey relic in bucket.OrderedRelics) yield return relic;
    }

}
