using System.Collections.Immutable;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;

internal static class PredictorDeckKnowledgeChecks
{
    private static readonly PredictorCard Attack = Card("STRIKE_IRONCLAD", EffectCardType.Attack);
    private static readonly PredictorCard Skill = Card("DEFEND_IRONCLAD", EffectCardType.Skill);
    private static readonly PredictorCard Power = Card("CONTROLLED_POWER", EffectCardType.Power);
    private static readonly PredictorCard TwoLevel = Card("CONTROLLED_TWO_LEVEL", EffectCardType.Attack) with { MaxUpgradeLevel = 2 };
    private static readonly PredictorCard CatalogStrike = Card("CONTROLLED_STARTER", EffectCardType.Attack);
    private static readonly PredictorCard MisleadingName = Card("CONTROLLED_STRIKE_NAME", EffectCardType.Attack);
    private static readonly PredictorCard Quest = Card("CONTROLLED_QUEST", EffectCardType.Skill);
    private static readonly PredictorContext Context = new("SKDPGF6RC1PC", "0.111.0", new("CHARACTER", "IRONCLAD"),
        0, "deck-knowledge-fixture", "unlocks", [], [], [], 1)
    {
        Catalog = new([], [])
        {
            CardDefinitions = new[] { Attack, Skill, Power, TwoLevel, CatalogStrike, MisleadingName, Quest }
                .Select(c => new PredictorCardDefinition(c,
                    c.Key == Attack.Key || c.Key == Skill.Key || c.Key == CatalogStrike.Key ? "Basic" : "Common",
                    new("CARD_POOL", "CONTROLLED"), c.Key == Quest.Key,
                    c.Key == Attack.Key || c.Key == CatalogStrike.Key, c.Key == Skill.Key)
                { Levels = Enumerable.Range(0, c.MaxUpgradeLevel + 1).Select(level => new PredictorCardLevelMetadata(
                    level, 1, false, false, false, false, false, false)).ToImmutableArray() }).ToImmutableArray(),
            ShopGainsBlock = [Skill.Key]
        }
    };

    private static PredictorCard Card(string key, EffectCardType type) => new(0, new("CARD", key), type, 0, 1, true, 1);
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    internal static void Run()
    {
        CompleteAndSelectiveKnowledge();
        CorrelatedChoicesAndSkip();
        ActualObtainHooksAndSideEffects();
        ExistingChangesAndKnownEdits();
        UnknownHooksAndMetadata();
        CatalogQualificationsAndManyRewards();
        RandomUpgradeMutationCounts();
        RandomUpgradeSynchronization();
        EmptyPrefixAndUnknownAdditions();
        EventChoiceCompositionAndTargetReads();
        Console.WriteLine("PREDICTOR_DECK_KNOWLEDGE_PASS deck owner; typed counts/candidates; source correlation; actual Egg/BingBong/Hoarder/Lens hooks; scoped gaps; known edits; 512 independent rewards; persistent W/WP population mutations");
    }

    private static PredictorState State(PredictorContext? context = null, params string[] relics) =>
        PredictorSeedState.Create(context ?? Context, Enumerable.Repeat(Attack, 6).Concat(Enumerable.Repeat(Skill, 4)),
            relics.Select(k => new ModelKey("RELIC", k)), 99, 2, new(-.05f, .4f));

    private static PredictorDeckKnowledge Add(PredictorDeckKnowledge deck, PredictorState state,
        string id, params PredictorCard?[] offers) => deck.AddRewardChoice(id, "node:" + id,
            offers.Select((card, index) => new PredictorDeckRewardAlternative("option:" + index, card)).ToImmutableArray(),
            Context, state, hookKnown: true);

    private static void CompleteAndSelectiveKnowledge()
    {
        var state = State();
        var baseline = PredictorDeckKnowledge.Create(Context, state);
        Check(baseline.Project(PredictorDeckProjectionScope.All).KnownCount == 10,
            "The owner must start with the actual complete deck population");
        var pending = Add(baseline, state, "skills", Skill, Skill with { Enchantment = new("ENCHANTMENT", "GLAM"), EnchantmentAmount = 1 });
        var attacks = pending.Project(PredictorDeckProjectionScope.UpgradableAttack);
        Check(attacks.KnownCount == 6 && attacks.OrderedCandidatesKnown && attacks.KnownCandidates.Select(c => c.Id)
            .SequenceEqual(state.Deck.Take(6)) && attacks.UnresolvedCandidateSources.IsEmpty,
            "A skill-only pending choice must preserve the complete known Attack candidate order");
        var skills = pending.Project(PredictorDeckProjectionScope.UpgradableSkill);
        Check(skills.KnownCount == 5 && !skills.OrderedCandidatesKnown && skills.KnownCandidates.Length == 4 &&
            skills.UnresolvedCountSources.IsEmpty && skills.UnresolvedCandidateSources.Single().ChoiceId == "skills",
            "Constant candidate count must remain separate from unknown new candidate identities");
        Check(pending.Choices.Single().Effects.All(e => e.AddedCards.All(c => c.Id == 0)) &&
            pending.Project(PredictorDeckProjectionScope.All).KnownCandidates.All(c => c.Id > 0),
            "Scratch choice instance IDs must never become known inventory facts");
        Check(baseline.Project(PredictorDeckProjectionScope.All).KnownCount == 10 && state.Deck.Length == 10,
            "Adding knowledge must leave the old owner and detached source state unchanged");
    }

