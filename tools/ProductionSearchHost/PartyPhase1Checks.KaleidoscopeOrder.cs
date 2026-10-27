using RolltheSpire2.Ui.Workspace;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Controllers;
using RolltheSpire2.Ui.Pages.Analysis;

internal static partial class PartyPhase1Checks
{
    private static void CheckKaleidoscopeOrder(Node host, string output, GameVersionDetection detection)
    {
        MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = false;
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        // Two consecutive Owner searches whose valid P1 order differs. Preserve the
        // whole-table witness through Search -> information navigation and seat changes.
        foreach (var (seed, requiredP1Leaf) in new[] { ("R93FXJN77VRC", false), ("R93FXT6SM6K5", true) })
        {
            var profile = Beta111Profile.Instance;
            var bones = BaseGameModelKeys.Relics.NeowsBones;
            var leaf = BaseGameModelKeys.Relics.NewLeaf;
            var kalei = BaseGameModelKeys.Relics.Kaleidoscope;
            var target = new ModelKey("CARD", "OUTRAGE");
            var root = profile.ComputeRootSeed(seed);
            var party = PartyRuntimeAuthorityCapture.Capture(profile, seed,
                [BaseGameModelKeys.Characters.Defect, BaseGameModelKeys.Characters.Defect],
                [UnlockState.all.ToSerializable(), UnlockState.all.ToSerializable()], 10, "0.111.0");
            var context = SearchContextFactory.From(profile.ProfileId, BaseGameModelKeys.Characters.Defect, 10,
                party.Players[0], detection, AncientOptionConditionProfile.BroadDefault) with { Party = party };
            var valid = typeof(NeowsBones).GetMethod("GetValidRelics", BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (bool p1Leaf in new[] { true, false })
            foreach (bool p2Leaf in new[] { true, false })
            {
                var players = Enumerable.Range(0,2).Select(slot => Player.CreateForNewRun(
                    ModelDb.AllCharacters.Single(c => c.Id.Entry == "DEFECT"), UnlockState.all, (ulong)(slot+1))).ToArray();
                var run = RunState.CreateForTest(players, ascensionLevel:10, seed:seed);
                var shared = NeowEffectRngContext.CreateFromRootHash(profile, root, 0);
                bool[] firstLeaf = [p1Leaf, p2Leaf];
                for (int slot = 0; slot < 2; slot++)
                {
                    var owner = party.Players[slot];
                    SeedPredictionRequest.TryCreate(seed, owner.Character, 10, 2, slot, owner,
                        AncientOptionConditionProfile.BroadDefault, SeedPredictionDomainSelection.Neow, 10, false,
                        out var infoRequest, out _);
                    var choice = PartyNeowProjection.ProjectChoices(infoRequest!, root, shared.Niche, shared.CombatPotionGeneration)
                        .Single(c => c.RelicKey == bones);
                    var route = choice.BonesOutcome!.OriginalRoutes.Single(r => r.AcquisitionOrder[0] == (firstLeaf[slot] ? leaf : kalei));
                    var groups = route.RelicScopedResults.Single(r => r.SourceRelicKey == kalei).EffectGroups;
                    var expected = groups.Select(g => string.Join(",", g.OrderedItems.Where(e => e.Kind == PredictedEffectKind.AddCard)
                        .Select(e => e.TargetKey!.Value.Entry))).Where(g => g.Length > 0).ToArray();
                    var next = route.OpeningRewardContinuation!;
                    shared = shared.WithShared(next.NicheState!.Restore(), next.CombatPotionGenerationState!.Restore());
                    // Execute the DLL's actual pool shuffle, transform and CardFactory calls on a detached run.
                    var player = players[slot];
                    var relics = ((IEnumerable<RelicModel>)valid.Invoke(null, [player])!).ToList();
                    player.PlayerRng.Rewards.Shuffle(relics);
                    void Transform() => CardFactory.CreateRandomCardForTransform(player.Deck.Cards.First(c => c.Id.Entry == "STRIKE_DEFECT"), false, run.Rng.Niche);
                    if (firstLeaf[slot]) Transform();
                    var actual = new List<string>();
                    for (int group = 0; group < 2; group++)
                    {
                        var pools = player.UnlockState.CharacterCardPools.Where(p => p != player.Character.CardPool).ToList().StableShuffle(run.Rng.Niche).Take(3);
                        actual.Add(string.Join(",", pools.Select(pool => CardFactory.CreateForReward(player, 1,
                            new CardCreationOptions([pool], CardCreationSource.Other, CardRarityOddsType.RegularEncounter)
                                .WithFlags(CardCreationFlags.NoCardPoolModifications)).First().Card.Id.Entry)));
                    }
                    if (!firstLeaf[slot]) Transform();
                    var curses = ModelDb.CardPool<CurseCardPool>().GetUnlockedCards(player.UnlockState, ((IRunState)run).CardMultiplayerConstraint)
                        .Where(c => c.CanBeGeneratedByModifiers).OrderBy(c => c.Id).ToList();
                    _ = run.Rng.Niche.NextItem(curses);
                    GD.Print($"KALEI_ORDER seed={seed};P1Leaf={p1Leaf};P2Leaf={p2Leaf};slot={slot};expected={string.Join('/',expected)};native={string.Join('/',actual)}");
                    Check(expected.SequenceEqual(actual), "Kaleidoscope native offer mismatch");
                }
                var query = SearchQuery.Empty with { Players = Enumerable.Range(0,2).Select(slot => new PlayerOfferQuery(slot, ModelKeySetFilter.Empty)
                { Conditions = SearchQuery.Empty with { OpeningRoute = new(bones),
                    OpeningRouteRelicRequirement = new(bones, firstLeaf[slot] ? [leaf,kalei] : [kalei,leaf], BonesRouteOrderMode.ExactOrder),
                    StructuredOpeningEffects = [new(kalei, NeowStructuredConditionKind.IndependentOfferGroupTargets,
                        NeowStructuredEffectScope.SelectableOfferGroups, NeowStructuredOutputKind.Card, [target], AllowDuplicateOutputs:true)] }
                }).ToArray() };
                var request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context), new(seed,1,1,1)).Plan!;
                var result = ProductionExactSearchEvaluator.Evaluate(request, TrustedRootHashInput.FromCanonicalSeed(profile,seed));
                GD.Print($"KALEI_EXACT seed={seed};P1Leaf={p1Leaf};P2Leaf={p2Leaf};match={result.IsMatch}");
                Check(result.IsMatch == (p1Leaf == requiredP1Leaf && !p2Leaf), "Reported seed order mismatch");
                if (!result.IsMatch) continue;
                query = query with { Players = query.Players.Select(p => p with { Conditions = p.Conditions with {
                    OpeningRouteRelicRequirement = p.Conditions.OpeningRouteRelicRequirement! with { OrderMode = BonesRouteOrderMode.AnyOrder } } }).ToArray() };
                request = ExactSearchExecutionRequestFactory.Compile(SearchCompiler.Compile(query, context), new(seed,1,1,1)).Plan!;
                result = ProductionExactSearchEvaluator.Evaluate(request, TrustedRootHashInput.FromCanonicalSeed(profile,seed));
                Check(result.IsMatch, "AnyOrder retains a joint witness");
                var runtime = ModRuntimeSnapshot.NotInitialized with { Profile = profile, Detection = detection,
                    Compatibility = RuntimeProfileRegistry.Resolve(detection) };
                var draft = new WorkbenchSearchDraft(BaseGameModelKeys.Characters.Defect,10,query,AncientOptionConditionProfile.BroadDefault)
                { Mode = RolltheSpire2.Core.World.Snapshots.WorldGameMode.Multiplayer, GameVersion = "0.111.0", Profile = profile.ProfileId,
                    Players = Enumerable.Range(0,2).Select(i => new WorkbenchPlayerDraft(i,BaseGameModelKeys.Characters.Defect,
                        UnlockState.all.ToSerializable(),"AssumedFullyUnlocked")).ToArray() };
                var candidate = new SearchCandidate(seed, profile.ProfileId, BaseGameModelKeys.Characters.Defect,10,"order-probe",
                    result.Request!,result.Document!,result.Authority,[],[],[]);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                object Field(object o, string name) => o.GetType().GetField(name,flags)!.GetValue(o)!;
                var surface = new Control { Size = new Vector2(1600,900) }; host.AddChild(surface);
                var shell = new WorkspaceShell();
                shell.InitializeWithPersistence(runtime,Path.Combine(output,"ui-state")); surface.AddChild(shell);
                try
                {
                    typeof(WorkspaceShell).GetMethod("ShowPartyResult",flags)!.Invoke(shell,[candidate,draft]);
                    var controller = (AnalysisPageController)Field(shell,"_predictorController");
                    var page = (AnalysisPage)Field(shell,"_predictor");
                    foreach (int slot in new[] {0,1,0})
                    {
                        typeof(AnalysisPageController).GetMethod("SelectPartyPlayer",flags)!.Invoke(controller,[slot]);
                        var expectedRoute = result.Document!.Party!.Transactions.Single(t => t.Slot == slot).OpeningRouteId;
                        GD.Print($"KALEI_UI seed={seed};slot={slot};expected={expectedRoute};displayed={page.PreferredOpeningRouteId}");
                        Check(page.PreferredOpeningRouteId == expectedRoute, "Search witness is preserved by information page");
                    }
                }
                finally { host.RemoveChild(surface); surface.QueueFree(); }
            }
        }
        GD.Print($"PARTY_KALEIDOSCOPE_ORDER_PASS checks={checks};nativeOffers=true;exactJointOrder=true;informationNavigation=true");
    }
}
