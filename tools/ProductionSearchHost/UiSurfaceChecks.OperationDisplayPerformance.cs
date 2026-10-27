using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Cards;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    // A bounded display experiment in the existing native game fixture. Prepared
    // DTOs are reused verbatim; no capture, simulation or worker enters a sample.
    private static async Task RunOperationDisplayPerformance(Node root, string output,
        SubViewport viewport, Control hoverRoot, Player player)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var controller = new NeowOperationHoverController();
        hoverRoot.AddChild(controller); controller.SetProcess(false);
        var owner = new Control { Name = "DisplayPerformanceOwner", Position = new(650, 880), Size = new(500, 90),
            MouseFilter = Control.MouseFilterEnum.Ignore };
        hoverRoot.AddChild(owner);
        void Set(string name, object? value) => typeof(NeowOperationHoverController).GetField(name, flags)!.SetValue(controller, value);
        var position = (Action)typeof(NeowOperationHoverController).GetMethod("PositionView", flags)!.CreateDelegate(typeof(Action), controller);
        var hide = (Action)typeof(NeowOperationHoverController).GetMethod("HideView", flags)!.CreateDelegate(typeof(Action), controller);
        var reuse = (OperationCardDisplayReuse)typeof(NeowOperationHoverController).GetField("_cardDisplays", flags)!.GetValue(controller)!;
        var present = (Action<OperationPreview>)typeof(NeowOperationHoverController).GetMethod("PresentPreview", flags)!.CreateDelegate(typeof(Action<OperationPreview>), controller);
        Set("_owner", owner);
        var deck = player.Deck.Cards.DistinctBy(card => card.Id).ToArray();
        var cards = Enumerable.Range(0, 15).Select(index => { var card = deck[index % deck.Length]; return new PredictorCard(index + 1,
            new ModelKey(card.Id.Category, card.Id.Entry), Enum.Parse<EffectCardType>(card.Type.ToString()),
            card.CurrentUpgradeLevel, card.MaxUpgradeLevel, true, 0); }).ToImmutableArray();
        var style = new OperationDisplayStyle(OperationTextMode.GroupRows, OperationCardLayout.Groups);
        OperationPreview Prepared(string entry, int count, bool text, bool reverse)
        {
            var source = new ModelKey("RELIC", entry);
            var chosen = cards.Take(count).ToImmutableArray();
            var fixedCards = reverse ? chosen.Reverse().ToImmutableArray() : chosen;
            var groups = fixedCards.Chunk(entry == "ASTROLABE" ? 1 : 3).Select((chunk, index) =>
                new OperationPreviewCardGroup(index + 1, chunk.ToImmutableArray(), 0, 0, false, false)
                { Label = (index + 1).ToString(), Highlighted = entry == "ASTROLABE" && index == 1,
                    ForceSingleRow = entry == "ASTROLABE" }).ToImmutableArray();
            return new(source, [], [], false,
                [new([], [new(source, count == 0 ? [new(OperationPreviewAction.ObtainRelic, [new("RELIC", "BURNING_BLOOD")], [], false)] : [],
                    groups)
                    { Style = text ? style : style with { Text = OperationTextMode.None } }], [], 1, true)], [])
                { Style = text ? style : style with { Text = OperationTextMode.None },
                    Notices = text ? ["各位置为候选，组合取决于选牌顺序 / Candidates by position; combinations depend on selection order"] : [] };
        }
        var prepared = new[] {
            (Name: "astrolabe-3-cards", A: Prepared("ASTROLABE", 3, false, false), B: Prepared("ASTROLABE", 3, false, true)),
            (Name: "orrery-15-cards-text", A: Prepared("ORRERY", 15, true, false), B: Prepared("ORRERY", 15, true, true)),
            (Name: "bell-text-icon", A: Prepared("CALLING_BELL", 0, true, false), B: Prepared("CALLING_BELL", 0, true, true)) };
        var rows = new List<object>();
        var phases = new List<object>();
        var allNativeCards = new HashSet<NCard>();
        var correctness = new List<object>();
        string playerBefore = JsonSerializer.Serialize(player.ToSerializable());
        string PreparedSignature() => JsonSerializer.Serialize(prepared.Select(item => new { item.Name, item.A, item.B }));
        string preparedBefore = PreparedSignature();
        long now() => Stopwatch.GetTimestamp();
        (double Ms, long Bytes) Measure(Action action)
        {
            long bytes = GC.GetAllocatedBytesForCurrentThread(), start = now(); action();
            return (Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - bytes);
        }
        double[] Stats(IEnumerable<double> values)
        {
            var sorted = values.Order().ToArray();
            return [sorted.Average(), sorted[sorted.Length / 2], sorted[(int)((sorted.Length - 1) * .95)], sorted[^1]];
        }
        NeowOperationOverlay? view = null;
        async Task FrameSamples(string name, int frames, bool layout, long displayStarted = 0)
        {
            var intervals = new List<double>(); var work = new List<double>(); var alloc = new List<double>();
            var frameAlloc = new List<double>();
            string? previousRegions = null;
            int sameRegions = 0, firstVisibleFrame = -1, stableFrame = -1;
            double? firstVisibleMs = null, stableMs = null;
            for (int i = 0; i < frames; i++)
            {
                long start = now(), bytes = GC.GetAllocatedBytesForCurrentThread();
                var sample = layout ? Measure(position) : (Ms: 0d, Bytes: 0L);
                await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
                intervals.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds); work.Add(sample.Ms);
                alloc.Add(sample.Bytes); frameAlloc.Add(GC.GetAllocatedBytesForCurrentThread() - bytes);
                if (displayStarted != 0 && view is { Visible: true })
                {
                    string regions = string.Join(";", view.VisibleRegions);
                    if (regions.Length > 0 && firstVisibleFrame < 0)
                    { firstVisibleFrame = i + 1; firstVisibleMs = Stopwatch.GetElapsedTime(displayStarted).TotalMilliseconds; }
                    sameRegions = regions == previousRegions ? sameRegions + 1 : 0;
                    if (sameRegions >= 2 && stableFrame < 0)
                    { stableFrame = i + 1; stableMs = Stopwatch.GetElapsedTime(displayStarted).TotalMilliseconds; }
                    previousRegions = regions;
                }
            }
            phases.Add(new { name, frames, statsOrder = "mean,median,p95,max", intervalMs = Stats(intervals),
                positionMs = Stats(work), positionBytes = Stats(alloc), mainThreadFrameBytes = Stats(frameAlloc),
                rawIntervalMs = intervals, rawFrameBytes = frameAlloc,
                firstVisibleFrame, firstVisibleMs, stableFrame, stableMs });
        }
        void Require(bool value, string reason)
        { if (!value) throw new InvalidOperationException("DisplayPerformance: " + reason); }
        async Task Show(string name, OperationPreview preview)
        {
            if (view != null) hide();
            long displayStarted = now();
            var construct = Measure(() => { view = new NeowOperationOverlay(); view.UseCardDisplays(reuse); });
            var ready = Measure(() => { controller.AddChild(view!); Set("_view", view); });
            var build = Measure(() => view!.SetPreview(preview));
            var layout = Measure(position);
            rows.Add(new { name, constructMs = construct.Ms, constructBytes = construct.Bytes,
                addToTreeMs = ready.Ms, addToTreeBytes = ready.Bytes,
                setPreviewMs = build.Ms, setPreviewBytes = build.Bytes,
                layoutMs = layout.Ms, layoutBytes = layout.Bytes,
                nativeCreateMs = view!.NativeCardCreateMilliseconds, nativeReadyMs = view.NativeCardReadyMilliseconds,
                view.NativeCardCount, nativeNodes = Descendants(view).OfType<NCard>().Count(),
                nativeReused = view.NativeCardReuseCount, nativeCreated = view.NativeCardCount - view.NativeCardReuseCount,
                totalNodes = Descendants(view).Count() });
            await FrameSamples(name + "/show-subsequent", 12, true, displayStarted);
            Require(ReferenceEquals(typeof(NeowOperationOverlay).GetField("_preview", flags)!.GetValue(view), preview), "same prepared DTO");
            int expected = preview.Branches.SelectMany(b => b.Sections).SelectMany(s => s.CardGroups).Sum(g => g.Cards.Length);
            Require(view.NativeCardCount == expected && Descendants(view).OfType<NCard>().Count() == expected,
                "all native cards survive Ready");
            Require(Descendants(view).OfType<NCard>().All(card => card.Model is { RunState: null, CombatState: null }), "native models stay detached");
            Require(Descendants(view).OfType<NCard>().All(card => card.IsVisibleInTree() &&
                card.MouseFilter == Control.MouseFilterEnum.Ignore), "native Ready succeeded and remains passive");
            foreach (var card in Descendants(view).OfType<NCard>()) allNativeCards.Add(card);
            Require(view.Visible && view.VisibleRegions.Count > 0 && view.VisibleRegions.All(rect =>
                view.GetViewportRect().Grow(1).Encloses(rect) && !rect.Intersects(owner.GetGlobalRect())), "settled display stays visible and in bounds");
        }
        try
        {
            await FrameSamples("no-view-baseline", 60, false);
            foreach (var scenario in prepared)
            {
                await FrameSamples(scenario.Name + "/no-view-before", 30, false);
                await Show(scenario.Name + "/first-A", scenario.A);
                var removal = Measure(hide); view = null;
                rows.Add(new { name = scenario.Name + "/remove-first", removeMs = removal.Ms, removeBytes = removal.Bytes });
                await FrameSamples(scenario.Name + "/remove-subsequent", 8, false);
                await Show(scenario.Name + "/repeat-A", scenario.A);
                for (int repeat = 2; repeat <= 5; repeat++) await Show(scenario.Name + "/repeat-A-" + repeat, scenario.A);
                await Show(scenario.Name + "/switch-B", scenario.B);
                await Show(scenario.Name + "/return-A", scenario.A);
                await FrameSamples(scenario.Name + "/stationary", 120, true);
                // Same DTO and native nodes through viewport changes; catches stale
                // layout caches after Ready and verifies native card scale refresh.
                var oldSize = viewport.Size;
                viewport.Size = new(1680, 1260); root.GetWindow().Size = viewport.Size;
                await FrameSamples(scenario.Name + "/resized", 12, true);
                Require(view!.VisibleRegions.Count > 0 && view.VisibleRegions.All(rect => view.GetViewportRect().Grow(1).Encloses(rect)), "resize updates placement");
                Require(Descendants(view).OfType<NCard>().All(card =>
                    Math.Abs(card.GetParent<Control>().Scale.X - view.CardDisplayScale) < .001f), "resize updates native tile scale");
                viewport.Size = oldSize; root.GetWindow().Size = oldSize;
                await FrameSamples(scenario.Name + "/restored", 8, true);
                await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "display-" + scenario.Name + "-zh.png"));
                var remove = Measure(hide); view = null;
                rows.Add(new { name = scenario.Name + "/remove-final", removeMs = remove.Ms, removeBytes = remove.Bytes });
                await FrameSamples(scenario.Name + "/final-cleanup", 8, false);
                await FrameSamples(scenario.Name + "/no-view-after", 30, false);
            }
            OperationPreview SingleGroup(params PredictorCard[] snapshots) => new(new("RELIC", "ASTROLABE"), [], [], false,
                [new([], [new(new("RELIC", "ASTROLABE"), [], [new(1, snapshots.ToImmutableArray(), 0, 0, false, false)])], [], 1, true)], [])
                { Style = new(OperationTextMode.None, OperationCardLayout.Parallel) };
            async Task NativeCase(string name, OperationPreview preview, int expectedReuse)
            {
                present(preview);
                view = (NeowOperationOverlay)typeof(NeowOperationHoverController).GetField("_view", flags)!.GetValue(controller)!;
                position();
                await FrameSamples("correctness/" + name, 3, true);
                var expected = preview.Branches.SelectMany(b => b.Sections).SelectMany(s => s.CardGroups).SelectMany(g => g.Cards).ToArray();
                var actual = Descendants(view).OfType<NCard>().ToArray();
                Require(view.NativeCardCount == expected.Length && actual.Length == expected.Length &&
                    view.NativeCardReuseCount == expectedReuse, name + " preserves count and exact cache-hit multiplicity");
                Require(actual.Select(c => c.GetInstanceId()).Distinct().Count() == actual.Length,
                    name + " duplicate faces remain separate physical native cards");
                for (int i = 0; i < expected.Length; i++)
                {
                    var model = actual[i].Model!;
                    Require(model.Id.Entry == expected[i].Key.Entry && model.CurrentUpgradeLevel == expected[i].UpgradeLevel &&
                        model.Enchantment?.Id.Entry == expected[i].Enchantment?.Entry &&
                        (model.Enchantment?.Amount ?? 0) == expected[i].EnchantmentAmount &&
                        model.RunState == null && model.CombatState == null && actual[i].IsVisibleInTree(),
                        name + " native identity/upgrade/enchantment is current and detached");
                    allNativeCards.Add(actual[i]);
                }
                Require(reuse.Count == 0, name + " unmatched prior-display entries were cleared");
                correctness.Add(new { name, nativeCount = actual.Length, reused = view.NativeCardReuseCount,
                    created = view.NativeCardCount - view.NativeCardReuseCount,
                    nativeIds = actual.Select(card => card.GetInstanceId()).ToArray() });
            }
            var basic = cards[0];
            var upgraded = basic with { UpgradeLevel = 1 };
            var enchanted = basic with { Enchantment = new("ENCHANTMENT", "SHARP"), EnchantmentAmount = 2 };
            await NativeCase("production-first-duplicates", SingleGroup(basic, basic with { Id = 201 }, basic with { Id = 202 }), 0);
            await NativeCase("same-face-different-instance-id", SingleGroup(basic with { Id = 301 }, basic with { Id = 302 }, basic with { Id = 303 }), 3);
            await NativeCase("upgrade-changed", SingleGroup(basic, upgraded, basic), 2);
            await NativeCase("enchantment-changed", SingleGroup(enchanted, upgraded, basic), 2);
            await NativeCase("identity-changed", SingleGroup(cards[1], upgraded, basic), 2);
            await NativeCase("duplicate-count-increased", SingleGroup(basic, basic, basic, basic, basic), 1);
            await NativeCase("same-five-duplicates", SingleGroup(basic, basic, basic, basic, basic), 5);
            MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage("eng");
            await NativeCase("language-changed-eng", SingleGroup(basic, basic, basic, basic, basic), 0);
            MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage("zhs");
            await NativeCase("language-restored-zhs", SingleGroup(basic), 0);
            // Existing native fallback contract for an unsupported persistent
            // display attribute: no successful native tile may enter reuse.
            present(SingleGroup(basic with { PermanentCostOverride = 0 }));
            view = (NeowOperationOverlay)typeof(NeowOperationHoverController).GetField("_view", flags)!.GetValue(controller)!;
            position(); await FrameSamples("correctness/fallback", 3, true);
            Require(view.NativeCardCount == 1 && view.NativeCardReuseCount == 0 && !Descendants(view).OfType<NCard>().Any(),
                "failed native construction uses existing visible fallback");
            hide(); view = null;
            Require(reuse.Count == 0, "failed native construction never enters reuse");
            correctness.Add(new { name = "failed-native-construction-not-retained", passed = true });
            await NativeCase("after-fallback", SingleGroup(basic), 0);
            Require(JsonSerializer.Serialize(player.ToSerializable()) == playerBefore && PreparedSignature() == preparedBefore,
                "native display reuse changes neither fixture player nor prepared DTOs");
            hide(); view = null;
            Require(reuse.Count == 1 && allNativeCards.Where(GodotObject.IsInstanceValid).All(card => !card.IsInsideTree()),
                "hide detaches retained native faces from render tree");
            controller.Free();
            await FrameSamples("correctness/controller-destroy-cleanup", 8, false);
            Require(reuse.Count == 0 && allNativeCards.All(card => !GodotObject.IsInstanceValid(card)), "controller exit frees all retained native faces");
            correctness.Add(new { name = "controller-exit-clears-native-retention-and-immutable-inputs", passed = true });
            var resolver = new List<object>();
            for (int i = 0; i < 8; i++)
            {
                var cost = Measure(() => GC.KeepAlive(new ReflectionGameIconResolver("display-performance")));
                resolver.Add(new { iteration = i, ms = cost.Ms, bytes = cost.Bytes });
            }
            File.WriteAllText(Path.Combine(output, "operation-display-performance.json"), JsonSerializer.Serialize(new
            {
                passed = true, renderer = RenderingServer.GetCurrentRenderingMethod(), driver = RenderingServer.GetCurrentRenderingDriverName(),
                runtime = System.Environment.Version.ToString(), processId = System.Environment.ProcessId,
                language = MegaCrit.Sts2.Core.Localization.LocManager.Instance.Language,
                semantics = "Synthetic display fixtures: Astrolabe three ordinal groups of one, middle highlighted, force single row; Orrery five groups of three plus Chinese text; Bell text/relic icon. Distinct native starter-deck identities repeat to fixed counts. Same immutable DTO per A/repeat/return; B reverses order. No prediction claims. Stable means three consecutive identical visible-region samples, bounded12frames, not proof of GPU completion.",
                limits = "Process-frame wall intervals include rendering, engine scheduling, native work and fixture overhead; not exclusive CPU/GPU. Current-thread managed allocation excludes native allocations. nativeReady is overlay Ready-signal callback, not the native NCard._Ready body; nativeCreate and nativeReady are nested build subsets, not additive wall stages. First card sample is first surface in this initialized game fixture, not cold OS/driver. Synthetic owner uses actual controller PositionView and native top bar; no native hover tip/relic inventory obstacles. Instrumentation itself adds bounded frame overhead.",
                measurements = rows, frames = phases, resolverConstruction = resolver, correctness
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            if (view != null) hide();
            if (GodotObject.IsInstanceValid(controller)) controller.Free();
            owner.Free();
        }
    }
}
