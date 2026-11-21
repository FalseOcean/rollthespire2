using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Infrastructure.Diagnostics;

internal sealed record FeedbackPlayObservations(string Status, string? RequestedSeed,
    IReadOnlyList<FeedbackPlayRecord> Records);

internal sealed record FeedbackPlayRecord(string Source, string Seed, Guid RunId, int Act, int Floor, int PlayerSlotIndex,
    DateTimeOffset TimeUtc, string Character, int Ascension, string? Ancient,
    int? OfferIndex, ulong? OfferListId, IReadOnlyList<FeedbackPlayItem> Items, bool Truncated);

internal sealed record FeedbackPlayItem(string? Key, string? OptionTextKey, bool? IsLocked);

/// <summary>Detached observations of already-generated native results during actual play.</summary>
internal static class FeedbackPlayObservation
{
    private const int RecordLimit = 48;
    private const int OfferLimit = 16;
    private static readonly Queue<FeedbackPlayRecord> Recent = new();
    private static readonly HashSet<string> FailedSources = new(StringComparer.Ordinal);
    private static RunBinding? _binding;

    internal static FeedbackPlayObservations Capture(IRunState? run)
    {
        string? seed = run is null ? null : Text(run.Rng.StringSeed);
        var binding = _binding;
        if (run is not null && (binding is null || !ReferenceEquals(run, binding.Run)))
            return new("NoMatchingRunObservation", seed, []);
        Guid? runId = run is null ? null : binding?.RunId;
        lock (Recent)
        {
            var records = Recent.Where(record => (seed is null || record.Seed == seed) &&
                (runId is null || record.RunId == runId)).ToArray();
            return new(records.Length == 0 ? "NoRecentObservation" : "Observed", seed, records);
        }
    }

    internal static void InstallRewardObserver(Harmony harmony) => harmony.Patch(
        MainMenuPatchInstaller.RequiredMethod(typeof(NRewardButton), "_Ready", Type.EmptyTypes),
        postfix: new HarmonyMethod(MainMenuPatchInstaller.RequiredMethod(typeof(FeedbackPlayObservation), nameof(RewardReady))));

    internal static void InstallAncientObserver(Harmony harmony) => harmony.Patch(
        MainMenuPatchInstaller.RequiredMethod(typeof(NAncientEventLayout), nameof(NAncientEventLayout.OnSetupComplete), Type.EmptyTypes),
        postfix: new HarmonyMethod(MainMenuPatchInstaller.RequiredMethod(typeof(FeedbackPlayObservation), nameof(AncientReady))));

    internal static void Bind(NTopBar topBar, IRunState run)
    {
        if (ReferenceEquals(_binding?.TopBar, topBar) && ReferenceEquals(_binding.Run, run)) return;
        _binding?.Dispose();
        _binding = new(topBar, run);
        _binding.Attach();
    }

    private sealed class RunBinding(NTopBar topBar, IRunState run) : IDisposable
    {
        private readonly List<(Player Player, Action<RelicModel> Handler)> _relicHandlers = [];
        internal Guid RunId { get; } = Guid.NewGuid();
        internal NTopBar TopBar => topBar;
        internal IRunState Run => run;

        internal void Attach()
        {
            topBar.TreeExiting += Dispose;
            foreach (var player in run.Players)
            {
                Action<RelicModel> handler = relic =>
                {
                    try { Record(this, "Player.RelicObtained", player, [new(Text(relic.Id.ToString()), null, null)]); }
                    catch (Exception error) { RecordError("Player.RelicObtained", error); }
                };
                player.RelicObtained += handler;
                _relicHandlers.Add((player, handler));
            }
        }

        public void Dispose()
        {
            if (ReferenceEquals(_binding, this)) _binding = null;
            topBar.TreeExiting -= Dispose;
            foreach (var (player, handler) in _relicHandlers) player.RelicObtained -= handler;
            _relicHandlers.Clear();
        }
    }

    private static void AncientReady(NAncientEventLayout __instance)
    {
        const string source = "NAncientEventLayout.OptionButtons";
        try
        {
            // EventRoom entry can complete before the layout's asynchronous dialogue/setup.
            var buttons = __instance.OptionButtons.Take(OfferLimit + 1).ToArray();
            if (_binding is not { } binding ||
                buttons.FirstOrDefault()?.Event is not AncientEventModel { IsFinished: false } ancient ||
                ancient.Owner is not { } player || !ReferenceEquals(player.RunState, binding.Run) ||
                buttons.Any(button => !ReferenceEquals(button.Event, ancient))) return;
            Record(binding, source, player, buttons.Select(button =>
                new FeedbackPlayItem(Text(button.Option.Relic?.Id.ToString()),
                    Text(button.Option.TextKey), button.Option.IsLocked)).ToArray());
        }
        catch (Exception error) { RecordError(source, error); }
    }

    private static void RewardReady(NRewardButton __instance)
    {
        const string source = "NRewardButton._Ready";
        try
        {
            if (__instance.Reward is not { } reward || _binding is not { } binding ||
                !ReferenceEquals(reward.Player.RunState, binding.Run) ||
                binding.Run.CurrentRoom is not EventRoom { CanonicalEvent: AncientEventModel } ||
                __instance.GetParent() is not { } offerList) return;

            switch (reward)
            {
                case CardReward cards:
                    var items = cards.Cards.Take(OfferLimit + 1)
                        .Select(card => new FeedbackPlayItem(Text(card.Id.ToString()), null, null)).ToArray();
                    if (items.Length > 0)
                        Record(binding, source + ".Cards", cards.Player, items, __instance.GetIndex(), offerList.GetInstanceId());
                    break;
                case RelicReward { Relic: { } relic }:
                    Record(binding, source + ".Relic", reward.Player, [new(Text(relic.Id.ToString()), null, null)],
                        __instance.GetIndex(), offerList.GetInstanceId());
                    break;
            }
        }
        catch (Exception error) { RecordError(source, error); }
    }

    private static void Record(RunBinding binding, string source, Player player, FeedbackPlayItem[] items,
        int? offerIndex = null, ulong? offerListId = null)
    {
        var run = player.RunState;
        int slot = run.GetPlayerSlotIndex(player);
        if (slot < 0 || string.IsNullOrEmpty(run.Rng.StringSeed)) return;
        var ancient = (run.CurrentRoom as EventRoom)?.CanonicalEvent as AncientEventModel;
        bool truncated = items.Length > OfferLimit;
        var record = new FeedbackPlayRecord(source, Text(run.Rng.StringSeed)!, binding.RunId, run.CurrentActIndex + 1,
            run.TotalFloor, slot, DateTimeOffset.UtcNow, Text(player.Character.Id.ToString())!,
            run.AscensionLevel, Text(ancient?.Id.ToString()), offerIndex, offerListId,
            truncated ? items[..OfferLimit] : items, truncated);
        lock (Recent)
        {
            if (Recent.Count == RecordLimit) Recent.Dequeue();
            Recent.Enqueue(record);
        }
        RuntimeLog.Info("playResultObservation=true;observationJson=" + RuntimeLog.SafeJson(record));
    }

    private static void RecordError(string source, Exception error)
    {
        if (FailedSources.Add(source))
            RuntimeIssueLedger.Current.Record(error, "FeedbackPlayObservationFailed",
                nameof(FeedbackPlayObservation), source, "issues.capture_failed");
    }

    private static string? Text(string? value) => value is { Length: > 160 } ? value[..160] : value;
}
