using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    private static async Task RunCombatRerollPathChecks(Node root, string output, ModRuntimeSnapshot runtime,
        NGame game, SubViewport viewport, RunState run, Player player, NOverlayStack overlays,
        Func<string> signature, Action<bool, string> check,
        Func<string, Control, Control, string, Func<PredictorOperationScene, bool>?, Func<OperationPreview, bool>,
            Action<NeowOperationOverlay, OperationPreview>?, bool, Task> exercise,
        Action<object, string, object?> set)
    {
        string original = signature();
        var oldRelics = player.Relics.ToArray();
        var oldCards = PredictorOperationCapture.Read<List<CardModel>>(run, "_allCards").ToArray();
        var oldDiscovered = player.DiscoveredCards.ToArray();
        var oldDiscoveredRelics = player.DiscoveredRelics.ToArray();
        var oldRng = player.PlayerRng.ToSerializable(); var oldOdds = player.PlayerOdds.ToSerializable();
        var oldRunRng = run.Rng.ToSerializable();
        var oldBag = player.RelicGrabBag.ToSerializable(); var oldSharedBag = run.SharedRelicGrabBag.ToSerializable();
        var history = PredictorOperationCapture.Read<List<List<MapPointHistoryEntry>>>(run, "_mapPointHistory");
        var oldHistory = history.Select(act => act.ToArray()).ToArray();
        var evidence = new List<object>();
        CombatRoom? room = null; RewardsSet? rewards = null; NRewardsScreen? screen = null;
        NCardRewardSelectionScreen? picker = null; Task<bool>? selecting = null;
        bool passed = false, observedBracket = false, observedArrow = false; string? failure = null;
        async Task Frames(int count = 3)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        async Task Until(Func<bool> ready, string reason)
        { var watch = System.Diagnostics.Stopwatch.StartNew(); while (!ready() && watch.Elapsed.TotalSeconds < 10) await Frames(1); check(ready(), reason); }
        async Task Open(CardReward reward)
        {
            selecting = RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(reward);
            await Until(() => overlays.Peek() is NCardRewardSelectionScreen || selecting.IsCompleted, "combat reroll opens original target picker");
            check(!selecting.IsCompleted && overlays.Peek() is NCardRewardSelectionScreen, "combat target awaits real input");
            picker = (NCardRewardSelectionScreen)overlays.Peek()!; await Frames(30);
        }
        void Alternative(string key)
        {
            var options = PredictorOperationCapture.Read<IReadOnlyList<CardRewardAlternative>>(picker!, "_extraOptions");
            int index = options.ToList().FindIndex(option => option.OptionId.Equals(key, StringComparison.OrdinalIgnoreCase));
            check(index >= 0, "combat picker exposes " + key);
            AccessTools.Method(typeof(NCardRewardSelectionScreen), "OnAlternateRewardSelected").Invoke(picker, [index]);
        }
        async Task Close()
        {
            Alternative("Skip"); await Until(() => selecting!.IsCompleted, "combat picker exits through original Skip");
            check(selecting!.IsCompletedSuccessfully, "combat picker exits without fault"); picker = null; selecting = null;
        }
        try
        {
            check(player.Relics.OfType<Driftwood>().Any() && !player.Relics.OfType<WhiteStar>().Any() &&
                !player.Relics.OfType<LastingCandy>().Any(), "combat reroll fixture adds only its absent White Star and Candy");
            player.AddRelicInternal(ModelDb.Relic<WhiteStar>().ToMutable(), silent: true);
            var candy = (LastingCandy)ModelDb.Relic<LastingCandy>().ToMutable(); player.AddRelicInternal(candy, silent: true);
            foreach (bool active in new[] { false, true })
            {
                candy.CombatRewardsSeen = active ? 1 : 0;
                room = new CombatRoom(ModelDb.AllEncounters.First(encounter => encounter.RoomType == RoomType.Elite).ToMutable(), run);
                run.PushRoom(room); run.AppendToMapPointHistory(MapPointType.Elite, RoomType.Elite, room.Encounter.Id);
                // These are the exact native TheHunt extra-reward producer's
                // arguments. Combat/fatal damage itself is outside this fixture.
                for (int hunt = 0; hunt < 2; hunt++) room.AddExtraReward(player,
                    new CardReward(CardCreationOptions.ForRoom(player, room.RoomType), 3, player));
                rewards = new RewardsSet(player).WithRewardsFromRoom(room); await rewards.GenerateWithoutOffering();
                var rows = rewards.Rewards.OfType<CardReward>().ToArray();
                check(rows.Length == 4 && ReferenceEquals(rewards.Room, room), "native elite set supplies base, two Hunt extras and White Star");
                _ = RunManager.Instance.RewardsSetSynchronizer.BeginRewardsSet(rewards);
                screen = NRewardsScreen.ShowScreen(rewards, false, run); await Frames(30);
                var ids = rows.Select(PredictorLiveRunCapture.Id).ToArray();
                for (int target = 0; target < rows.Length; target++)
                {
                    await Open(rows[target]);
                    var snapshot = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
                    var before = PredictorOperation.Preview(snapshot, ImmutableArray<PredictorInput>.Empty);
                    var nativeBase = before.Rewards.Single(row => row.RerollRecipe?.IsFromCombat == true);
                    var nativeStar = before.Rewards.Single(row => row.RerollRecipe?.Odds == PredictorCardOdds.Boss);
                    string Icon(long id) => id == nativeStar.Id ? "WHITE_STAR" : active && id == nativeBase.Id ? "LASTING_CANDY" : "DRIFTWOOD";
                    string stable = signature();
                    var reference = new List<(int Depth, string Cards, string Path)>();
                    foreach (var prefix in CombatRerollPrefixes(ids.Where(id => id != ids[target]).ToArray()))
                    {
                        ImmutableArray<PredictorInput> inputs = [];
                        if (prefix.Length > 0)
                        {
                            inputs = inputs.Add(new ExitCardSelection());
                            foreach (long id in prefix) inputs = inputs.Add(new EnterCardSelection(id)).Add(new RerollCardReward(id)).Add(new ExitCardSelection());
                            inputs = inputs.Add(new EnterCardSelection(ids[target]));
                        }
                        inputs = inputs.Add(new RerollCardReward(ids[target]));
                        var result = PredictorOperation.Preview(snapshot, inputs);
                        check(result.Disposition == PredictorInputResult.Accepted, "independent explicit prefix is accepted");
                        reference.Add((prefix.Length + 1, CombatRerollCards(result.Rewards.Single(row => row.Id == ids[target]).Cards),
                            string.Join(">", prefix.Append(ids[target]).Select(Icon))));
                    }
                    var preview = SurfaceOperationPreview.BuildRewardAlternative(snapshot, ids[target], "REROLL", null);
                    CheckCombatRerollReference(preview, ids[target], Icon(ids[target]), reference, check);
                    if (active && target == 1)
                    {
                        var scene = (PredictorOperationRewardsScene)snapshot.Scene;
                        var missing = snapshot with { Scene = scene with { Rewards = scene.Rewards.Select(row => row.Id == nativeStar.Id ? row with { RerollRecipe = null } : row).ToImmutableArray() } };
                        var unavailable = SurfaceOperationPreview.BuildRewardAlternative(missing, ids[target], "REROLL", null);
                        check(unavailable.Faults.Any(fault => fault.Exception.Message == "PredictorCardRewardRerollRecipeMissing") &&
                            RerollGroups(unavailable).Length > 0, "a missing sibling recipe preserves useful target results and original failure cause");
                        foreach (int used in new[] { 0, 2 })
                        {
                            var crucible = new PredictorRelic(snapshot.State.NextInstanceId, new("RELIC", "SILVER_CRUCIBLE"), snapshot.State.Position.Floor) { RewardUpgradeUses = used };
                            var variant = snapshot with { State = snapshot.State with { NextInstanceId = snapshot.State.NextInstanceId + 1, Relics = snapshot.State.Relics.Add(crucible) } };
                            var variantPreview = SurfaceOperationPreview.BuildRewardAlternative(variant, ids[target], "REROLL", null);
                            CheckCombatRerollReference(variantPreview, ids[target], Icon(ids[target]), CombatRerollReference(variant, ids, ids[target], Icon, check), check);
                        }
                        var noPowers = snapshot.Context.Catalog!.CharacterCards.Where(card => card.CardType != RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Power).Select(card => card.CardKey).ToImmutableArray();
                        var noPowerSnapshot = snapshot with { Scene = scene with { Rewards = scene.Rewards.Select(row => row.RerollRecipe == null ? row : row with { RerollRecipe = row.RerollRecipe with { CardPoolOverride = noPowers } }).ToImmutableArray() } };
                        string NoPowerIcon(long id) => id == nativeStar.Id ? "WHITE_STAR" : "DRIFTWOOD";
                        CheckCombatRerollReference(SurfaceOperationPreview.BuildRewardAlternative(noPowerSnapshot, ids[target], "REROLL", null), ids[target], "DRIFTWOOD",
                            CombatRerollReference(noPowerSnapshot, ids, ids[target], NoPowerIcon, check), check);
                        var boss = snapshot with { Scene = scene with { CombatKind = PredictorCombatKind.Boss, Rewards = scene.Rewards.Select(row => row.RerollRecipe == null ? row : row with { RerollRecipe = row.RerollRecipe with { Odds = PredictorCardOdds.Boss } }).ToImmutableArray() },
                            State = snapshot.State with { Relics = snapshot.State.Relics.Where(relic => relic.Key.Entry != "LASTING_CANDY").ToImmutableArray() } };
                        CheckCombatRerollReference(SurfaceOperationPreview.BuildRewardAlternative(boss, ids[target], "REROLL", null), ids[target], "DRIFTWOOD",
                            CombatRerollReference(boss, ids, ids[target], _ => "DRIFTWOOD", check), check);
                        var ambiguous = snapshot with { Scene = scene with { Rewards = scene.Rewards.Select(row => row.Id == ids[2] ? row with { RerollRecipe = row.RerollRecipe! with { CardPoolOverride = noPowers } } : row).ToImmutableArray() } };
                        var ambiguousPreview = SurfaceOperationPreview.BuildRewardAlternative(ambiguous, ids[target], "REROLL", null);
                        check(ambiguousPreview.Faults.IsEmpty && RerollGroups(ambiguousPreview).Length == 1 && ambiguousPreview.Notices.Contains("RewardRerollCombatRecipes"), "same icon with different actual pool keeps a qualified direct result rather than ambiguous routes");
                        var fallbackRows = Enumerable.Range(0, 10).Select(index => before.Rewards.Single(row => row.Id == ids[target]) with {
                            Id = index == 0 ? ids[target] : snapshot.State.NextInstanceId + 100 + index, CardSelectionEntered = index == 0,
                            RerollRecipe = before.Rewards.Single(row => row.Id == ids[target]).RerollRecipe! with { NoUpgradeRoll = true } }).ToImmutableArray();
                        var fallback = snapshot with { Scene = scene with { Rewards = fallbackRows }, State = snapshot.State with { NextInstanceId = snapshot.State.NextInstanceId + 200 } };
                        check(RerollGroups(SurfaceOperationPreview.BuildRewardAlternative(fallback, ids[target], "REROLL", null)).Length == 8, "unclassified combat recipes also obey the eight-group display ceiling");
                    }
                    var options = PredictorOperationCapture.Read<IReadOnlyList<CardRewardAlternative>>(picker!, "_extraOptions");
                    int index = options.ToList().FindIndex(option => option.OptionId == "REROLL");
                    var button = Descendants(picker!).OfType<NCardRewardAlternativeButton>().ElementAt(index);
                    string name = "combat-reroll-" + (active ? "candy" : "plain") + "-target-" + target;
                    await exercise(name, button, button, "DRIFTWOOD", scene => scene is PredictorOperationRewardsScene,
                        hovered => RerollGroups(hovered).Length == RerollGroups(preview).Length,
                        (view, hovered) =>
                        {
                            CheckCombatRerollReference(hovered, ids[target], Icon(ids[target]), reference, check);
                            observedBracket |= Descendants(view).OfType<Label>().Any(node => node.Text == "[");
                            observedArrow |= Descendants(view).OfType<Label>().Any(node => node.Text == "→");
                            CheckOperationIconPaths(view, hovered, check);
                        }, false);
                    if (active && target == 1)
                    {
                        var oldSize = viewport.Size; var oldWindow = root.GetWindow().Size;
                        try
                        {
                            viewport.Size = new(1280, 720); root.GetWindow().Size = viewport.Size; await Frames(8);
                            await exercise(name + "-1280", button, button, "DRIFTWOOD", scene => scene is PredictorOperationRewardsScene,
                                hovered => RerollGroups(hovered).Length == RerollGroups(preview).Length,
                                (view, hovered) => check(Descendants(view).OfType<Control>().Where(node => (node.Name.ToString().StartsWith("Rt2Icon", StringComparison.Ordinal) || node.Name.ToString().StartsWith("Rt2Path", StringComparison.Ordinal)))
                                    .All(node => viewport.GetVisibleRect().Grow(.2f).Encloses(node.GetGlobalRect())), "1280 native render keeps all path icons and symbols in viewport"), false);
                        }
                        finally { viewport.Size = oldSize; root.GetWindow().Size = oldWindow; await Frames(8); }
                    }
                    check(signature() == stable, "all explicit references and real hover preserve live state and RNG");
                    evidence.Add(new { active, target, targetId = ids[target], reference = reference.Select(row => new { row.Depth, row.Cards, row.Path }),
                        groups = RerollGroups(preview).Select(group => new { group.Id, Cards = CombatRerollCards(group.Cards), group.Label, group.IconPaths }) });
                    await Close();
                }
                // Real callback sequence crosses all three rules and checks the
                // independent numerical runtime, complete RNG streams and odds.
                await Open(rows[0]);
                var nativeSnapshot = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
                await Close();
                ImmutableArray<PredictorInput> nativeInputs = [new ExitCardSelection()];
                foreach (int index in new[] { 3, 1, 0, 2 })
                {
                    await Open(rows[index]); nativeInputs = nativeInputs.Add(new EnterCardSelection(ids[index])).Add(new RerollCardReward(ids[index]));
                    var expected = PredictorOperation.Preview(nativeSnapshot, nativeInputs);
                    Alternative("REROLL"); await Until(() => !rows[index].CanReroll, "original combat reroll callback completes");
                    var actual = rows[index].Cards.Select(card => PredictorLiveRunCapture.Card(card, nativeSnapshot.Context.Catalog!));
                    var after = PredictorOperationCapture.Capture(run, runtime.Detection.NormalizedVersion, game);
                    check(expected.Disposition == PredictorInputResult.Accepted && SameCards(actual, expected.Rewards.Single(row => row.Id == ids[index]).Cards) &&
                        after.State.Streams.SequenceEqual(expected.State.Streams) && after.State.Odds == expected.State.Odds,
                        "real mixed-rule reroll matches ordered cards, all RNG cursors and rarity odds");
                    await Close(); nativeInputs = nativeInputs.Add(new ExitCardSelection());
                }
                RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet(); overlays.Remove(screen); screen = null; rewards = null;
                run.PopCurrentRoom(); room = null;
            }
            check(observedBracket && observedArrow, "actual native hover visibly exercises both exchangeable brackets and ordered arrows");
            passed = true;
        }
        catch (Exception error) { failure = error.ToString(); throw; }
        finally
        {
            if (picker != null && selecting is { IsCompleted: false })
            { PredictorOperationCapture.Read<TaskCompletionSource<int?>>(picker, "_completionSource").TrySetResult(null); await Until(() => selecting.IsCompleted, "combat fixture unwinds picker"); }
            if (rewards != null && !RunManager.Instance.RewardsSetSynchronizer.IsRewardsSetCompleted(rewards)) RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
            if (screen != null && GodotObject.IsInstanceValid(screen) && screen.GetParent() != null) overlays.Remove(screen);
            if (room != null && ReferenceEquals(run.CurrentRoom, room)) run.PopCurrentRoom();
            foreach (var relic in player.Relics.Where(relic => !oldRelics.Contains(relic)).ToArray()) player.RemoveRelicInternal(relic, silent: true);
            foreach (var card in PredictorOperationCapture.Read<List<CardModel>>(run, "_allCards").Where(card => !oldCards.Contains(card)).ToArray()) run.RemoveCard(card);
            set(player, nameof(Player.DiscoveredCards), oldDiscovered.ToList()); set(player, nameof(Player.DiscoveredRelics), oldDiscoveredRelics.ToList());
            player.PlayerRng.LoadFromSerializable(oldRng); player.PlayerOdds.LoadFromSerializable(oldOdds); run.Rng.LoadFromSerializable(oldRunRng);
            player.RelicGrabBag.LoadFromSerializable(oldBag); run.SharedRelicGrabBag.LoadFromSerializable(oldSharedBag);
            history.Clear(); foreach (var act in oldHistory) history.Add(act.ToList());
            bool restored = signature() == original;
            File.WriteAllText(Path.Combine(output, "combat-reroll-paths-result.json"), JsonSerializer.Serialize(new { passed = passed && restored, restored, observedBracket, observedArrow, failure, evidence,
                source = "Controlled native Elite RewardsSet.WithRewardsFromRoom; exact TheHunt AddExtraReward producer arguments twice, native WhiteStar modifier and Candy parity. All 16 explicit prefixes per hovered target; original native REROLL callbacks in one mixed order per Candy parity. No played combat/fatal or natural-seed claim." }, new JsonSerializerOptions { WriteIndented = true }));
            if (passed) check(restored, "combat reroll fixture restores original player/run/RNG/odds/bags/history");
        }
    }

    private static string CombatRerollCards(IEnumerable<PredictorCard> cards) => JsonSerializer.Serialize(cards.Select(card => card with { Id = 0, FloorAdded = 0 }));
    private static List<(int Depth, string Cards, string Path)> CombatRerollReference(PredictorOperationSnapshot snapshot,
        long[] ids, long target, Func<long, string> icon, Action<bool, string> check)
    {
        var reference = new List<(int, string, string)>();
        foreach (var prefix in CombatRerollPrefixes(ids.Where(id => id != target).ToArray()))
        {
            ImmutableArray<PredictorInput> inputs = [];
            if (prefix.Length > 0)
            {
                inputs = inputs.Add(new ExitCardSelection());
                foreach (long id in prefix) inputs = inputs.Add(new EnterCardSelection(id)).Add(new RerollCardReward(id)).Add(new ExitCardSelection());
                inputs = inputs.Add(new EnterCardSelection(target));
            }
            var result = PredictorOperation.Preview(snapshot, inputs.Add(new RerollCardReward(target)));
            check(result.Disposition == PredictorInputResult.Accepted, "detached edge-case reference accepts each explicit path");
            reference.Add((prefix.Length + 1, CombatRerollCards(result.Rewards.Single(row => row.Id == target).Cards), string.Join(">", prefix.Append(target).Select(icon))));
        }
        return reference;
    }
    private static IEnumerable<long[]> CombatRerollPrefixes(long[] ids)
    {
        yield return [];
        foreach (long id in ids)
            foreach (var tail in CombatRerollPrefixes(ids.Where(other => other != id).ToArray())) yield return new[] { id }.Concat(tail).ToArray();
    }
    private static IEnumerable<string[]> CombatRerollPermutations(string[] icons)
    {
        if (icons.Length == 0) { yield return []; yield break; }
        for (int i = 0; i < icons.Length; i++)
            foreach (var tail in CombatRerollPermutations(icons.Where((_, index) => index != i).ToArray())) yield return new[] { icons[i] }.Concat(tail).ToArray();
    }
    private static void CheckCombatRerollReference(OperationPreview preview, long target, string targetIcon,
        List<(int Depth, string Cards, string Path)> reference, Action<bool, string> check)
    {
        var groups = RerollGroups(preview);
        check(preview.Faults.IsEmpty && preview.PendingReasons.IsEmpty && groups.Length is > 0 and <= 8, "combat forecast obeys eight merged groups without missing input/failure");
        int expectedCount = 0, maxDepth = 0;
        foreach (var layer in reference.GroupBy(row => row.Depth).OrderBy(layer => layer.Key))
        { int count = layer.Select(row => row.Cards).Distinct().Count(); if (expectedCount + count > 8) break; expectedCount += count; maxDepth = layer.Key; }
        check(groups.Length == expectedCount, "forecast includes complete shortest depths before the eight-group boundary");
        var covered = new HashSet<(string Cards, string Path)>();
        foreach (var group in groups)
        {
            check(group.Id == target && !group.IconPaths.IsEmpty, "every result group belongs to the hovered reward and has a real path");
            string cards = CombatRerollCards(group.Cards);
            foreach (var path in group.IconPaths)
            {
                check(path.Segments.Length > 0 && path.Segments[^1].Length == 1 && path.Segments[^1][0].Entry == targetIcon, "target remains an unbracketed final step");
                IEnumerable<string[]> expanded = [Array.Empty<string>()];
                foreach (var segment in path.Segments)
                    expanded = expanded.SelectMany(prefix => CombatRerollPermutations(segment.Select(icon => icon.Entry).ToArray()).Select(order => prefix.Concat(order).ToArray())).ToArray();
                foreach (var order in expanded)
                {
                    string key = string.Join(">", order);
                    check(reference.Any(row => row.Depth == order.Length && row.Cards == cards && row.Path == key), "every bracket permutation is a real explicit path to exactly this complete ordered card face");
                    covered.Add((cards, key));
                }
            }
        }
        check(reference.Where(row => row.Depth <= maxDepth).All(row => covered.Contains((row.Cards, row.Path))), "merged path rows retain every distinct reachable icon order at each displayed depth");
    }
}
