using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Cards;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Prediction;
using MegaCrit.Sts2.Core.Entities.Players;

namespace RolltheSpire2.Infrastructure.Snapshots;

/// <summary>
/// Read-only source pool capture at the authored entry context. No card creation,
/// hooks, RNG consumption or writes to the supplied player's RunState.
/// Capture on the owning thread, then pass only the immutable values to projection.
/// </summary>
public static class Beta111MorphicGroveAuthorityCapture
{
    /// <summary>
    /// UI-authored entry template: the player commits to having two legal initial
    /// basics at this event. This reads current unlocked pools without constructing
    /// a Player/RunState, and makes no claim about survival through prior rooms.
    /// </summary>
    public static MorphicGroveScenario CaptureAuthoredInitialBasics(RuntimeContextAuthoritySnapshot authority) =>
        CaptureAuthoredBasics(authority, 2);

    internal static MorphicGroveScenario CaptureAuthoredEventResult(RuntimeContextAuthoritySnapshot authority,
        RolltheSpire2.Search.Contracts.EventResultConditionKind kind, MegaCrit.Sts2.Core.Unlocks.UnlockState? explicitUnlocks = null)
    {
        var source = ObserveAuthoredBasics(authority, Search.Semantics.EventResultTransformSemantics.DrawCount(kind), explicitUnlocks);
        return new(source.Authority, source.Premises with { EventOccurrenceBasis =
            "Authored E entry: " + Search.Semantics.EventResultTransformSemantics.Entry(kind) +
            "; select the whitelisted transform option and legal initial Basics; unchanged local pool/RNG premise. " +
            "Canonical internal draws replay normally. Raw replacements only; occurrence/route/deck-survival not proven." }, source.Targets);
    }

    internal static MorphicGroveScenario CaptureAuthoredBasics(RuntimeContextAuthoritySnapshot authority, int count,
        MegaCrit.Sts2.Core.Unlocks.UnlockState? explicitUnlocks = null)
    {
        var targets = CaptureAuthoredTargets(authority.ProfileId, authority.Character.CharacterKey, authority.PlayersCount, count, explicitUnlocks);
        if (count == 1)
            return new(authority, new("Authored event transform: one legal initial Strike/Defend target at entry; unchanged local pool and RNG. Event occurrence and deck history are premises.",
                true, false, null), targets);
        return new(authority, new("Fragile E: authored event order [Morphic Grove / Group] occurs; no extra relevant RNG perturbation before the result. Two legal initial Strike/Defend targets at entry. Canonical internal draws replay normally; occurrence/route/deck-survival are premises, not proof obligations.",
            true, false, null), targets);
    }

    private sealed class InitialBasicsUnavailable(string reason, IReadOnlyList<MorphicGroveTarget> captured, Exception? cause = null)
        : InvalidOperationException(reason, cause)
    {
        internal IReadOnlyList<MorphicGroveTarget> Captured { get; } = captured;
    }

    internal static MorphicGroveScenario ObserveAuthoredBasics(RuntimeContextAuthoritySnapshot authority, int count,
        MegaCrit.Sts2.Core.Unlocks.UnlockState? explicitUnlocks = null)
    {
        try { return CaptureAuthoredBasics(authority, count, explicitUnlocks); }
        catch (InitialBasicsUnavailable issue)
        {
            Diagnostics.RuntimeIssueLedger.Current.Record(issue, "InitialBasicCaptureUnavailable", "EventTransform",
                "AuthoredEntry", "issues.capture_failed", Diagnostics.RuntimeIssueSeverity.Warning,
                correlationId: $"character={authority.Character.CharacterKey.Serialized};slot={authority.PlayerSlotIndex};targets={count}");
            return new(authority, new("Authored event transform with legal initial Strike/Defend targets at entry.",
                true, false, null) { InitialBasicCaptureIssue = issue.Message }, issue.Captured);
        }
    }