    private static void CorrelatedChoicesAndSkip()
    {
        var state = State();
        var deck = Add(PredictorDeckKnowledge.Create(Context, state), state, "first", Attack, Skill);
        var attack = deck.Project(PredictorDeckProjectionScope.UpgradableAttack);
        var skill = deck.Project(PredictorDeckProjectionScope.UpgradableSkill);
        Check((attack.UnstableShuffleDraws! + skill.UnstableShuffleDraws!).KnownValue == 9,
            "Whetstone and War Paint consume exactly nine draws across the same mutually exclusive reward");
        Check(attack.Sources.Single().NodeId == "node:first" && attack.UnresolvedCountSources.Single().ChoiceId == "first",
            "A projection must retain the original node and choice identity");
        var twice = Add(deck, state, "second", Attack, Skill);
        Check((twice.Project(PredictorDeckProjectionScope.UpgradableAttack).UnstableShuffleDraws! +
            twice.Project(PredictorDeckProjectionScope.UpgradableSkill).UnstableShuffleDraws!).KnownValue == 10,
            "Two original rewards must combine without losing each reward's internal correlation");
        var skip = Add(PredictorDeckKnowledge.Create(Context, state), state, "skip", Attack, Skill, null);
        var advance = skip.Project(PredictorDeckProjectionScope.UpgradableAttack).UnstableShuffleDraws! +
            skip.Project(PredictorDeckProjectionScope.UpgradableSkill).UnstableShuffleDraws!;
        Check(advance.KnownValue == null && advance.Bounds == new PredictorChoiceIntBounds(8, 9) &&
            advance.Evaluate(new Dictionary<string, int> { ["skip"] = 2 }) == 8,
            "A skipped reward allows eight or nine draws, never the invented independent maximum ten");
        Throws(() => deck.AddRewardChoice("first", "other-node", [new("other-option", Attack)], Context, state, true),
            "A duplicate choice identity must not be counted again or silently change its contract");
    }

    private static void ActualObtainHooksAndSideEffects()
    {
        var eggState = State(null, "MOLTEN_EGG", "TOXIC_EGG", "FROZEN_EGG");
        var eggs = Add(PredictorDeckKnowledge.Create(Context, eggState), eggState, "egg", Attack, Skill, Power);
        Check(eggs.Choices.Single().Effects.All(e => e.AddedCards.Single().UpgradeLevel == 1) &&
            eggs.Project(PredictorDeckProjectionScope.UpgradableAttack).KnownCount == 6 &&
            eggs.Project(PredictorDeckProjectionScope.UpgradableAttack).OrderedCandidatesKnown,
            "Egg obtain upgrades must remove fully upgraded new cards from the candidate population");
        var alreadyUpgraded = Add(PredictorDeckKnowledge.Create(Context, eggState), eggState, "already-upgraded",
            TwoLevel with { UpgradeLevel = 1 });
        Check(alreadyUpgraded.Choices.Single().Effects.Single().AddedCards.Single().UpgradeLevel == 1,
            "Deck insertion Eggs must not upgrade an already upgraded card a second time");

        var context = Context with { Modifiers = [new(new("MODIFIER", "HOARDER"))] };
        var state = State(context, "BING_BONG", "BING_BONG", "LUCKY_FYSH", "BOOK_OF_FIVE_RINGS", "FRESNEL_LENS");
        var beforeRelics = state.Relics;
        var deck = PredictorDeckKnowledge.Create(context, state).AddRewardChoice("copies", "copy-node",
            [new("Attack", Attack), new("Skill", Skill), new("Skip", null)], context, state, true);
        var effects = deck.Choices.Single().Effects;
        Check(effects[0].AddedCards.Length == 5 && effects[1].AddedCards.Length == 5 && effects[2].AddedCards.IsEmpty,
            "Two Bing Bong listeners plus Hoarder must produce five cards for a taken reward");
        Check(effects[1].AddedCards.All(c => c.Enchantment?.Entry == "NIMBLE" && c.EnchantmentAmount == 2),
            "Fresnel Lens must use actual block qualification and permanent obtain properties for copies");
        long bookId = state.Relics.Single(r => r.Key.Entry == "BOOK_OF_FIVE_RINGS").Id;
        Check(deck.ProjectGoldDelta()!.Evaluate(new Dictionary<string, int> { ["copies"] = 0 }) == 75 &&
            deck.ProjectBookCounterDelta(bookId)!.Evaluate(new Dictionary<string, int> { ["copies"] = 1 }) == 5 &&
            deck.ProjectGoldDelta()!.Evaluate(new Dictionary<string, int> { ["copies"] = 2 }) == 0,
            "Gold and Book counter deltas must preserve the same reward alternatives rather than mutate central state");
        Check(state.Relics == beforeRelics && state.GoldBudget == 99 && state.HoarderCardsToSkip.IsEmpty,
            "Single-alternative hook replay must not mutate the caller's relic counters, gold or skip sets");

        var skipState = State(null, "BING_BONG");
        long offeredId = skipState.NextInstanceId;
        var offered = Attack with { Id = offeredId };
        skipState = skipState with { NextInstanceId = offeredId + 1, Cards = skipState.Cards.Add(offered),
            Relics = skipState.Relics.SetItem(0, skipState.Relics[0] with { BingBongCardsToSkip = [offeredId] }) };
        var skippedCopy = Add(PredictorDeckKnowledge.Create(Context, skipState), skipState, "skip-copy", offered);
        Check(skippedCopy.Choices.Single().Effects.Single().AddedCards.Length == 1,
            "An already registered offer in Bing Bong's lifetime skip set must not be copied again");
    }

