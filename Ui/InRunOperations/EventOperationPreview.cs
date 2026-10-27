using System.Collections.Immutable;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation;

namespace RolltheSpire2.Ui.InRunOperations;

// Detached, node-local event queries. Every alternative starts at the same
// captured present and carries its explicit input path. Combat forecasts use
// the Owner's stable-context/no-extra-actions premise, never route defaults.
internal static class EventOperationPreview
{
    private const int MaximumAlternatives = 256;

    internal static OperationPreview Build(PredictorOperationSnapshot snapshot, string optionKey,
        CancellationToken token = default, bool playRoyalties = false, bool includeContinuation = true,
        bool includeCombat = true)
    {
        var scene = RequireEvent(snapshot);
        if (!scene.Options.Any(o => o.Key == optionKey && o.Enabled))
            throw new ArgumentException("EventPreviewOptionNotInSnapshot", nameof(optionKey));
        return new Projection(snapshot, scene, optionKey, token, playRoyalties: playRoyalties,
            includeContinuation: includeContinuation, includeCombat: includeCombat).Build([new ChooseEventOption(optionKey)]);
    }

    // Only actual offered, enabled battle entrances own this local toggle.
    // This does not consult or update the route's combat defaults.
    internal static bool CanToggleRoyalties(PredictorOperationSnapshot snapshot, string optionKey) =>
        snapshot.Scene is PredictorOperationEventScene scene &&
        scene.Options.Any(o => o.Key == optionKey && o.Enabled) &&
        snapshot.State.Deck.Select(snapshot.State.Card).Any(c => c.Key.Entry == "ROYALTIES") &&
        OperationPreviewEligibility.RoyaltiesBattleOption(scene.Event.Entry, optionKey);

    internal static OperationPreview BuildSelected(PredictorOperationSnapshot snapshot,
        ImmutableArray<long> cardIds, CancellationToken token = default, bool includeContinuation = true,
        bool includeCombat = true)
    {
        var scene = RequireEvent(snapshot);
        if (!scene.AwaitingCards || cardIds.IsDefault || cardIds.Length != scene.CardsRequired ||
            cardIds.Distinct().Count() != cardIds.Length || cardIds.Any(id => !scene.CardCandidates.Contains(id)))
            throw new ArgumentException("EventPreviewSelectionNotInSnapshot", nameof(cardIds));
        return new Projection(snapshot, scene, scene.Branch ?? "", token, cardIds,
            includeContinuation: includeContinuation, includeCombat: includeCombat).Build([new SelectEventCards(cardIds)]);
    }

    private static PredictorOperationEventScene RequireEvent(PredictorOperationSnapshot snapshot) =>
        snapshot.Scene as PredictorOperationEventScene ?? throw new ArgumentException("EventPreviewRequiresActualEventScene");

