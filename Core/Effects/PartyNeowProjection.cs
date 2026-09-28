using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.Effects;

internal static class PartyNeowProjection
{
    internal static IReadOnlyList<Core.Neow.NeowChoiceResult> ProjectChoices(
        SeedPredictionRequest request, ulong root, Xoshiro256StarStar niche, Xoshiro256StarStar potions,
        bool sharedRngKnown = true,
        IReadOnlyDictionary<ModelKey, IReadOnlySet<ModelKey>>? authoredCapsuleEffects = null)
    {
        var owner = request.Authority;
        if (owner.EffectAuthority is { } effects)
            owner = owner.WithEffectAuthority(effects with { CurrentPotionCount = 0, PotionCapacity = owner.Ascension >= 4 ? 2 : 3 });
        var offers = Core.Neow.ModernNeowIdentityPredictor.PredictModernCore(root, owner, Beta111Profile.Instance, true);
        return offers.RelicKeys.Select((key, index) =>
        {
            var rng = NeowEffectRngContext.CreateFromRootHash(Beta111Profile.Instance, root, owner.PlayerSlotIndex)
                .WithShared(niche.Clone(), potions.Clone());
            bool sharedConsumer = key == BaseGameModelKeys.Relics.PhialHolster ||
                !Core.Rewards.VanillaRelicRewardEffects.TryGet(owner.ProfileId, key, out var behavior) ||
                (behavior.OnObtainCapabilities & (Core.Rewards.RelicOnObtainRewardEffects.ConsumesNicheRng |
                    Core.Rewards.RelicOnObtainRewardEffects.NestedRelicObtain)) != 0;
            var effect = !sharedRngKnown && sharedConsumer
                ? NeowEffectProjection.UnknownAuthority(key, "party.earlier-player-shared-opening-unresolved")
                : new NeowEffectProjectionEngine(Beta111Profile.Instance, "beta111.party-neow", root, owner,
                    enableComplexBonesDeckInteractions: true) { AuthoredCapsuleEffects = authoredCapsuleEffects }.Project(key, rng);
            return Core.Prediction.Profiles.SeedPredictionDocumentFactory.Choice(index + 1, key, request,
                offers.ExactAuthority ? PredictionPrecision.Exact : PredictionPrecision.Unknown,
                "beta111.party-neow.initial", effect);
        }).ToArray();
    }

    internal static IEnumerable<Core.Neow.NeowChoiceResult> ConcreteRoutes(Core.Neow.NeowChoiceResult choice)
    {
        if (choice.BonesOutcome is not { } bones) { yield return choice; yield break; }
        foreach (var route in bones.OriginalRoutes)
        {
            var continuation = route.OpeningRewardContinuation;
            if (continuation is null) continue;
            yield return choice with
            {
                BonesOutcome = bones with { OriginalRoutes = [route], OutcomeGroups = bones.OutcomeGroups
                    .Where(g => g.OriginalRoutes.Any(r => r.RouteId == route.RouteId))
                    .Select(g => g with { RepresentativeRoute = route, OriginalRoutes = [route], EquivalentAcquisitionOrders = [route.AcquisitionOrder] }).ToArray() },
                OpeningRewardContinuations = new([continuation], continuation.Precision, "", continuation.Route.EvidenceCode)
            };
        }
    }