    internal static MorphicGroveTarget[] CaptureAuthoredTargets(Compatibility.RuntimeProfileId profileId, ModelKey characterKey,
        int players, int count, MegaCrit.Sts2.Core.Unlocks.UnlockState? explicitUnlocks = null)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (count is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(count));
        if (profileId != RolltheSpire2.Compatibility.RuntimeProfileId.Beta111)
            throw new InvalidOperationException("MorphicGrove.Beta111Required");
        var targets = new List<MorphicGroveTarget>(count);
        try
        {
            var character = ModelDb.GetById<CharacterModel>(new ModelId("CHARACTER", characterKey.Entry));
            var unlocks = explicitUnlocks;
            var constraint = players > 1 ? CardMultiplayerConstraint.MultiplayerOnly : CardMultiplayerConstraint.SingleplayerOnly;
            foreach (var basic in character.StartingDeck.Where(c => c.Rarity == CardRarity.Basic && c.IsRemovable &&
                c.Type != CardType.Quest && (c.Tags.Contains(CardTag.Strike) || c.Tags.Contains(CardTag.Defend))).Take(count))
            {
                unlocks ??= MegaCrit.Sts2.Core.Saves.SaveManager.Instance.GenerateUnlockStateFromProgress();
                // Native out-of-combat Basic transformation, in source pool order.
                var pool = basic.Pool.GetUnlockedCards(unlocks, constraint)
                    .Where(c => c.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare)
                    .Where(c => c.Id != basic.Id).Select(Snapshot).ToArray();
                if (pool.Length == 0 || targets.Count > 0 && !targets[0].OrderedSourceCandidates!.SequenceEqual(pool))
                    throw new InitialBasicsUnavailable("MorphicGrove.InitialBasicPoolsUnavailableOrDifferent", targets);
                targets.Add(new($"basic:{targets.Count}", Snapshot(basic), pool));
            }
        }
        catch (Exception ex) when (ex is not (InitialBasicsUnavailable or OperationCanceledException or OutOfMemoryException))
        {
            // Only the native source read is unavailable. Retain targets already observed
            // and the native exception for the request-correlated capture diagnostic.
            throw new InitialBasicsUnavailable("EventTransform.InitialBasicSourceReadFailed:" + ex.GetType().Name + ":" + ex.Message, targets, ex);
        }
        if (targets.Count < count) throw new InitialBasicsUnavailable("EventTransform.LegalInitialBasicsRequired:" + count, targets);
        return targets.ToArray();
    }

    /// <summary>
    /// Default Group targets: two available initial Strike/Defend cards, without a
    /// player-authored split. Pool equality is checked, never inferred from names.
    /// The supplied player is the known/authored event-entry context, not a promise
    /// that the current starting deck survives the path to the event.
    /// </summary>
    public static MorphicGroveScenario CaptureInitialBasics(RuntimeContextAuthoritySnapshot authority,
        MorphicGrovePremises premises, Player player)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(player);
        if (player.Character.Id.Entry != authority.Character.CharacterKey.Entry ||
            player.RunState.Players.Count != authority.PlayersCount ||
            player.RunState.GetPlayerSlotIndex(player) != authority.PlayerSlotIndex)
            throw new ArgumentException("MorphicGrove.CaptureContextMismatch");
        var basics = player.Deck.Cards.Where(c => c.Rarity == CardRarity.Basic && c.IsTransformable &&
            (c.Tags.Contains(CardTag.Strike) || c.Tags.Contains(CardTag.Defend))).ToArray();
        if (basics.Length < 2) throw new ArgumentException("MorphicGrove.TwoLegalInitialBasicsRequired");
        var captures = basics.Select((c, i) => Capture($"basic:{i}", c)).ToArray();
        if (captures.Skip(1).Any(t => !t.OrderedSourceCandidates!.SequenceEqual(captures[0].OrderedSourceCandidates!)))
            throw new InvalidOperationException("MorphicGrove.BasicTargetsHaveDifferentSourcePools;ExplicitDeliveredOrderRequired");
        return new(authority, premises, captures.Take(2).ToArray());
    }

    public static MorphicGroveTarget Capture(string instanceId, CardModel target)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentException(nameof(instanceId));
        if (target.Pile?.Type != PileType.Deck || !target.IsTransformable || target.Type == CardType.Quest)
            throw new ArgumentException("Target must be a legally selectable deck card.");
        var pool = CardFactory.GetDefaultTransformationOptions(target, false)
            .Select(Snapshot).ToArray();
        if (pool.Length == 0) throw new InvalidOperationException("Source returned an empty transform pool.");
        return new(instanceId, Snapshot(target), Array.AsReadOnly(pool));
    }

    // CanTransform describes the replacement once added to the deck. For a
    // detached canonical card IsTransformable can ignore Eternal; IsRemovable
    // is the corresponding deck-state fact in this source version.
    public static MorphicGroveCard Snapshot(CardModel card) => new(
        new ModelKey(BaseGameModelKeys.Categories.Card, card.Id.Entry),
        card.Pool.Id.Entry, card.Type.ToString(), card.Rarity.ToString(),
        card.CurrentUpgradeLevel, card.MaxUpgradeLevel, card.IsUpgradable,
        card.IsRemovable, card.Enchantment?.Id.Entry);
}
