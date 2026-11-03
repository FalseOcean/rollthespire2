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
using MegaCrit.Sts2.Core.Nodes.HoverTips;
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
                string[] order = [.. preceding, source.Potion.Key.Entry];
                recipes.TryAdd(string.Join(">", order), order);
            }
            Recipe();
            foreach (var slots in inventories)
            {
                int selectedSlot = source.HeldSlot ?? Array.FindIndex(slots, p => p != null && p.Id.Entry == source.Potion.Key.Entry &&
                    !player.PotionSlots.Contains(p));
                var others = slots.Where((p, index) => index != selectedSlot && p != null && p.Id.Entry != "ENTROPIC_BREW" &&
                    keys.Contains(p.Id.Entry)).Select(p => p!).ToArray();
                foreach (var predecessor in others) Recipe(predecessor.Id.Entry);
                // Enumerate real bottle indices and both orders. Identical
                // bottle types coalesce only after preserving multiplicity.
                for (int first = 0; first < others.Length; first++)
                for (int second = 0; second < others.Length; second++)
                    if (first != second) Recipe(others[first].Id.Entry, others[second].Id.Entry);
            }
            var reference = new List<(int Depth, string Cards, string Path)>();
            foreach (var (sequence, order) in recipes)
            {
                var rng = new Rng(run.Rng.CombatCardGeneration.ToSerializable());
                foreach (string predecessor in order.SkipLast(1)) NativeCards(predecessor, rng);
                reference.Add((order.Length, CardSignature(NativeCards(source.Potion.Key.Entry, rng), source.Potion.Key.Entry), sequence));
            }
            bool thirdUse = reference.GroupBy(row => (row.Depth, row.Cards)).Count() <= 8;
            var expected = reference.Where(row => thirdUse || row.Depth < 3).ToArray();
            var groups = Groups(preview);
            var actual = new HashSet<(int Depth, string Cards, string Path)>();
            foreach (var group in groups)
            {
                var paths = group.IconPaths.SelectMany(ExpandOperationIconPath).ToArray();
                check(paths.Length > 0 && paths.All(path => path.Length is >= 1 and <= 3 && path[^1] == source.Potion.Key.Serialized) &&
                    paths.Select(path => path.Length).Distinct().Count() == 1 && !group.Possible,
                    "each potion result preserves complete target-last paths at one use depth");
                int depth = paths[0].Length;
                check(group.Label == null || group.Label == "PotionUseStep:" + depth,
                    "potion result ordinal agrees with every merged route");
                int select = source.Potion.Key.Entry is "COSMIC_CONCOCTION" or "OROBIC_ACID" ? group.Cards.Length : 1;
                check(group.MinimumSelect == select && group.MaximumSelect == select,
                    "merged potion result preserves actual choose-one versus generate-all behavior");
                foreach (var path in paths)
                {
                    string sequence = string.Join(">", path.Select(key => key.Split(':')[1]));
                    actual.Add((depth, GroupSignature(group), sequence));
                }
            }
            check(preview.Faults.IsEmpty && groups.Length == expected.GroupBy(row => (row.Depth, row.Cards)).Count() &&
                actual.SetEquals(expected),
                "native cloned CardFactory oracle preserves every explicit legal route while merging equal ordered cards within a depth: " + source.Potion.Key.Entry);
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
            CheckOperationIconPaths(view, preview, check);
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

            // Native colorless factory cursor is shared by the two bottles.
            // Their direct effects differ: Cosmic upgrades all generated cards,
            // while Colorless presents unupgraded choose-one candidates.
            await Leave(); ClearInventory(); Combat(false);
            var attackTarget = Add("ATTACK_POTION");
            var colorlessBottle = Add("COLORLESS_POTION"); var cosmicBottle = Add("COSMIC_CONCOCTION");
            var colorlessPreview = PotionOperationPreview.Build(Capture(colorlessBottle));
            var cosmicPreview = PotionOperationPreview.Build(Capture(cosmicBottle));
            CheckCardOracle(colorlessPreview, Capture(colorlessBottle)); CheckCardOracle(cosmicPreview, Capture(cosmicBottle));
            var colorlessDirect = Groups(colorlessPreview).Single(group => group.IconPaths[0].Segments.Sum(segment => segment.Length) == 1);
            var cosmicDirect = Groups(cosmicPreview).Single(group => group.IconPaths[0].Segments.Sum(segment => segment.Length) == 1);
            check(colorlessDirect.Cards.Select(card => card.Key).SequenceEqual(cosmicDirect.Cards.Select(card => card.Key)) &&
                colorlessDirect.MinimumSelect == 1 && cosmicDirect.MinimumSelect == cosmicDirect.Cards.Length &&
                colorlessDirect.Cards.Zip(cosmicDirect.Cards).All(pair => pair.Second.UpgradeLevel == Math.Min(pair.First.MaxUpgradeLevel, pair.First.UpgradeLevel + 1)) &&
                colorlessDirect.Cards.Zip(cosmicDirect.Cards).Any(pair => pair.Second.UpgradeLevel > pair.First.UpgradeLevel),
                "native factory and upgrade oracle distinguish direct Cosmic upgraded generate-all from Colorless unupgraded choose-one");
            var alternateHolder = await Mount(attackTarget); var alternateObservation = Capture(attackTarget);
            var alternatePreview = PotionOperationPreview.Build(alternateObservation); CheckCardOracle(alternatePreview, alternateObservation);
            var secondUse = Groups(alternatePreview).Single(group => group.IconPaths[0].Segments.Sum(segment => segment.Length) == 2);
            check(secondUse.IconPaths.SelectMany(ExpandOperationIconPath).Select(path => string.Join(">", path)).ToHashSet().SetEquals(new[] {
                    "POTION:COLORLESS_POTION>POTION:ATTACK_POTION", "POTION:COSMIC_CONCOCTION>POTION:ATTACK_POTION" }),
                "Colorless and Cosmic as alternate prerequisites merge into one target result with both complete legal routes");
            await exercise("potion-sequence-colorless-cosmic-alternatives", alternateHolder, alternateHolder, "ATTACK_POTION", null,
                preview => Groups(preview).Length == Groups(alternatePreview).Length,
                (view, preview) =>
                {
                    InspectCards(view, preview);
                    check(Descendants(view).OfType<Label>().Any(label => label.Name == "Rt2AlternativeOr" && label.Text == "或") &&
                        Descendants(view).OfType<Label>().Any(label => label.Name == "Rt2PathArrow") &&
                        Descendants(view).OfType<Label>().Any(label => label.Name == "Rt2PathOpenBracket"),
                        "actual potion hover renders alternate prerequisites with or, sequential arrow and exchangeable all-steps bracket");
                    var stacks = Descendants(view).OfType<Control>().Where(node => node.Name.ToString().StartsWith("Rt2NeowCardStack_", StringComparison.Ordinal)).ToArray();
                    var shown = Groups(preview);
                    int alternativeIndex = Array.FindIndex(shown, group => group.IconPaths[0].Segments.Sum(segment => segment.Length) == 2);
                    int bothIndex = Array.FindIndex(shown, group => group.IconPaths[0].Segments.Sum(segment => segment.Length) == 3);
                    check(Descendants(stacks[alternativeIndex]).Count(node => node.HasMeta("rt2_alternatives")) == 1 &&
                        !Descendants(stacks[bothIndex]).Any(node => node.HasMeta("rt2_alternatives")),
                        "actual choose-either precursor is a stacked bottle pair while two required bottles remain separate bracketed steps");
                }, false);
            var alternateOldViewport = viewport.Size; var alternateOldWindow = root.GetWindow().Size;
            var alternateLanguage = new RolltheSpire2.Ui.Workspace.SearchWorkspace(OS.GetUserDataDir(), false);
            string alternateOldLanguage = alternateLanguage.Preferences.LanguageOverride;
            try
            {
                foreach (string language in new[] { "zh", "en" })
                foreach (var size in new[] { new Vector2I(1920, 1080), new Vector2I(1280, 720) })
                {
                    if (language == "zh" && size.X == 1920) continue;
                    alternateLanguage.SetLanguageOverride(language);
                    viewport.Size = size; root.GetWindow().Size = viewport.Size; await Frames(8);
                    await exercise("potion-sequence-colorless-cosmic-alternatives-" + language + "-" + size.X, alternateHolder, alternateHolder, "ATTACK_POTION", null,
                        preview => Groups(preview).Length == Groups(alternatePreview).Length, InspectCards, false);
                }
            }
            finally { alternateLanguage.SetLanguageOverride(alternateOldLanguage); viewport.Size = alternateOldViewport; root.GetWindow().Size = alternateOldWindow; await Frames(8); }
            evidence.Add(new { source = "NPotionHolder", key = "ATTACK_POTION", caseName = "colorless-cosmic-alternatives",
                groups = Groups(alternatePreview).Select(group => new { group.Cards, group.MinimumSelect, group.MaximumSelect, group.IconPaths }),
                directColorless = colorlessDirect.Cards, directCosmic = cosmicDirect.Cards });

            // A heterogeneous current inventory keeps distinct results.
            // Three identical bottles collapse to one recipe per use position.
            foreach (bool mixed in new[] { true, false })
            {
                await Leave(); ClearInventory(); Combat(false);
                var potion = Add("ATTACK_POTION");
                Add(mixed ? "SKILL_POTION" : "ATTACK_POTION"); Add(mixed ? "POWER_POTION" : "ATTACK_POTION");
                var holder = await Mount(potion); var observation = Capture(potion);
                var expected = PotionOperationPreview.Build(observation); CheckCardOracle(expected, observation);
                check(Groups(expected).Select(g => g.IconPaths[0].Segments.Sum(segment => segment.Length)).Distinct().Order().SequenceEqual([1, 2, 3]) &&
                    Groups(expected).Length == (mixed ? 4 : 3), "three actual bottles retain all first-to-third-use recipes and equivalent predecessor identities coalesce");
                if (mixed)
                {
                    // Both predecessors consume no RNG in this reduced legal
                    // pool. Equal results merge but retain every genuine route.
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
                    check(collisions.Length == 3 && collisions.All(group => group.Cards.Length == 1) &&
                        collisions.Select(GroupSignature).Distinct().Count() == 1 &&
                        collisions.Single(group => group.IconPaths[0].Segments.Sum(segment => segment.Length) == 2).IconPaths
                            .SelectMany(ExpandOperationIconPath).Select(path => string.Join(">", path)).ToHashSet().SetEquals(new[] {
                                "POTION:POWER_POTION>POTION:ATTACK_POTION", "POTION:SKILL_POTION>POTION:ATTACK_POTION" }) &&
                        collisions.Single(group => group.IconPaths[0].Segments.Sum(segment => segment.Length) == 3).IconPaths
                            .SelectMany(ExpandOperationIconPath).Count() == 2,
                        "same-depth equal cards merge while retaining alternative prerequisites and both exchangeable predecessor orders");
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
                (Name: "seven-slots", Others: new[] { "SKILL_POTION", "POWER_POTION", "COLORLESS_POTION", "OROBIC_ACID", "COSMIC_CONCOCTION", "ATTACK_POTION" }, Groups: 6, Depth: 2)
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
                    check(Groups(expected).Length == scenario.Groups && Groups(expected).Count(group => group.IconPaths[0].Segments.Sum(segment => segment.Length) == 1) == 1 &&
                        Groups(expected).Max(group => group.IconPaths[0].Segments.Sum(segment => segment.Length)) == scenario.Depth,
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
            // Reproduce the player's entry point: a single held Power Potion
            // while the native reward screen is open, not its offered bottle.
            await Leave(); ClearInventory();
            var heldPower = Add("POWER_POTION");
            var heldPowerHolder = await Mount(heldPower);
            var offeredBrew = ModelDb.GetById<PotionModel>(new("POTION", "ENTROPIC_BREW")).ToMutable();
            var heldRewards = new RewardsSet(player).WithCustomRewards([new PotionReward(offeredBrew, player)]);
            await heldRewards.GenerateWithoutOffering();
            _ = RunManager.Instance.RewardsSetSynchronizer.BeginRewardsSet(heldRewards);
            NRewardsScreen? heldRewardScreen = null;
            var originalViewportSize = viewport.Size;
            var originalWindowSize = root.GetWindow().Size;
            int originalOverlayIndex = overlays.GetIndex();
            try
            {
                // run.tscn places the overlay stack before TopBar. The host
                // appends its synthetic stack later, so its real full-screen
                // backstop would otherwise intercept this topbar pointer.
                game.MoveChild(overlays, global.TopBar.GetIndex());
                heldRewardScreen = NRewardsScreen.ShowScreen(heldRewards, false, run); await Frames(40);
                foreach (var size in new[] { new Vector2I(1920, 1080), new Vector2I(1280, 720) })
                {
                    viewport.Size = size; root.GetWindow().Size = size; await Frames(6);
                    var expected = PotionOperationPreview.Build(Capture(heldPower));
                    check(Groups(expected) is [{ Cards.Length: 3 }],
                        "reward-screen held Power Potion has one actual three-candidate group");
                    await exercise("potion-reward-screen-held-power-" + size.X, heldPowerHolder, heldPowerHolder,
                        "POWER_POTION", null, preview => JsonSerializer.Serialize(preview) == JsonSerializer.Serialize(expected),
                        (view, preview) =>
                        {
                            InspectCards(view, preview);
                            var tiles = Descendants(view).OfType<NCard>().Select(face => face.GetParent<Control>()).ToArray();
                            check(tiles.Length == 3 && tiles.All(tile => Math.Abs(tile.GlobalPosition.Y - tiles[0].GlobalPosition.Y) < .2f) &&
                                tiles.Zip(tiles.Skip(1)).All(pair => pair.First.GetGlobalRect().End.X <= pair.Second.GlobalPosition.X) &&
                                tiles.All(tile => view.GetViewportRect().Grow(.2f).Encloses(tile.GetGlobalRect())),
                                "single held potion candidates spread left-to-right in one complete row inside the reward-screen viewport");
                            var tips = hoverRoot.GetChildren().OfType<NHoverTipSet>()
                                .Where(tip => ReferenceEquals(PredictorOperationCapture.ReadNullable(tip, "_owner"), heldPowerHolder))
                                .Select(tip => tip.GetNodeOrNull<Control>("textHoverTipContainer"))
                                .Where(tip => tip != null && tip.IsVisibleInTree() && tip.Size.X > 0 && tip.Size.Y > 0).ToArray();
                            check(tips.Length == 1 && view.VisibleRegions.All(region => !region.Intersects(tips[0]!.GetGlobalRect())),
                                "held potion preserves its original native tooltip alongside the candidate row");
                            evidence.Add(new { source = "NPotionHolder", key = "POWER_POTION", rewardScreen = true,
                                width = size.X, groups = Groups(preview).Length, nativeFaces = tiles.Length, scale = view.CardDisplayScale });
                        }, false);
                }
            }
            finally
            {
                await Leave();
                viewport.Size = originalViewportSize; root.GetWindow().Size = originalWindowSize;
                if (heldRewardScreen != null && GodotObject.IsInstanceValid(heldRewardScreen)) overlays.Remove(heldRewardScreen);
                game.MoveChild(overlays, originalOverlayIndex);
                RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet(); await Frames();
            }
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