    private static void ExistingChangesAndKnownEdits()
    {
        var state = State();
        var deck = Add(PredictorDeckKnowledge.Create(Context, state), state, "pending", Attack, Skill)
            .MarkExistingChangePending("whetstone-node", "UpgradeTargetsNotObserved", PredictorDeckUncertainFact.Upgrade,
                cardTypes: [EffectCardType.Attack]);
        Check(deck.Project(PredictorDeckProjectionScope.UpgradableAttack).Count == null &&
            deck.Project(PredictorDeckProjectionScope.All).KnownCount == 11,
            "An unresolved upgrade must mask affected qualification, without inventing a membership change");
        var skill = deck.Project(PredictorDeckProjectionScope.UpgradableSkill);
        Check(skill.Count != null && skill.Gaps.IsEmpty,
            "An Attack-only old-card change must leave the correlated Skill count expressible");
        var fresh = Skill with { Id = 10000 };
        var edited = deck.AddKnown(fresh).RemoveKnown(state.Deck[6])
            .ReplaceKnown(state.Deck[7], Skill with { Id = 10001 });
        var editedSkills = edited.Project(PredictorDeckProjectionScope.UpgradableSkill);
        Check(editedSkills.Count!.Bounds == new PredictorChoiceIntBounds(4, 5) &&
            edited.Project(PredictorDeckProjectionScope.UpgradableAttack).Count == null && edited.Choices.Length == 1,
            "Fixed add/remove/replace must retain earlier choice contributions and affected old-card gaps");
        Check(editedSkills.KnownCandidates.Select(c => c.Id).SequenceEqual(new long[] { state.Deck[8], state.Deck[9], 10000, 10001 }),
            "Accepted replacements append in actual transform order while preserving the known subset order");
        var late = PredictorDeckKnowledge.Create(Context, state).MarkExistingChangePending("prior", "RandomUpgrade",
            PredictorDeckUncertainFact.Upgrade).AddKnown(Attack with { Id = 10002 });
        Check(late.Project(PredictorDeckProjectionScope.UpgradableAttack).KnownCandidates.Single().Id == 10002,
            "A later known add must not inherit an earlier old-card modification gap");
    }

    private static void UnknownHooksAndMetadata()
    {
        var state = State();
        var unknown = PredictorDeckKnowledge.Create(Context, state).AddRewardChoice("hook-unknown", "unknown-node",
            [new("take", Skill), new("skip", null)], Context, state, hookKnown: false);
        var projection = unknown.Project(PredictorDeckProjectionScope.All);
        Check(projection.Count == null && !projection.OrderedCandidatesKnown && projection.KnownCandidates.Length == 10 &&
            projection.Gaps.Single().Reason == "CardObtainHooksNotKnown" &&
            projection.Gaps.Single().ChoiceId == "hook-unknown" && unknown.ProjectGoldDelta() == null,
            "An unresolved hook inventory must not permit affirmative count or side-effect derivation from a witness");
        var unsupported = Add(PredictorDeckKnowledge.Create(Context, state), state, "unsupported",
            Card("CONTROLLED_MISSING_DEFINITION", EffectCardType.Skill));
        Check(unsupported.Project(PredictorDeckProjectionScope.All).Count == null &&
            unsupported.Choices.Single().Effects.Single().UnsupportedReason!.StartsWith("CardObtainMetadataMissing:", StringComparison.Ordinal),
            "Missing obtain metadata must remain an explicit implementation gap, not an invented ordinary add");
    }

    private static void CatalogQualificationsAndManyRewards()
    {
        var state = PredictorSeedState.Create(Context, [CatalogStrike, MisleadingName, Quest], [], 0, 0, new(0, 0));
        var deck = PredictorDeckKnowledge.Create(Context, state);
        Check(deck.Project(PredictorDeckProjectionScope.BasicStrike).KnownCandidates.Single().Key == CatalogStrike.Key &&
            deck.Project(PredictorDeckProjectionScope.Basic).KnownCount == 1,
            "Basic and Strike qualification must use catalog metadata rather than guessing from card names");
        Check(deck.Project(PredictorDeckProjectionScope.Transformable).KnownCount == 2 &&
            deck.Project(PredictorDeckProjectionScope.Cloneable).KnownCount == 2 &&
            deck.Project(PredictorDeckProjectionScope.Removable).KnownCount == 3,
            "Quest exclusions differ from removable population and must remain consumer-specific");
        state = State();
        deck = PredictorDeckKnowledge.Create(Context, state);
        const int rewards = 512;
        for (int i = 0; i < rewards; i++) deck = Add(deck, state, "many:" + i, Attack, Skill);
        var advance = deck.Project(PredictorDeckProjectionScope.UpgradableAttack).UnstableShuffleDraws! +
            deck.Project(PredictorDeckProjectionScope.UpgradableSkill).UnstableShuffleDraws!;
        Check(advance.KnownValue == 8 + rewards && deck.Choices.Length == rewards,
            "Many independent rewards must retain per-source mutual exclusion without Cartesian expansion");
    }

