using System.Collections.Immutable;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Nodes.Cards;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Infrastructure.ContentNames;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.InRunOperations;
using RolltheSpire2.Ui.Settings;

internal static partial class UiSurfaceChecks
{
    private static int[] HuntCounts(Node node, string key) => node.GetMeta(key).AsString().Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
    private static string HuntOfferSignature(OperationPreviewSection source, OperationPreviewCardGroup group) => JsonSerializer.Serialize(new
    {
        source.Source, source.Category, source.FeatureId, SourceLabel = source.Label, source.EmptyReason, source.Style,
        Cards = group.Cards.Select(card => card with { Id = 0, FloorAdded = 0 }), group.MinimumSelect, group.MaximumSelect,
        group.CanSkip, group.Possible, GroupLabel = group.Label, group.Highlighted, group.EarliestFirst, group.ForceSingleRow
    });

    // Independently compare complete offer prefixes. Ending a route at an
    // existing prefix does not create a second successor and is not a fork.
    private static bool HuntHasCardFork(OperationPreviewBranch branch)
    {
        var next = new Dictionary<string, HashSet<string>>();
        foreach (var outcome in branch.HuntOutcomes)
        {
            string prefix = "";
            foreach (var section in outcome.Sections)
            foreach (var group in section.CardGroups.Where(group => !group.Cards.IsEmpty))
            {
                string offer = HuntOfferSignature(section, group);
                if (!next.TryGetValue(prefix, out var choices)) next[prefix] = choices = [];
                choices.Add(offer); prefix += "\n" + offer;
            }
        }
        return next.Values.Any(choices => choices.Count > 1);
    }

    private static bool HuntShouldLimit(OperationPreviewBranch branch) =>
        branch.HuntOutcomes.Any(outcome => outcome.Triggers > 2) && HuntHasCardFork(branch);

