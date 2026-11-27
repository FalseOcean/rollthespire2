using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Test;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Infrastructure.Snapshots;

public partial class Runner : Node
{
    private sealed record Sample(string seed, int actionSeed);
    private sealed record AncientReplay(string NodeId, string Relic);
    public override void _Ready() => Callable.From(RunChecks).CallDeferred();

    private async void RunChecks()
    {
        int code = 0;
        try
        {
            string[] args = OS.GetCmdlineUserArgs();
            string Arg(string name) => args[Array.IndexOf(args, name) + 1];
            var replay = args.Contains("--ancient-node") ? new AncientReplay(Arg("--ancient-node"), Arg("--ancient-relic")) : null;
            string output = Arg("--output"), pck = Arg("--game-pck");
            Directory.CreateDirectory(output);
            string gameRoot = Path.GetDirectoryName(pck)!;
            string info = Path.Combine(gameRoot, "release_info.json");
            using var version = JsonDocument.Parse(File.ReadAllText(info));
            static string NormalizeVersion(string value) => value.Trim().TrimStart('v','V');
            string actualVersion = NormalizeVersion(version.RootElement.GetProperty("version").GetString()!);
            string expectedVersion = NormalizeVersion(Arg("--game-version"));
            if (!Version.TryParse(actualVersion, out _) || actualVersion != expectedVersion)
                throw new InvalidOperationException($"Expected native {expectedVersion} resources, found {actualVersion}");
            string gameAssembly = Path.Combine(gameRoot, "data_sts2_windows_x86_64", "sts2.dll");
            static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
            static Guid Mvid(string path)
            {
                using var input = File.OpenRead(path);
                using var pe = new PEReader(input);
                var metadata = pe.GetMetadataReader();
                return metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
            }
            if (Mvid(gameAssembly) != typeof(ModelDb).Module.ModuleVersionId) throw new InvalidOperationException("Loaded sts2.dll differs from the PCK installation");
            if (!ProjectSettings.LoadResourcePack(pck, false)) throw new InvalidOperationException("Cannot mount native PCK");
            Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(typeof(ModelDb).Assembly);
            TestMode.IsOn = true;
            typeof(ModManager).GetProperty("State")!.SetValue(null, ModManagerState.Initialized);
            ModelDb.Init();
            MegaCrit.Sts2.Core.Modding.AssemblyInfo.Init();
            ModelIdSerializationCache.Init();
            ModelDb.InitIds();
            var save = new SaveManager(new MockGodotFileIo("user://drawing-journey"), true);
            SaveManager.MockInstanceForTesting(save);
            save.InitSettingsDataForTest();
            save.InitPrefsDataForTest();
            save.InitProfileId(1);
            save.InitProgressData();
            save.SettingsSave.Language = "eng";
            LocManager.Initialize();
            RuntimeSnapshotThreadGuard.BindCurrentThread();
            TestMode.IsOn = false;
            if (args.Contains("--node-hover-ui"))
            {
                await NodeHoverChecks.CheckMapHoverStart(this, output);
                GetTree().Quit();
                return;
            }
            var samples = JsonSerializer.Deserialize<Sample[]>(File.ReadAllText(Arg("--seeds")))!;
            if (samples.Length != 100 || samples.DistinctBy(x => x.seed).Count() != 100)
                throw new InvalidDataException("Expected the original 100 unique seeds");
            File.WriteAllText(Path.Combine(output, "environment.json"), JsonSerializer.Serialize(new {
                release = version.RootElement, expectedVersion, actualVersion, pck, gameSha256 = Hash(gameAssembly), loadedProductMvid = typeof(PredictorRun).Module.ModuleVersionId,
                seedsSha256 = Hash(Arg("--seeds")), userDirectory = OS.GetUserDataDir(), account = "all vanilla content",
                character = "IRONCLAD", ascension = 0, combat = "conditional on winning each combat", policy = "LiveMapDefaults"
            }));
            var profile = RuntimeProfileRegistry.Select(GameVersionDetector.DetectFromCandidates([info]));
            var character = CharacterIdentity.FromKey(new("CHARACTER", "IRONCLAD"));
            var unlocks = UnlockState.all;
            var discovered = ModelDb.Acts.Select(a => new ModelKey(a.Id.Category, a.Id.Entry)).ToImmutableArray();
            foreach (int ordinal in Arg("--ordinals").Split(',').Select(int.Parse))
            {
                var sample = samples[ordinal];
                bool minimal = ordinal < 50;
                var log = new List<object>();
                var timer = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var authority = RuntimeContextAuthorityCapture.CaptureRuntimeReadOnly(profile, sample.seed, character, 0, actualVersion,
                        predictionGameMode: WorldGameMode.Singleplayer, predictionGameModeAuthority: PredictionGameModeAuthority.ExplicitRequest,
                        explicitUnlockState: unlocks, explicitDiscoveredActs: discovered);
                    var run = PredictorSeedCapture.CreateFromSeedContext(sample.seed, authority, unlocks.ToSerializable());
                    if (!Journey(run, new Random(sample.actionSeed), log, minimal, ordinal == 50, Path.Combine(output,"progress.log"), replay)) code = 2;
                }
                catch (Exception ex) { log.Add(new { stage = "exception", error = ex.ToString() }); code = 2; }
                File.WriteAllText(Path.Combine(output, sample.seed + ".json"), JsonSerializer.Serialize(new { ordinal, sample, minimal, elapsedMs=timer.ElapsedMilliseconds, log }));
                GD.Print($"drawing sample={sample.seed} records={log.Count} final={log.LastOrDefault()?.GetType().Name}");
            }
        }
        catch (Exception error) { GD.PrintErr(error); code = 1; }
        GetTree().Quit(code);
    }

    private static bool Journey(PredictorRun run, Random random, List<object> log, bool minimal, bool checkUndo, string progressLog, AncientReplay? replay)
    {
        for (int act = 1; act <= 3; act++)
        {
            var map = run.RouteMap(act);
            var draft = new PredictorMapRouteDraft(map);
            var point = map.Points.Single(p => p.Position.Row == 0);
            var path = ImmutableArray.CreateBuilder<MapPosition>();
            path.Add(point.Position);
            while (point.Children.Count > 0)
            {
                var next = point.Children[random.Next(point.Children.Count)];
                path.Add(next);
                point = map.Points.Single(p => p.Position == next);
            }
            var route = path.ToImmutable();
            if (draft.ApplyStroke(route, PredictorMapStrokeMode.Draw) != PredictorMapDraftEditResult.Changed)
                throw new InvalidOperationException("Drawn path refused");
            int erased = random.Next(1, route.Length);
            if (draft.ApplyStroke([route[erased]], PredictorMapStrokeMode.Erase) != PredictorMapDraftEditResult.Changed ||
                !draft.Undo() || !draft.ReadyPrefix.SequenceEqual(route)) throw new InvalidOperationException("Drawing erase/undo failed");
            run.SetRoute(act, draft.ReadyPrefix);
            log.Add(new { stage = "draw-erase-undo", act, map = map.Act.Entry, route, erased = route[erased] });
        }
        var plans = ImmutableDictionary<string,PredictorPlannedNode>.Empty;
        var observedEdits = new HashSet<string>();
        var supplements = new HashSet<string>();
        PredictorRouteContinuation Preview(ImmutableDictionary<string,PredictorPlannedNode> current) =>
            run.PreviewRouteContinuation(current.Values.OrderBy(p => p.NodeId).ToImmutableArray(), 64,
                scope: PredictorRoutePreviewScope.AllModeledNodes, policy: PredictorRoutePreviewPolicy.LiveMapDefaults,
                allowSelectedPrefixes: true, isolateNodeFailures: true);
        bool first = true;
        for (int iteration = 0; ; iteration++)
        {
            if (iteration % 10 == 0)
                File.AppendAllText(progressLog, $"{run.Context.Seed} preview={iteration} last-action={JsonSerializer.Serialize(log.Last())}{System.Environment.NewLine}");
            if (iteration == 240)
                throw new InvalidOperationException("Drawing journey exceeded 240 plan revisions; examine progress.log and the recorded actions");
            var forecast = Preview(plans);
            var last = forecast.Nodes.LastOrDefault();
            if (first)
            {
                log.Add(new { stage = "drawing-only", nodes = forecast.Nodes.Length, last = last?.Position, phase = last?.Phase.ToString(),
                    offers = last?.Ancient?.Offers.Select(o => o.Value?.Relic.Entry), causes = last?.PendingCauses.Select(c => c.Code) });
                first = false;
            }
            // Author chronological choices, including ones whose untouched
            // default allowed a suffix preview, using the displayed offer.
            var editable = forecast.Nodes.FirstOrDefault(node => !observedEdits.Contains(node.Position.NodeId) &&
                (node.Event?.Occurrence is { Candidates.Length: > 1 } || node.Event is { Event: not null, EntryOptions.Length: > 0 } || !minimal &&
                (node.Shop != null || !node.RestOffers.IsEmpty || node.CombatRequirements is { StolenCards: true } ||
                 node.CombatRequirements is { GremlinGold: true } ||
                 node.Rewards.Any(r => r.Kind == PredictorRewardKind.Card && r.OfferBinding != null))));
            if (editable != null)
            {
                observedEdits.Add(editable.Position.NodeId);
                var plan = plans.GetValueOrDefault(editable.Position.NodeId) ?? new(editable.Position.NodeId);
                if (editable.Shop is {} shop)
                {
                    var slots = editable.PotionBoundaries.FirstOrDefault(p=>p.Stage == PredictorRoutePotionStage.BeforeChoices)?.Before.PotionSlots;
                    var offers = shop.Offers.Where(o => o.IsKnown && o.Value!.Stocked && o.Affordable == true && o.Kind != PredictorShopEntryKind.CardRemoval &&
                        (o.Kind != PredictorShopEntryKind.Potion || slots?.Any(s=>s.IsKnown && s.Value == null) == true)).ToArray();
                    int index = random.Next(offers.Length + 1) - 1;
                    var actions = index < 0 ? ImmutableArray<PredictorPlannedShopAction>.Empty :
                        [new(PredictorShopActionKind.Buy) { ExpectedOffer = offers[index].Value }];
                    plans = plans.SetItem(plan.NodeId, plan with { Shop = new() { Actions = actions } });
                    log.Add(new { stage = "shop-choice", node = plan.NodeId, buy = index < 0 ? null : offers[index].Value!.Key?.Entry });
                }
                else if (!editable.RestOffers.IsEmpty)
                {
                    var smith = editable.RestCards.Where(c => c.IsKnown && c.Value.UpgradeLevel < c.Value.MaxUpgradeLevel).ToArray();
                    var offers = editable.RestOffers.Where(o => o.Enabled && (o.Action is PredictorRestAction.Heal or PredictorRestAction.Dig or PredictorRestAction.Lift ||
                        o.Action == PredictorRestAction.Smith && smith.Length > 0)).ToArray();
                    if (offers.Length > 0)
                    {
                        var action = new PredictorPlannedRestAction(offers[random.Next(offers.Length)].Action);
                        if (action.Action == PredictorRestAction.Smith) action = action with { SmithCard = Target(smith[random.Next(smith.Length)].Value) };
                        plans = plans.SetItem(plan.NodeId, plan with { RestActions = [action] });
                        log.Add(new { stage = "rest-choice", node = plan.NodeId, action = action.Action.ToString(), target = action.SmithCard?.Key.Entry });
                    }
                }
                else if (editable.Event is { Occurrence: { Candidates.Length: > 1 } occurrence } && plan.Event?.Occurrence == null)
                {
                    var alternatives = occurrence.Candidates.Where(c => !c.ExcludedByPlayer).ToArray();
                    var picked = alternatives[random.Next(alternatives.Length)];
                    var input = new ChooseEventOccurrence(picked.Event,picked.QueueEvent,picked.RawOrdinal,occurrence.Cursor,picked.Cursor);
                    plans = plans.SetItem(plan.NodeId,plan with { Event = new(picked.Event) { Occurrence = input } });
                    observedEdits.Remove(plan.NodeId);
                    log.Add(new { stage = "event-occurrence-premise", node = plan.NodeId, input, alternatives = alternatives.Select(c => c.Event.Entry) });
                }
                else if (editable.Event is { Event: {} eventKey } eventNode)
                {
                    var inventory = editable.PotionBoundaries.FirstOrDefault(p => p.Stage == PredictorRoutePotionStage.BeforeChoices)?.Before;
                    if (!minimal && eventKey.Entry == "ENDLESS_CONVEYOR" && eventNode.ConveyorInput == null &&
                        plan.Event?.Actions.Any(a=>a.Action==PredictorEventActionKind.ConveyorInputs) != true &&
                        inventory is { PotionPendingCauses.IsEmpty:true } && inventory.PotionSlots.All(slot => slot.IsKnown) &&
                        eventNode.NeededInput == PredictorEventNeededInput.Health)
                    {
                        var fact = new PredictorConveyorInput(inventory.PotionSlots.Count(slot => slot.Value == null), 0);
                        var eventPlan = plan.Event ?? new(eventKey);
                        plans = plans.SetItem(plan.NodeId, plan with { Event = eventPlan with
                            { Actions = eventPlan.Actions.Add(new(PredictorEventActionKind.ConveyorInputs) { Conveyor = fact }) } });
                        observedEdits.Remove(plan.NodeId);
                        log.Add(new { stage = "amended-local-premise", node = plan.NodeId, premise = "enter conveyor at full health", fact });
                        continue;
                    }
                    var available = eventNode.EntryOptions.Where(o => o.Enabled == true && o.Value != null).ToArray();
                    if (available.Length > 0)
                    {
                        var chosen = available[random.Next(available.Length)].Value!;
                        plans = plans.SetItem(plan.NodeId, plan with { Event = (plan.Event ?? new(eventKey)) with { Actions = [new(PredictorEventActionKind.Option){ ExpectedOption = chosen }] } });
                        log.Add(new { stage = "event-choice", node = plan.NodeId, eventKey = eventKey.Entry, option = chosen.Key });
                    }
                }
                else if (editable.Event == null)
                {
                    if (editable.CombatRequirements is {} combat && (combat.StolenCards || combat.GremlinGold))
                    {
                        // These describe the player's hypothetical strategy on that encounter.
                        plan = plan with { Combat = new() {
                            StolenCards = combat.StolenCards ? new([]) : null,
                            GremlinGold = combat.GremlinGold ? new(false,false,null) : null } };
                        log.Add(new { stage = "combat-choice", node = plan.NodeId, plan.Combat.StolenCards, plan.Combat.GremlinGold });
                    }
                    var choices = ImmutableArray.CreateBuilder<PredictorPlannedRewardChoice>();
                    foreach (var reward in editable.Rewards)
                    {
                        if (reward.Kind == PredictorRewardKind.Card && reward.OfferBinding is {} binding)
                        {
                            int index = random.Next(binding.Value.Cards.Length + 1) - 1;
                            choices.Add(new(binding.Value, reward.Occurrence, index < 0 ? null : index, Skip: index < 0)
                                { UnresolvedEnchantmentIndices = binding.UnresolvedEnchantmentIndices });
                            log.Add(new { stage = "reward-choice", node = plan.NodeId, index, card = index < 0 ? null : binding.Value.Cards[index].Key.Entry });
                        }
                        if (reward is { Kind: PredictorRewardKind.Potion, IsKnown: true } && reward.Value!.CanSkip)
                        {
                            var slots = editable.PotionBoundaries.FirstOrDefault(p => p.Stage == PredictorRoutePotionStage.BeforeChoices)?.Before.PotionSlots;
                            bool take = slots?.Any(s => s.IsKnown && s.Value == null) == true && random.Next(2) == 0;
                            choices.Add(new(reward.Value!, reward.Occurrence, Skip: !take));
                            log.Add(new { stage = "potion-choice", node = plan.NodeId, potion = reward.Value!.Key.Entry, take });
                        }
                    }
                    var updated = plans.SetItem(plan.NodeId, plan with { RewardChoices = choices.ToImmutable() });
                    if (checkUndo && choices.Count > 0 && forecast.Nodes.IndexOf(editable) < forecast.Nodes.Length-1)
                    {
                        var selected = choices.FirstOrDefault(c => c.ExpectedReward.Kind == PredictorRewardKind.Card);
                        if (selected == null) { plans = updated; continue; }
                        var probe = plan with { RewardChoices = [selected with { CardIndex = 0, Skip = false }] };
                        var edited = Preview(plans.SetItem(plan.NodeId,probe));
                        var editedNode = edited.Nodes.Single(n=>n.Position.NodeId==plan.NodeId);
                        var cleared = Preview(plans);
                        string stamp = JsonSerializer.Serialize(forecast.Nodes.Select(Receipt));
                        if (forecast.Fault != null || cleared.Fault != null || edited.Fault != null ||
                            !editedNode.Rewards.Any(r=>r.AppliedChoice?.CardIndex == 0 && r.AppliedChoice.Skip == false) ||
                            stamp != JsonSerializer.Serialize(cleared.Nodes.Select(Receipt)) ||
                            forecast.TerminalBoundary?.TargetPosition!=cleared.TerminalBoundary?.TargetPosition)
                            throw new InvalidOperationException("Route and rewards not restored after clearing edit");
                        log.Add(new { stage = "reward-edit-clear-recompute", node=plan.NodeId, downstreamNodes=forecast.Nodes.Length-1-forecast.Nodes.IndexOf(editable),
                            originalSha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(stamp))), restored = true,
                            edited = editedNode.Rewards.Select(r => new { kind = r.Kind.ToString(), chosen = r.AppliedChoice?.CardIndex, skipped = r.AppliedChoice?.Skip }) });
                        checkUndo = false;
                    }
                    plans = updated;
                }
                continue;
            }
            if (forecast.Fault == null && last != null && !last.Advanced && TryChoose(last, plans.GetValueOrDefault(last.Position.NodeId), random, replay, out var choice))
            {
                plans = plans.SetItem(choice.NodeId, choice);
                log.Add(new { stage = "action", node = choice.NodeId, ancient = choice.Ancient?.ExpectedOffer.Relic.Entry,
                    conditional = choice.Ancient?.AssumeProjectedOffer, children = choice.Ancient?.Children.Select(c=> new { kind=c.Action.ToString(), source=c.ExpectedSource.Entry }),
                    eventActions = choice.Event?.Actions.Select(a => new { kind = a.Action.ToString(), key = a.ExpectedOption?.Key, a.ConfirmAbandon }) });
                continue;
            }
            if (forecast.TerminalBoundary is { RedrawMap: {} replacement } boundary &&
                supplements.Add(boundary.DeparturePosition.NodeId + ".map." + replacement.Act))
            {
                var route = replacement.Visited.ToBuilder();
                var point = route.Count == 0 ? replacement.Map.Points.Single(p=>p.Position.Row==0) :
                    replacement.Map.Points.Single(p=>p.Position==route[^1]);
                if (route.Count==0) route.Add(point.Position);
                while (point.Children.Count>0)
                {
                    var next = point.Children[random.Next(point.Children.Count)];
                    point = replacement.Map.Points.Single(p=>p.Position==next);
                    route.Add(next);
                }
                var owner = plans.GetValueOrDefault(boundary.DeparturePosition.NodeId) ?? new(boundary.DeparturePosition.NodeId);
                var redrawn = new PredictorRoute(replacement.Act,route.ToImmutable());
                plans = plans.SetItem(owner.NodeId,owner with {RedrawnRoute = redrawn});
                log.Add(new {stage="redraw-changed-map", node=owner.NodeId, replacement.Act, replacement.Visited,
                    redrawn.Points, causes=boundary.PendingCauses.Select(c=>c.Code)});
                continue;
            }
            if (last?.Event is { Occurrence: {} revised } &&
                plans.GetValueOrDefault(last.Position.NodeId) is { Event.Occurrence: {} invalidated } previous &&
                last.PendingCauses.Any(c=>c.NodeId == last.Position.NodeId && c.Code == "PlannedEventOccurrenceChanged"))
            {
                var candidates = revised.Candidates.Where(c=>!c.ExcludedByPlayer).ToArray();
                string revision = string.Join("|", candidates.Select(c=>c.Event.Entry + "-" + c.Cursor));
                if (candidates.Length>0 && supplements.Add(last.Position.NodeId + ".occurrence." + revision))
                {
                    var picked = candidates[random.Next(candidates.Length)];
                    var input = new ChooseEventOccurrence(picked.Event,picked.QueueEvent,picked.RawOrdinal,revised.Cursor,picked.Cursor);
                    plans = plans.SetItem(last.Position.NodeId,previous with {Event=new(picked.Event){Occurrence=input}});
                    observedEdits.Remove(last.Position.NodeId);
                    log.Add(new {stage="player-replaced-invalid-occurrence",node=last.Position.NodeId,old=invalidated,
                        reason="PlannedEventOccurrenceChanged",candidates=candidates.Select(c=>new{c.Event,c.Cursor,c.RawOrdinal}),selected=input});
                    continue;
                }
            }
            if (last?.Event is {Event:{} now} actual &&
                plans.GetValueOrDefault(last.Position.NodeId) is {Event:{} oldEvent} stale &&
                oldEvent.ExpectedEvent!=now && last.PendingCauses.Any(c=>c.NodeId==last.Position.NodeId && c.Code=="PlannedEventChanged") &&
                supplements.Add(last.Position.NodeId+".new-event."+now.Serialized))
            {
                var updated = new PredictorPlannedEvent(now);
                if (actual.Occurrence is {} observed && actual.SelectedOccurrence is {} selected)
                    updated=updated with {Occurrence=new(selected.Event,selected.QueueEvent,selected.RawOrdinal,observed.Cursor,selected.Cursor)};
                plans=plans.SetItem(stale.NodeId,stale with {Event=updated});
                observedEdits.Remove(stale.NodeId);
                log.Add(new {stage="player-replaced-invalid-event",node=stale.NodeId,reason="PlannedEventChanged",
                    previous=oldEvent.ExpectedEvent.Entry,previousOptions=oldEvent.Actions.Select(a=>a.ExpectedOption?.Key),
                    revised=now.Entry,options=actual.Options.Select(o=>o.Key)});
                continue;
            }
            if (forecast.Fault == null && (last?.Advanced == false || forecast.TerminalBoundary != null || !HasBossCardOffers(forecast)) &&
                TrySupplement(forecast,plans,supplements,random,log,out var correction))
            {
                plans = plans.SetItem(correction.NodeId,correction);
                continue;
            }
            foreach (var node in forecast.Nodes) log.Add(Receipt(node));
            bool complete = last is { Position.Act: 3, Room: PredictorRoomKind.Boss or PredictorRoomKind.Victory, Advanced: true } &&
                forecast.Fault == null && forecast.TerminalBoundary == null;
            var bossCards = forecast.Nodes.Where(n=> n.Room == PredictorRoomKind.Boss).Select(n => new { act = n.Position.Act, boss = n.Model?.Entry,
                groups = n.Rewards.Where(r=>r.Kind == PredictorRewardKind.Card).Select(r=>r.Value?.Cards.Select(c=>c.Key.Entry).ToArray() ??
                    r.CardOptions.Select(c=>c.Value.Key.Entry).ToArray()).ToArray(),
                rarities = n.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card)
                    .Select(r=>(r.Value?.Cards ?? r.CardOptions.Select(c=>c.Value).ToImmutableArray())
                        .Select(c=>PredictorCardChanges.Definition(run.Context,c.Key).Rarity).ToArray()).ToArray() }).ToArray();
            bool bossCardsKnown = new[]{1,2}.All(act => bossCards.Any(b=>b.act == act && b.groups.Any(cards=>cards.Length >= 3)));
            log.Add(new { stage = "result", complete, bossCardsKnown, bossCards, nodes = forecast.Nodes.Length, last = last?.Position,
                terminal = forecast.TerminalBoundary?.TargetPosition, causes = forecast.PendingCauses.Select(c => new { c.NodeId, c.Code, parents = c.Parents.Select(p => p.Code) }),
                fault = forecast.Fault?.Error.ToString() });
            return complete && bossCardsKnown && (replay == null ||
                plans.GetValueOrDefault(replay.NodeId)?.Ancient?.ExpectedOffer.Relic.Entry == replay.Relic);
        }
    }

    private static object Receipt(PredictorRouteNodeKnowledge n) => new
    {
        stage = "node", n.Position, room = n.Room.ToString(), model = n.Model?.Entry, n.Advanced, phase = n.Phase.ToString(),
        rewards = n.Rewards.Select(r => new { kind = r.Kind.ToString(), r.IsKnown, key = r.Value?.Key.Entry, gold = r.Value?.GoldAmount,
            cards = r.Value?.Cards.Select(c => c.Key.Entry) ?? r.CardOptions.Select(c => c.Value.Key.Entry),
            causes = r.PendingCauses.Select(c => c.Code) }),
        ancient = n.Ancient?.Offers.Select(o => o.Value?.Relic.Entry), eventOptions = n.Event?.EntryOptions.Select(o => o.Key),
        ancientName = n.Ancient?.Ancient.Entry, ancientOffers = n.Ancient?.Offers.Select(o => new { o.Index, item = o.Value?.Relic.Entry, causes = o.PendingCauses.Select(c => new { c.NodeId,c.Code, c.RequiredPropertyKeys }) }),
        options = n.Event?.Options.Select(o=>new {o.Key,o.Enabled,hasIdentity=o.Value!=null,detail=o.Value,causes=o.PendingCauses.Select(c=>c.Code)}),
        children = n.Ancient?.RequiredChoices.Select(ch=> new { source=ch.Source.Entry, stage=ch.Phase.ToString(), ch.MinimumCards, ch.MaximumCards }),
        nextEventInput = n.Event?.NeededInput.ToString(), occurrences = n.Event?.Occurrence?.Candidates.Select(c => new { key = c.Event.Entry, c.EligibilityAssumed, c.Cursor }),
        shop = n.Shop?.Offers.Select(o => new { key = o.Value?.Key?.Entry, price = o.Value?.Cost }), treasure = n.Treasure?.OfferedRelic?.Entry,
        rest = n.RestOffers.Select(r => r.Action.ToString()), unmodeledRelics=n.UnmodeledRelics.Select(r=>r.Entry),
        causes = n.PendingCauses.Select(c => new { c.Code, c.NodeId, c.RequiredPropertyKeys })
    };
}