    private static void RandomUpgradeMutationCounts()
    {
        var state = State();
        var deck = Add(PredictorDeckKnowledge.Create(Context, state), state, "upgrade-choice", Attack, Skill);
        var whetstone = deck.ApplyRandomUpgrade("w-node", "whetstone:1", EffectCardType.Attack);
        var warPaint = whetstone.Deck.ApplyRandomUpgrade("wp-node", "war-paint:1", EffectCardType.Skill);
        Check((whetstone.Consumption! + warPaint.Consumption!).KnownValue == 9 &&
            warPaint.Deck.Project(PredictorDeckProjectionScope.Upgradable).KnownCount == 7 &&
            warPaint.Deck.Project(PredictorDeckProjectionScope.Upgraded).KnownCount == 4,
            "Two type-specific mutations must preserve shared-choice cancellation in consumption and aggregate populations");
        Check(whetstone.CandidateSources.Single().ChoiceId == "upgrade-choice" &&
            whetstone.SourceOperation == "whetstone:1" &&
            whetstone.Deck.Project(PredictorDeckProjectionScope.UpgradableSkill).Gaps.IsEmpty &&
            whetstone.Deck.Project(PredictorDeckProjectionScope.Basic).KnownCount == 11 &&
            whetstone.Deck.Project(PredictorDeckProjectionScope.All).KnownCount == 11,
            "Unknown Attack targets must preserve original choice provenance and independent Skill/Basic/membership facts");
        var after = warPaint.Deck.AddKnown(Attack with { Id = 20000 });
        var candidates = after.Project(PredictorDeckProjectionScope.UpgradableAttack);
        Check(candidates.Count!.Bounds == new PredictorChoiceIntBounds(5, 6) &&
            candidates.KnownCandidates.Single().Id == 20000 && !candidates.OrderedCandidatesKnown &&
            after.Project(PredictorDeckProjectionScope.Upgradable).KnownCount == 8,
            "A future known add must increase the retained mutation expression and remain a known individual candidate");
        after = Add(after, state, "future-choice", Attack, Skill);
        Check(after.Project(PredictorDeckProjectionScope.Upgradable).KnownCount == 9,
            "A later independent reward must add after the old upgrade boundary, without repeating old mutations");

        var skip = Add(PredictorDeckKnowledge.Create(Context, state), state, "upgrade-skip", Attack, Skill, null);
        var skipW = skip.ApplyRandomUpgrade("skip-w", "w", EffectCardType.Attack);
        var skipWP = skipW.Deck.ApplyRandomUpgrade("skip-wp", "wp", EffectCardType.Skill);
        var consumption = skipW.Consumption! + skipWP.Consumption!;
        Check(consumption.Bounds == new PredictorChoiceIntBounds(8, 9) &&
            skipWP.Deck.Project(PredictorDeckProjectionScope.Upgradable).Count!.Bounds == new PredictorChoiceIntBounds(6, 7),
            "The skip branch must survive both population mutations without independent maximum ten");
        var twice = whetstone.Deck.ApplyRandomUpgrade("second-w", "whetstone:2", EffectCardType.Attack);
        Check(twice.Deck.Project(PredictorDeckProjectionScope.UpgradableAttack).Count!.Bounds == new PredictorChoiceIntBounds(2, 3),
            "Repeated same-type upgrades must compose against the owned post-upgrade count, not the old raw cards");

        var concrete = PredictorDeckKnowledge.Create(Context, state).ApplyRandomUpgrade("concrete", "selected", EffectCardType.Attack,
            selectedIds: state.Deck.Take(2).ToImmutableArray());
        var concreteCards = concrete.Deck.Project(PredictorDeckProjectionScope.UpgradableAttack);
        Check(concreteCards.KnownCount == 4 && concreteCards.OrderedCandidatesKnown &&
            concreteCards.KnownCandidates.Select(c => c.Id).SequenceEqual(state.Deck.Skip(2).Take(4)),
            "Concrete selected identities must reconcile upgrades while preserving complete candidate truth");
        var sync = concrete.Deck.SyncKnown([PredictorCardLevelEffects.Reconcile(Context, state.Card(state.Deck[0]) with { UpgradeLevel = 1 })]);
        Check(sync.Project(PredictorDeckProjectionScope.UpgradableAttack).KnownCount == 4,
            "Sync of already reflected concrete facts must not apply a second numeric upgrade");
        Throws(() => deck.ApplyRandomUpgrade("bad", "witness", EffectCardType.Attack, selectedIds: state.Deck.Take(2).ToImmutableArray()),
            "An incomplete candidate population must not certify selected IDs from a representative witness");

        var multi = PredictorSeedState.Create(Context, [TwoLevel, TwoLevel, TwoLevel], [], 0, 0, new(0, 0));
        var multiUpgrade = PredictorDeckKnowledge.Create(Context, multi).ApplyRandomUpgrade("multi", "m", EffectCardType.Attack);
        Check(multiUpgrade.Consumption!.KnownValue == 2 &&
            multiUpgrade.Deck.Project(PredictorDeckProjectionScope.UpgradableAttack).Count == null &&
            multiUpgrade.Gaps.Any(g => g.Reason == "RandomUpgradeCountDerivationNotImplemented") &&
            multiUpgrade.Deck.AddKnown(Attack with { Id = 20001 }).Project(PredictorDeckProjectionScope.UpgradableAttack)
                .KnownCandidates.Single().Id == 20001,
            "Unsupported multi-level count derivation must preserve exact consumption and a bounded old-card gap");
        var laterGap = warPaint.Deck.MarkExistingChangePending("later", "AdditionalUnobservedUpgrade",
            PredictorDeckUncertainFact.Upgrade, cardTypes: [EffectCardType.Attack]);
        Check(laterGap.Project(PredictorDeckProjectionScope.UpgradableAttack).Count == null &&
            laterGap.Project(PredictorDeckProjectionScope.UpgradableSkill).Count != null,
            "A later missing old-card change must invalidate earlier affected count proofs without masking another type");
        var empty = PredictorSeedState.Create(Context, [], [], 0, 0, new(0, 0));
        var small = Add(PredictorDeckKnowledge.Create(Context, empty), empty, "small", Attack, Skill)
            .ApplyRandomUpgrade("small-w", "w", EffectCardType.Attack).Deck;
        Check(small.Project(PredictorDeckProjectionScope.UpgradableAttack).KnownCount == 0 &&
            small.Project(PredictorDeckProjectionScope.UpgradableAttack).OrderedCandidatesKnown &&
            small.Project(PredictorDeckProjectionScope.UpgradableAttack).Sources.Single().ChoiceId == "small",
            "An exact zero population must recover an exact empty candidate set while retaining its original choice provenance");
        var sole = small.AddKnown(Attack with { Id = 20002 }).Project(PredictorDeckProjectionScope.UpgradableAttack);
        Check(sole.KnownCount == 1 && sole.OrderedCandidatesKnown && sole.KnownCandidates.Single().Id == 20002,
            "A known late add after an exact zero prefix must recover the complete candidate set");
    }

