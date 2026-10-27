using RolltheSpire2.Ui.Pages.Search.Editors;
using RolltheSpire2.Ui.Controls.Pickers;
using Godot;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Core.Events;
using RolltheSpire2.Core.Neow;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Pages.Search.CombatReward;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Presentation.Localization;

internal static partial class PartyPhase1Checks
{
    private static void RunProbability(GameVersionDetection detection)
    {
        MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = false;
        int checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception(why); checks++; }
        void Equal(double? actual, double? expected, string why) => Check(actual is { } a && expected is { } e &&
            Math.Abs(a - e) < 1e-12, why + $": actual={actual}, expected={expected}");
        var party = PartyRuntimeAuthorityCapture.Capture(Beta111Profile.Instance, "000000000040",
            BaseGameModelKeys.Characters.All.Take(2).ToArray(), Enumerable.Range(0, 2).Select(_ => UnlockState.all.ToSerializable()).ToArray(), 10, "0.111.0");
        var context = SearchContextFactory.From(RuntimeProfileId.Beta111, party.Players[0].Character.CharacterKey,
            10, party.Players[0], detection, AncientOptionConditionProfile.BroadDefault) with { Party = party };
        checks += CheckPairedMapProbability(context);
        checks += CheckOpeningProbability(context);
        SearchQuery Table(SearchQuery first, SearchQuery? second = null) => SearchQuery.Empty with
        { Players = [new(0, ModelKeySetFilter.Empty) { Conditions = first }, new(1, ModelKeySetFilter.Empty) { Conditions = second ?? SearchQuery.Empty }] };
        CompiledSearch Compile(SearchQuery q) => SearchCompiler.Compile(q, context);
        JointSelectivityResult Estimate(SearchQuery q) => JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(Compile(q)));
        var n = SearchQuery.Empty with { LegacyNeow = SearchQuery.Empty.LegacyNeow with
        { NeowRelics = new([], [BaseGameModelKeys.Relics.LeafyPoultice], []) } };
        var single = Estimate(Table(n)); var both = Estimate(Table(n, n));
        Check(ModernNeowIdentityPredictor.TryGetEligibleCursePool(party.Players[0], out var curse0) &&
            ModernNeowIdentityPredictor.TryGetEligibleCursePool(party.Players[1], out _), "captured personal curse pools");
        ModernNeowIdentityPredictor.TryGetEligibleCursePool(party.Players[1], out var curse1);
        Equal(single.Probability, 1d / curse0.Count, "personal initial curse marginal uses runtime eligible pool");
        Equal(both.Probability, 1d / (curse0.Count * curse1.Count), "two personal N marginals, no pickup-order multiplier");
        Check(both.Confidence == SearchSelectivityConfidence.Medium && both.Assumptions.Contains("SharedFactsPaidOnce=true"), "approximation provenance");
        var offered = Table(SearchQuery.Empty) with { Players = [new(0, new([], [BaseGameModelKeys.Relics.LeafyPoultice], [])), new(1, ModelKeySetFilter.Empty)] };
        Equal(Estimate(offered).Probability, single.Probability, "envelope offers are not omitted");

        var offerKeys = new HashSet<ModelKey>();
        Check(ModernNeowIdentityPredictor.TryVisitOfferSpace(party.Players[0], (first, second, third, _) =>
            { offerKeys.Add(first); offerKeys.Add(second); offerKeys.Add(third); }), "joint offer authority");
        var offerSamples = offerKeys.Take(4).ToArray();
        ModelKeySetFilter offerA = new([offerSamples[0], offerSamples[1]], [], []),
            offerB = new([offerSamples[1], offerSamples[2]], [], []), offerC = new([offerSamples[2], offerSamples[3]], [], []);
        var offerClauses = SearchQuery.Empty with { LegacyNeow = SearchQuery.Empty.LegacyNeow with { NeowRelics = offerB },
            LegacyWorld = SearchQuery.Empty.LegacyWorld with { AncientOptionFilters = [new(1, offerC)] } };
        ModernNeowIdentityPredictor.TryEstimateOfferProbability(party.Players[0], (first, second, third) =>
            new[] { offerA, offerB, offerC }.All(f => PartyInitialQuery.Matches(f, [first, second, third])), out double offerJoint, out _);
        Equal(Estimate(Table(offerClauses) with { Players = [new(0, offerA) { Conditions = offerClauses }, new(1, ModelKeySetFilter.Empty)] }).Probability,
            offerJoint, "separate initial-offer Any clauses require the same three offers");
        Equal(Estimate(Table(SearchQuery.Empty with { LegacyWorld = offerClauses.LegacyWorld })).Probability,
            Estimate(Table(SearchQuery.Empty) with { Players = [new(0, offerC), new(1, ModelKeySetFilter.Empty)] }).Probability,
            "Act 1 options share initial Neow offer ownership");
        var neowIdentity = new ModelKey(BaseGameModelKeys.Categories.Event, "NEOW");
        var act1Identity = SearchQuery.Empty with { LegacyWorld = SearchQuery.Empty.LegacyWorld with
            { AncientIdentityFilters = [new(1, new([], [neowIdentity], []))] } };
        Equal(Estimate(Table(act1Identity)).Probability, 1, "Act 1 shared Ancient identity is Neow");
        Equal(Estimate(Table(act1Identity with { LegacyWorld = act1Identity.LegacyWorld with
            { AncientIdentityFilters = [new(1, new([], [], [neowIdentity]))] } })).Probability, 0, "Act 1 Neow identity Ban is impossible");
        Equal(Estimate(Table(SearchQuery.Empty with { LegacyWorld = SearchQuery.Empty.LegacyWorld with
            { AncientSeaGlassTargetFilters = [new(1, new([], [BaseGameModelKeys.Characters.Silent], []))] } })).Probability, 1,
            "Act 1 Sea Glass target refinement is vacuous");

        var act = party.World.Beta109Generation!.OrderedActCatalog.First(a => a.Act == 1);
        var w = SearchQuery.Empty with { LegacyWorld = SearchQuery.Empty.LegacyWorld with
        { BossFilters = [new(1, new([], [act.Bosses[0]], []))] } };
        var once = Estimate(Table(w));
        Equal(Estimate(Table(w, w)).Probability, once.Probability, "shared Boss duplicate counted once");
        Equal(Estimate(Table(n, n) with { LegacyWorld = w.LegacyWorld }).Probability,
            once.Probability * both.Probability, "shared W times personal N product");
        var different = w with { LegacyWorld = w.LegacyWorld with { BossFilters = [new(1, new([], [act.Bosses[1]], []))] } };
        Check(Estimate(Table(w, different)).ExactlyImpossible, "conflicting shared facts remain impossible");
        var generation = party.World.Beta109Generation!;
        var fixedGeneration = generation with
        {
            PartyBossDiscoveryOverrides = new Dictionary<string, ModelKey> { [act.ActKey.Serialized] = act.Bosses[0] },
            ActSelectionGroups = generation.ActSelectionGroups.Select(g => g.Act != 1 ? g : g with
            { EligibleActsInSourceOrder = [act.ActKey], SelectionMode = Beta109ActSelectionMode.DeterministicFirst }).ToArray()
        };
        var fixedContext = context with { Party = null, Authority = context.Authority.WithWorldAuthority(party.World with { Beta109Generation = fixedGeneration }) };
        Equal(JointSelectivityEstimator.EstimateQuery(SearchSelectivityInput.From(SearchCompiler.CompilePlayer(w, fixedContext))).Probability,
            1, "discovery-fixed first Boss is not uniform over its original pool");

        var fakePool = Beta111EventResultCatalog.FakeMerchantRelics.ToArray();
        var fake = SearchQuery.Empty with { EventResultConditions = [new(EventResultConditionKind.FakeMerchantOfferedFakeRelic, fakePool[0])] };
        Equal(Estimate(Table(fake, fake)).Probability, Estimate(Table(fake)).Probability, "shared Fake Merchant fact counted once");
        Equal(Estimate(Table(fake, fake with { EventResultConditions = [new(EventResultConditionKind.FakeMerchantOfferedFakeRelic, fakePool[1])] })).Probability,
            6d * 5 / (fakePool.Length * (fakePool.Length - 1)), "shared Fake Merchant two-target without-replacement conjunction");

        bool checkedOptions = false;
        foreach (var a in party.Players[0].WorldAuthority!.Beta109Generation!.AncientEventContexts.Where(a => a.Act > 1 && a.Catalog is not null))
        {
            foreach (var key in a.Catalog!.Pools.SelectMany(p => p.OrderedOptions).Distinct())
            {
                var branch = new AncientSearchBranchCondition(a.Act, a.AncientKey, [key], []);
                var aq = SearchQuery.Empty with { AncientBranches = [branch] };
                var compiled = Compile(Table(aq, aq));
                var p0 = AncientOptionProbabilityEstimator.EstimateConditional(SearchSelectivityInput.From(compiled.PlayerSearches[0]), branch).Probability;
                var p1 = AncientOptionProbabilityEstimator.EstimateConditional(SearchSelectivityInput.From(compiled.PlayerSearches[1]), branch).Probability;
                if (p0 is not (> 0 and < 1) || p1 is not (> 0 and <= 1)) continue;
                var identity = aq with { AncientBranches = [branch with { OptionAny = [] }] };
                var identityMass = Estimate(Table(identity)).Probability;
                if (identityMass is not > 0) continue;
                Equal(Estimate(Table(aq, aq)).Probability, identityMass * p0 * p1, "one shared Ancient identity, two conditional option marginals");
                checkedOptions = true; break;
            }
            if (checkedOptions) break;
        }
        Check(checkedOptions, "runtime Ancient conditional fixture found");
        var c = SearchQuery.Empty with { CombatPotionRewards = new(1, CombatRewardSequenceOrderMode.Ordered,
            [new(CombatPotionSlotRequirement.NoDrop, null)]) };
        Equal(Estimate(Table(n, c)).Probability, single.Probability * .6, "N and modeled personal C compose without a multiplayer gate");
        Equal(Estimate(Table(c, c)).Probability, .36, "two personal C probabilities compose");
        if (OS.GetCmdlineUserArgs().Contains("--party-c-probability"))
        {
            SearchSelectivityEstimate C(SearchQuery query)
            {
                var result = CombatRewardProbabilityEstimator.Estimate(SearchSelectivityInput.From(Compile(Table(query)).PlayerSearches[0]));
                if (result.Probability is null) GD.Print("PARTY_C_UNPRICED " + result.EvidenceCode);
                return result;
            }
            SearchQuery Potion(int count, CombatRewardSequenceOrderMode mode, params CombatPotionRewardSlotSearchCondition[] slots) =>
                SearchQuery.Empty with { CombatPotionRewards = new(count, mode, slots) };
            var drop = new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.DropAny, null);
            var noDrop = new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.NoDrop, null);
            Equal(C(Potion(1, CombatRewardSequenceOrderMode.Ordered, drop)).Probability, .4, "first potion drop");
            Equal(C(Potion(3, CombatRewardSequenceOrderMode.Ordered, noDrop, noDrop, noDrop)).Probability, .6 * .5 * .4, "potion pity across battles");
            Check(C(Potion(6, CombatRewardSequenceOrderMode.Ordered, Enumerable.Repeat(noDrop, 6).ToArray())).Probability is > 0, "six potion battles are modeled");
            var effects = party.Players[0].EffectAuthority!;
            var mpPotion = effects.PotionPool!.FirstOrDefault(p => p.IsMultiplayerOnly) ?? effects.PotionPool!.First();
            var specific = new CombatPotionRewardSlotSearchCondition(CombatPotionSlotRequirement.DropSpecific, mpPotion.PotionKey);
            double rarityMass = mpPotion.Rarity switch { EffectPotionRarity.Common => .65, EffectPotionRarity.Uncommon => .25, _ => .10 };
            Equal(C(Potion(1, CombatRewardSequenceOrderMode.Ordered, specific)).Probability,
                .4 * rarityMass / effects.PotionPool!.Count(p => p.Rarity == mpPotion.Rarity), "captured multiplayer potion and full rarity denominator");
            var mpCard = effects.CharacterRewardPool!.FirstOrDefault(k => k.IsMultiplayerOnly && k.Rarity == EffectCardRarity.Common && k.EligibleForPostCombatRewardByPoolMembership)
                ?? effects.CharacterRewardPool!.First(k => k.Rarity == EffectCardRarity.Common && k.EligibleForPostCombatRewardByPoolMembership);
            var cardQuery = SearchQuery.Empty with { CombatCardRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [mpCard.CardKey]) };
            Check(C(cardQuery).Probability is > 0, "MP-only combat card included");
            var marked = effects with { PotionPool = effects.PotionPool!.Select(p => p with { IsMultiplayerOnly = p.PotionKey == mpPotion.PotionKey }).ToArray() };
            var childContext = Compile(Table(cardQuery)).PlayerSearches[0].Context;
            var markedInput = SearchSelectivityInput.From(SearchCompiler.CompilePlayer(Potion(1, CombatRewardSequenceOrderMode.Ordered, specific),
                childContext with { Authority = childContext.Authority.WithEffectAuthority(marked) }));
            Equal(CombatRewardProbabilityEstimator.Estimate(markedInput).Probability, C(Potion(1, CombatRewardSequenceOrderMode.Ordered, specific)).Probability,
                "multiplayer-only marker never removes a captured eligible potion");
            Equal(C(cardQuery with { CombatPotionRewards = new(1, CombatRewardSequenceOrderMode.Ordered, [drop]) }).Probability,
                C(cardQuery).Probability * .4, "card and potion query conjunction");
            var small = Potion(1, CombatRewardSequenceOrderMode.Ordered, drop) with { OpeningRoute = new(BaseGameModelKeys.Relics.SmallCapsule) };
            foreach (var capsule in new[] { BaseGameModelKeys.Relics.SmallCapsule, BaseGameModelKeys.Relics.LargeCapsule })
            {
                var heldCards = C(cardQuery with { OpeningRoute = new(capsule) });
                Equal(heldCards.Probability, C(cardQuery).Probability, "Unspecified captured Capsule outputs use the same declared neutral C profile");
            }
            var white = new ModelKey("RELIC", "WHITE_BEAST_STATUE");
            var pinnedWhite = small with { StructuredOpeningEffects = [new(BaseGameModelKeys.Relics.SmallCapsule,
                NeowStructuredConditionKind.ExactSingle, NeowStructuredEffectScope.NestedRelics, NeowStructuredOutputKind.Relic, [white])] };
            Equal(C(pinnedWhite).Probability, 1, "authored White Beast forces potion drop");
            Equal(C(pinnedWhite with { CombatPotionRewards = c.CombatPotionRewards }).Probability, 0, "forced potion contradicts no-drop without blocking authoring");
            var smallEstimate = C(small);
            Equal(smallEstimate.Probability, .4, "Unlisted White Beast does not enter the approved product C profile");
            var large = small with { OpeningRoute = new(BaseGameModelKeys.Relics.LargeCapsule) };
            Equal(C(large).Probability, .4, "Unspecified Large Capsule uses the same product neutral approximation");
            var withQueue = small with { RelicSequenceConstraints = [new(RelicSequenceKind.Rare, SearchSequenceRangeMode.ExactSlot, 1, new([white], [], []))] };
            Equal(C(withQueue).Probability, smallEstimate.Probability, "Initial R queue cannot infer an unlisted held C effect");
            var parent = small with { CombatPotionRewards = null };
            Equal(Estimate(Table(small)).Probability, Estimate(Table(parent)).Probability * smallEstimate.Probability, "whole query pays Capsule identity once");
            var bones = small with { OpeningRoute = new(BaseGameModelKeys.Relics.NeowsBones) };
            Equal(C(bones).Probability, .4, "Unspecified Bones companions use the same approved product neutral approximation");
            var noRngChange = small with { OpeningRoute = new(BaseGameModelKeys.Relics.Kaleidoscope) };
            Equal(C(noRngChange).Probability, .4, "RNG-only advancement uses default independent marginal");
            var catalog = CombatRewardSearchUiCatalog.FromAuthority(RuntimeProfileId.Beta111, marked, true);
            Check(catalog.PotionCandidates.Contains(mpPotion.PotionKey), "multiplayer potion picker membership");
            var runtime = ModRuntimeSnapshot.NotInitialized with { Profile = Beta111Profile.Instance, Detection = detection,
                Compatibility = RuntimeProfileRegistry.Resolve(detection) };
            var picker = new ObjectPickerPanel();
            var opening = new NeowSearchEditor(runtime, picker);
            var editor = new CombatRewardSearchEditor(runtime, picker,
                (players, seat) => players > 1 ? opening.ExportPartyConditions(seat) : opening.ExportQuery());
            var text = JsonUiTextProvider.CreateUi13("zh");
            var uiQuery = cardQuery with { CombatPotionRewards = new(6, CombatRewardSequenceOrderMode.Unordered,
                [specific, noDrop, drop, new(CombatPotionSlotRequirement.Neutral, null), noDrop, drop]) };
            editor.Refresh("zh", text, party.Players[0].Character.CharacterKey, 10, 2, 0, UnlockState.all.ToSerializable(), false);
            editor.ImportQuery(uiQuery);
            Check(System.Text.Json.JsonSerializer.Serialize(editor.ExportPartyPotionCondition(0)) ==
                System.Text.Json.JsonSerializer.Serialize(uiQuery.CombatPotionRewards), "potion import/export preserves six independent slots and order");
            editor.Refresh("zh", text, party.Players[1].Character.CharacterKey, 10, 2, 1, UnlockState.all.ToSerializable(), false);
            editor.ImportQuery(c);
            Check(editor.ExportPartyPotionCondition(0)!.OrderMode == CombatRewardSequenceOrderMode.Unordered &&
                editor.ExportPartyPotionCondition(1)!.Slots[0].Requirement == CombatPotionSlotRequirement.NoDrop, "potion drafts stay personal");
            editor.Free(); opening.Free(); picker.Free();
            GD.Print($"PARTY_C_PROBABILITY_PASS checks={checks};noQueryGate=true;potionsUiRoundtrip=true");
        }
        var request = ExactSearchExecutionRequestFactory.Compile(Compile(Table(n, n)), new("000000000040", 1024, 1, 1)).Plan!;
        var eta = FamilySearchEtaProjectorV1.Project(request, FamilyExecutionCoordinator.Plan(request, false));
        Equal(eta.AcceptedResultProbability, both.Probability, "ETA reads the same probability authority");
        Check(eta.TargetFamilyPipelineMs is null && eta.MissingEvidence.Any(reason => reason.Contains("N.Neow", StringComparison.Ordinal)),
            "A priced party probability does not invent the missing actual N unit cost");
        GD.Print($"PARTY_PROBABILITY_PASS checks={checks};sharedOnce=true;personalProductApproximation=true;noSamplingOrThroughputClaim=true");
    }

    private static async Task CaptureCombatPotionUi(Node host, string output, GameVersionDetection detection)
    {
        var runtime = ModRuntimeSnapshot.NotInitialized with { Profile = Beta111Profile.Instance, Detection = detection,
            Compatibility = RuntimeProfileRegistry.Resolve(detection) };
        var picker = new ObjectPickerPanel();
        var opening = new NeowSearchEditor(runtime, picker);
        var editor = new CombatRewardSearchEditor(runtime, picker,
            (players, seat) => players > 1 ? opening.ExportPartyConditions(seat) : opening.ExportQuery()) { Position = new(28, 24), Size = new(1120, 946) };
        var background = new ColorRect { Color = new Color("121e2b"), Size = new(1200, 1000) };
        host.GetWindow().Size = new(1200, 1000);
        host.AddChild(background); host.AddChild(picker); host.AddChild(editor);
        var catalog = CombatRewardSearchUiCatalog.CaptureForPicker(runtime, BaseGameModelKeys.Characters.Ironclad, 10, 2, 0, UnlockState.all);
        var query = SearchQuery.Empty with { CombatCardRewards = new(3, CombatRewardSequenceOrderMode.Ordered,
            [catalog.CardCandidates[0], null, null]), CombatPotionRewards = new(3, CombatRewardSequenceOrderMode.Ordered,
            [new(CombatPotionSlotRequirement.DropSpecific, catalog.PotionCandidates[0]),
                new(CombatPotionSlotRequirement.NoDrop, null), new(CombatPotionSlotRequirement.DropAny, null)]) };
        foreach (string language in new[] { "zh", "en" })
        {
            editor.Refresh(language, JsonUiTextProvider.CreateUi13(language), BaseGameModelKeys.Characters.Ironclad, 10, 2, 0, UnlockState.all.ToSerializable(), false);
            editor.ImportQuery(query);
            editor.Refresh(language, JsonUiTextProvider.CreateUi13(language), BaseGameModelKeys.Characters.Ironclad, 10, 2, 0, UnlockState.all.ToSerializable());
            for (int slot = 0; slot < 3; slot++)
                if (((OptionButton)editor.FindChild("CombatPotionRequirement" + slot, true, false)).GetSelectedId() != (int)query.CombatPotionRewards!.Slots[slot].Requirement)
                    throw new Exception("Potion dropdown must preserve semantic ids independently of display order");
            await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = host.GetViewport().GetTexture().GetImage();
            image.SavePng(System.IO.Path.Combine(output, "combat-potions-" + language + ".png"));
        }
        editor.Free(); opening.Free(); picker.Free(); background.Free();
    }
}
