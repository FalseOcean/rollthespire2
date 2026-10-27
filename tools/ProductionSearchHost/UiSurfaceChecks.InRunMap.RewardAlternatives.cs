using System.Collections.Immutable;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction.Maps;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.InRunMap;

internal static partial class UiSurfaceChecks
{
    private static async Task RunInRunMapRewardAlternativesSmoke(Node root, string output, NMapScreen screen,
        InRunMapPredictionPanel panel, RunState native, PredictorMapRouteDraft draft, MapPosition first,
        Func<string> signature, Action<bool, string> check)
    {
        string before = signature();
        var text = (IUiTextProvider)DiagnosticField(panel, "_text");
        var names = (IGameContentNameResolver)DiagnosticField(panel, "_names");
        var product = (InRunMapNodeDetails)DiagnosticField(panel, "_details");
        string Alternative(string key) => text.Get("predictor.runtime.alternative." + key);
        InRunMapDetailSection Cards(InRunMapNodeDetailData data) => data.Sections.First(section =>
            section.Objects.Count > 0 && section.Objects.All(item => item.Kind == GameContentKind.Card));
        async Task Picture(string name)
        {
            bool productVisible = product.Visible;
            try
            {
                // The panel's earlier persistent editor remains open for its
                // own checks. Hide it only while photographing this fixture.
                product.Hide(); await DiagnosticFrame(root);
                using var image = root.GetViewport().GetTexture().GetImage();
                image.SavePng(Path.Combine(output, name));
            }
            finally { product.Visible = productVisible; }
            await DiagnosticFrame(root);
        }

        foreach (var fixture in new[] { (Entry: "DRIFTWOOD", Count: 0, Gift: ""), (Entry: "PAELS_WING", Count: 0, Gift: ""),
            (Entry: "PAELS_WING", Count: 1, Gift: "DOLLYS_MIRROR"), (Entry: "PAELS_WING", Count: 1, Gift: "ORRERY") })
        {
            string entry = fixture.Entry;
            // Only this detached snapshot owns the fixture relic. Native
            // inventory, RNG, route draft and the panel session stay untouched.
            var isolated = new InRunMapPredictionSession();
            isolated.Update(native, "0.111.0", true, new Dictionary<int, PredictorMapRouteDraft> { [0] = draft });
            var captured = isolated.Snapshot!;
            var source = captured.Run;
            var state = source.Working;
            long id = state.NextInstanceId;
            var controlled = new PredictorRun(source.Context, state with
            {
                NextInstanceId = id + 1,
                Relics = state.Relics.Where(relic => relic.Key.Entry is not ("DRIFTWOOD" or "PAELS_WING"))
                    .Append(new PredictorRelic(id, new ModelKey("RELIC", entry), state.Position.Floor)
                    { RewardsSacrificed = fixture.Count }).ToImmutableArray(),
                PersonalBag = fixture.Count == 1 ? new(false, [], new[] { "Common", "Uncommon", "Rare" }
                    .Select(rarity => new PredictorBagBucket(rarity, [new("RELIC", fixture.Gift)])).ToImmutableArray(), []) : state.PersonalBag
            }) { CombatDefaults = source.CombatDefaults, AutomaticRewardPickup = source.AutomaticRewardPickup };
            void Capture(PredictorRun run) => typeof(InRunMapPredictionSession).GetProperty(nameof(InRunMapPredictionSession.Snapshot),
                BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(isolated, captured with { Run = run });
            Capture(controlled); isolated.Recompute();
            var presenter = new InRunMapNodePresenter(isolated, names, text);
            var view = new InRunMapNodeDetails((IGameIconResolver)DiagnosticField(product, "_icons"), names);
            root.AddChild(view); view.SetProcess(false);
            string key = entry == "DRIFTWOOD" ? "REROLL" : "SACRIFICE";
            bool editorOpen = false;
            void Refresh()
            {
                if (editorOpen) view.ShowEditor(presenter.Editor((0, first))!, screen.GetGlobalRect());
                else view.ShowNode(presenter.Detail((0, first))!, new Rect2(new(530, 260), new(30, 30)), screen.GetGlobalRect());
            }
            view.ChoiceRequested += (nodeId, choiceId) => { if (presenter.ApplyChoice(nodeId, choiceId)) Refresh(); };
            try
            {
                Refresh(); await DiagnosticFrame(root);
                var section = Cards(presenter.Detail((0, first))!);
                var action = section.Choices.Single(choice => choice.Label == Alternative(key));
                var firstCard = section.Choices.First(choice => choice.Object?.Kind == GameContentKind.Card);
                check(action.Enabled && ReferenceEquals(DiagnosticButton(view, action.Id).GetParent(), DiagnosticButton(view, firstCard.Id).GetParent()) &&
                    DiagnosticButton(view, action.Id).GetParent() is HBoxContainer &&
                    !section.Choices.Any(choice => choice.Label == text.Get("predictor.runtime.plan.skip")) &&
                    !section.Choices.Any(choice => choice.Label == Alternative(key == "REROLL" ? "SACRIFICE" : "REROLL")),
                    entry + " appears beside its actual card choices only for the qualifying detached relic, without a redundant Skip");
                if (entry == "DRIFTWOOD") await Picture("in-run-map-reward-driftwood.png");
                string stale = action.Id;
                DiagnosticButton(view, action.Id).EmitSignal(Button.SignalName.Pressed); await DiagnosticFrame(root);
                var node = isolated.Nodes[(0, first)];
                var receipt = isolated.GetPlan(0, first)!.RewardChoices.Single(choice => choice.ExpectedReward.Kind == PredictorRewardKind.Card);
                section = Cards(presenter.Detail((0, first))!);
                check(section.Choices.Single(choice => choice.Label == Alternative(key)).Selected &&
                    !presenter.ApplyChoice(node.Position.NodeId, stale),
                    entry + " highlights only its applied alternative and rejects the obsolete button generation");
                if (key == "REROLL")
                {
                    var generated = node.Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card).Value!;
                    check(receipt.Reroll && receipt.CardIndex == null && generated.HasRerolled &&
                        section.Objects.Select(item => item.Key).SequenceEqual(generated.Cards.Select(card => card.Key)),
                        "Reroll keeps its card group open and renders the replacement offer");
                    var take = section.Choices.First(choice => choice.Object?.Kind == GameContentKind.Card);
                    DiagnosticButton(view, take.Id).EmitSignal(Button.SignalName.Pressed); await DiagnosticFrame(root);
                    receipt = isolated.GetPlan(0, first)!.RewardChoices.Single(choice => choice.ExpectedReward.Kind == PredictorRewardKind.Card);
                    check(receipt.Reroll && receipt.CardIndex == 0 && receipt.ExpectedRerolledReward != null,
                        "choosing a rerolled card retains both source and replacement reward bindings");
                    section = Cards(presenter.Detail((0, first))!);
                    var cancelTake = section.Choices.Single(choice => choice.Selected && choice.Object?.Kind == GameContentKind.Card &&
                        choice.Label == text.Get("predictor.runtime.cancel"));
                    DiagnosticButton(view, cancelTake.Id).EmitSignal(Button.SignalName.Pressed); await DiagnosticFrame(root);
                    receipt = isolated.GetPlan(0, first)!.RewardChoices.Single(choice => choice.ExpectedReward.Kind == PredictorRewardKind.Card);
                    check(receipt.Reroll && !receipt.Skip && receipt.CardIndex == null && receipt.ExpectedRerolledReward == null &&
                        isolated.Nodes[(0, first)].Rewards.Any(reward => reward.Kind == PredictorRewardKind.Card && reward.DefaultSkipped &&
                            reward.AppliedChoice is { Reroll: true, CardIndex: null, Skip: false }),
                        "cancelling the rerolled card's Take preserves the original reroll while the approved default owns Skip");
                    editorOpen = true; Refresh(); await DiagnosticFrame(root);
                    var editorSection = presenter.Editor((0, first))!.Sections.First(value => value.RewardActionsRow);
                    var reroll = editorSection.Choices.Single(choice => choice.Label == Alternative("REROLL"));
                    var editorCard = editorSection.Choices.First(choice => choice.Object?.Kind == GameContentKind.Card);
                    // The retained full editor uses its card grid and action
                    // row; compact map reward controls share one inline row.
                    // Both still expose the same real choice/cancellation data.
                    check(DiagnosticButton(view, reroll.Id).GetParent().Name == "CardRewardActions" &&
                        DiagnosticButton(view, editorCard.Id).GetParent().Name == "CardResultsGrid" &&
                        !DiagnosticButton(view, reroll.Id).Disabled && !DiagnosticButton(view, editorCard.Id).Disabled &&
                        !editorSection.Choices.Any(choice => choice.Label == text.Get("predictor.runtime.plan.skip")),
                        "retained full editor exposes its actual card grid and alternative action without a redundant Skip; cancellation semantics match the compact map");
                    DiagnosticButton(view, reroll.Id).EmitSignal(Button.SignalName.Pressed); await DiagnosticFrame(root);
                    check(isolated.GetPlan(0, first)?.RewardChoices.Any(choice => choice.Reroll) != true &&
                        isolated.Nodes[(0, first)].Rewards.Single(reward => reward.Kind == PredictorRewardKind.Card) is
                            { DefaultSkipped: true, AppliedChoice: null, Value.HasRerolled: false } &&
                        !isolated.Nodes[(0, first)].PendingCauses.Any(cause => cause.Code == "PlannedRewardChanged"),
                        "clicking selected Reroll removes only its explicit choice and restores the valid default offer");
                    reroll = presenter.Editor((0, first))!.Sections.First(value => value.RewardActionsRow).Choices
                        .Single(choice => choice.Label == Alternative("REROLL"));
                    DiagnosticButton(view, reroll.Id).EmitSignal(Button.SignalName.Pressed); await DiagnosticFrame(root);
                    Capture(new PredictorRun(source.Context, controlled.Working with
                    { Relics = controlled.Working.Relics.Where(relic => relic.Id != id).ToImmutableArray() })
                    { CombatDefaults = source.CombatDefaults, AutomaticRewardPickup = source.AutomaticRewardPickup });
                    isolated.Recompute();
                    check(!Cards(presenter.Detail((0, first))!).Choices.Any(choice => choice.Selected),
                        "a now-ineligible saved reroll cannot make replacement cards or Skip appear accepted");
                }
                else
                {
                    var reward = node.Rewards.Single(value => value.Kind == PredictorRewardKind.Card && value.AppliedChoice?.SacrificeRelicId == id);
                    check(receipt.SacrificeRelicId == id && reward.SacrificeCount == fixture.Count + 1 &&
                        section.Text.Contains(Alternative("SACRIFICE"), StringComparison.Ordinal) &&
                        !section.Text.Contains(text.Get("predictor.runtime.taken"), StringComparison.Ordinal) &&
                        !section.Choices.Any(choice => choice.Object?.Kind == GameContentKind.Card && choice.Selected),
                        "Sacrifice displays its exact count without presenting an offered card as taken");
                    if (fixture.Gift == "DOLLYS_MIRROR")
                    {
                        var child = reward.RequiredChoices;
                        check(reward.SacrificeRelic?.Entry == "DOLLYS_MIRROR" && child?.Phase == PredictorPhase.ObtainDeckCards &&
                            presenter.Detail((0, first))!.Sections.Any(value => value.Objects.Any(item => item.Key.Entry == "DOLLYS_MIRROR")),
                            "the second Sacrifice displays its obtained relic and its required deck choice on the same node");
                        await Picture("in-run-map-reward-wing-mirror.png");
                        editorOpen = true; Refresh(); await DiagnosticFrame(root);
                        var childSection = presenter.Editor((0, first))!.Sections.First(value => value.Title ==
                            text.Get("predictor.map.select_one_card"));
                        var target = childSection.Choices.First(value => value.Object?.Kind == GameContentKind.Card);
                        DiagnosticButton(view, target.Id).EmitSignal(Button.SignalName.Pressed); await DiagnosticFrame(root);
                        receipt = isolated.GetPlan(0, first)!.RewardChoices.Single(choice => choice.ExpectedReward.Kind == PredictorRewardKind.Card);
                        reward = isolated.Nodes[(0, first)].Rewards.Single(value => value.Kind == PredictorRewardKind.Card);
                        check(receipt.SacrificeRelicId == id && receipt.Children is [{ Action: PredictorObtainChildAction.ChooseDeckCards }] &&
                            reward.AppliedChildren == 1 && reward.RequiredChoices == null && reward.SacrificeCount == 2,
                            "the real deck target button authors and settles a child under its originating Sacrifice plan");
                        view.CloseEditor(); editorOpen = false; Refresh(); await DiagnosticFrame(root);
                        section = Cards(presenter.Detail((0, first))!);
                    }
                    else if (fixture.Gift == "ORRERY")
                    {
                        var completed = node.Rewards.Where(value => value.Kind == PredictorRewardKind.Card && value.DefaultSkipped &&
                            value.CompletedEdit != null).ToArray();
                        check(reward.SacrificeRelic?.Entry == "ORRERY" && reward.RequiredChoices == null && completed.Length == 5 &&
                            receipt.Children.IsEmpty,
                            "Orrery's five optional card groups finish by default without authored Skip children and retain typed edit ownership");
                        var childReward = completed[0];
                        InRunMapDetailSection ChildSection() => presenter.Detail((0, first))!.Sections.First(value =>
                            value.Objects.Select(item => item.Key).SequenceEqual(childReward.Value!.Cards.Select(card => card.Key)));
                        var childSection = ChildSection();
                        var take = childSection.Choices.First(value => value.Object?.Kind == GameContentKind.Card);
                        DiagnosticButton(view, take.Id).EmitSignal(Button.SignalName.Pressed); await DiagnosticFrame(root);
                        receipt = isolated.GetPlan(0, first)!.RewardChoices.Single(choice => choice.ExpectedReward.Kind == PredictorRewardKind.Card);
                        check(receipt.SacrificeRelicId == id && receipt.Children.Any(child => child.ExpectedSource.Entry == "ORRERY" &&
                            child.Reward is { CardIndex: 0, Skip: false }),
                            "taking a completed default Orrery offer revises its originating Sacrifice child rather than authoring a root reward");
                        PredictorCombatRewardKnowledge EditedChild() => isolated.Nodes[(0, first)].Rewards.First(value =>
                            value.Occurrence == childReward.Occurrence && value.CompletedEdit != null && value.Value is { } offer &&
                            offer.Kind == childReward.Kind && offer.Origin == childReward.Value!.Origin && offer.Key == childReward.Value.Key);
                        check(EditedChild() is { DefaultSkipped: false, AppliedChoice.CardIndex: 0, CompletedEdit: not null },
                            "completed Orrery Take keeps its accepted receipt and typed edit path for further revision");
                        var childCancel = ChildSection().Choices.Single(value => value.Selected && value.Object?.Kind == GameContentKind.Card &&
                            value.Label == text.Get("predictor.runtime.cancel"));
                        DiagnosticButton(view, childCancel.Id).EmitSignal(Button.SignalName.Pressed); await DiagnosticFrame(root);
                        receipt = isolated.GetPlan(0, first)!.RewardChoices.Single(choice => choice.ExpectedReward.Kind == PredictorRewardKind.Card);
                        check(receipt.Children.IsEmpty &&
                            EditedChild() is { DefaultSkipped: true, AppliedChoice: null, CompletedEdit: not null },
                            "cancelling the completed Orrery card removes only its leaf while preserving the parent Sacrifice and default offer");
                        section = Cards(presenter.Detail((0, first))!);
                    }
                    var cancelSacrifice = section.Choices.Single(choice => choice.Selected && choice.Label == Alternative("SACRIFICE"));
                    DiagnosticButton(view, cancelSacrifice.Id).EmitSignal(Button.SignalName.Pressed); await DiagnosticFrame(root);
                    check(isolated.GetPlan(0, first)?.RewardChoices.Any(choice => choice.ExpectedReward.Kind == PredictorRewardKind.Card) != true &&
                        isolated.Nodes[(0, first)].Rewards.Single(value => value.Kind == PredictorRewardKind.Card) is
                            { SacrificeCount: null, AppliedChoice: null, DefaultSkipped: true },
                        "clicking the applied Sacrifice removes its actual counter/children and restores the original default offer");
                }
                check(signature() == before, entry + " native button callbacks mutate only the detached forecast");
            }
            finally { view.CloseEditor(); view.CloseHover(); view.QueueFree(); await DiagnosticFrame(root); }
        }
    }
}