    // Expected paths are the original independently settled outcomes, never
    // the renderer's OperationHuntTree.Build result. Read the actual native
    // card faces and the visible line endpoints/count captions back instead.
    private static bool CheckVisibleHuntTrees(NeowOperationOverlay view, OperationPreview preview,
        PredictorCatalog catalog, Action<bool, string> check)
    {
        bool sawTree = false;
        bool anyLimited = preview.Branches.Any(HuntShouldLimit);
        var uiText = JsonUiTextProvider.CreateUi13(UiLanguagePreferences.LanguageCode);
        check(Descendants(view).OfType<Label>().Count(label => label.Text == uiText.Get("operation.hunt.displayLimit")) == (anyLimited ? 1 : 0),
            "the sidebar discloses the 0–2 display limit exactly once, only when a genuine fork loses higher-count routes");
        for (int branchIndex = 0; branchIndex < preview.Branches.Length; branchIndex++)
        {
            var branch = preview.Branches[branchIndex];
            if (branch.HuntOutcomes.IsEmpty) continue;
            bool limited = HuntShouldLimit(branch);
            var displayed = branch.HuntOutcomes.Where(outcome => !limited || outcome.Triggers <= 2).ToArray();
            var prefixes = new HashSet<string>(); var next = new Dictionary<string, HashSet<string>>();
            foreach (var outcome in displayed)
            {
                string prefix = "";
                foreach (var section in outcome.Sections)
                foreach (var group in section.CardGroups.Where(group => !group.Cards.IsEmpty))
                {
                    string offer = HuntOfferSignature(section, group);
                    if (!next.TryGetValue(prefix, out var choices)) next[prefix] = choices = [];
                    choices.Add(offer); prefix += "\n" + offer; prefixes.Add(prefix);
                }
            }
            var host = Descendants(view).OfType<Control>().SingleOrDefault(node => node.Name == "Rt2HuntTree_" + branchIndex);
            if (host == null)
            {
                check(next.Values.All(choices => choices.Count <= 1), "a flat Hunt display is allowed only when every complete settlement is a linear prefix");
                if (preview.Branches.Length == 1)
                {
                    var expected = displayed.OrderBy(outcome => outcome.Triggers).Last().Sections.SelectMany(section => section.CardGroups)
                        .Where(group => !group.Cards.IsEmpty).ToArray();
                    var actual = Descendants(view).OfType<Control>().Where(node => node.Name.ToString().StartsWith("Rt2NeowCardStack_", StringComparison.Ordinal))
                        .Select(node => Descendants(node).OfType<NCard>().Select(card => PredictorLiveRunCapture.Card(card.Model!, catalog)).ToArray()).ToArray();
                    check(actual.Length == expected.Length && actual.Select((cards, index) => SameCards(cards, expected[index].Cards)).All(same => same),
                        "linear appended native offers retain every full result through the highest trigger count, including count four");
                }
                continue;
            }
            sawTree = true;
            var nodes = Descendants(host).OfType<Control>().Where(node => node.HasMeta("rt2_hunt_id")).ToDictionary(node => node.GetMeta("rt2_hunt_id").AsInt32());
            check(nodes.Count == prefixes.Count + 1 && nodes.ContainsKey(0), "each distinct ordered prefix is drawn exactly once, including its cardless root");
            check(nodes.Values.All(node => node.GetMeta("rt2_hunt_branch").AsInt32() == branchIndex), "tree sharing stays inside its actual outer combat hypothesis");
            var visibleCounts = displayed.Select(outcome => outcome.Triggers).ToHashSet();
            check(nodes.Values.All(node => HuntCounts(node, "rt2_hunt_counts").Concat(HuntCounts(node, "rt2_hunt_ends")).All(visibleCounts.Contains)),
                "rendered Hunt nodes and endpoints carry only the exact retained trigger counts");
            foreach (var outcome in displayed)
            {
                var expected = outcome.Sections.SelectMany(section => section.CardGroups.Where(group => !group.Cards.IsEmpty).Select(group => (section, group))).ToArray();
                int current = 0, index = 0; var visited = new HashSet<int>();
                while (true)
                {
                    check(visited.Add(current), "visible Hunt route cannot cycle or re-enter another lane");
                    if (HuntCounts(nodes[current], "rt2_hunt_ends").Contains(outcome.Triggers)) break;
                    var children = nodes.Where(pair => pair.Value.GetMeta("rt2_hunt_parent").AsInt32() == current &&
                        HuntCounts(pair.Value, "rt2_hunt_counts").Contains(outcome.Triggers)).ToArray();
                    check(children.Length == 1 && index < expected.Length, "each displayed trigger count has exactly one next offer on its complete route");
                    current = children[0].Key; var node = children[0].Value;
                    var actual = Descendants(node).OfType<NCard>().Select(card => PredictorLiveRunCapture.Card(card.Model!, catalog)).ToArray();
                    var wanted = expected[index++];
                    check(SameCards(actual, wanted.group.Cards) && node.GetMeta("rt2_hunt_source").AsString() == wanted.section.Source.Serialized &&
                        node.GetMeta("rt2_hunt_minimum").AsInt32() == wanted.group.MinimumSelect && node.GetMeta("rt2_hunt_maximum").AsInt32() == wanted.group.MaximumSelect &&
                        node.GetMeta("rt2_hunt_canskip").AsBool() == wanted.group.CanSkip && node.GetMeta("rt2_hunt_possible").AsBool() == wanted.group.Possible,
                        "reading native card faces along one visible Hunt line reproduces the complete ordered offer and selection semantics");
                }
                check(index == expected.Length, "Hunt route ends only after every offer in that count's full result");
            }
            foreach (var pair in nodes)
            {
                var children = nodes.Where(child => child.Value.GetMeta("rt2_hunt_parent").AsInt32() == pair.Key).ToArray();
                var ends = HuntCounts(pair.Value, "rt2_hunt_ends");
                if (ends.Length > 0 && (pair.Key == 0 || children.Length > 0))
                    check(host.GetNodeOrNull<Label>("Rt2HuntTreeEnd_" + pair.Key) is { } end && end.IsVisibleInTree() && ends.All(count => end.Text.Contains(count.ToString(), StringComparison.Ordinal)),
                        "a shared prefix that is also a complete result visibly identifies its ending count");
                if (children.Length > 1 || ends.Length > 0)
                    foreach (var child in children)
                    {
                        var counts = HuntCounts(child.Value, "rt2_hunt_counts");
                        string range = counts.Length > 1 && counts[^1] - counts[0] == counts.Length - 1 ? counts[0] + "–" + counts[^1] : string.Join("、", counts);
                        check(child.Value.GetChildren().OfType<Label>().Any(label => label.Visible && label.Text.Contains(range, StringComparison.Ordinal)),
                            "every actual divergence or continuation past a stop visibly labels its trigger count");
                    }
            }
            foreach (var edge in Descendants(host).OfType<Line2D>())
            {
                int id = edge.GetMeta("rt2_hunt_id").AsInt32(), parent = edge.GetMeta("rt2_hunt_parent").AsInt32();
                check(nodes.ContainsKey(id) && nodes.ContainsKey(parent) && nodes[id].GetMeta("rt2_hunt_parent").AsInt32() == parent && edge.Points.Length >= 2,
                    "each drawn connector belongs to exactly its declared parent and child");
                check(HuntCounts(edge, "rt2_hunt_counts").SequenceEqual(HuntCounts(nodes[id], "rt2_hunt_counts")),
                    "each native connector carries exactly its child's retained trigger counts");
                var points = edge.Points.Select(edge.ToGlobal).ToArray();
                check(points[0].X <= points[^1].X && points.All(point => view.GetViewportRect().Grow(1).HasPoint(point)), "Hunt connector flows toward its child within the viewport");
                foreach (var node in nodes.Where(node => node.Key != 0 && node.Key != id && node.Key != parent))
                for (int segment = 1; segment < points.Length; segment++)
                    check(!HuntSegmentCrossesRect(points[segment - 1], points[segment], node.Value.GetGlobalRect().Grow(-1)),
                        "a Hunt connector never crosses an unrelated card stack and suggests the wrong result line");
            }
            check(nodes.Values.Where(node => node.GetMeta("rt2_hunt_id").AsInt32() != 0).All(node => view.GetViewportRect().Grow(1).Encloses(node.GetGlobalRect())),
                "all actual Hunt card nodes remain visible inside the viewport");
            var physicalNodes = nodes.Where(pair => pair.Key != 0).Select(pair => pair.Value).ToArray();
            for (int first = 0; first < physicalNodes.Length; first++)
            for (int second = first + 1; second < physicalNodes.Length; second++)
                check(!physicalNodes[first].GetGlobalRect().Grow(-.5f).Intersects(physicalNodes[second].GetGlobalRect().Grow(-.5f)),
                    "compacted Hunt offers occupy disjoint visible rectangles");
            foreach (var label in host.GetChildren().OfType<Label>().Concat(physicalNodes.SelectMany(node => node.GetChildren().OfType<Label>()))
                .Where(label => label.IsVisibleInTree() && label.Text.Length > 0))
                check(view.GetViewportRect().Grow(1).Encloses(label.GetGlobalRect()) && label.Size.Y + 1 >= label.GetMinimumSize().Y,
                    "long Hunt count/source captions and ending labels retain their full visible height inside the viewport; " +
                    label.GetPath() + "; text=" + label.Text + "; size=" + label.Size + "; minimum=" + label.GetMinimumSize() +
                    "; rect=" + label.GetGlobalRect() + "; viewport=" + view.GetViewportRect());
            foreach (var node in physicalNodes)
                foreach (var label in node.GetChildren().OfType<Label>().Where(label => label.Visible && label.Text.Length > 0))
                    check(Descendants(node).OfType<NCard>().All(card => !label.GetGlobalRect().Grow(-.5f).Intersects(card.GetParent<Control>().GetGlobalRect())),
                        "Hunt count/source headings occupy their own rows above the native card faces; " + label.GetPath() +
                        "; text=" + label.Text + "; label=" + label.GetGlobalRect() + "; cards=" +
                        string.Join(";", Descendants(node).OfType<NCard>().Select(card => card.GetParent<Control>().GetGlobalRect().ToString())));
            if (limited)
            {
                var sidebar = Descendants(view).OfType<Control>().SingleOrDefault(node => node.Name == "Rt2OperationBranch_" + branchIndex);
                var captions = sidebar == null ? [] : Descendants(sidebar).OfType<Label>().Select(label => label.Text).ToArray();
                foreach (int omitted in branch.HuntOutcomes.Select(outcome => outcome.Triggers).Where(count => count > 2))
                    check(!captions.Contains(uiText.Format("operation.label.huntTriggers", omitted)),
                        "exact non-card drops above two triggers are not separately itemized after the branch display is capped");
                var names = RuntimeGameContentNameResolver.Create(UiLanguagePreferences.LanguageCode);
                string Name(ModelKey key) => names.Resolve(key, key.Category == "POTION" ? GameContentKind.Potion : GameContentKind.Relic);
                bool Retained(OperationPreviewSection section) => section.Label?.StartsWith("EventHuntTriggers:", StringComparison.Ordinal) != true ||
                    !int.TryParse(section.Label[18..], out int count) || count <= 2;
                var expectedNames = branch.Sections.Where(Retained).SelectMany(section => section.Rows).SelectMany(row => row.Models).Select(Name).ToArray();
                foreach (string name in branch.Sections.SelectMany(section => section.Rows).SelectMany(row => row.Models).Select(Name).Distinct())
                    check(captions.Count(caption => caption == name) == expectedNames.Count(expected => expected == name),
                        "the actual sidebar model-name occurrences equal the retained complete 0–2 drop rows, without hidden higher-count duplicates");
            }
        }
        return sawTree;
    }

