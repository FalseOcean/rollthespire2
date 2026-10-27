using RolltheSpire2.Ui.Pages.Search;
using System.Reflection;
using Godot;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Ui.Shell;

internal static partial class UiSurfaceChecks
{
    // Exercise the real workbench bindings with deterministic observations only.
    // No session, seed scan, clock wait, planner or calibration update is started.
    internal static void RunLiveEtaChecks(QueryWorkbenchFrame workbench)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(QueryWorkbenchFrame);
        object? Field(string name) => type.GetField(name, flags)!.GetValue(workbench);
        void Set(string name, object? value) => type.GetField(name, flags)!.SetValue(workbench, value);
        object? Call(string name, params object?[] values) => type.GetMethod(name, flags)!.Invoke(workbench, values);
        int checks = 0;
        void Require(bool ok, string message)
        {
            checks++;
            if (!ok) throw new InvalidOperationException("WorkbenchLiveEta:" + message);
        }
        Require(!workbench.HasActiveSearch && Field("_lastProgress") is null, "fixture needs a fresh idle workbench");
        var runtimeLabels = (Label?[])Field("_runValues")!;
        var expectationLabels = (Label?[])Field("_expectationValues")!;
        Require(runtimeLabels.All(label => label is not null) && expectationLabels.All(label => label is not null),
            "runtime and sidebar bindings exist");
        string[] savedFields = ["_runEta", "_lastProgress", "_currentScanningSpeed", "_predictedRunEta", "_displayedRunEta", "_showResults"];
        var original = savedFields.ToDictionary(name => name, Field);
        var labels = runtimeLabels.Concat(expectationLabels)
            .OfType<Label>().Select(label => (Label: label, label.Text, label.TooltipText)).ToArray();
        var plan = new FamilyExecutionPlan("LiveEta.Fixed", "Fixed", FamilyPlannerRankingAuthority.FixedExecutionOrder,
            [], 1, ["Fixed fixture"], []);
        var eta = new FamilySearchEtaProjectionV1(
            Status: FamilySearchEtaStatus.Ready, AcceptedResultProbability: .25,
            AcceptedResultProbabilityEvidence: "Fixed fixture", Plan: plan, TargetCount: 3, ScanCount: 1000,
            FirstMeanRootHorizon: 4, FirstP99RootHorizon: 17, TargetMeanRootHorizon: 12,
            FirstResultMeanMs: 1200, FirstResultP99Ms: 4200, TargetMeanMs: 3200, FixedScanRangeMs: 250000,
            ExpectedMatchesWithinScanRange: 250, TargetProbabilityWithinScanRange: 1,
            TargetFamilyPipelineMs: 3000, TargetExactTailMs: 100, TargetStages: [], MissingEvidence: [])
        { FirstResultSearchMeanMs = 1000, FirstResultSearchP99Ms = 4000, TargetSearchMeanMs = 3000 };
        SearchProgressSnapshot Progress(double seconds, long roots, int matches = 0, SearchRunState state = SearchRunState.Running) =>
            new(state, roots, matches, 1000, 3, seconds, 999999, SearchDisposition.NoMatch, "", state == SearchRunState.Cancelled);
        SearchEtaQuickView View() => (SearchEtaQuickView)Field("_displayedRunEta")!;
        void Observe(SearchProgressSnapshot progress)
        {
            Call("ObserveRuntimeProgress", progress);
            Call("UpdateRuntimeStats");
        }
        void CheckPredicted(string state)
        {
            Require(!View().IsLive && View().TargetMeanMs == 3000 && View().FirstResultMeanMs == 1000,
                state + " retains frozen predicted ETA");
            Require(expectationLabels[2]!.Text == (string)Call("Duration", 1000d)! &&
                runtimeLabels[3]!.Text == (string)Call("Duration", 1000d)!, state + " renders predicted ETA");
        }
        try
        {
            Set("_showResults", true);
            Call("BeginRuntimeProgress", eta, Progress(0, 0));
            Observe(Progress(10, 0));
            CheckPredicted("cold startup");
            Require(runtimeLabels[0]!.Text == "—", "cold startup has no invented speed");
            Observe(Progress(20, 100));
            Observe(Progress(20.5, 150));
            CheckPredicted("incomplete rolling window");
            Observe(Progress(21, 200, 1));
            Require(View().IsLive && View().FirstResultMeanMs == 40 && View().TargetMeanMs == 80,
                "live ETA uses rolling completed roots and frozen probability");
            Require((double?)Field("_currentScanningSpeed") == 100 && runtimeLabels[0]!.Text == (string)Call("Rate", 100d)!,
                "current speed excludes startup and ignores cumulative speed field");
            Require(expectationLabels[2]!.Text == (string)Call("Duration", 40d)!, "sidebar renders one-result live ETA");
            Require(ReferenceEquals(Field("_runEta"), eta) && eta.Plan == plan && eta.TargetSearchMeanMs == 3000,
                "live observations leave plan, probability and quote frozen");
            Require(expectationLabels[1]!.Text == (string)Call("Rate", 4d)!, "expected speed retains frozen quote");
            Observe(Progress(22, 200, 1));
            CheckPredicted("stalled completed-root window");
            foreach (var state in new[] { SearchRunState.Completed, SearchRunState.Cancelled, SearchRunState.Faulted })
            {
                Observe(Progress(23, 300, 3, state) with { ObservedScanningRoots = 300, ObservedScanningSeconds = 3 });
                CheckPredicted(state.ToString());
                Require((double?)Field("_currentScanningSpeed") == 100,
                    "terminal speed uses observed scanning time for " + state);
            }
            Call("BeginRuntimeProgress", eta, Progress(0, 0));
            Observe(Progress(1, 100));
            CheckPredicted("new session window reset");
            Require(Field("_currentScanningSpeed") is null, "new session cannot reuse prior speed samples");
            var unknown = eta with { AcceptedResultProbability = null, FirstResultSearchMeanMs = null,
                FirstResultSearchP99Ms = null, TargetSearchMeanMs = null, Status = FamilySearchEtaStatus.Unavailable };
            Call("BeginRuntimeProgress", unknown, Progress(0, 100));
            Observe(Progress(1, 200));
            Require(!View().IsLive && View().TargetMeanMs is null && View().AcceptedResultSurvivalRate is null,
                "observed speed does not invent an unknown probability or ETA");
            GD.Print($"WORKBENCH_LIVE_ETA_PASS checks={checks};deterministic=true;search=false;frozenQuote=true");
        }
        finally
        {
            ((SearchScanningSpeed)Field("_scanningSpeed")!).Reset();
            foreach (var entry in original) Set(entry.Key, entry.Value);
            foreach (var label in labels) { label.Label.Text = label.Text; label.Label.TooltipText = label.TooltipText; }
        }
    }
}
