using System.Text.Json;
using System.Text.Json.Nodes;
using RolltheSpire2.Infrastructure.Persistence;

internal static class HistoricalDataChecks
{
    internal static int ArchiveCurrent()
    {
        string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"SlayTheSpire2","RolltheSpire2");
        Console.WriteLine(JsonSerializer.Serialize(HistoricalRuntimeData.Archive(root)));
        return 0;
    }
    internal static int Run()
    {
        int checks=0;
        void Check(bool b,string s){checks++;if(!b)throw new InvalidOperationException(s);}
        string root=Path.GetFullPath(".build-evidence/release-closure/data-"+Guid.NewGuid().ToString("N"));
        string cache=Path.Combine(root,"cache");Directory.CreateDirectory(cache);
        string old=Path.Combine(cache,"family_local_performance_v1.json");
        string content="""{"schemaVersion":1,"storeAbi":"FamilyLocalPerformance.20260903.v1","entries":[]}""";
        File.WriteAllText(old,content);
        File.WriteAllText(Path.Combine(cache,"gpu_neow_calibration_p7_v1.json"),"""{"SchemaVersion":1,"RuntimeTarget":"Beta110-0.110.1","PlanAbi":8}""");
        File.WriteAllText(Path.Combine(cache,"planner_reference_v1.json"),"""{"schemaVersion":1,"referenceVersion":"planner-reference-beta110-20260812-v1","runtimeTarget":"Modern110-Physical-v1"}""");
        File.WriteAllText(Path.Combine(cache,"search_performance_profile_v1.json"),"""{"SchemaVersion":1,"RuntimeTarget":"Modern110-Physical-v1","MeasurementAttributionSchemaVersion":2,"Entries":[]}""");
        string[] protectedFiles=["cache/cpu_cost_peaks_v1.json","cache/gpu_cost_peaks_v1.json","cache/search_predictability_verification_v1.json",
            "state/user_preferences.json","state/search_workspace.json","logs/rt2-current.log",".build-evidence/reference.json"];
        foreach(string relative in protectedFiles){string f=Path.Combine(root,relative);Directory.CreateDirectory(Path.GetDirectoryName(f)!);File.WriteAllText(f,"KEEP:"+relative);}
        var first=HistoricalRuntimeData.Archive(root);Check(first.Files==4&&first.Failures==0,"first archive");
        Check(!File.Exists(old)&&HistoricalRuntimeData.Archive(root).Files==0,"idempotent startup");
        string mp=Path.Combine(first.ArchivePath,"manifest.json");
        string manifest=File.ReadAllText(mp);var node=JsonNode.Parse(manifest)!;
        Check(node["Entries"]!.AsArray().Count==4&&File.ReadAllText(Path.Combine(first.ArchivePath,"README.md")).Contains("当前"),"manifest/readme");
        foreach(var e in node["Entries"]!.AsArray())
            Check(File.Exists(e!["ArchivedPath"]!.GetValue<string>())&&e["Sha256"]!.GetValue<string>().Length==64&&e["DetectedFormat"] is not null,"archive provenance");
        string unknown=Path.Combine(first.ArchivePath,"my-debug-note.txt");File.WriteAllText(unknown,"KEEP unknown");
        node["Entries"]![0]!["ArchivedPath"]=Path.Combine(root,"cache/cpu_cost_peaks_v1.json");File.WriteAllText(mp,node.ToJsonString());
        var tamper=HistoricalRuntimeData.DeleteArchived(root);Check(tamper.Failures==1&&File.Exists(Path.Combine(root,"cache/cpu_cost_peaks_v1.json")),"tampered manifest cannot escape archive");
        File.WriteAllText(mp,manifest);
        var deletion=HistoricalRuntimeData.DeleteArchived(root);Check(deletion.Files==1&&deletion.Failures==0,"confirmed deletion only manifested files");
        Check(File.Exists(unknown)&&HistoricalRuntimeData.DeleteArchived(root).Files==0,"unknown files and repeated delete");
        foreach(string relative in protectedFiles)Check(File.ReadAllText(Path.Combine(root,relative))=="KEEP:"+relative,"protected "+relative);
        File.WriteAllText(old,content);
        var failure=HistoricalRuntimeData.Archive(root,(_,_)=>throw new IOException("InjectedMoveFailure"));
        Check(failure.Files==0&&failure.Failures==1&&File.ReadAllText(old)==content,"failed atomic move preserves original");
        using(var locked=new FileStream(old,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            Check(HistoricalRuntimeData.Archive(root).Failures==1,"locked original untouched");
        File.WriteAllText(old,"not json");Check(HistoricalRuntimeData.Archive(root).Failures==1&&File.ReadAllText(old)=="not json","malformed not moved");
        File.WriteAllText(old,content.Replace("schemaVersion\":1","schemaVersion\":99"));
        Check(HistoricalRuntimeData.Archive(root).Failures==1&&File.Exists(old),"future schema not moved");
        File.WriteAllText(old,content);Check(HistoricalRuntimeData.Archive(root).Files==1,"retry after failure");
        var entries=JsonNode.Parse(File.ReadAllText(mp))!["Entries"]!.AsArray();
        string archived=entries.Last()!["ArchivedPath"]!.GetValue<string>();File.AppendAllText(archived,"changed");
        Check(HistoricalRuntimeData.DeleteArchived(root).Failures==1&&File.Exists(archived),"modified archive is not silently deleted");
        Console.WriteLine($"HISTORICAL_DATA PASS checks={checks};root={root};actualUserDataUntouched=true");
        return 0;
    }
}
