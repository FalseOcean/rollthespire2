using RolltheSpire2.Ui.Pages.Search.Editors;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.TestSupport;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Pages.Search.Neow;
using RolltheSpire2.Ui.Pages.Search.Shop;
using RolltheSpire2.Ui.Shell;
using System.Reflection;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Tests.FastSearch;

internal static class ObjectPickerContextChecks
{
    public static int Run()
    {
        if (Environment.GetEnvironmentVariable("RT2_AUTHORED_GODOT_RUNNER") != "1")
            throw new InvalidOperationException("Use the existing AuthoredEventChecks native headless host");
        TestMode.IsOn = true;
        RuntimeSnapshotThreadGuard.BindCurrentThread();
        typeof(MegaCrit.Sts2.Core.Modding.ModManager).GetProperty("State")!.SetValue(null, MegaCrit.Sts2.Core.Modding.ModManagerState.Initialized);
        ModelDb.Init(); MegaCrit.Sts2.Core.Modding.AssemblyInfo.Init();
        MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache.Init(); ModelDb.InitIds();
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.InitSettingsDataForTest();
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.SettingsSave.Language = "eng";
        MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
        CheckAllocationMatching(Check);
        var runtime = ModRuntimeSnapshot.NotInitialized with { Profile = Beta111Profile.Instance };
        CheckShopCatalogAvailability(runtime, Check);
        ModelKey Key(string entry) => new("RELIC", entry);
        foreach (var character in new[] { BaseGameModelKeys.Characters.Ironclad, BaseGameModelKeys.Characters.Silent })
        {
            var solo = NeowSearchUiCatalog.CaptureForPicker(runtime, character, 10, 1, 0, UnlockState.all);
            var multi = NeowSearchUiCatalog.CaptureForPicker(runtime, character, 10, 2, 1, UnlockState.all);
            var locked = NeowSearchUiCatalog.CaptureForPicker(runtime, character, 10, 2, 1, UnlockState.none);
            Check(solo.RouteRelics.Contains(Key("WINGED_BOOTS")) && !multi.RouteRelics.Contains(Key("WINGED_BOOTS")), "Winged Boots solo only");
            Check(solo.RouteRelics.Contains(Key("SILVER_CRUCIBLE")) && !multi.RouteRelics.Contains(Key("SILVER_CRUCIBLE")), "Silver Crucible solo only");
            Check(!solo.RouteRelics.Contains(Key("MASSIVE_SCROLL")) && multi.RouteRelics.Contains(Key("MASSIVE_SCROLL")), "Massive Scroll multiplayer only");
            Check(solo.MultiplayerOnlyCards.Count == 0, "Solo candidate capture removes multiplayer cards");
            Check(multi.MassiveScrollCards.Count > 0 && multi.MassiveScrollCards.All(k => multi.MultiplayerOnlyCards.Contains(k)
                && (multi.CharacterCards.Contains(k) || multi.ColorlessCards.Contains(k))), "Massive Scroll precise pool");
            Check(multi.OtherCharacterCards.All(k => !multi.CardPickerMetadata[k].CharacterKeys.Contains(character)), "Kaleidoscope excludes current character");
            Check(solo.RareCharacterCards.Count > 0 && solo.RareCharacterCards.All(k => solo.CardPickerMetadata[k].Rarity == EffectCardRarity.Rare
                && solo.CardPickerMetadata[k].CharacterKeys.Contains(character)), "Arcane Scroll rare own-character pool");
            Check(multi.ColorlessCards.All(k => multi.CardPickerMetadata[k].CharacterKeys.Count == 0), "Paperweight colorless provenance");
            Check(multi.ColorlessCards.All(k => multi.CardPickerMetadata[k].IsColorlessPoolMember), "Colorless provenance is explicit");
            Check(multi.CharacterCards.All(k => !multi.CardPickerMetadata[k].IsColorlessPoolMember), "Character cards are not inferred colorless");
            Check(locked.CharacterCards.Count < multi.CharacterCards.Count, "Seat unlocks change pool");
            Check(!locked.RouteRelics.Contains(Key("KALEIDOSCOPE")), "Locked seat cannot select Kaleidoscope");
            var modeOnly = NeowSearchUiCatalog.CaptureModeAllowedIdentities(character, 10, 2);
            Check(!modeOnly.Contains(Key("WINGED_BOOTS")) && !modeOnly.Contains(Key("SILVER_CRUCIBLE")) && modeOnly.Contains(Key("MASSIVE_SCROLL")),
                "Known mode restrictions remain available before remote unlock read");
            var context = solo.CreateCardPickerContext(solo.RareCharacterCards, true, "test-arcane");
            var filters = new CardPickerFilterState();
            foreach (var rarity in new[] { EffectCardRarity.Common, EffectCardRarity.Uncommon, EffectCardRarity.Rare })
            {
                filters.ToggleRarity(rarity);
                Check(multi.CharacterCards.Concat(multi.OtherCharacterCards).Where(k => filters.Matches(context, k))
                    .All(solo.RareCharacterCards.Contains), "Facet cannot widen candidates");
                filters.ToggleRarity(rarity);
            }
            // A catalog refresh limits new choices without discarding saved authoring state.
            var picker = new ObjectPickerPanel();
            var editor = new NeowSearchEditor(runtime, picker);
            editor.Refresh("en", character, 10, 1, 0, UnlockState.all.ToSerializable(), render: false);
            const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
            var editorType = typeof(NeowSearchEditor);
            editorType.GetProperty("_catalog", members)!.SetValue(editor, solo);
            editorType.GetProperty("_source", members)!.SetValue(editor, Key("MASSIVE_SCROLL"));
            var bones = (List<ModelKey>)editorType.GetProperty("_bones", members)!.GetValue(editor)!;
            bones.Add(Key("MASSIVE_SCROLL")); bones.Add(Key("LEAFY_POULTICE"));
            var slots = (Dictionary<string, ModelKey?>)editorType.GetProperty("_slots", members)!.GetValue(editor)!;
            slots[$"{Key("MASSIVE_SCROLL")}/multiplayer/0"] = multi.MassiveScrollCards[0];
            slots[$"{Key("LEAFY_POULTICE")}/transforms/0"] = solo.TransformCards[0];
            editor.Refresh("en", character, 9, 1, 0, UnlockState.all.ToSerializable(), render: false);
            Check(editor.ExportEditorState().Source == Key("MASSIVE_SCROLL"), "Solo catalog refresh retains the authored multiplayer-only source");
            Check(bones.SequenceEqual(new[] { Key("MASSIVE_SCROLL"), Key("LEAFY_POULTICE") }), "Catalog refresh retains unavailable and available Bones source drafts");
            Check(slots.Count == 2 && slots[$"{Key("MASSIVE_SCROLL")}/multiplayer/0"] == multi.MassiveScrollCards[0] &&
                slots[$"{Key("LEAFY_POULTICE")}/transforms/0"] == solo.TransformCards[0], "Catalog refresh preserves both nested targets exactly");
            Check((int)editorType.GetField("_removed", members)!.GetValue(editor)! == 0, "Catalog refresh reports no discarded conditions");
            editorType.GetProperty("_catalog", members)!.SetValue(editor, solo);
            IReadOnlyList<ModelKey> Candidates(ModelKey source, string component, int index, IReadOnlyList<ModelKey> pool) =>
                (IReadOnlyList<ModelKey>)editorType.GetMethod("SlotCandidates", members)!.Invoke(editor, new object[] { source, component, index, pool })!;
            foreach (var (source, component, pool, repeats) in new[]
            {
                (Key("LARGE_CAPSULE"), "nested-relics", solo.OrdinaryRelics, false),
                (Key("PHIAL_HOLSTER"), "potions", solo.Potions, false),
                (Key("SCROLL_BOXES"), "bundle", solo.CommonCharacterCards, false),
                (Key("LEAFY_POULTICE"), "transforms", solo.TransformCards, true),
                (Key("KALEIDOSCOPE"), "independent-offers", multi.OtherCharacterCards, true)
            })
            {
                slots.Clear(); slots[$"{source}/{component}/0"] = pool[0];
                Check(Candidates(source, component, 1, pool).Contains(pool[0]) == repeats, $"{source}: source-local duplicate policy");
                Check(Candidates(source, component, 0, pool).Contains(pool[0]), $"{source}: own selection remains editable");
                slots.Clear();
                Check(Candidates(source, component, 1, pool).SequenceEqual(pool), $"{source}: deleting sibling restores pool");
            }
            var claw = new ModelKey("CARD", "CLAW");
            slots[$"{Key("SCROLL_BOXES")}/special/0"] = claw;
            Check(Candidates(Key("SCROLL_BOXES"), "special", 1, new[] { claw }).Contains(claw), "Normal bundle exclusion never applies to TripleClaw special branch");
            slots.Clear();
            var draft = editorType.GetField("_draft", members)!.GetValue(editor)!;
            var orderField = draft.GetType().GetField("KaleidoscopeOrder")!;
            var scrollField = draft.GetType().GetField("ScrollOffer")!;
            string kaleidoPrefix = $"{Key("KALEIDOSCOPE")}/independent-offers/";
            slots[kaleidoPrefix + "1"] = solo.OtherCharacterCards[0];
            Check(editor.AdvancedSourceCondition(Key("KALEIDOSCOPE"))!.KaleidoscopeGroupOrder == KaleidoscopeGroupOrderMode.AnyOrder,
                "Kaleidoscope defaults to AnyOrder");
            orderField.SetValue(draft, KaleidoscopeGroupOrderMode.ExactOrder);
            var orderedCondition = editor.AdvancedSourceCondition(Key("KALEIDOSCOPE"))!;
            Check(orderedCondition.KaleidoscopePositionalSlots[0] is null && orderedCondition.KaleidoscopePositionalSlots[1] == solo.OtherCharacterCards[0],
                "Exact order preserves empty first group");
            editorType.GetMethod("SwapKaleidoscopeTargets", members)!.Invoke(editor, null);
            Check(editor.AdvancedSourceCondition(Key("KALEIDOSCOPE"))!.KaleidoscopePositionalSlots[0] == solo.OtherCharacterCards[0]
                && !slots.ContainsKey(kaleidoPrefix + "1"), "Swap preserves empty slot without compacting");
            slots[kaleidoPrefix + "1"] = solo.OtherCharacterCards[0];
            Check(editor.AdvancedSourceCondition(Key("KALEIDOSCOPE"))!.AllowDuplicateOutputs, "Ordered duplicate groups remain legal");
            scrollField.SetValue(draft, NeowSpecialOfferKind.ScrollBoxesTripleClaw);
            Check(editor.AdvancedSourceCondition(Key("SCROLL_BOXES"))!.SpecialOffer == NeowSpecialOfferKind.ScrollBoxesTripleClaw,
                "An authored TripleClaw requirement remains represented for a non-Defect catalog");
            var defect = NeowSearchUiCatalog.CaptureForPicker(runtime, BaseGameModelKeys.Characters.Defect, 10, 1, 0, UnlockState.all);
            editorType.GetProperty("_catalog", members)!.SetValue(editor, defect);
            Check(editor.AdvancedSourceCondition(Key("SCROLL_BOXES"))!.SpecialOffer == NeowSpecialOfferKind.ScrollBoxesTripleClaw,
                "TripleClaw is the existing SpecialOffer, not three ordinary slots");
            editor.Refresh("en", character, 10, 1, 0, UnlockState.all.ToSerializable(), render: false);
            Check((NeowSpecialOfferKind)scrollField.GetValue(draft)! == NeowSpecialOfferKind.ScrollBoxesTripleClaw,
                "Character catalog refresh preserves the authored special mode");
            editorType.GetProperty("_catalog", members)!.SetValue(editor, solo);
            slots.Clear(); bones.Clear();
            bones.Add(Key("LEAFY_POULTICE")); bones.Add(Key("NEW_LEAF"));
            editorType.GetProperty("_source", members)!.SetValue(editor, Key("NEOWS_BONES"));
            editorType.GetProperty("_ordered", members)!.SetValue(editor, true);
            slots[$"{Key("LEAFY_POULTICE")}/transforms/0"] = solo.TransformCards[0];
            slots[$"{Key("NEW_LEAF")}/transform/0"] = solo.NewLeafTransformCards[0];
            Check(editor.BuildBonesQuery().StructuredOpeningEffects.Count == 2, "Separate pair retains source-bound predicates");
            editorType.GetMethod("SetTransformAllocation", members)!.Invoke(editor, new object[] { true });
            Check(slots.Count == 2 && editor.BuildBonesQuery().TransformationAggregate?.TargetMultiset.Count == 2,
                "Entering joint carries targets and remembers source allocation");
            var jointTargets = (ModelKey?[])draft.GetType().GetField("TransformTargets")!.GetValue(draft)!;
            editorType.GetMethod("SetTransformAllocation", members)!.Invoke(editor, new object[] { false });
            Check(slots[$"{Key("LEAFY_POULTICE")}/transforms/0"] == solo.TransformCards[0] &&
                slots[$"{Key("NEW_LEAF")}/transform/0"] == solo.NewLeafTransformCards[0] && slots.Count == 2,
                "Unchanged joint round trip restores original source slots, including the gap");
            editorType.GetMethod("SetTransformAllocation", members)!.Invoke(editor, new object[] { true });
            Array.Clear(jointTargets);
            jointTargets[0] = solo.TransformCards[0]; jointTargets[1] = solo.TransformCards[0];
            var jointQuery = editor.BuildBonesQuery();
            Check(jointQuery.TransformationAggregate is { Opening: TransformationOpening.BonesLeafyNewLeaf, OpportunityCount: 3,
                PickupOrder: TransformationPickupOrder.LeafyThenNewLeaf } && jointQuery.TransformationAggregate.TargetMultiset.Count == 2,
                "Joint donor keeps repeated targets, empty third slot and pickup order");
            Check(jointQuery.StructuredOpeningEffects.Count == 0, "Joint mode does not retain individual result predicates");
            bones.Reverse();
            Check(editor.BuildBonesQuery().TransformationAggregate!.PickupOrder == TransformationPickupOrder.NewLeafThenLeafy, "Pickup swap remains orthogonal");
            editorType.GetMethod("SetTransformAllocation", members)!.Invoke(editor, new object[] { false });
            Check(jointTargets.Count(k => k.HasValue) == 2 && slots.Count == 2 &&
                slots[$"{Key("LEAFY_POULTICE")}/transforms/0"] == solo.TransformCards[0] &&
                slots[$"{Key("NEW_LEAF")}/transform/0"] == solo.NewLeafTransformCards[0]
                && bones.Count == 2 && (bool)editorType.GetProperty("_ordered", members)!.GetValue(editor)!,
                "Returning to separate restores its own source targets while retaining joint duplicates and pickup order");
            editorType.GetMethod("SetTransformAllocation", members)!.Invoke(editor, new object[] { true });
            Array.Clear(jointTargets);
            editorType.GetMethod("SetTransformAllocation", members)!.Invoke(editor, new object[] { false });
            Check(jointTargets.All(k => !k.HasValue) && slots.Count == 2 && editor.BuildBonesQuery().StructuredOpeningEffects.Count == 2,
                "Clearing joint targets preserves the independent separate requirements and the empty joint draft");
            slots.Clear();
            bones.Clear(); bones.Add(Key("SMALL_CAPSULE")); bones.Add(Key("LARGE_CAPSULE"));
            slots[$"{Key("SMALL_CAPSULE")}/nested-relic/0"] = solo.OrdinaryRelics[0];
            slots[$"{Key("LARGE_CAPSULE")}/nested-relics/1"] = solo.OrdinaryRelics[1];
            editorType.GetMethod("SetCapsuleAllocation", members)!.Invoke(editor, new object[] { true });
            Check(editor.BuildBonesQuery().StructuredOpeningEffects.Single().OutputKeys.Count == 2, "Capsule joint carries individual targets");
            editorType.GetMethod("SetCapsuleAllocation", members)!.Invoke(editor, new object[] { false });
            Check(slots[$"{Key("SMALL_CAPSULE")}/nested-relic/0"] == solo.OrdinaryRelics[0] &&
                slots[$"{Key("LARGE_CAPSULE")}/nested-relics/1"] == solo.OrdinaryRelics[1] && slots.Count == 2,
                "Capsule round trip restores original ownership and empty slot");
            slots.Clear();
            draft.GetType().GetField("JointCapsules")!.SetValue(draft, true);
            var capsuleTargets = (ModelKey?[])draft.GetType().GetField("CapsuleTargets")!.GetValue(draft)!;
            Array.Clear(capsuleTargets);
            capsuleTargets[0] = solo.OrdinaryRelics[0]; capsuleTargets[1] = solo.OrdinaryRelics[0];
            Check(capsuleTargets[0] == solo.OrdinaryRelics[0] && capsuleTargets[1] == solo.OrdinaryRelics[0],
                "Existing duplicate capsule requirements remain authored while candidate choices exclude new duplicates");
            var capsulePool = (IReadOnlyList<ModelKey>)editorType.GetMethod("CapsuleCandidates", members)!.Invoke(editor, new object[] { 1 })!;
            Check(!capsulePool.Contains(solo.OrdinaryRelics[0]), "Joint capsule excludes occupied target");
            slots[$"{Key("LARGE_CAPSULE")}/nested-relics/0"] = solo.OrdinaryRelics[0];
            Check(!Candidates(Key("SMALL_CAPSULE"), "nested-relic", 0, solo.OrdinaryRelics).Contains(solo.OrdinaryRelics[0]), "Separate capsules exclude sibling source target");
            Check(editor.BuildBonesQuery().StructuredOpeningEffects.Single().Kind == NeowStructuredConditionKind.ExactGroupedCapsuleMultiset
                && !editor.BuildBonesQuery().StructuredOpeningEffects.Single().AllowDuplicateOutputs, "Capsule grouped targets use distinct authoring policy");
            // Exercise actual C editor state and candidate callbacks with native captured pools.
            var neowUi = new NeowSearchEditor(runtime, picker);
            neowUi.Refresh("en", character, 7, 2, 0, UnlockState.all.ToSerializable(), render: false);
            var combatUi = new CombatRewardSearchEditor(runtime, picker,
                (players, seat) => players > 1 ? neowUi.ExportPartyConditions(seat) : neowUi.ExportQuery());
            var textUi = RolltheSpire2.Presentation.Localization.JsonUiTextProvider.CreateUi13("en");
            void RefreshC(int asc = 7, int seat = 0, ModelKey? who = null) => combatUi.Refresh("en", textUi, who ?? character, asc, 2, seat, UnlockState.all.ToSerializable(), render: false);
            RefreshC();
            var ct = typeof(CombatRewardSearchEditor);
            var cd = ct.GetField("_draft", members)!.GetValue(combatUi)!;
            var cs = (ModelKey?[])cd.GetType().GetProperty("Slots")!.GetValue(cd)!;
            bool Unspecified(string field) => (bool)cd.GetType().GetField(field)!.GetValue(cd)!;
            Check(Unspecified("Unordered") && Unspecified("PotionsUnordered"),
                "new card and potion reward drafts both default to no specified battle");
            combatUi.ImportQuery(RolltheSpire2.Search.Semantics.SearchQuery.Empty);
            Check(Unspecified("Unordered") && Unspecified("PotionsUnordered"),
                "importing an empty query retains unspecified reward defaults");
            var explicitRewards = RolltheSpire2.Search.Semantics.SearchQuery.Empty with {
                CombatCardRewards = new(3, RolltheSpire2.Search.Contracts.CombatRewardSequenceOrderMode.Ordered, [solo.CharacterCards[0], null, null]),
                CombatPotionRewards = new(3, RolltheSpire2.Search.Contracts.CombatRewardSequenceOrderMode.Ordered,
                    [new(RolltheSpire2.Search.Contracts.CombatPotionSlotRequirement.NoDrop, null),
                     new(RolltheSpire2.Search.Contracts.CombatPotionSlotRequirement.Neutral, null),
                     new(RolltheSpire2.Search.Contracts.CombatPotionSlotRequirement.Neutral, null)]) };
            combatUi.ImportQuery(explicitRewards);
            Check(!Unspecified("Unordered") && !Unspecified("PotionsUnordered") &&
                combatUi.ExportCondition()?.OrderMode == RolltheSpire2.Search.Contracts.CombatRewardSequenceOrderMode.Ordered &&
                combatUi.ExportPotionCondition()?.OrderMode == RolltheSpire2.Search.Contracts.CombatRewardSequenceOrderMode.Ordered,
                "explicit saved card and potion battle ordinals survive import/export");
            var restoreCombat = combatUi.ClearConditions(true, 0); RefreshC();
            var clearedCombat = ct.GetField("_draft", members)!.GetValue(combatUi)!;
            Check((bool)clearedCombat.GetType().GetField("Unordered")!.GetValue(clearedCombat)! &&
                (bool)clearedCombat.GetType().GetField("PotionsUnordered")!.GetValue(clearedCombat)!,
                "clearing a seat creates unspecified defaults for both reward blocks");
            restoreCombat(); RefreshC();
            Check(ReferenceEquals(ct.GetField("_draft", members)!.GetValue(combatUi), cd) &&
                !Unspecified("Unordered") && !Unspecified("PotionsUnordered"),
                "undoing clear restores the player's explicit battle matching");
            Array.Clear(cs); // Following feasibility cases deliberately use ordered mode.
            RefreshC();
            var cc = (RolltheSpire2.Ui.Pages.Search.CombatReward.CombatRewardSearchUiCatalog)ct.GetProperty("_catalog", members)!.GetValue(combatUi)!;
            var rp = cc.CardCandidates.First(k => cc.CardPickerMetadata[k].Rarity == EffectCardRarity.Rare && cc.CardPickerMetadata[k].CardType == EffectCardType.Power);
            var ra = cc.CardCandidates.First(k => cc.CardPickerMetadata[k].Rarity == EffectCardRarity.Rare && cc.CardPickerMetadata[k].CardType == EffectCardType.Attack);
            Check(cc.CardPickerMetadata.Values.All(m => m.CharacterKeys.Contains(character)), "C cards retain captured character provenance");
            Check(!combatUi.ResidualCandidates(1).Contains(rp), "C baseline B2 excludes real Rare Power");
            Check(ReferenceEquals(combatUi.ResidualCandidates(1), combatUi.ResidualCandidates(1)), "C candidate support is cached for unchanged context");
            RefreshC(6);
            Check(combatUi.ResidualCandidates(1).Contains(ra), "C A6 B2 Rare becomes feasible");
            RefreshC();
            cc = (RolltheSpire2.Ui.Pages.Search.CombatReward.CombatRewardSearchUiCatalog)ct.GetProperty("_catalog", members)!.GetValue(combatUi)!;
            Check(!combatUi.ResidualCandidates(1).Contains(ra), "C ascension change invalidates support cache");
            var ns = (Dictionary<string, ModelKey?>)editorType.GetProperty("_slots", members)!.GetValue(neowUi)!;
            editorType.GetProperty("_source", members)!.SetValue(neowUi, Key("SMALL_CAPSULE"));
            ns[$"{Key("SMALL_CAPSULE")}/nested-relic/0"] = Key("LASTING_CANDY"); RefreshC();
            Check(combatUi.ResidualCandidates(1).Contains(rp) && !combatUi.ResidualCandidates(1).Contains(ra), "Explicit Candy admits only B2 Rare Power");
            cs[1] = rp;
            ns.Clear(); RefreshC();
            Check(cs[1] == rp && !combatUi.HasImpossibleCardSelection && combatUi.CardFeasibilityNotice.Contains("unverified"),
                "Unspecified Capsule contents retain target with unknown feasibility");
            editorType.GetProperty("_source", members)!.SetValue(neowUi, Key("LEAFY_POULTICE")); RefreshC();
            Check(cs[1] == rp && combatUi.HasImpossibleCardSelection && combatUi.CardFeasibilityNotice.Contains("kept"),
                "Known neutral opening retains impossible target with an explicit conflict notice");
            Array.Clear(cs);
            editorType.GetProperty("_source", members)!.SetValue(neowUi, Key("SMALL_CAPSULE"));
            ns[$"{Key("SMALL_CAPSULE")}/nested-relic/0"] = Key("PRAYER_WHEEL"); RefreshC();
            Check(combatUi.ResidualCandidates(1).Contains(ra), "Explicit Wheel admits B2 Rare Attack");
            editorType.GetProperty("_source", members)!.SetValue(neowUi, Key("LEAFY_POULTICE")); RefreshC();
            Check(!combatUi.ResidualCandidates(1).Contains(ra), "Hidden Capsule history is not a commitment");
            cd.GetType().GetField("BattleRange")!.SetValue(cd, 4); // Explicit prototype horizon; production default is now three.
            cs[2] = ra; cs[3] = ra; RefreshC();
            Check(cs[2] == ra && cs[3] == ra && combatUi.HasImpossibleCardSelection, "Ordered conflict retains both authored targets");
            Check(!combatUi.ResidualCandidates(3).Contains(ra), "C residual candidate respects prior Rare pity reset");
            ct.GetProperty("_catalog", members)!.SetValue(combatUi, cc with { CardSupportProofAvailable = false }); RefreshC();
            Check(combatUi.ResidualCandidates(3).Contains(ra) && combatUi.HasImpossibleCardSelection &&
                combatUi.CardFeasibilityNotice.Contains("current model"), "Unknown Mod hooks retain candidates and show model-only impossibility");
            ct.GetProperty("_catalog", members)!.SetValue(combatUi, cc); RefreshC();
            Array.Clear(cs); cd.GetType().GetField("Unordered")!.SetValue(cd, true); cs[0] = ra; cs[1] = rp; RefreshC();
            Check(cs[0] == ra && cs[1] == rp && combatUi.HasImpossibleCardSelection, "Unordered conflict preserves targets and distinct-battle semantics");
            RefreshC(7, 1, character == BaseGameModelKeys.Characters.Ironclad ? BaseGameModelKeys.Characters.Silent : BaseGameModelKeys.Characters.Ironclad);
            var otherCatalog = ct.GetProperty("_catalog", members)!.GetValue(combatUi);
            RefreshC();
            Check(ReferenceEquals(ct.GetProperty("_catalog", members)!.GetValue(combatUi), cc) && !ReferenceEquals(cc, otherCatalog), "Seat catalog restored independently");
            Check((int)cd.GetType().GetField("Removed")!.GetValue(cd)! == 0, "Feasibility checks never discard authored conditions");
            combatUi.Free(); neowUi.Free();
            editor.Free();
            picker.Free();
            Console.WriteLine($"PICKER_POOLS {character}: soloRare={solo.RareCharacterCards.Count}; multiOther={multi.OtherCharacterCards.Count}; massive={multi.MassiveScrollCards.Count}; lockedCards={locked.CharacterCards.Count}; unlockedCards={multi.CharacterCards.Count}; relics={multi.OrdinaryRelics.Count}; potions={multi.Potions.Count}");
        }
        Console.WriteLine($"OBJECT_PICKER_CONTEXT_PASS checks={checks}");
        return 0;
    }