    private sealed class Projection(PredictorOperationSnapshot snapshot, PredictorOperationEventScene scene,
        string optionKey, CancellationToken token, ImmutableArray<long> selectedTargets = default,
        bool playRoyalties = false, bool includeContinuation = true, bool includeCombat = true)
    {
        private readonly ImmutableArray<OperationPreviewBranch>.Builder _branches = ImmutableArray.CreateBuilder<OperationPreviewBranch>();
        private readonly ImmutableArray<OperationPreviewFault>.Builder _faults = ImmutableArray.CreateBuilder<OperationPreviewFault>();
        private readonly ImmutableArray<string>.Builder _pending = ImmutableArray.CreateBuilder<string>();
        private bool _combatPremise;
        private bool _skippedRestRewardClaims;
        private bool _transformAlternatives;
        private bool _royaltiesAvailable;
        private int _evaluations;

        internal OperationPreview Build(ImmutableArray<PredictorInput> inputs)
        {
            if (!OperationPreviewEligibility.EventMayDisplay(scene.Event.Entry, optionKey,
                scene.TinkerType != null, scene.TabletCount,
                scene.Options.Any(o => o.Key == optionKey && o.OfferedRelic != null)))
                return Finish();
            var before = PredictorOperation.Preview(snapshot, ImmutableArray<PredictorInput>.Empty, token);
            if (scene.Event.Entry == "SLIPPERY_BRIDGE") Bridge(before, inputs);
            else if (Query(inputs) is { } first)
            {
                var sections = Observe(before, first);
                if (scene.Event.Entry == "DENSE_VEGETATION" && optionKey == "REST")
                    sections = sections.Select(s => s with { Label = "EventRestRewards" }).ToImmutableArray();
                if (scene.Event.Entry == "TABLET_OF_TRUTH" && optionKey == "DECIPHER")
                    Tablet(inputs, first, sections);
                else Explore(inputs, first, sections, null, 0);
            }
            return Finish();
        }

        private OperationPreview Finish() => new(scene.Event, [], [], false, _branches.Select(branch => branch with
            { FullyEnumerated = branch.FullyEnumerated && _pending.Count == 0 && _faults.Count == 0,
                Faulted = branch.Faulted || _faults.Count > 0 }).ToImmutableArray(),
            _pending.Distinct().ToImmutableArray())
        {
            Faults = _faults.ToImmutable(), OptionKey = optionKey,
            Notices = (scene.Event.Entry == "SLIPPERY_BRIDGE" && _branches.Count > 0 ? ImmutableArray.Create("BridgeAlternativeRemovalProposals") : [])
                .AddRange(_combatPremise ? ImmutableArray.Create("EventCombatStableContextNoExtraActions") : ImmutableArray<string>.Empty)
                .AddRange(_skippedRestRewardClaims ? ImmutableArray.Create("EventCombatNoInterveningRewardClaims") : ImmutableArray<string>.Empty)
                .AddRange(_transformAlternatives ? ImmutableArray.Create("EventTransformSlotAlternativesRelated") : ImmutableArray<string>.Empty)
                .AddRange(_royaltiesAvailable ? ImmutableArray.Create(playRoyalties
                    ? "EventCombatRoyaltiesPlayed" : "EventCombatRoyaltiesNotPlayed") : ImmutableArray<string>.Empty),
            Style = scene.Event.Entry == "ROOM_FULL_OF_CHEESE" && optionKey == "GORGE"
                ? new(OperationTextMode.None, OperationCardLayout.Spread) : null
        };

        private PredictorOperationResult? Query(ImmutableArray<PredictorInput> inputs)
        {
            token.ThrowIfCancellationRequested();
            _evaluations++;
            try
            {
                var result = PredictorOperation.Preview(snapshot, inputs, token);
                if (result.Disposition == PredictorInputResult.Accepted) return result;
                _pending.Add("EventInput:" + result.Disposition);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { _faults.Add(new("EventProjection:" + scene.Event.Entry, inputs, ex)); }
            return null;
        }

        private void Add(ImmutableArray<OperationPreviewSection> sections, string? label = null,
            params string[] pending) => _branches.Add(new([], sections, pending.ToImmutableArray(), _evaluations,
                pending.Length == 0) { Label = label });

        private void Explore(ImmutableArray<PredictorInput> inputs, PredictorOperationResult result,
            ImmutableArray<OperationPreviewSection> sections, string? label, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (result.Waiting.Phase == PredictorPhase.CombatFacts)
            {
                if (includeCombat) Combat(inputs, result, sections, label);
                else Add(sections, label);
                return;
            }
            if (depth > 8)
            { Add(sections, label, "EventContinuationLimit"); return; }
            if (scene.Event.Entry == "TINKER_TIME" && result.Waiting.Phase == PredictorPhase.EventChoice)
            {
                if (!includeContinuation) { Add(sections, label); return; }
                foreach (var option in result.Waiting.EventOptions.Where(o => o.Enabled))
                {
                    var nextInputs = inputs.Add(new ChooseEventOption(option.Key));
                    if (Query(nextInputs) is not { } next) continue;
                    Explore(nextInputs, next, sections.AddRange(Observe(result, next)),
                        label == null ? option.Key : label + "/" + option.Key, depth + 1);
                }
                return;
            }
            if (scene.Event.Entry == "ROUND_TEA_PARTY" && optionKey == "PICK_FIGHT" &&
                result.Waiting.EventOptions.Any(o => o.Key == "CONTINUE_FIGHT"))
            {
                if (!includeContinuation) { Add(sections, label); return; }
                var nextInputs = inputs.Add(new ChooseEventOption("CONTINUE_FIGHT"));
                if (Query(nextInputs) is { } next)
                    Explore(nextInputs, next, sections.AddRange(Observe(result, next)), label, depth + 1);
                return;
            }
            if (scene.Event.Entry is "PUNCH_OFF" or "THE_LANTERN_KEY" &&
                result.Waiting.EventOptions.Any(o => o.Key == "FIGHT"))
            {
                if (!includeCombat) { Add(sections, label); return; }
                var nextInputs = inputs.Add(new ChooseEventOption("FIGHT"));
                if (Query(nextInputs) is { } next) Explore(nextInputs, next, sections, label, depth + 1);
                return;
            }
            if (scene.Event.Entry == "DOLL_ROOM" && result.Waiting.EventOptions.Any(o => o.OfferedRelic != null))
            {
                // This reveals the generated offers, without silently obtaining
                // one of the mutually exclusive dolls.
                var dolls = result.Waiting.EventOptions.Where(o => o.OfferedRelic != null)
                    .Select(o => o.OfferedRelic!.Value).ToImmutableArray();
                Add(sections.Add(new(scene.Event, dolls.Select(d =>
                    new OperationPreviewRow(OperationPreviewAction.Choose, [d], [], true)).ToImmutableArray(), [])), label);
                return;
            }
            if (result.Waiting.Phase == PredictorPhase.EventCards && !result.Waiting.Cards.IsEmpty)
            {
                var group = new OperationPreviewCardGroup(-1, result.Waiting.Cards,
                    result.Waiting.Selection.RequiredCardCount, result.Waiting.Selection.RequiredCardCount, false, false);
                Add(sections.Add(new(scene.Event, [], [group])), label);
                return;
            }
            if (result.Waiting.Phase == PredictorPhase.EventCards && IsRandomTransform(scene.Event.Entry, optionKey))
            {
                int examined = 0;
                var slots = Enumerable.Range(0, result.Waiting.Selection.RequiredCardCount)
                    .Select(_ => new List<(PredictorCard Card, int Rank)>()).ToArray();
                bool complete = true;
                var candidates = result.State.Deck.Where(result.Waiting.Selection.CandidateCardIds.Contains).ToImmutableArray();
                foreach (var ids in OrderedTargets(candidates,
                    result.Waiting.Selection.RequiredCardCount, []))
                {
                    if (examined++ >= MaximumAlternatives)
                    { _pending.Add("EventTargetAlternativesLimit:256"); complete = false; break; }
                    var nextInputs = inputs.Add(new SelectEventCards(ids));
                    if (Query(nextInputs) is not { } next) { complete = false; continue; }
                    var changes = next.Effects.Skip(result.Effects.Length).SelectMany(e => e.Transformations).ToArray();
                    for (int slot = 0; slot < slots.Length; slot++)
                    {
                        var change = changes.SingleOrDefault(t => t.OriginalId == ids[slot]);
                        if (change == null) { complete = false; continue; }
                        var card = next.State.Card(change.ReplacementId);
                        int rank = result.State.Deck.IndexOf(change.OriginalId);
                        slots[slot].Add((card, rank));
                    }
                }
                _transformAlternatives = true;
                var groups = slots.Select((cards, slot) => new OperationPreviewCardGroup(-1 - slot,
                    cards.OrderBy(item => item.Rank).DistinctBy(item => item.Card with { Id = 0 })
                        .Select(item => item.Card).ToImmutableArray(), 1, 1, false, true)
                    { Label = (slot + 1).ToString(), EarliestFirst = true, SuppressPossiblePrefix = true }).ToImmutableArray();
                // Query records its actual rejection/fault and the cap records
                // its actual limit. Incompleteness does not invent a missing
                // player input in addition to those originating causes.
                _branches.Add(new([], sections.Add(new(scene.Event, [], groups)
                    { Category = RolltheSpire2.Ui.Settings.OperationPredictionCategory.CardTransform }), [], _evaluations, complete));
                return;
            }
            if (scene.Event.Entry == "DENSE_VEGETATION" && optionKey is "REST" or "FIGHT")
            {
                if (!includeCombat) { Add(sections, label); return; }
                _combatPremise = true;
                if (result.Waiting.Phase == PredictorPhase.Rewards)
                {
                    // No optional reward claim joins the stable inventory used
                    // for this battle forecast. Generation already consumed its
                    // streams, so skipping cannot rewind the rest rewards.
                    _skippedRestRewardClaims = true;
                    var skipInputs = inputs.Add(new LeaveRewards());
                    if (Query(skipInputs) is { } skipped)
                        Explore(skipInputs, skipped, sections, label, depth + 1);
                    return;
                }
                if (result.Waiting.EventOptions.Any(o => o.Key == "FIGHT"))
                {
                    var fightInputs = inputs.Add(new ChooseEventOption("FIGHT"));
                    if (Query(fightInputs) is { } fighting)
                        Explore(fightInputs, fighting, sections, label, depth + 1);
                    return;
                }
                Add(sections, label, "EventCombatFactsRequired:DENSE_VEGETATION");
                return;
            }
            if (result.Waiting.Phase is PredictorPhase.ObtainDeckCards or PredictorPhase.ObtainChooseCard or
                PredictorPhase.ObtainChooseManyCards or PredictorPhase.TransformCards or PredictorPhase.ChooseBundle)
            { Add(sections, label, "AwaitingPlayerChoice:" + result.Waiting.Phase); return; }
            Add(sections, label);
        }

        private void Combat(ImmutableArray<PredictorInput> inputs, PredictorOperationResult boundary,
            ImmutableArray<OperationPreviewSection> sections, string? label)
        {
            _combatPremise = true;
            var state = boundary.State;
            var royalties = state.Deck.Select(state.Card).FirstOrDefault(c => c.Key.Entry == "ROYALTIES");
            _royaltiesAvailable = royalties != null;
            var facts = new PredictorCombatFacts(AutomaticCardEffects: false)
            {
                CardTriggers = state.Deck.Select(state.Card).Where(PredictorCombatConsequences.IsTriggeredSource)
                    .Select(c => new PredictorCardTrigger(c.Id, playRoyalties && c.Id == royalties?.Id ? 1 : 0)).ToImmutableArray(),
                Potions = PredictorCombatPotionFacts.NoAdditional
            };
            var hunts = state.Deck.Select(state.Card).Where(c => c.Key.Entry == "THE_HUNT").ToArray();
            int huntCount = 0;
            if (hunts.Length > 0)
            {
                try
                {
                    // BeginEventCombat records the actual chosen child at
                    // this input boundary. The route view intentionally rejects
                    // operation runs, so read that source record directly.
                    // These event rosters have fixed counts; no route default
                    // or hypothetical spawned enemy enters this upper bound.
                    const string entryPrefix = "EnterEventCombat:";
                    string entry = boundary.Effects.LastOrDefault(effect =>
                        effect.Action.StartsWith(entryPrefix, StringComparison.Ordinal))?.Action ??
                        throw new InvalidOperationException("EventCombatEncounterMissing");
                    var encounter = new ModelKey("ENCOUNTER", entry[entryPrefix.Length..]);
                    huntCount = Math.Min(hunts.Length, PredictorEncounterEntry.InitialEnemyCount(encounter,
                        Beta111Profile.Instance.ComputeRootSeed(snapshot.Context.Seed), state.Position.Floor));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { _faults.Add(new("EventCombatHuntRoster", inputs, ex)); }
            }
            bool unknownHealth = state.Relics.Any(r => !r.Melted && r.Key.Entry == "MEAT_ON_THE_BONE");
            bool unknownBudget = state.Relics.Any(r => !r.Melted && r.Key.Entry == "SEAL_OF_GOLD") && snapshot.Context.SealOfGoldFixedDeduction == null;
            bool masked = unknownHealth || unknownBudget;
            if (masked && scene.Event.Entry == "BATTLEWORN_DUMMY")
            { Add(sections, label, unknownHealth ? "EventCombatVictoryHealthRequired" : "EventCombatSealBudgetRequired"); return; }
            bool lamp = state.Relics.Any(r => !r.Melted && r.Key.Entry == "LAVA_LAMP");
            bool bone = state.Relics.Any(r => !r.Melted && r.Key.Entry == "BONE_TEA" && r.CombatsLeft > 0);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (bool damage in lamp ? new[] { false, true } : new[] { false })
            foreach (bool firstTurn in bone ? new[] { false, true } : new[] { false })
            {
                var conditional = facts with { TookDamageThisCombat = damage,
                    FirstPlayerTurnOccurred = bone ? firstTurn : null };
                var baseline = ObserveCombat(inputs, boundary, conditional, masked);
                if (baseline == null) continue;
                var combatSections = baseline.Sections;
                string signature = CombatSignature(combatSections);
                ImmutableArray<OperationPreviewSection>? alternateSections = null;
                string? branchLabel = lamp ? damage ? "EventCombatDamageTaken" : "EventCombatNoDamage" : label;
                if (huntCount > 0)
                {
                    var huntIds = hunts.Take(huntCount).Select(c => c.Id).ToHashSet();
                    var huntFacts = conditional with { CardTriggers = conditional.CardTriggers.Select(t =>
                        huntIds.Contains(t.InstanceId) ? t with { EffectiveTriggers = 1 } : t).ToImmutableArray() };
                    if (ObserveCombat(inputs, boundary, huntFacts, masked) is { } hypothesis)
                    {
                        // Original Hunt card rewards precede additions such as
                        // Prayer Wheel. Subscription order survives display
                        // sorting; masked values retain their same-kind order.
                        var huntRewards = hypothesis.Rewards.Where(r => r.Kind == PredictorRewardKind.Card &&
                                r.RerollRecipe is { EncounterSource: true, IsFromCombat: false })
                            .OrderBy(r => r.SubscriptionOrder).Take(huntCount).ToArray();
                        var huntGroups = huntRewards.Select((r, i) => new OperationPreviewCardGroup(r.Id,
                            r.Cards, 1, 1, r.CanSkip, true)
                            { Label = "EventHuntConditional:" + (i + 1) }).ToImmutableArray();
                        var huntCards = huntRewards.Select(r => r.Cards).ToList();
                        var withoutHunts = hypothesis.Sections.Select(s => s with { CardGroups = s.CardGroups.Where(g =>
                        {
                            int index = huntCards.FindIndex(cards => SameCards(cards, g.Cards));
                            if (index < 0) return true;
                            huntCards.RemoveAt(index); return false;
                        }).ToImmutableArray() }).ToImmutableArray();
                        var huntSection = new OperationPreviewSection(new("CARD", "THE_HUNT"), [], huntGroups);
                        if (huntRewards.Length == huntCount && CombatSignature(withoutHunts) == signature)
                            combatSections = combatSections.Add(huntSection);
                        else
                        {
                            // Hunt/Royalties are original rewards. Later native
                            // modifier additions read their advanced streams.
                            // Preserve the correlated full replay when that
                            // changes another visible result (or replaces the
                            // Hunt reward itself, e.g. Vintage).
                            combatSections = Conditional(combatSections, "EventCombatNoHunt");
                            var full = Conditional(withoutHunts, "EventHuntTriggers:" + huntCount);
                            if (!huntGroups.IsEmpty) full = full.Add(huntSection);
                            alternateSections = full;
                        }
                    }
                }
                if (lamp) combatSections = Conditional(combatSections);
                if (!seen.Add(CombatSignature(combatSections) + "||" +
                    (alternateSections is { } alternate ? CombatSignature(alternate) : ""))) continue;
                Add(sections.AddRange(combatSections), branchLabel);
                if (alternateSections is { } correlated) Add(sections.AddRange(correlated), branchLabel);
            }
        }

        private sealed record CombatObservation(ImmutableArray<OperationPreviewSection> Sections,
            ImmutableArray<PredictorReward> Rewards);

        private CombatObservation? ObserveCombat(ImmutableArray<PredictorInput> inputs, PredictorOperationResult boundary,
            PredictorCombatFacts facts, bool masked)
        {
            if (masked) return MaskedCombat(inputs, facts);
            var nextInputs = inputs.Add(new ResolveCombat(facts, scene.Event.Entry == "BATTLEWORN_DUMMY" ? false : null));
            if (Query(nextInputs) is not { } settled) return null;
            return new(Observe(boundary, settled).Select(s => s with { Label = "EventCombatRewards" }).ToImmutableArray(), settled.Rewards);
        }

        private static ImmutableArray<OperationPreviewSection> Conditional(ImmutableArray<OperationPreviewSection> sections,
            string? label = null) => sections.Select(s => s with
        {
            Label = label ?? s.Label,
            Rows = s.Rows.Select(r => r with { Possible = true }).ToImmutableArray(),
            CardGroups = s.CardGroups.Select(g => g with { Possible = true }).ToImmutableArray()
        }).ToImmutableArray();

        private static bool SameCards(ImmutableArray<PredictorCard> a, ImmutableArray<PredictorCard> b) =>
            a.Select(c => c with { Id = 0 }).SequenceEqual(b.Select(c => c with { Id = 0 }));

        private static string CombatSignature(ImmutableArray<OperationPreviewSection> sections) =>
            string.Join(";", sections.SelectMany(s => s.Rows).Select(r => r.Action + ":" +
                string.Join(',', r.Models) + ":" + string.Join(',', r.Cards.Select(c => c with { Id = 0 })))) + "|" +
            string.Join(";", sections.SelectMany(s => s.CardGroups).Select(g => string.Join(',', g.Cards.Select(c => c with { Id = 0 }))));

        private PredictorRun RestoreBoundary(ImmutableArray<PredictorInput> inputs)
        {
            var run = new PredictorRun(snapshot.Context, snapshot.State);
            snapshot.Scene.Restore(run);
            run.StartOperationHealth(snapshot.CurrentHp, snapshot.MaximumHp);
            foreach (var input in inputs)
                if (run.SubmitOperation(input, token) != PredictorInputResult.Accepted)
                    throw new InvalidOperationException("EventCombatProjectionPrefixRejected");
            return run;
        }

        private CombatObservation? MaskedCombat(ImmutableArray<PredictorInput> inputs, PredictorCombatFacts facts)
        {
            try
            {
                _evaluations++;
                var run = RestoreBoundary(inputs);
                var known = run.PreviewOperationCombatRewards(facts, token);
                var rewards = ImmutableArray.CreateBuilder<PredictorReward>();
                var rows = ImmutableArray.CreateBuilder<OperationPreviewRow>();
                var groups = ImmutableArray.CreateBuilder<OperationPreviewCardGroup>();
                foreach (var reward in known.Rewards)
                {
                    if (!reward.IsKnown)
                    {
                        if (reward.Kind is PredictorRewardKind.Card or PredictorRewardKind.Potion or PredictorRewardKind.Relic)
                            foreach (var cause in reward.PendingCauses) _pending.Add("EventCombatRewardPending:" + cause.Code);
                        continue;
                    }
                    var value = reward.Value!;
                    rewards.Add(value);
                    if (value.Kind == PredictorRewardKind.Card)
                        groups.Add(new(groups.Count, value.Cards, 1, 1, value.CanSkip, false));
                    if (value.Kind is PredictorRewardKind.Potion or PredictorRewardKind.Relic)
                        rows.Add(new(value.Kind == PredictorRewardKind.Potion ? OperationPreviewAction.GeneratePotion :
                            OperationPreviewAction.ObtainRelic, [value.Key], [], false));
                }
                return new([new(scene.Event, rows.ToImmutable(), groups.ToImmutable()) { Label = "EventCombatRewards" }], rewards.ToImmutable());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _faults.Add(new("EventMaskedCombatProjection", inputs.Add(new ResolveCombat(facts)), ex));
                return null;
            }
        }

        private IEnumerable<ImmutableArray<long>> OrderedTargets(ImmutableArray<long> candidates, int count,
            ImmutableArray<long> selected)
        {
            token.ThrowIfCancellationRequested();
            if (selected.Length == count) { yield return selected; yield break; }
            foreach (long id in candidates)
                if (!selected.Contains(id))
                    foreach (var ids in OrderedTargets(candidates, count, selected.Add(id))) yield return ids;
        }

        private void Tablet(ImmutableArray<PredictorInput> inputs, PredictorOperationResult result,
            ImmutableArray<OperationPreviewSection> sections)
        {
            int step = scene.TabletCount + 1;
            sections = sections.Select(s => s with { Label = step.ToString() }).ToImmutableArray();
            // The fifth decipher upgrades the whole eligible deck. Its public
            // consequence is omitted from display; settlement stays unchanged.
            while (step < 4 && result.Waiting.EventOptions.Any(o => o.Key == "DECIPHER" && o.Enabled))
            {
                token.ThrowIfCancellationRequested();
                inputs = inputs.Add(new ChooseEventOption("DECIPHER"));
                if (Query(inputs) is not { } next) break;
                step++;
                sections = sections.AddRange(Observe(result, next).Select(s => s with { Label = step.ToString() }));
                result = next;
            }
            Add(sections);
        }

        private void Bridge(PredictorOperationResult before, ImmutableArray<PredictorInput> inputs)
        {
            var sections = ImmutableArray.CreateBuilder<OperationPreviewSection>();
            PredictorOperationResult current = before;
            // The public OVERCOME proposal is omitted. HOLD_ON begins with
            // the first future reroll and keeps the entire reachable sequence.
            var prefix = ImmutableArray<PredictorInput>.Empty;
            if (optionKey == "HOLD_ON")
            {
                prefix = inputs;
                if (Query(prefix) is not { } first) return;
                current = first;
            }
            int step = 1;
            while (current.Waiting.EventOptions.FirstOrDefault(o => o.Key == "OVERCOME")?.CardTargetId is { } id)
            {
                if (step > MaximumAlternatives)
                { _pending.Add("BridgeContinuationLimit:256"); break; }
                sections.Add(new(scene.Event,
                    [new(OperationPreviewAction.RemoveCard, [], [current.State.Card(id)], false)], [])
                    { Label = (step++).ToString() });
                if (!includeContinuation) break;
                if (!current.Waiting.EventOptions.Any(o => o.Key == "HOLD_ON" && o.Enabled)) break;
                prefix = prefix.Add(new ChooseEventOption("HOLD_ON"));
                if (Query(prefix) is not { } next) break;
                current = next;
            }
            Add(sections.ToImmutable());
        }

        private ImmutableArray<OperationPreviewSection> Observe(PredictorOperationResult before, PredictorOperationResult after)
        {
            var sections = RelicOperationPreview.ObserveResult(snapshot.Context, before.State, before, after, scene.Event).ToBuilder();
            if (scene.Event.Entry == "TRIAL" && optionKey == "NONDESCRIPT_GUILTY")
            {
                int ordinal = 0;
                for (int i = 0; i < sections.Count; i++)
                    sections[i] = sections[i] with { CardGroups = sections[i].CardGroups
                        .Select(group => group with { Label = (++ordinal).ToString() }).ToImmutableArray() };
            }
            // These identities are already printed on their native option.
            // Retain only the hidden obtain consequences (potions, upgrades,
            // extra relics, etc.) in the prediction surface.
            ModelKey? publicRelic = scene.Event.Entry switch
            {
                "RELIC_TRADER" => scene.Options.FirstOrDefault(o => o.Key == optionKey)?.OfferedRelic,
                "HUNGRY_FOR_MUSHROOMS" => new("RELIC", optionKey),
                _ => null
            };
            if (publicRelic is { } known)
                for (int i = 0; i < sections.Count; i++)
                    sections[i] = sections[i] with { Rows = sections[i].Rows.Select(row => row.Action == OperationPreviewAction.ObtainRelic
                        ? row with { Models = row.Models.Where(key => key != known).ToImmutableArray() } : row)
                        .Where(row => !row.Models.IsEmpty || !row.Cards.IsEmpty).ToImmutableArray() };
            var rows = ImmutableArray.CreateBuilder<OperationPreviewRow>();
            var transformGroups = ImmutableArray.CreateBuilder<OperationPreviewCardGroup>();
            var effects = after.Effects.Skip(before.Effects.Length);
            foreach (var effect in effects)
            {
                if (effect.Action is ("EventTransform" or "ConveyorJellyTransform") && !effect.Transformations.IsEmpty)
                {
                    if (!selectedTargets.IsDefault)
                    {
                        for (int slot = 0; slot < selectedTargets.Length; slot++)
                            if (effect.Transformations.SingleOrDefault(t => t.OriginalId == selectedTargets[slot]) is { } change)
                                transformGroups.Add(new(-1 - slot, [after.State.Card(change.ReplacementId)], 1, 1, false, false)
                                    { Label = (slot + 1).ToString() });
                    }
                    else
                    {
                        var cards = effect.Transformations.Where(t => after.State.Cards.Any(c => c.Id == t.ReplacementId))
                            .Select(t => after.State.Card(t.ReplacementId)).ToImmutableArray();
                        if (!cards.IsEmpty) rows.Add(new(OperationPreviewAction.TransformCard, [], cards, false));
                    }
                }
                var action = effect.Action switch
                {
                    "ReflectionsDowngrade" or "WongoLeaveDowngrade" => OperationPreviewAction.DowngradeCard,
                    "ReflectionsUpgrade" or "TabletUpgrade" or "DoorsLightUpgrade" or "ConveyorSpicyUpgrade" or
                        "ConveyorChefUpgrade" or "BattlewornDummyUpgrade" => OperationPreviewAction.UpgradeCard,
                    "AutomatonStudyCard" or "AutomatonCoreCard" or "TinkerTimeCard" or "TrashHeapGrab" or
                        "ConveyorFriedEel" => OperationPreviewAction.AddCard,
                    _ => (OperationPreviewAction?)null
                };
                if (action == null) continue;
                var ids = !effect.Targets.IsEmpty ? effect.Targets : ImmutableArray.Create(effect.ObjectId);
                var cardsForEffect = ids.Where(id => after.State.Cards.Any(c => c.Id == id)).Select(after.State.Card).ToImmutableArray();
                // Reflections may downgrade and then upgrade the same card. Its
                // final state cannot stand in for the earlier downgrade image.
                if (effect.Action is "ReflectionsDowngrade" or "WongoLeaveDowngrade")
                    cardsForEffect = cardsForEffect.Select(c => PredictorCardLevelEffects.Reconcile(snapshot.Context,
                        c with { UpgradeLevel = 0 })).ToImmutableArray();
                if (!cardsForEffect.IsEmpty) rows.Add(new(action.Value, [], cardsForEffect, false));
            }
            if (rows.Count > 0) sections.Add(new(scene.Event, rows.ToImmutable(), []));
            if (transformGroups.Count > 0) sections.Add(new(scene.Event, [], transformGroups.ToImmutable())
                { Category = RolltheSpire2.Ui.Settings.OperationPredictionCategory.CardTransform });
            return sections.ToImmutable();
        }
    }

    private static bool IsRandomTransform(string ev, string option) => (ev, option) is
        ("AROMA_OF_CHAOS", "LET_GO") or ("MORPHIC_GROVE", "GROUP") or
        ("SYMBIOTE", "KILL_WITH_FIRE") or ("WHISPERING_HOLLOW", "HUG") or
        ("ENDLESS_CONVEYOR", "GRAB_JELLY_LIVER") or ("TRIAL", "NONDESCRIPT_INNOCENT");

}