    private static void RandomUpgradeSynchronization()
    {
        var state = State();
        var initial = Add(PredictorDeckKnowledge.Create(Context, state), state, "sync-choice", Attack, Skill);
        var upgraded = initial.ApplyRandomUpgrade("sync-w", "w", EffectCardType.Attack).Deck
            .ApplyRandomUpgrade("sync-wp", "wp", EffectCardType.Skill).Deck;
        var witness = state.Deck.Select(state.Card).Select((card, index) => index is 0 or 1 or 6 or 7
            ? PredictorCardLevelEffects.Reconcile(Context, card with { UpgradeLevel = 1 }) : card).ToImmutableArray();
        var afterRoom = witness.Select(card => card with
        { RoomsEntered = 1, CombatsSeen = 1, IncreasedBlock = 8, IncreasedDamage = 9,
            PermanentCostOverride = 0, FloorAdded = 20 }).ToImmutableArray();
        var synced = upgraded.SyncKnown(afterRoom);
        Check(synced.Project(PredictorDeckProjectionScope.UpgradableAttack).Count!.Bounds == new PredictorChoiceIntBounds(4, 5) &&
            synced.Project(PredictorDeckProjectionScope.UpgradableSkill).Count!.Bounds == new PredictorChoiceIntBounds(2, 3) &&
            synced.Project(PredictorDeckProjectionScope.Upgradable).KnownCount == 7 &&
            synced.Project(PredictorDeckProjectionScope.Upgraded).KnownCount == 4,
            "Room/combat counters and unrelated numeric synchronization must preserve both actual upgrade count mutations");
        var attacks = synced.Project(PredictorDeckProjectionScope.UpgradableAttack);
        Check(attacks.KnownCandidates.IsEmpty && !attacks.OrderedCandidatesKnown && attacks.Gaps.Length != 0 &&
            synced.Project(PredictorDeckProjectionScope.All).KnownCandidates.All(c => c.UpgradeLevel == 0 &&
                c.RoomsEntered == 1 && c.CombatsSeen == 1 && c.IncreasedDamage == 9),
            "Metadata from a witness may synchronize while its unresolved upgrade values stay masked and old candidate identity gaps survive");
        var routeSync = upgraded;
        for (int i = 0; i < witness.Length; i++)
            routeSync = routeSync.ReplaceKnown(witness[i].Id, afterRoom[i], append: false, observedBefore: witness[i]);
        Check(routeSync.Project(PredictorDeckProjectionScope.Upgradable).KnownCount == 7 &&
            routeSync.Project(PredictorDeckProjectionScope.UpgradableAttack).Count!.Equals(attacks.Count) &&
            routeSync.Project(PredictorDeckProjectionScope.UpgradableSkill).Count!.Bounds == new PredictorChoiceIntBounds(2, 3),
            "Actual before/after witness comparisons must ignore stale selected upgrades when only metadata changed");
        var later = routeSync.AddKnown(Attack with { Id = 21000 });
        Check(later.Project(PredictorDeckProjectionScope.UpgradableAttack).Count!.Bounds == new PredictorChoiceIntBounds(5, 6) &&
            later.Project(PredictorDeckProjectionScope.UpgradableAttack).KnownCandidates.Single().Id == 21000 &&
            later.Project(PredictorDeckProjectionScope.Upgradable).KnownCount == 8,
            "A synchronized old mutation must continue composing with later independent known additions");

        var realUnknownEdit = routeSync.ReplaceKnown(witness[0].Id,
            afterRoom[0] with { UpgradeLevel = 0 }, append: false, observedBefore: afterRoom[0]);
        Check(realUnknownEdit.Project(PredictorDeckProjectionScope.UpgradableAttack).Count == null &&
            realUnknownEdit.Project(PredictorDeckProjectionScope.UpgradableSkill).Count!.Bounds == new PredictorChoiceIntBounds(2, 3) &&
            realUnknownEdit.Project(PredictorDeckProjectionScope.All).KnownCount == 11,
            "A genuinely changed unresolved upgrade read must invalidate its count proof without invalidating another type or deck membership");
        var concrete = PredictorDeckKnowledge.Create(Context, state).ApplyRandomUpgrade("sync-concrete", "selected", EffectCardType.Attack,
            selectedIds: state.Deck.Take(2).ToImmutableArray()).Deck;
        var third = PredictorCardLevelEffects.Reconcile(Context, state.Card(state.Deck[2]) with { UpgradeLevel = 1 });
        concrete = concrete.SyncKnown([third]);
        Check(concrete.Project(PredictorDeckProjectionScope.UpgradableAttack).KnownCount == 3 &&
            concrete.Project(PredictorDeckProjectionScope.UpgradableAttack).OrderedCandidatesKnown,
            "A proven upgrade property edit must still adjust a concrete mutation rather than being ignored as metadata");

        var identityChoice = Add(initial, state, "identity-only", Attack, CatalogStrike);
        Check(identityChoice.Project(PredictorDeckProjectionScope.UpgradableAttack).Count?.Bounds == new PredictorChoiceIntBounds(7, 8) &&
            identityChoice.Choices.Last().Effects.All(e => e.AdditionsKnown),
            "Both identity-only alternatives must be supported by the real obtain helper and add exactly one eligible Attack");
        var mixedSources = identityChoice.ApplyRandomUpgrade("sources-w", "w", EffectCardType.Attack).Deck
            .Project(PredictorDeckProjectionScope.UpgradableAttack);
        Check(mixedSources.Count?.Bounds == new PredictorChoiceIntBounds(5, 6) &&
            mixedSources.UnresolvedCountSources.Select(s => s.ChoiceId).SequenceEqual(["sync-choice"]) &&
            mixedSources.UnresolvedCandidateSources.Select(s => s.ChoiceId).SequenceEqual(["sync-choice", "identity-only"]) &&
            mixedSources.UnresolvedCountGaps.IsEmpty && !mixedSources.Gaps.IsEmpty,
            "Restored count proofs must use only numeric choice roots; constant-count identity choices and random-target gaps belong to candidates");
        var multi = PredictorSeedState.Create(Context, [TwoLevel, TwoLevel, TwoLevel], [], 0, 0, new(0, 0));
        var unsupported = PredictorDeckKnowledge.Create(Context, multi)
            .ApplyRandomUpgrade("sync-multi", "m", EffectCardType.Attack).Deck.Project(PredictorDeckProjectionScope.UpgradableAttack);
        Check(unsupported.Count == null && unsupported.UnresolvedCountGaps.Any(g => g.Reason == "RandomUpgradeCountDerivationNotImplemented"),
            "Count-specific gap filtering must retain genuine unsupported multi-level derivations");
    }

