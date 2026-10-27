using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Ui.Pages.Analysis;
using RolltheSpire2.Ui.Controllers;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;

internal static partial class PartyPhase1Checks
{
    private sealed record RunEntryReuseProbe(string? Binding, string RecomputedKey, bool Loaded,
        bool GlobalComplete, string Workspace, AnalysisPage? Page, object? Document, bool Visible,
        string InterpretationStatus, string InterpretationReason, bool InterpretationKeyPresent,
        string? EffectiveContext, string? EffectiveContextIssue, int? DisplayedChoice, string? DisplayedRoute);

    // Explicit diagnostic call sites only. This reads the exact production key
    // inputs without recapturing catalogs or touching production/performance paths.
    private static RunEntryReuseProbe CaptureRunEntryReuseProbe(WorkspaceShell shell, RunState run, ModRuntimeSnapshot runtime)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(WorkspaceShell).GetField(name,flags)!.GetValue(shell);
        var store = (SearchWorkspace)Field("_persistence")!;
        var context = WorkspaceShell.CaptureRunPredictionContext(run,runtime.Detection.NormalizedVersion,
            runtime.Profile.ProfileId,LocalContext.NetId,store.PredictorContext);
        var environment = RuntimeAuthorityEnvironment.Current.Authority;
        string key = JsonSerializer.Serialize(new { Seed=run.Rng.StringSeed, Context=context,
            environment.Fingerprint, environment.CaptureIssues });
        var page = (AnalysisPage?)Field("_predictor");
        string? effective = null, effectiveIssue = null;
        try { effective = JsonSerializer.Serialize(((AnalysisPageController)Field("_predictorController")!).CaptureSeedLibraryContext()); }
        catch (Exception ex) { effectiveIssue = ex.ToString(); }
        return new((string?)Field("_livePredictionBinding"),key,(bool)Field("_runPredictionLoaded")!,
            environment.Fingerprint.Complete,Field("_workspace")!.ToString()!,page,page?.LastDocument,page?.Visible == true,
            RuntimeAuthorityEnvironment.Current.Interpretation.EnvironmentStatus.ToString(),
            RuntimeAuthorityEnvironment.Current.Interpretation.EnvironmentReason,
            ((string?)typeof(RuntimeAuthorityEnvironment).GetField("_interpretationKey",BindingFlags.Static|BindingFlags.NonPublic)!
                .GetValue(null))?.Length > 0, effective, effectiveIssue,
            page?.PreferredOpeningChoiceSlotIndex, page?.PreferredOpeningRouteId);
    }

    private static void LogRunEntryReuseProbe(RunEntryReuseProbe before, RunEntryReuseProbe after, string output)
    {
        var differences = new List<object>();
        void Diff(JsonNode? a, JsonNode? b, string path)
        {
            if (JsonNode.DeepEquals(a,b)) return;
            if (a is JsonObject ao && b is JsonObject bo)
            {
                foreach (string key in ao.Select(p => p.Key).Concat(bo.Select(p => p.Key)).Distinct())
                    Diff(ao[key],bo[key],path+"."+key);
            }
            else if (a is JsonArray aa && b is JsonArray ba && aa.Count == ba.Count)
            { for (int i=0;i<aa.Count;i++) Diff(aa[i],ba[i],path+"["+i+"]"); }
            else differences.Add(new { Path=path, Before=a?.ToJsonString(), After=b?.ToJsonString() });
        }
        Diff(JsonNode.Parse(before.Binding ?? "null"),JsonNode.Parse(after.RecomputedKey),"StoredBefore_vs_CurrentAfter");
        var receipt = new
        {
            Before = new { before.Loaded,before.GlobalComplete,before.Workspace,before.Visible,before.InterpretationStatus,before.InterpretationReason,before.InterpretationKeyPresent,before.EffectiveContextIssue,before.DisplayedChoice,before.DisplayedRoute,
                StoredMatchesCurrent=before.Binding==before.RecomputedKey },
            After = new { after.Loaded,after.GlobalComplete,after.Workspace,after.Visible,after.InterpretationStatus,after.InterpretationReason,after.InterpretationKeyPresent,after.EffectiveContextIssue,after.DisplayedChoice,after.DisplayedRoute,
                StoredMatchesCurrent=after.Binding==after.RecomputedKey },
            SamePage=ReferenceEquals(before.Page,after.Page), SameDocument=ReferenceEquals(before.Document,after.Document),
            BeforeBinding=before.Binding, BeforeRecomputed=before.RecomputedKey,
            AfterBinding=after.Binding, AfterRecomputed=after.RecomputedKey,
            BeforeEffectiveContext=before.EffectiveContext, AfterEffectiveContext=after.EffectiveContext, Differences=differences
        };
        File.WriteAllText(Path.Combine(output,"run-entry-reuse-key-diff.json"),
            JsonSerializer.Serialize(receipt,new JsonSerializerOptions { WriteIndented=true }));
        GD.Print("RUN_ENTRY_REUSE_PROBE "+JsonSerializer.Serialize(new {
            receipt.Before,receipt.After,receipt.SamePage,receipt.SameDocument,
            DifferenceCount=differences.Count, Differences=differences.Take(24) }));
    }
}
