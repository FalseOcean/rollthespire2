using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class ReflectionNeowEffectSnapshotAdapter
{
    private static async Task<(IReadOnlyList<CharacterCardPoolSnapshot>? Pools, string? Issue)> ObserveKaleidoscopePools(
        object character, object unlocks, IReadOnlyList<CharacterCardPoolSnapshot>? rawPools,
        int playersCount, int ascension, int playerSlotIndex, string canonicalSeed, NativeObservationSchedule schedule,
        IReadOnlyList<(ModelKey Character, UnlockState Unlocks)>? partyRoster)
    {
        if (rawPools is null) return (null, null);
        string correlation = $"character={character};players={playersCount}";
        try
        {
            var unlockState = (UnlockState)unlocks;
            var nativeRoster = partyRoster?.Select(seat => (
                Character: ModelDb.GetById<CharacterModel>(new ModelId(seat.Character.Category, seat.Character.Entry)),
                seat.Unlocks)).ToArray();
            var player = KaleidoscopeCardCreationCapture.CreateDetachedPlayer((CharacterModel)character, unlockState, playersCount,
                ascension: ascension, playerSlotIndex: playerSlotIndex, seed: canonicalSeed, roster: nativeRoster);
            KaleidoscopeCardCreationCapture.CheckRewardResultHooks(player);
            var sources = unlockState.CharacterCardPools.ToLookup(p => new Core.Identity.ModelKey(p.Id.Category, p.Id.Entry).Serialized);
            var result = new List<CharacterCardPoolSnapshot>(rawPools.Count);
            bool changed = false;
            foreach (var raw in rawPools)
            {
                await schedule.Checkpoint("Kaleidoscope:" + raw.PoolId).ConfigureAwait(false);
                try
                {
                    var models = KaleidoscopeCardCreationCapture.GetPossibleCards(player, sources[raw.PoolId].First());
                    if (raw.Cards.Count == models.Count && models.Select((m, i) =>
                        m.IsCanonical && TryModelKey(m, out var key) && key == raw.Cards[i].CardKey).All(equal => equal))
                    {
                        result.Add(raw);
                        continue;
                    }
                    var cards = new List<NeowEffectCardSnapshot>(models.Count);
                    foreach (var model in models)
                    {
                        await schedule.Checkpoint("KaleidoscopeCard").ConfigureAwait(false);
                        cards.Add(ConvertCardModel(model, cards.Count, raw.PoolId)
                            ?? throw new InvalidOperationException($"UnrecognizedKaleidoscopeCard:{model.Id}"));
                    }
                    result.Add(raw with { Cards = cards });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    result.Add(raw with { Cards = [], CandidateCaptureIssue = Failure(ex, correlation + ";pool=" + raw.PoolId) });
                }
                changed = true;
            }
            return (changed ? result : null, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, Failure(ex, correlation));
        }

        static string Failure(Exception ex, string context)
        {
            Diagnostics.RuntimeIssueLedger.Current.Record(ex, "KaleidoscopeCandidatesUnavailable",
                nameof(NeowEffectRuntimeSnapshotCapture), "KaleidoscopeCandidates", "issues.capture_failed", correlationId: context);
            return $"KaleidoscopeCandidatesUnavailable:{ex.GetType().Name}:{ex.Message}";
        }
    }

    private static IEnumerable<string> EnumerateKaleidoscopeDescriptors(
        IReadOnlyList<CharacterCardPoolSnapshot>? pools, string? issue)
    {
        if (issue is not null) yield return "kaleidoscope-issue:" + issue;
        foreach (var pool in pools ?? [])
        {
            yield return "kaleidoscope-pool:" + pool.PoolId + ":" + pool.CandidateCaptureIssue;
            foreach (var card in pool.Cards) yield return CardDescriptor(card);
        }
    }
}