    private static bool HuntSegmentCrossesRect(Vector2 a, Vector2 b, Rect2 rect) =>
        Math.Abs(a.Y - b.Y) < .01f ? a.Y > rect.Position.Y && a.Y < rect.End.Y && Math.Max(a.X, b.X) > rect.Position.X && Math.Min(a.X, b.X) < rect.End.X :
        Math.Abs(a.X - b.X) < .01f && a.X > rect.Position.X && a.X < rect.End.X && Math.Max(a.Y, b.Y) > rect.Position.Y && Math.Min(a.Y, b.Y) < rect.End.Y;

    private sealed record HuntCardGeometry(int Cards, float MinWidth, float MinHeight, float MaxWidth, float MaxHeight);
    private static HuntCardGeometry HuntGeometry(NeowOperationOverlay view)
    {
        var tiles = Descendants(view).OfType<NCard>().Select(card => card.GetParent<Control>().GetGlobalRect().Size).ToArray();
        return new(tiles.Length, tiles.Min(size => size.X), tiles.Min(size => size.Y), tiles.Max(size => size.X), tiles.Max(size => size.Y));
    }

    private static async Task RunHuntPrefixDisplayChecks(Node root, string output, SubViewport viewport, Control hoverRoot,
        PredictorOperationSnapshot basis, Action<bool, string> check, Rect2? nativeOwner = null)
    {
        var context = basis.Context with { Live = null, Crystal = null, Modifiers = [], NumberOfRuns = 5, SealOfGoldFixedDeduction = 0 };
        var state = basis.State with { Relics = [], OpeningCompleted = true, CurrentRoomIsCombat = false,
            Position = new(basis.State.Acts[0].Act, 10, "hunt-prefix-ui"),
            Deck = basis.State.Deck.Where(id => basis.State.Card(id).Key.Entry is not ("THE_HUNT" or "ROYALTIES")).ToImmutableArray() };
        foreach (string entry in new[] { "THE_HUNT", "THE_HUNT", "THE_HUNT", "THE_HUNT", "ROYALTIES" }) state = PredictorObtainSources.AddCard(context, state, new ModelKey("CARD", entry));
        var nativeDense = ModelDb.Encounter<DenseVegetationEventEncounter>().ToMutable();
        var nativeRoster = (System.Collections.IEnumerable)typeof(DenseVegetationEventEncounter).GetMethod("GenerateMonsters",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(nativeDense, null)!;
        check(nativeDense.Slots.Count == 4 && nativeRoster.Cast<object>().Count() == 4,
            "the actual native Dense Vegetation encounter independently generates four starting enemies");
        var rng = state.Streams.Single(stream => stream.Stream == PredictorStream.Rewards);
        var evidence = new List<object>(); NeowOperationOverlay? view = null;
        var originalSize = viewport.Size; var originalWindow = root.GetWindow().Size;
        async Task Frames() { for (int frame = 0; frame < 4; frame++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        try
        {
            foreach (var source in new[] { (Event: "PUNCH_OFF", Option: "I_CAN_TAKE_THEM", Encounter: "PUNCH_OFF_EVENT_ENCOUNTER", Max: 2),
                         (Event: "THE_LANTERN_KEY", Option: "KEEP_THE_KEY", Encounter: "MYSTERIOUS_KNIGHT_EVENT_ENCOUNTER", Max: 1),
                         (Event: "DENSE_VEGETATION", Option: "FIGHT", Encounter: "", Max: 4) })
            foreach (var variant in new[] { (Name: "base", Royal: false, Wheel: false), (Name: "royal", Royal: true, Wheel: false), (Name: "royal-wheel", Royal: true, Wheel: true), (Name: "royal-wheel-long", Royal: true, Wheel: true) })
            {
                if (variant.Name == "royal-wheel-long" && source.Event != "DENSE_VEGETATION") continue;
                var current = variant.Wheel ? state with { Relics = [new(state.NextInstanceId, new("RELIC", "PRAYER_WHEEL"), 1)], NextInstanceId = state.NextInstanceId + 1 } : state;
                var scene = new PredictorOperationEventScene(new("EVENT", source.Event), rng, [new(source.Option)], new(80, 80))
                    { LayoutEncounter = source.Encounter.Length == 0 ? null : new("ENCOUNTER", source.Encounter) };
                var snapshot = new PredictorOperationSnapshot("hunt-prefix:" + source.Event, context, current, scene, 80, 80);
                string unchanged = JsonSerializer.Serialize(snapshot.State);
                var preview = EventOperationPreview.Build(snapshot, source.Option, playRoyalties: variant.Royal);
                if (variant.Name == "royal-wheel-long")
                    preview = preview with { Branches = preview.Branches.Select(branch => branch with
                    {
                        HuntOutcomes = branch.HuntOutcomes.Select(outcome => outcome with
                        {
                            Sections = outcome.Sections.Select(section => section with
                            {
                                CardGroups = section.CardGroups.Select(group => group with
                                    { Label = "当前战斗条件下的完整奖励组 · 长标签保持清晰与完整" }).ToImmutableArray()
                            }).ToImmutableArray()
                        }).ToImmutableArray()
                    }).ToImmutableArray() };
                check(preview.Faults.IsEmpty && preview.PendingReasons.IsEmpty && preview.Branches.All(branch =>
                    branch.HuntOutcomes.Select(outcome => outcome.Triggers).Order().SequenceEqual(Enumerable.Range(0, source.Max + 1))),
                    source.Event + " retains every independent exact-count settlement including one trigger");
                if (source.Event == "DENSE_VEGETATION" && variant.Name is "base" or "royal-wheel")
                    check(preview.Branches.Any(HuntHasCardFork) == (variant.Name == "royal-wheel"),
                        "the real four-enemy Dense sample distinguishes a full linear 0–4 sequence from Royalties plus Prayer Wheel's genuine card fork");
                foreach (var size in new[] { new Vector2I(1920, 1080), new Vector2I(1280, 720) })
                {
                    viewport.Size = size; root.GetWindow().Size = size;
                    view?.Free(); view = new NeowOperationOverlay(); hoverRoot.AddChild(view); view.SetPreview(preview); await Frames();
                    view.SetLayout(new(20, 80, 380, size.Y - 110), new(new Vector2(420, 80), new Vector2(size.X - 440, size.Y - 110))); await Frames();
                    File.WriteAllText(Path.Combine(output, "hunt-prefix-geometry-progress.json"), JsonSerializer.Serialize(new
                        { current = source.Event + "/" + variant.Name + "/" + size.X, geometry = HuntGeometry(view), evidence }, new JsonSerializerOptions { WriteIndented = true }));
                    if (variant.Name == "royal-wheel-long") viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "hunt-long-current-" + size.X + ".png"));
                    bool tree = CheckVisibleHuntTrees(view, preview, context.Catalog!, check);
                    if (source.Event == "DENSE_VEGETATION" && variant.Name == "royal-wheel")
                        check(HuntGeometry(view).MinWidth >= 350 * view.CardDisplayScale * .98f &&
                            HuntGeometry(view).MinHeight >= 470 * view.CardDisplayScale * .98f,
                            "the capped divergent Dense tree retains this viewport's normal readable native target card size in its roomy fixed region");
                    await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    string name = "hunt-prefix-" + source.Event.ToLowerInvariant() + "-" + variant.Name + "-" + size.X;
                    viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "operation-" + name + "-zh.png"));
                    evidence.Add(new { name, tree, limited = preview.Branches.Any(HuntShouldLimit), geometry = HuntGeometry(view),
                        natural = view.CardsDesiredSize.ToString(), bounds = new Vector2(size.X - 440, size.Y - 110).ToString(),
                        outcomes = preview.Branches.Select(branch => branch.HuntOutcomes) });
                    if (source.Event == "DENSE_VEGETATION" && variant.Name.StartsWith("royal-wheel", StringComparison.Ordinal))
                    {
                        Rect2 anchor = nativeOwner is { } actual ? new(actual.Position * (new Vector2(size.X, size.Y) / new Vector2(originalSize.X, originalSize.Y)),
                            actual.Size * (new Vector2(size.X, size.Y) / new Vector2(originalSize.X, originalSize.Y))) :
                            new(size.X * .55f, size.Y * .68f, size.X * .40f, size.Y * .10f);
                        var top = new Rect2(0, 0, size.X, size.Y * .075f);
                        var nativeTip = new Rect2(anchor.Position.X - 360, anchor.Position.Y, 360, 120);
                        var placement = view.PlanNativeHuntAreas(new(Vector2.Zero, size), anchor, [top], nativeTip) ??
                            NeowHoverPlacement.Plan(new(Vector2.Zero, size), anchor, [anchor], [top], view.SummaryDesiredSize,
                            view.CardsDesiredSize, false, view.CardsSizeForWidth, nativeTip, view.HasHuntTree);
                        view.SetLayout(placement.Summary, placement.Cards); await Frames();
                        CheckVisibleHuntTrees(view, preview, context.Catalog!, check);
                        if (variant.Name == "royal-wheel")
                            check(HuntGeometry(view).MinWidth >= 350 * view.CardDisplayScale * .98f &&
                                HuntGeometry(view).MinHeight >= 470 * view.CardDisplayScale * .98f,
                                "the real hover owner's compact Hunt layout retains the viewport's normal native card target without covering the option");
                        check(view.VisibleRegions.All(region => new Rect2(Vector2.Zero, size).Grow(1).Encloses(region)) &&
                            view.VisibleRegions.All(region => !region.Intersects(anchor) && !region.Intersects(top) &&
                                !region.Intersects(new Rect2(nativeTip.Position + placement.NativeTipShift, nativeTip.Size))),
                            "the actual hover planner keeps enlarged Hunt content within the viewport and clear of its real owner and top bar");
                        await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                        viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "operation-" + name + "-hover-zh.png"));
                        evidence.Add(new { name = name + "-hover", tree, geometry = HuntGeometry(view), nativeOwner = nativeOwner.HasValue,
                            anchor = anchor.ToString(), summary = placement.Summary.ToString(), cards = placement.Cards.ToString(),
                            natural = view.CardsDesiredSize.ToString() });
                    }
                }
                check(JsonSerializer.Serialize(snapshot.State) == unchanged, "Hunt tree rendering preserves the original detached numerical state");
            }
            File.WriteAllText(Path.Combine(output, "hunt-prefix-display-result.json"), JsonSerializer.Serialize(new
                { passed = true, nativeRenderer = true, cardOnlyTree = true, controlledEventSnapshots = true, naturalCombatClaim = false, evidence }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { view?.Free(); viewport.Size = originalSize; root.GetWindow().Size = originalWindow; await Frames(); }
    }
}