    private static void CheckAllocationMatching(Action<bool, string> check)
    {
        ModelKey a = new("CARD", "A"), b = new("CARD", "B"), c = new("CARD", "C");
        IReadOnlyList<ModelKey>[] pools = [[a, b, c], [a, b, c], [a, b, c]];
        check(NeowTargetAllocation.Reconcile([a, null, c], [a, c], pools)!.SequenceEqual(new ModelKey?[] { a, null, c }),
            "Allocation preserves previous ownership and gaps");
        check(NeowTargetAllocation.Reconcile([a, null, c], [a, b, c], pools)!.SequenceEqual(new ModelKey?[] { a, b, c }),
            "Allocation fills an empty slot with the new target");
        check(NeowTargetAllocation.Reconcile([a, b, c], [a, c], pools)!.SequenceEqual(new ModelKey?[] { a, null, c }),
            "Allocation removes cleared targets rather than reviving old history");
        check(NeowTargetAllocation.Reconcile([a, b, c], [a, a], pools)!.Count(k => k == a) == 2,
            "Allocation preserves duplicate transform occurrences");
        check(NeowTargetAllocation.Reconcile([a, b, c], [], pools)!.All(k => k is null), "Clearing all targets propagates across modes");
        pools[2] = [a];
        var constrained = NeowTargetAllocation.Reconcile([a, b, null], [a, b, c], pools)!;
        check(constrained[2] == a && constrained[1] == b && constrained[0] == c,
            "Reallocate a flexible target when its old position would block a valid split");
        check(NeowTargetAllocation.Reconcile([a, null, null], [b, b, b], pools) is null,
            "An unrepresentable split is explicit, never silently drops a target");
    }

