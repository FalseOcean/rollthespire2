using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    // Controlled native controls in the existing isolated host. The combat
    // admission flag is real, but no turn loop, potion Use or combat hooks run.
    // The oracle calls the original identity-selection pipeline on cloned RNG.
    private static async Task RunPotionSequenceChecks(Node root, string output, ModRuntimeSnapshot runtime,
        NGame game, SubViewport viewport, Control hoverRoot, RunState run, Player player,
        NGlobalUi global, NOverlayStack overlays, Func<string> signature, Action<bool, string> check,
        Func<string, Control, Control, string, Func<PredictorOperationScene, bool>?, Func<OperationPreview, bool>,
            Action<NeowOperationOverlay, OperationPreview>?, bool, Task> exercise,
        Action<object, string, object?> set)
    {
        var receipt = Path.Combine(output, "potion-sequences-result.json");
        File.WriteAllText(receipt, "{\"passed\":false,\"state\":\"running\"}");
        string original = signature();
        var originalSlots = player.PotionSlots.ToArray();
        var originalRng = run.Rng.ToSerializable();
        var turnField = AccessTools.Field(typeof(CombatManager), "_turnState");
        object? originalTurn = turnField.GetValue(CombatManager.Instance);
        var potionContainer = global.TopBar.GetNode<NPotionContainer>("%PotionContainer");
        var originalOverlayFilter = overlays.MouseFilter;
        var evidence = new List<object>();
        var holders = new List<NPotionHolder>();
        string[] keys = ["ATTACK_POTION", "SKILL_POTION", "POWER_POTION", "COLORLESS_POTION",
            "COSMIC_CONCOCTION", "OROBIC_ACID", "ENTROPIC_BREW"];
        bool passed = false;
        string? failure = null;
        async Task Frames(int count = 3)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Until(Func<bool> ready, string reason)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!ready() && clock.Elapsed < TimeSpan.FromSeconds(10)) await Frames(1);
            check(ready(), reason);
        }
        NeowOperationHoverController Controller() => hoverRoot.GetNode<NeowOperationHoverController>(NeowOperationHoverController.NodeName);
        NeowOperationOverlay? View() => hoverRoot.GetNodeOrNull<NeowOperationHoverController>(NeowOperationHoverController.NodeName)
            ?.GetNodeOrNull<NeowOperationOverlay>("Rt2NeowOperationOverlay");
        OperationPreview? VisiblePreview() => View() is { Visible: true } view
            ? PredictorOperationCapture.ReadNullable(view, "_preview") as OperationPreview : null;
        void Combat(bool active)
        {
            if (!active) { turnField.SetValue(CombatManager.Instance, null); return; }
            // Only IsInProgress is observed by these hover entry points. Do not
            // start a turn loop or manufacture a native-use completion.
            object turn = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(turnField.FieldType);
            set(turn, "IsInProgress", true);
            turnField.SetValue(CombatManager.Instance, turn);
            check(CombatManager.Instance.IsInProgress, "potion fixture exposes native combat admission flag");
        }
        async Task Leave()
        {
            viewport.PushInput(new InputEventMouseMotion { Position = new(4, viewport.Size.Y - 4),
                GlobalPosition = new(4, viewport.Size.Y - 4) }, true);
            await Frames();
        }
        void ClearInventory()
        {
            foreach (var holder in holders) if (GodotObject.IsInstanceValid(holder)) holder.Free();
            holders.Clear();
            foreach (var potion in player.Potions.ToArray()) player.DiscardPotionInternal(potion, silent: true);
        }
        PotionModel Add(string key)
        {
            var potion = ModelDb.GetById<PotionModel>(new("POTION", key)).ToMutable();
            check(player.AddPotionInternal(potion, silent: true).success, "potion fixture obtains actual bottle " + key);
            return potion;
        }
        async Task<NPotionHolder> Mount(PotionModel potion)
        {
            var holder = NPotionHolder.Create(true);
            potionContainer.AddChild(holder); holder.Position = new(50, 0);
            holder.AddPotion(NPotion.Create(potion)!); holders.Add(holder); await Frames();
            return holder;
        }
        PredictorOperationPotionObservation Capture(PotionModel potion, long? reward = null) =>
            PredictorOperationPotionTargetCapture.CaptureLive(run, player.NetId, runtime.Detection.NormalizedVersion,
                new(new(PredictorLiveRunCapture.Id(potion), PredictorLiveRunCapture.Key(potion)),
                    reward == null ? player.PotionSlots.ToList().IndexOf(potion) : null, reward));
        string ObservationSignature(PredictorOperationPotionObservation observation) => JsonSerializer.Serialize(observation);
        OperationPreviewCardGroup[] Groups(OperationPreview preview) => preview.Branches.SelectMany(b => b.Sections)
            .SelectMany(s => s.CardGroups).ToArray();
        string CardSignature(IEnumerable<CardModel> cards, string key) => string.Join("|", cards.Select(card =>
        {
            var clone = card.ToMutable();
            if (key == "COSMIC_CONCOCTION" && clone.IsUpgradable)
            { clone.UpgradeInternal(); clone.FinalizeUpgradeInternal(); }
            return clone.Id.Entry + ":" + clone.CurrentUpgradeLevel;
        }));
        string GroupSignature(OperationPreviewCardGroup group) => string.Join("|", group.Cards.Select(c => c.Key.Entry + ":" + c.UpgradeLevel));
        CardModel[] NativeCards(string key, Rng rng)
        {
            var own = player.Character.CardPool.GetUnlockedCards(player.UnlockState, ((IRunState)run).CardMultiplayerConstraint);
            var colorless = ModelDb.CardPool<ColorlessCardPool>().GetUnlockedCards(player.UnlockState, ((IRunState)run).CardMultiplayerConstraint);
            IEnumerable<CardModel> Generate(IEnumerable<CardModel> pool, CardType? type, int count)
            {
                var selected = pool.Where(c => type == null || c.Type == type);
                // Same native player-count filtering used by GetDistinctForCombat,
                // before FilterForCombat/TakeRandom; omit only combat card creation.
                selected = (IEnumerable<CardModel>)AccessTools.Method(typeof(CardFactory), "FilterForPlayerCount")
                    .Invoke(null, [run, selected])!;
                return CardFactory.FilterForCombat(selected).TakeRandom(count, rng);
            }
            return key == "OROBIC_ACID" ? Generate(own, CardType.Attack, 1).Concat(Generate(own, CardType.Skill, 1))
                .Concat(Generate(own, CardType.Power, 1)).ToArray() :
                Generate(key is "COLORLESS_POTION" or "COSMIC_CONCOCTION" ? colorless : own, key switch
                { "ATTACK_POTION" => CardType.Attack, "SKILL_POTION" => CardType.Skill, "POWER_POTION" => CardType.Power, _ => null }, 3).ToArray();
        }
        void CheckCardOracle(OperationPreview preview, PredictorOperationPotionObservation observation)
        {
            string before = signature(), detachedBefore = ObservationSignature(observation);
            var source = observation.Source;
            var inventories = new List<PotionModel?[]>();
            if (source.HeldSlot != null) inventories.Add(player.PotionSlots.ToArray());
            else if (player.HasOpenPotionSlots)
            {
                var slots = player.PotionSlots.ToArray();
                slots[Array.FindIndex(slots, p => p == null)] = ModelDb.GetById<PotionModel>(new(source.Potion.Key.Category, source.Potion.Key.Entry));
                inventories.Add(slots);
            }
            else for (int replace = 0; replace < player.MaxPotionCount; replace++)
            {
                var slots = player.PotionSlots.ToArray();
                slots[replace] = ModelDb.GetById<PotionModel>(new(source.Potion.Key.Category, source.Potion.Key.Entry));
                inventories.Add(slots);
            }
            var recipes = new Dictionary<string, string[]>(StringComparer.Ordinal);
            void Recipe(params string[] preceding)
            {
                string[] order = [.. preceding.Order(StringComparer.Ordinal), source.Potion.Key.Entry];
                recipes.TryAdd(string.Join("|", order.Select(key => "POTION:" + key)), order);
            }
            Recipe();
            foreach (var slots in inventories)
            {
                int selectedSlot = source.HeldSlot ?? Array.FindIndex(slots, p => p != null && p.Id.Entry == source.Potion.Key.Entry &&
                    !player.PotionSlots.Contains(p));
                var others = slots.Where((p, index) => index != selectedSlot && p != null && p.Id.Entry != "ENTROPIC_BREW" &&
                    keys.Contains(p.Id.Entry)).Select(p => p!).ToArray();
                foreach (var predecessor in others.DistinctBy(p => p.Id)) Recipe(predecessor.Id.Entry);
                // Pair actual bottle indices before canonicalizing their type
                // order: a repeated type requires two captured bottles.
                for (int first = 0; first < others.Length; first++)
                for (int second = first + 1; second < others.Length; second++)
                    Recipe(others[first].Id.Entry, others[second].Id.Entry);
            }
            // Decide the depth from recipe identities alone. Equal generated
            // cards never collapse two different potion conditions.
            bool thirdUse = recipes.Count <= 8;
            var expected = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var (sequence, order) in recipes.Where(recipe => thirdUse || recipe.Value.Length < 3))
            {
                var rng = new Rng(run.Rng.CombatCardGeneration.ToSerializable());
                foreach (string predecessor in order.SkipLast(1)) NativeCards(predecessor, rng);
                string value = CardSignature(NativeCards(source.Potion.Key.Entry, rng), source.Potion.Key.Entry);
                expected.Add(sequence, [value]);
                if (order.Length == 3)
                {
                    var reversed = new Rng(run.Rng.CombatCardGeneration.ToSerializable());
                    foreach (string predecessor in order.Take(2).Reverse()) NativeCards(predecessor, reversed);
                    string reverseValue = CardSignature(NativeCards(source.Potion.Key.Entry, reversed), source.Potion.Key.Entry);
                    check(reverseValue == value && JsonSerializer.Serialize(reversed.ToSerializable()) ==
                        JsonSerializer.Serialize(rng.ToSerializable()),
                        "native fixed pools give both predecessor orders the same hovered cards and RNG cursor");
                }
            }
            var groups = Groups(preview);
            var actual = groups.GroupBy(g => string.Join("|", g.PotionUseOrder.Select(key => key.Serialized)))
                .ToDictionary(g => g.Key, g => g.Select(GroupSignature).ToHashSet());
            check(preview.Faults.IsEmpty && groups.Length == expected.Sum(pair => pair.Value.Count) &&
                groups.All(group => !group.Possible && group.PotionUseOrder.Length is >= 1 and <= 3 && group.PotionUseOrder[^1] == source.Potion.Key &&
                    (group.Label == null || group.Label == "PotionUseStep:" + group.PotionUseOrder.Length)) &&
                actual.Count == expected.Count && expected.All(pair =>
                actual.TryGetValue(pair.Key, out var values) && values.SetEquals(pair.Value)),
                "native cloned CardFactory oracle covers distinct recipes through use three when at most eight groups, otherwise use two: " + source.Potion.Key.Entry);
            check(signature() == before && ObservationSignature(observation) == detachedBefore,
                "native identity oracle and forecast leave live and captured input unchanged");
        }
        void InspectCards(NeowOperationOverlay view, OperationPreview preview)
        {
            var stacks = Descendants(view).OfType<Control>().Where(c => c.Name.ToString()
                .StartsWith("Rt2NeowCardStack_", StringComparison.Ordinal)).ToArray();
            bool grouped = Groups(preview).Any(g => g.Label != null);
            check(stacks.Length == Groups(preview).Length && stacks.All(s => s.GetMeta("rt2_stacked").AsBool() == grouped),
                "native potion artwork is flat for one group and stacked separately for multiple bottle ordinals");
            check(Descendants(view).OfType<NCard>().Count() == Groups(preview).Sum(g => g.Cards.Length),
                "native potion renderer retains every candidate card");
            var groups = Groups(preview);
            check(stacks.Select((stack, index) =>
            {
                var strips = Descendants(stack).OfType<Control>().Where(control => control.Name == "Rt2PotionUseOrder").ToArray();
                if (strips.Length != 1) return false;
                var icons = strips[0].GetChildren().OfType<Control>().Where(control => control.HasMeta("rt2_model")).ToArray();
                return icons.Select(icon => icon.GetMeta("rt2_model").AsString())
                    .SequenceEqual(groups[index].PotionUseOrder.Select(key => key.Serialized)) &&
                    icons.All(icon => icon.CustomMinimumSize.IsEqualApprox(new(28, 28))) &&
                    view.GetViewportRect().Grow(.2f).Encloses(strips[0].GetGlobalRect());
            }).All(value => value), "each native card group shows its actual one-to-three-bottle order as small icons inside the viewport");
            if (grouped)
                check(stacks.Select((stack, index) =>
                {
                    var faces = Descendants(stack).OfType<NCard>().ToArray();
                    var front = faces.OrderByDescending(face => face.GetParent<Control>().ZIndex).First();
                    var tile = front.GetParent<Control>();
                    return front.Model?.Id.Entry == groups[index].Cards[0].Key.Entry &&
                        tile.Modulate.IsEqualApprox(Colors.White) &&
                        faces.All(face => tile.Position.X <= face.GetParent<Control>().Position.X &&
                            tile.Position.Y >= face.GetParent<Control>().Position.Y);
                }).All(value => value),
                    "each potion stack puts the native first/leftmost candidate bright and foremost at lower-left");
        }
        void CheckBrew(OperationPreview preview, PredictorOperationPotionObservation observation)
        {
            var rows = preview.Branches.SelectMany(b => b.Sections).SelectMany(s => s.Rows).ToArray();
            int empty = observation.Inventory.Count(p => p == null) + (observation.Source.HeldSlot != null ? 1 : 0);
            // Reward acquisition fills an existing empty slot before use; full
            // inventory replacement also leaves only its consumed source slot.
            if (observation.Source.RewardId != null) empty = Math.Max(1, empty);
            int brews = observation.Inventory.Count(p => p?.Key.Entry == "ENTROPIC_BREW") +
                (observation.Source.RewardId != null ? 1 : 0);
            var rng = new Rng(run.Rng.CombatPotionGeneration.ToSerializable());
            var expected = new List<string[]>();
            for (int i = 0; i < Math.Min(2, brews); i++)
            {
                int count = i == 0 ? empty : 1;
                expected.Add(Enumerable.Range(0, count).Select(_ => PotionFactory.CreateRandomPotionOutOfCombat(player, rng).Id.Entry).ToArray());
            }
            check(preview.Faults.IsEmpty && rows.Length == expected.Count && rows.Select((row, index) =>
                row.Models.Select(key => key.Entry).SequenceEqual(expected[index])).All(value => value),
                "Entropic uses native out-of-combat factory per actual empty slot for at most two original bottle ordinals");
            check(rows.All(row => row.Label != null && !row.Label.Contains("OtherSlotsEmpty", StringComparison.Ordinal)),
                "Entropic current inventory never carries the superseded all-other-slots-empty hypothetical");
        }
        void InspectBrew(NeowOperationOverlay view, OperationPreview preview)
        {
            var panel = Descendants(view).OfType<Control>().SingleOrDefault(control => control.Name == "Rt2OperationPrimaryPanel");
            check(panel is { Visible: true } && panel.IsVisibleInTree() &&
                panel.GetChildren().OfType<NinePatchRect>().Any(backdrop =>
                    backdrop.Modulate.IsEqualApprox(new Color(.48f, .67f, .94f, 1))) &&
                preview.Branches.SelectMany(branch => branch.Sections).SelectMany(section => section.Rows)
                    .SelectMany(row => row.Models).GroupBy(key => key).All(models =>
                    {
                        string name = (string)AccessTools.Method(typeof(NeowOperationOverlay), "NameOf").Invoke(null, [models.Key])!;
                        return Descendants(panel).OfType<Label>().Count(label => label.IsVisibleInTree() && label.Text == name) >= models.Count();
                    }),
                "Entropic renders its generated potion names in the night-blue primary text panel");
            check(!Descendants(view).OfType<Control>().Any(control => control.Name.ToString().StartsWith("Rt2OperationIconGroup_", StringComparison.Ordinal)),
                "Entropic result has no free-standing icon groups");
            check(preview.Style?.Text == OperationTextMode.GroupRows && Groups(preview).Length == 0,
                "Entropic explicitly requests text rows rather than card or icon artwork");
        }
        try
        {
            check(originalTurn == null && player.MaxPotionCount >= 3 && overlays.Peek() == null,
                "potion fixture starts outside a turn loop with native reward stack empty and three normal slots");
            // The host constructed this full-rect stack with new NOverlayStack
            // and bypassed its native scene startup.
            // Its default Stop intercepted the actual pointer above topbar
            // holders. Change only this empty fixture root; reward children
            // below still use the original stack/backstop input lifecycle.
            overlays.MouseFilter = Control.MouseFilterEnum.Ignore;
            foreach (bool battle in new[] { false, true })
            {
                Combat(battle);
                foreach (string key in keys)
                {
                    await Leave(); ClearInventory();
                    var potion = Add(key); var holder = await Mount(potion);
                    if (key == "ENTROPIC_BREW") Add("ATTACK_POTION");
                    var observation = Capture(potion);
                    check(key == "ENTROPIC_BREW" ? observation.Context.Catalog == null &&
                        observation.Context.Operation is { CombatCardRng: null, CombatCardPools: null } &&
                        !observation.Context.PotionPool.IsEmpty : observation.Context.Catalog != null &&
                        observation.Context.Operation is { CombatCardRng: not null, CombatCardPools: not null } &&
                        observation.Context.PotionPool.IsEmpty,
                        "source-specific potion observation captures only its generation model inputs: " + key);
                    string before = signature(), detached = ObservationSignature(observation);
                    var expected = PotionOperationPreview.Build(observation);
                    if (key == "ENTROPIC_BREW") CheckBrew(expected, observation); else CheckCardOracle(expected, observation);
                    check(signature() == before && ObservationSignature(observation) == detached && observation.InCombat == battle,
                        "seven-potion live capture and pure projection retain source/target identity and combat admission");
                    await exercise("potion-" + (battle ? "battle-" : "outside-") + key.ToLowerInvariant(), holder, holder, key, null,
                        preview => JsonSerializer.Serialize(preview) == JsonSerializer.Serialize(expected),
                        key == "ENTROPIC_BREW" ? InspectBrew : InspectCards, false);
                    evidence.Add(new { source = "NPotionHolder", key, battle, groups = Groups(expected).Length });
                }
            }

            // A heterogeneous current inventory keeps distinct recipes.
            // Three identical bottles collapse to one recipe per use position.
            foreach (bool mixed in new[] { true, false })
            {
                await Leave(); ClearInventory(); Combat(false);
                var potion = Add("ATTACK_POTION");
                Add(mixed ? "SKILL_POTION" : "ATTACK_POTION"); Add(mixed ? "POWER_POTION" : "ATTACK_POTION");
                var holder = await Mount(potion); var observation = Capture(potion);
                var expected = PotionOperationPreview.Build(observation); CheckCardOracle(expected, observation);
                check(Groups(expected).Select(g => g.PotionUseOrder.Length).Distinct().Order().SequenceEqual([1, 2, 3]) &&
                    Groups(expected).Length == (mixed ? 4 : 3), "three actual bottles retain all first-to-third-use recipes and equivalent predecessor identities coalesce");
                if (mixed)
                {
                    // A valid reduced detached pool makes both predecessor
                    // recipes consume no RNG and the hovered result identical.
                    // Their visible order must still preserve recipe identity.
                    var operation = observation.Context.Operation!;
                    var pools = operation.CombatCardPools!;
                    var reduced = observation with { Context = observation.Context with { Operation = operation with
                    {
                        CombatCardPools = pools with { CharacterCards = pools.CharacterCards
                            .Where(card => card.Card.Type == EffectCardType.Attack &&
                                card.CanGenerate && card.Rarity is not ("Basic" or "Ancient" or "Event"))
                            .Where(card => operation.PlayerCount > 1 ? card.MultiplayerConstraint != "SingleplayerOnly" :
                                card.MultiplayerConstraint != "MultiplayerOnly")
                            .Take(1).ToImmutableArray() }
                    } } };
                    var collisions = Groups(PotionOperationPreview.Build(reduced));
                    check(collisions.Length == 4 && collisions.All(group => group.Cards.Length == 1) &&
                        collisions.Select(GroupSignature).Distinct().Count() == 1 &&
                        collisions.Where(group => group.PotionUseOrder.Length == 2).Select(group => group.PotionUseOrder[0].Entry)
                            .Order().SequenceEqual(new[] { "POWER_POTION", "SKILL_POTION" }) &&
                        collisions.Single(group => group.PotionUseOrder.Length == 3).PotionUseOrder.Select(key => key.Entry)
                            .SequenceEqual(new[] { "POWER_POTION", "SKILL_POTION", "ATTACK_POTION" }),
                        "different recipes remain distinct even when RNG cursor and visible card results coincide");
                }
                await exercise("potion-sequence-" + (mixed ? "mixed" : "equal"), holder, holder, "ATTACK_POTION", null,
                    preview => JsonSerializer.Serialize(preview) == JsonSerializer.Serialize(expected), InspectCards, false);
            }

            // Exercise both sides of the eight-recipe threshold, including
            // exact equality and duplicate bottle types in actual inventory.
            foreach (var scenario in new[]
            {
                (Name: "four-distinct", Others: new[] { "SKILL_POTION", "POWER_POTION", "COLORLESS_POTION" }, Groups: 7, Depth: 3),
                (Name: "exact-eight", Others: new[] { "SKILL_POTION", "SKILL_POTION", "POWER_POTION", "COLORLESS_POTION" }, Groups: 8, Depth: 3),
                (Name: "five-distinct", Others: new[] { "SKILL_POTION", "POWER_POTION", "COLORLESS_POTION", "OROBIC_ACID" }, Groups: 5, Depth: 2),
                (Name: "seven-slots", Others: new[] { "SKILL_POTION", "POWER_POTION", "COLORLESS_POTION", "OROBIC_ACID", "COSMIC_CONCOCTION", "ATTACK_POTION" }, Groups: 7, Depth: 2)
            })
            {
                await Leave(); ClearInventory(); Combat(false);
                check(player.MaxPotionCount == 3, "dense potion fixture begins with three original native slots");
                int slotCount = scenario.Others.Length + 1;
                player.AddToMaxPotionCount(slotCount - 3);
                try
                {
                    check(player.MaxPotionCount == slotCount, "native capacity API provides expanded actual potion slots");
                    var potion = Add("ATTACK_POTION");
                    foreach (string key in scenario.Others) Add(key);
                    var holder = await Mount(potion); var observation = Capture(potion);
                    var expected = PotionOperationPreview.Build(observation); CheckCardOracle(expected, observation);
                    check(Groups(expected).Length == scenario.Groups && Groups(expected).Count(group => group.PotionUseOrder.Length == 1) == 1 &&
                        Groups(expected).Max(group => group.PotionUseOrder.Length) == scenario.Depth,
                        "expanded actual bottles select the expected recipe count/depth at the eight-group boundary: " + scenario.Name);
                    await exercise("potion-sequence-dense-" + scenario.Name, holder, holder, "ATTACK_POTION", null,
                        preview => JsonSerializer.Serialize(preview) == JsonSerializer.Serialize(expected),
                        (view, preview) =>
                        {
                            InspectCards(view, preview);
                            var stacks = Descendants(view).OfType<Control>().Where(control =>
                                control.Name.ToString().StartsWith("Rt2NeowCardStack_", StringComparison.Ordinal)).ToArray();
                            check(stacks.All(stack => Math.Abs(stack.GlobalPosition.Y - stacks[0].GlobalPosition.Y) < .2f) &&
                                stacks.Zip(stacks.Skip(1)).All(pair => pair.First.GetGlobalRect().End.X <= pair.Second.GlobalPosition.X),
                                "all potion possibility groups occupy one left-to-right row, including exactly eight recipe groups");
                            float baselineScale = .6f * Math.Min(view.GetViewportRect().Size.X / 1920f,
                                view.GetViewportRect().Size.Y / 1080f);
                            var faces = Descendants(view).OfType<NCard>().ToArray();
                            check(view.CardDisplayScale > 0 && view.CardDisplayScale <= baselineScale + .0001f &&
                                faces.All(face => face.GetParent<Control>().Scale.IsEqualApprox(Vector2.One * view.CardDisplayScale)),
                                "dense potion candidates share one positive card scale no larger than the viewport baseline");
                            check(faces.Length == Groups(preview).Sum(group => group.Cards.Length) &&
                                faces.All(face => view.GetViewportRect().Grow(.2f).Encloses(face.GetParent<Control>().GetGlobalRect())),
                                "dense potion layout displays every predicted native face inside the screen bounds");
                            evidence.Add(new { source = "NPotionHolder", key = "ATTACK_POTION", slots = player.MaxPotionCount,
                                groups = Groups(preview).Length, nativeFaces = faces.Length, scale = view.CardDisplayScale, baselineScale });
                        }, false);
                }
                finally
                {
                    await Leave(); ClearInventory();
                    player.SubtractFromMaxPotionCount(player.MaxPotionCount - originalSlots.Length);
                    check(player.MaxPotionCount == originalSlots.Length, "dense potion fixture restores original capacity after clearing added slots");
                }
            }

            await Leave(); ClearInventory();
            var brew = Add("ENTROPIC_BREW"); Add("ENTROPIC_BREW"); Add("ENTROPIC_BREW");
            var brewHolder = await Mount(brew); var brewObservation = Capture(brew);
            var brewExpected = PotionOperationPreview.Build(brewObservation); CheckBrew(brewExpected, brewObservation);
            await exercise("potion-sequence-entropic", brewHolder, brewHolder, "ENTROPIC_BREW", null,
                preview => JsonSerializer.Serialize(preview) == JsonSerializer.Serialize(brewExpected), InspectBrew, false);

            // Advance the shared run stream in place while the true native
            // holder remains focused. Such a consumer can belong to this
            // player or a peer; the host does not pretend to execute either Use.
            foreach (string key in new[] { "ATTACK_POTION", "ENTROPIC_BREW" })
            {
                await Leave(); ClearInventory(); Combat(true);
                var potion = Add(key); var holder = await Mount(potion);
                var source = Capture(potion).Source;
                await exercise("potion-refresh-" + key.ToLowerInvariant(), holder, holder, key, null,
                    preview => preview.Faults.IsEmpty && preview.Branches.Length > 0, null, true);
                var old = VisiblePreview();
                string stamp = PredictorOperationPotionTargetCapture.ObservationStamp(run, player.NetId, source);
                var relevant = key == "ENTROPIC_BREW" ? run.Rng.CombatPotionGeneration : run.Rng.CombatCardGeneration;
                var unrelated = key == "ENTROPIC_BREW" ? run.Rng.CombatCardGeneration : run.Rng.CombatPotionGeneration;
                unrelated.NextInt();
                check(PredictorOperationPotionTargetCapture.ObservationStamp(run, player.NetId, source) == stamp,
                    "unrelated shared generation stream does not invalidate " + key);
                await Frames(40);
                check(ReferenceEquals(VisiblePreview(), old),
                    "normal freshness polling retains the same published result after only the unrelated stream advances: " + key);
                relevant.NextInt();
                check(PredictorOperationPotionTargetCapture.ObservationStamp(run, player.NetId, source) != stamp,
                    "relevant shared generation stream invalidates " + key + " with source bottle unchanged");
                string changed = signature();
                var refreshed = PotionOperationPreview.Build(Capture(potion));
                await Until(() => VisiblePreview() is { } visible && !ReferenceEquals(visible, old) &&
                    JsonSerializer.Serialize(visible) == JsonSerializer.Serialize(refreshed),
                    "native held potion refreshes after shared RNG advances without leave/refocus: " + key);
                check(ReferenceEquals(PredictorOperationCapture.ReadNullable(Controller(), "_owner"), holder) && signature() == changed,
                    "refresh keeps same native owner and does not consume live RNG or bottle");
                await Leave();
            }

            // Actual offered bottles and NRewardButtons, including full
            // inventories: receiving the offer requires replacing a real slot.
            Combat(false);
            overlays.MouseFilter = originalOverlayFilter;
            foreach (bool full in new[] { false, true })
            foreach (string key in keys)
            {
                await Leave(); ClearInventory();
                if (full)
                {
                    Add("ATTACK_POTION"); Add("SKILL_POTION"); Add("POWER_POTION");
                    while (player.HasOpenPotionSlots) Add("ATTACK_POTION");
                }
                var potion = ModelDb.GetById<PotionModel>(new("POTION", key)).ToMutable();
                var reward = new PotionReward(potion, player);
                var rewards = new RewardsSet(player).WithCustomRewards([reward]);
                await rewards.GenerateWithoutOffering();
                _ = RunManager.Instance.RewardsSetSynchronizer.BeginRewardsSet(rewards);
                NRewardsScreen? screen = null;
                try
                {
                    screen = NRewardsScreen.ShowScreen(rewards, false, run); await Frames(40);
                    var button = Descendants(screen).OfType<NRewardButton>().Single(b => ReferenceEquals(b.Reward, reward));
                    var observation = Capture(potion, PredictorLiveRunCapture.Id(reward));
                    var expected = PotionOperationPreview.Build(observation);
                    if (key == "ENTROPIC_BREW") CheckBrew(expected, observation); else CheckCardOracle(expected, observation);
                    check(expected.Notices.Contains("PotionAfterAcquisition") &&
                        expected.Notices.Contains("PotionAcquisitionRequiresSlot") == full,
                        "native potion reward keeps actual acquisition/replacement obligations");
                    await exercise("potion-reward-" + (full ? "full-" : "open-") + key.ToLowerInvariant(), button, button, key, null,
                        preview => JsonSerializer.Serialize(preview) == JsonSerializer.Serialize(expected),
                        key == "ENTROPIC_BREW" ? InspectBrew : InspectCards, false);
                    check(!reward.SuccessfullySelected && !player.PotionSlots.Contains(potion),
                        "potion reward hover does not claim offer or replace an actual bottle");
                    evidence.Add(new { source = "NRewardButton", key, battle = false, fullInventory = full, groups = Groups(expected).Length });
                }
                finally
                {
                    await Leave();
                    if (screen != null && GodotObject.IsInstanceValid(screen)) overlays.Remove(screen);
                    RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet(); await Frames();
                }
            }
            passed = true;
        }
        catch (Exception error) { failure = error.ToString(); throw; }
        finally
        {
            await Leave(); Combat(false); ClearInventory();
            if (player.MaxPotionCount > originalSlots.Length)
                player.SubtractFromMaxPotionCount(player.MaxPotionCount - originalSlots.Length);
            foreach (var (potion, index) in originalSlots.Select((potion, index) => (potion, index)))
                if (potion != null) check(player.AddPotionInternal(potion, index, silent: true).success, "restore original potion slot");
            run.Rng.LoadFromSerializable(originalRng);
            turnField.SetValue(CombatManager.Instance, originalTurn);
            overlays.MouseFilter = originalOverlayFilter;
            check(signature() == original, "potion fixture restores exact original live run/inventory/RNG");
            File.WriteAllText(receipt, JsonSerializer.Serialize(new { passed, failure, evidence,
                nativeHoverCaptureWorkerRenderer = true, nativeIdentityFactoryCloneOracle = true,
                nativePotionUseExecuted = false, fullCombatHookParity = false, ownerRuntimeAcceptance = false },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        GD.Print("POTION_SEQUENCES_UI_PASS nativeHeldAndReward=true;combatAdmission=true;cloneOracle=true;nativeUse=false");
    }
}