    private static void EmptyPrefixAndUnknownAdditions()
    {
        var one = PredictorSeedState.Create(Context, [Attack], [], 0, 0, new(0, 0));
        long id = one.Deck.Single();
        var membership = PredictorDeckKnowledge.Create(Context, one).MarkExistingChangePending("empty-prefix", "UnresolvedRemoval",
            PredictorDeckUncertainFact.Membership, [id], [EffectCardType.Attack], "remove-choice").RemoveKnown(id);
        var attacks = membership.Project(PredictorDeckProjectionScope.Attacks);
        Check(membership.Project(PredictorDeckProjectionScope.All).Count == null && attacks.Count == null &&
            !attacks.OrderedCandidatesKnown && attacks.KnownCandidates.IsEmpty &&
            attacks.UnresolvedCountGaps.Single().ChoiceId == "remove-choice",
            "A witness that empties the original prefix must retain the original instance's possible membership and its choice root");
        Check(membership.Project(PredictorDeckProjectionScope.Skills).KnownCount == 0 &&
            membership.Project(PredictorDeckProjectionScope.Skills).OrderedCandidatesKnown &&
            membership.Project(PredictorDeckProjectionScope.Upgraded).KnownCount == 0,
            "A retained membership alternative must not contaminate a known unrelated type or a known false upgraded predicate");
        Check(membership.MarkExistingChangePending("later-type", "OriginalTypeNotKnown", PredictorDeckUncertainFact.Type, [id])
            .Project(PredictorDeckProjectionScope.Skills).Count == null,
            "A later type gap must still find a retained original instance after its witness prefix was emptied");
        var knownLater = membership.AddKnown(Skill with { Id = 22000 });
        Check(knownLater.Project(PredictorDeckProjectionScope.All).Count == null &&
            knownLater.Project(PredictorDeckProjectionScope.Skills).KnownCount == 1 &&
            knownLater.Project(PredictorDeckProjectionScope.Skills).KnownCandidates.Single().Id == 22000,
            "A known later card must remain a guaranteed candidate without erasing an older possible instance");
        var confirmedAbsent = membership.RemoveKnown(id, absenceKnown: true);
        Check(confirmedAbsent.Project(PredictorDeckProjectionScope.All).KnownCount == 0 &&
            confirmedAbsent.Project(PredictorDeckProjectionScope.Attacks).OrderedCandidatesKnown,
            "Only a separate proof of absence may discharge the removed instance's retained membership uncertainty");
        var allFacts = PredictorDeckKnowledge.Create(Context, one).MarkExistingChangePending("all-prefix", "UnresolvedReplacement",
            PredictorDeckUncertainFact.All, [id]).RemoveKnown(id);
        Check(allFacts.Project(PredictorDeckProjectionScope.All).Count == null &&
            allFacts.Project(PredictorDeckProjectionScope.Skills).Count == null,
            "An unknown original type/definition must survive the witness removal and may affect other type projections");

        var multi = PredictorSeedState.Create(Context, [TwoLevel, TwoLevel, TwoLevel], [], 0, 0, new(0, 0));
        var removed = PredictorDeckKnowledge.Create(Context, multi).ApplyRandomUpgrade("removed-multi", "m", EffectCardType.Attack).Deck;
        foreach (long target in multi.Deck) removed = removed.RemoveKnown(target, absenceKnown: true);
        Check(removed.Project(PredictorDeckProjectionScope.UpgradableAttack).KnownCount == 0 &&
            removed.Project(PredictorDeckProjectionScope.UpgradableAttack).OrderedCandidatesKnown &&
            removed.Project(PredictorDeckProjectionScope.Upgraded).KnownCount == 0,
            "Confirmed removal of every instance must retire unsupported old upgrade count mutations and prove an empty prefix");

        var empty = PredictorSeedState.Create(Context, [], [], 0, 0, new(0, 0));
        var unknown = PredictorDeckKnowledge.Create(Context, empty).MarkUnknownAddition("unknown-offer", "RewardOfferNotDetermined",
            "unknown-choice", [EffectCardType.Skill]);
        var skills = unknown.Project(PredictorDeckProjectionScope.Skills);
        Check(unknown.Project(PredictorDeckProjectionScope.All).Count == null && skills.Count == null &&
            !skills.OrderedCandidatesKnown && skills.KnownCandidates.IsEmpty &&
            skills.Gaps.Single().ChoiceId == "unknown-choice" && skills.UnresolvedCountGaps.Single().Reason == "RewardOfferNotDetermined" &&
            unknown.ProjectGoldDelta() == null && unknown.ProjectBookCounterDelta(1) == null,
            "An unknown incoming offer on an empty deck must preserve possible additions and source evidence without inventing a witness card");
        Check(unknown.Project(PredictorDeckProjectionScope.Attacks).KnownCount == 0 &&
            unknown.Project(PredictorDeckProjectionScope.UpgradableAttack).OrderedCandidatesKnown,
            "An explicit possible-type contract for unknown additions must preserve unrelated Attack facts");
        var disjoint = PredictorDeckKnowledge.Create(Context, multi).MarkUnknownAddition("skill-offer", "OfferNotKnown",
            cardTypes: [EffectCardType.Skill]).ApplyRandomUpgrade("disjoint-w", "w", EffectCardType.Attack).Deck;
        foreach (long target in multi.Deck) disjoint = disjoint.RemoveKnown(target, absenceKnown: true);
        Check(disjoint.Project(PredictorDeckProjectionScope.UpgradableAttack).KnownCount == 0 &&
            disjoint.Project(PredictorDeckProjectionScope.UpgradableAttack).OrderedCandidatesKnown &&
            disjoint.Project(PredictorDeckProjectionScope.Skills).Count == null,
            "A confirmed empty Attack population must not inherit an obsolete upgrade mutation merely because an unknown Skill addition remains");
        var unknownLater = unknown.AddKnown(Attack with { Id = 22001 });
        Check(unknownLater.Project(PredictorDeckProjectionScope.All).Count == null &&
            unknownLater.Project(PredictorDeckProjectionScope.Attacks).KnownCount == 1 &&
            unknownLater.Project(PredictorDeckProjectionScope.Attacks).KnownCandidates.Single().Id == 22001,
            "A later definite addition must not settle an earlier unobserved offer");
        Check(PredictorDeckKnowledge.Create(Context, empty).MarkUnknownAddition("any-offer", "OfferNotKnown")
            .Project(PredictorDeckProjectionScope.Attacks).Count == null,
            "An unknown addition without a type contract must retain possible candidates of every type");
        Throws(() => unknown.MarkUnknownAddition("duplicate", "unknown", "unknown-choice"),
            "A duplicate unknown choice source must not be silently declared again");
        Throws(() => Add(unknown, empty, "unknown-choice", Skill),
            "A known choice cannot silently replace an earlier unknown declaration of the same source");
    }