    private static void CheckShopCatalogAvailability(ModRuntimeSnapshot runtime, Action<bool, string> check)
    {
        var authority = Beta110FastSearchFixtureAuthority.CreateBeta111(BaseGameModelKeys.Characters.Ironclad, 0, 0, 1).EffectAuthority!;
        authority = authority with
        {
            MerchantColorlessOrderedPool = authority.ColorlessRewardPool,
            MerchantColorlessOrderedPoolExact = true,
            MerchantColorlessPoolIdentityHooksExact = true,
            MerchantColorlessRarityIdentityHooksExact = true,
            MerchantInitialInventoryShapeExact = true
        };
        var exact = ShopColorlessSearchUiCatalog.FromAuthority(RuntimeProfileId.Beta111, authority);
        var partialAuthority = authority with
        {
            MerchantColorlessPoolIdentityHooksExact = false,
            MerchantColorlessRarityIdentityHooksExact = false,
            MerchantInitialInventoryShapeExact = false
        };
        var partial = ShopColorlessSearchUiCatalog.FromAuthority(RuntimeProfileId.Beta111, partialAuthority);
        check(exact.AuthorityExact && partial.CatalogAvailable && !partial.AuthorityExact,
            "Captured merchant candidates remain available without Exact hook/shape authority");
        check(!partialAuthority.HasExactMerchantColorlessV1Inputs &&
              partial.UncommonCandidates.SequenceEqual(exact.UncommonCandidates) && partial.RareCandidates.SequenceEqual(exact.RareCandidates),
            "Shop authoring retains captured candidates without promoting Exact authority");
        var context = partial.CreateCardPickerContext(partial.UncommonCandidates, "partial-shop-regression");
        check(partial.UncommonCandidates.All(context.IsColorlessPoolMember) && context.AvailableCharacterKeys.Count == 0,
            "Shop cards retain explicit colorless provenance");
        ModelKey opaque = new("CARD", "OPAQUE_MOD_CARD");
        var unknown = new CardPickerContext([opaque], new Dictionary<ModelKey, CardPickerCandidateMetadata>(ModelKeyComparer.Instance)
        {
            [opaque] = new(opaque, [], EffectCardRarity.Uncommon, EffectCardType.Skill)
        }, false, "unknown-provenance-regression");
        check(!unknown.IsColorlessPoolMember(opaque), "Missing role provenance never means colorless");

        const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
        var picker = new ObjectPickerPanel();
        var shop = new ShopSearchEditor(runtime, picker);
        var draft = typeof(ShopSearchEditor).GetField("_draft", members)!.GetValue(shop)!;
        draft.GetType().GetField("Cards")!.SetValue(draft, partial);
        var rowKind = typeof(ShopSearchEditor).GetNestedType("RowKind", BindingFlags.NonPublic)!;
        var available = typeof(ShopSearchEditor).GetMethod("HasCandidates", members)!;
        bool CanPick(string row) => (bool)available.Invoke(shop, [Enum.Parse(rowKind, row)])!;
        check(CanPick("ColorlessUncommon") && CanPick("ColorlessRare"), "Partial merchant catalog enables both picker rows");
        draft.GetType().GetField("Cards")!.SetValue(draft, ShopColorlessSearchUiCatalog.Empty(RuntimeProfileId.Beta111, "missing"));
        check(!CanPick("ColorlessUncommon") && !CanPick("ColorlessRare"), "Unavailable merchant rows remain visibly disabled");
        shop.Free();
        picker.Free();
    }
}