    internal static IReadOnlyList<PartyNeowResult>? Find(PartySeedInformation initial, OrderedPartyAuthority party,
        IReadOnlyList<PartyNeowParticipant> participants, Func<IReadOnlyList<PartyNeowResult>, bool> accepts,
        CancellationToken cancellationToken = default)
    {
        if (initial.PartyFingerprint != party.Fingerprint || initial.Root != Beta111Profile.Instance.ComputeRootSeed(initial.Seed))
            throw new InvalidOperationException("Party.N.WrongRootOrParty");
        if (participants.Select(p => p.Slot).Distinct().Count() != participants.Count ||
            !participants.Select(p => p.Slot).SequenceEqual(participants.Select(p => p.Slot).Order()) ||
            participants.Any(p => p.Slot < 0 || p.Slot >= party.Players.Count))
            throw new ArgumentException("Party.N.InvalidParticipantPrefix");
        foreach (var p in participants)
        {
            if (PartyNeowAdmission.UnsupportedReason(p.Plan.Option) is { } reason) throw new ArgumentException(reason);
            if (!initial.Players[p.Slot].Offers.Contains(p.Plan.Option)) return null;
        }
        var streams = NeowEffectRngContext.CreateFromRootHash(Beta111Profile.Instance, initial.Root, 0);
        var sharedBag = party.World.Beta109Generation!.SharedRelicBuckets.SelectMany(b => b.OrderedRelics).ToHashSet();
        return Visit(0, streams.Niche, streams.CombatPotionGeneration, sharedBag, []);

        IReadOnlyList<PartyNeowResult>? Visit(int progress, Xoshiro256StarStar niche, Xoshiro256StarStar potions,
            HashSet<ModelKey> remainingShared, List<PartyNeowResult> prefix)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (progress == participants.Count) return accepts(prefix) ? prefix.ToArray() : null;
            var participant = participants[progress];
            var owner = party.Players[participant.Slot];
            // Only the shared streams are carried. RNG owner remains the actual slot, not progress.
            var rng = NeowEffectRngContext.CreateFromRootHash(Beta111Profile.Instance, initial.Root, participant.Slot)
                .WithShared(niche.Clone(), potions.Clone());
            var nextShared = new HashSet<ModelKey>(remainingShared);
            var personalBag = party.World.Beta109Generation.PartyRelicBuckets[participant.Slot]
                .SelectMany(b => b.OrderedRelics).ToHashSet();
            // RelicCmd.Obtain removal. Admitted Ancient options do not draw from either bag.
            nextShared.Remove(participant.Plan.Option); personalBag.Remove(participant.Plan.Option);
            var engine = new NeowEffectProjectionEngine(Beta111Profile.Instance, "beta111.party-neow", initial.Root, owner);
            var projection = engine.ProjectPartyOption(participant.Plan.Option, rng);
            foreach (var result in Choices(participant, projection.EffectGroups))
            {
                prefix.Add(result);
                var found = Visit(progress + 1, rng.Niche, rng.CombatPotionGeneration, nextShared, prefix);
                prefix.RemoveAt(prefix.Count - 1);
                if (found is not null) return found;
            }
            return null;
        }
    }

    // Complete optional rewards before the next participant. No local predicate/first-match pruning.
    private static IEnumerable<PartyNeowResult> Choices(PartyNeowParticipant participant, IReadOnlyList<PredictedEffectGroup> groups)
    {
        var fixedGroups = groups.Where(g => g.SelectionPolicy is EffectSelectionPolicy.NoPlayerChoice or EffectSelectionPolicy.ForcedObtain).ToArray();
        var selections = new List<(string Id, IReadOnlyList<PredictedEffectGroup>[] Values)>();
        foreach (var alternatives in groups.Where(g => g.SelectionSetId is not null).GroupBy(g => g.SelectionSetId!))
            selections.Add((alternatives.Key, alternatives.OrderBy(g => g.GroupOrder).Select(g => (IReadOnlyList<PredictedEffectGroup>)new[] { g }).ToArray()));
        foreach (var group in groups.Where(g => g.SelectionSetId is null && !fixedGroups.Contains(g)).OrderBy(g => g.GroupOrder))
        {
            if (group.SelectionPolicy is not (EffectSelectionPolicy.ChooseOneOrSkip or EffectSelectionPolicy.OptionalClaim))
                throw new InvalidOperationException("Party.N.UnclosedChoice:" + group.GroupId);
            var alternatives = group.SelectionPolicy == EffectSelectionPolicy.OptionalClaim
                ? new[] { (IReadOnlyList<PredictedEffectGroup>)new[] { group } }
                : group.OrderedItems.GroupBy(e => e.OfferItemId ?? throw new InvalidOperationException("Party.N.MissingChoiceIdentity"))
                    .Select(items => (IReadOnlyList<PredictedEffectGroup>)new[] { group with { OrderedItems = items.ToArray() } }).ToArray();
            selections.Add((group.GroupId, alternatives));
        }
        if (participant.Plan.Choices.Select(c => c.GroupId).Distinct().Count() != participant.Plan.Choices.Count ||
            participant.Plan.Choices.Any(c => selections.All(s => s.Id != c.GroupId)))
            throw new ArgumentException("Party.N.IllegalChoiceGroup");
        foreach (var result in Expand(0, [], fixedGroups.ToList())) yield return result;

        IEnumerable<PartyNeowResult> Expand(int index, List<PartyNeowChoice> choices, List<PredictedEffectGroup> selected)
        {
            if (index == selections.Count)
            {
                yield return new(participant.Slot, participant.Plan.Option, participant.Plan.Intent, choices.ToArray(), groups,
                    selected.OrderBy(g => g.GroupOrder).ToArray());
                yield break;
            }
            var selection = selections[index];
            bool maySkip = groups.Any(g => g.GroupId == selection.Id &&
                g.SelectionPolicy is EffectSelectionPolicy.ChooseOneOrSkip or EffectSelectionPolicy.OptionalClaim);
            var explicitChoice = participant.Plan.Choices.SingleOrDefault(c => c.GroupId == selection.Id);
            IEnumerable<int> candidates = explicitChoice is not null ? new[] { explicitChoice.Index } : Enumerable.Range(maySkip ? -1 : 0, selection.Values.Length + (maySkip ? 1 : 0));
            foreach (int choice in candidates)
            {
                if (choice < (maySkip ? -1 : 0) || choice >= selection.Values.Length) throw new ArgumentException("Party.N.IllegalTakeSkipOrTarget");
                choices.Add(new(selection.Id, choice));
                int previous = selected.Count;
                if (choice >= 0) selected.AddRange(selection.Values[choice]);
                foreach (var result in Expand(index + 1, choices, selected)) yield return result;
                selected.RemoveRange(previous, selected.Count - previous); choices.RemoveAt(choices.Count - 1);
            }
        }
    }
}