    private static void EventChoiceCompositionAndTargetReads()
    {
        var state = PredictorSeedState.Create(Context, [Attack, Skill], [], 10, 0, new(0, 0));
        long attackId = state.Deck[0], skillId = state.Deck[1];
        var original = PredictorDeckKnowledge.Create(Context, state);
        var prior = Add(original, state, "prior-event", Attack, null);
        var offered = Attack with { Id = state.NextInstanceId };
        var priorWitness = state with { Cards = state.Cards.Add(offered), Deck = state.Deck.Add(offered.Id),
            NextInstanceId = state.NextInstanceId + 1 };
        var noOp = prior.AddStateChoice("gold-only", "next-node", priorWitness, priorWitness,
            [("keep", priorWitness), ("gold", priorWitness with { GoldBudget = 30 })]);
        Check(noOp.Choices.Select(c => c.ChoiceId).SequenceEqual(["prior-event", "gold-only"]) &&
            noOp.Choices.Last().Effects.All(e => e.AddedCards.IsEmpty) &&
            noOp.Project(PredictorDeckProjectionScope.All).Count!.Bounds == new PredictorChoiceIntBounds(2, 3) &&
            !noOp.HasFixedMember(offered.Id),
            "A later complete local choice must retain the prior source without promoting or duplicating its witness card");

        var upgradeGap = original.MarkExistingChangePending("upgrade-node", "UpgradeTargetUnknown",
            PredictorDeckUncertainFact.Upgrade, [attackId], choiceId: "upgrade-source");
        Check(upgradeGap.ReadTarget(attackId, PredictorDeckProjectionScope.Removable,
                PredictorDeckUncertainFact.Definition | PredictorDeckUncertainFact.Removable)?.Id == attackId &&
            upgradeGap.ReadTarget(attackId, PredictorDeckProjectionScope.All) == null &&
            upgradeGap.TargetGaps(attackId, PredictorDeckProjectionScope.All).Single().ChoiceId == "upgrade-source",
            "A stable removable target must survive unrelated Upgrade uncertainty while broad reads retain the original cause");
        var removalGap = upgradeGap.MarkExistingChangePending("eternal-node", "EternalMayChangeRemoval",
            PredictorDeckUncertainFact.Removable, [attackId]);
        Check(removalGap.ReadTarget(attackId, PredictorDeckProjectionScope.Removable,
                PredictorDeckUncertainFact.Definition | PredictorDeckUncertainFact.Removable) == null,
            "An explicit Eternal/removal uncertainty must still block the narrow remove reader");

        var upgradedCard = PredictorCardLevelEffects.Reconcile(Context, state.Card(attackId) with { UpgradeLevel = 1 });
        var upgradedState = state with { Cards = state.Cards.SetItem(state.Cards.FindIndex(c => c.Id == attackId), upgradedCard) };
        var upgradeChoice = upgradeGap.AddStateChoice("another-upgrade", "upgrade-choice-node", state, state,
            [("keep", state), ("upgrade", upgradedState)]);
        Check(upgradeChoice.Project(PredictorDeckProjectionScope.All).KnownCount == 2 &&
            upgradeChoice.Project(PredictorDeckProjectionScope.All).KnownCandidates.Select(c => c.Id).SequenceEqual(state.Deck) &&
            upgradeChoice.ReadTarget(attackId, PredictorDeckProjectionScope.Removable,
                PredictorDeckUncertainFact.Definition | PredictorDeckUncertainFact.Removable) != null &&
            upgradeChoice.Choices.Single().Effects.All(e => e.AddedCards.IsEmpty),
            "Property-only alternatives must preserve known membership and removal facts on an already partial deck");

        var removeAttack = state with { Deck = state.Deck.Remove(attackId), GoldBudget = 20 };
        var removeSkill = state with { Deck = state.Deck.Remove(skillId), GoldBudget = 30 };
        var removals = original.AddStateChoice("remove-one", "remove-node", state, removeAttack,
            [("attack", removeAttack), ("skill", removeSkill)]);
        var attacks = removals.Project(PredictorDeckProjectionScope.Attacks).Count!;
        var skills = removals.Project(PredictorDeckProjectionScope.Skills).Count!;
        Check(removals.Project(PredictorDeckProjectionScope.All).KnownCount == 1 && (attacks + skills).KnownValue == 1 &&
            attacks.Evaluate(new Dictionary<string, int> { ["remove-one"] = 0 }) == 0 &&
            removals.ProjectGoldDelta()!.Evaluate(new Dictionary<string, int> { ["remove-one"] = 0 }) == 0 &&
            removals.ProjectGoldDelta()!.Evaluate(new Dictionary<string, int> { ["remove-one"] = 1 }) == 10,
            "Completed removals must retain one correlated Gold/deck choice without admitting a fictitious zero- or two-card deck");

        var withoutAttack = priorWitness with { Deck = priorWitness.Deck.Remove(attackId) };
        var composed = prior.AddStateChoice("later-remove", "later-remove-node", priorWitness, priorWitness,
            [("keep", priorWitness), ("remove", withoutAttack)]);
        var composedCount = composed.Project(PredictorDeckProjectionScope.All).Count!;
        Check(composedCount.Bounds == new PredictorChoiceIntBounds(1, 3) &&
            composedCount.Evaluate(new Dictionary<string, int> { ["prior-event"] = 1, ["later-remove"] = 1 }) == 1 &&
            composed.ReadTarget(skillId, PredictorDeckProjectionScope.All) != null &&
            composed.Choices.Last().Effects.All(e => e.AddedCards.IsEmpty),
            "A subsequent removal must compose against the original conditional addition instead of counting its witness twice");
        var uncertainRemoval = upgradeGap.AddStateChoice("remove-after-upgrade", "uncertain-remove-node", state, state,
            [("keep", state), ("remove", state with { Deck = state.Deck.Remove(attackId) })]);
        Check(uncertainRemoval.Project(PredictorDeckProjectionScope.All).Count!.Bounds == new PredictorChoiceIntBounds(1, 2) &&
            uncertainRemoval.Project(PredictorDeckProjectionScope.UpgradableAttack).Count == null &&
            uncertainRemoval.Project(PredictorDeckProjectionScope.UpgradableSkill).KnownCount == 1,
            "Membership deltas may remain exact while unresolved Attack upgrades keep only affected upgrade counts unknown");
        var missing = state with { Cards = state.Cards.SetItem(0, state.Card(attackId) with { Key = new("CARD", "MISSING_TARGET") }) };
        Check(PredictorDeckKnowledge.Create(Context, missing).TargetGaps(attackId, PredictorDeckProjectionScope.All)
            .Any(g => g.Reason.StartsWith("DeckProjectionMetadataMissing:", StringComparison.Ordinal)),
            "A missing target definition must produce explicit metadata evidence instead of an unexplained failed read");
        var reordered = original.AddStateChoice("order", "order-node", state, state,
            [("keep", state), ("reverse", state with { Deck = state.Deck.Reverse().ToImmutableArray() })]);
        Check(reordered.Project(PredictorDeckProjectionScope.All).KnownCount == 2 &&
            !reordered.Project(PredictorDeckProjectionScope.All).OrderedCandidatesKnown,
            "An unsupported existing-card reorder may preserve counts but must not certify the original candidate order");
    }

    private static void Throws(Action action, string message)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException(message);
    }
}
