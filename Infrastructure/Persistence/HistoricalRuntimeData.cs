using System.Security.Cryptography;
using System.Text.Json;
using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Infrastructure.Persistence;

// Exact retired-file allowlist. Never scan a workspace or infer age from names.
internal static class HistoricalRuntimeData
{
    private static readonly string[] Retired = ["family_local_performance_v1.json", "gpu_neow_calibration_p7_v1.json",
        "planner_reference_v1.json", "search_performance_profile_v1.json"];
    internal sealed record Entry(string OriginalPath,string ArchivedPath,string FileName,string Reason,
        string DetectedFormat,long Size,DateTimeOffset ArchivedAt,string Sha256);
    private sealed record Manifest(int SchemaVersion,List<Entry> Entries);
    internal sealed record Result(int Files,long Bytes,int Failures,string ArchivePath);
    private static readonly JsonSerializerOptions Json = new() {WriteIndented=true};
    private static readonly object Sync = new();

    internal static Result Archive(string dataRoot, Action<string,string>? move = null)
    {
        lock(Sync)
        {
            int files=0,failures=0;long bytes=0;string archive="";
            try
            {
                string root=Path.GetFullPath(dataRoot);archive=Child(root,"HistoricalTrash");
                string cache=Child(root,"cache");
                if(!Directory.Exists(cache))return new(0,0,0,archive);
                var candidates=Retired.Select(n=>Child(cache,n)).Where(File.Exists).ToArray();
                if(candidates.Length==0)return new(0,0,0,archive);
                CheckLinks(cache);CheckLinks(archive);
                Directory.CreateDirectory(archive);
                string readme=Child(archive,"README.md");
                if(!File.Exists(readme))File.WriteAllText(readme,Readme);
                string manifestPath=Child(archive,"manifest.json");
                var manifest=Load(manifestPath);
                string batch=Child(archive,DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss")+"_1_2_0_"+Guid.NewGuid().ToString("N")[..8]);
                foreach(string source in candidates)
                {
                    Entry? entry=null;
                    try
                    {
                        CheckLinks(source);
                        var info=new FileInfo(source);
                        if(info.Length>16*1024*1024)throw new InvalidDataException("RetiredFileTooLargeToIdentify");
                        byte[] content=File.ReadAllBytes(source);
                        string name=Path.GetFileName(source);
                        string format=Identify(name,content);
                        Directory.CreateDirectory(batch);
                        string target=Child(batch,name);
                        entry=new(source,target,name,"Retired runtime cache; no current Production reader",format,
                            content.LongLength,DateTimeOffset.UtcNow,Convert.ToHexString(SHA256.HashData(content)));
                        // Persist provenance before the atomic same-volume move.
                        // An interrupted move can leave a receipt without a file;
                        // that is never counted or deleted as archived data.
                        manifest.Entries.Add(entry);Save(manifestPath,manifest);
                        (move ?? ((s,t)=>File.Move(s,t)))(source,target);
                        files++;bytes+=entry.Size;
                        RuntimeLog.Detail($"historicalDataArchived=true;source={source};target={target};format={format}");
                    }
                    catch(Exception ex)
                    {
                        failures++;
                        if(entry is not null && !File.Exists(entry.ArchivedPath))
                        {
                            manifest.Entries.Remove(entry);
                            try{Save(manifestPath,manifest);}catch{ /* original stays untouched */ }
                        }
                        RuntimeLog.TryBackgroundWarning($"historicalDataArchiveWarning=true;file={Path.GetFileName(source)};reason={ex.GetType().Name}:{ex.Message};originalNotDeleted=true");
                    }
                }
            }
            catch(Exception ex){failures++;RuntimeLog.TryBackgroundWarning($"historicalDataArchiveWarning=true;reason={ex.GetType().Name}:{ex.Message};searchUnaffected=true");}
            if(files>0 || failures>0)RuntimeLog.TryBackgroundInfo($"historicalDataCleanup=true;filesMoved={files};bytesMoved={bytes};failures={failures};archivePath={archive}");
            return new(files,bytes,failures,archive);
        }
    }

    // Called only after the UI confirmation. Never recurse over arbitrary files.
    internal static Result DeleteArchived(string dataRoot)
    {
        lock(Sync)
        {
            int files=0,failures=0;long bytes=0;string archive="";
            try
            {
                string root=Path.GetFullPath(dataRoot);archive=Child(root,"HistoricalTrash");CheckLinks(archive);
                string path=Child(archive,"manifest.json");
                if(!File.Exists(path))return new(0,0,0,archive);
                var manifest=Load(path);
                foreach(var entry in manifest.Entries.ToArray())
                {
                    try
                    {
                        string archived=Path.GetFullPath(entry.ArchivedPath);
                        if(!Retired.Contains(entry.FileName,StringComparer.Ordinal) ||
                            !string.Equals(entry.OriginalPath,Child(Child(root,"cache"),entry.FileName),StringComparison.OrdinalIgnoreCase) ||
                            !archived.StartsWith(archive+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) ||
                            Path.GetFileName(archived)!=entry.FileName)
                            throw new InvalidDataException("ArchiveManifestPathRejected");
                        CheckLinks(archived);
                        if(!File.Exists(archived)){manifest.Entries.Remove(entry);continue;}
                        var info=new FileInfo(archived);
                        if(info.Length!=entry.Size || info.Length>16*1024*1024 ||
                            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archived)))!=entry.Sha256)
                            throw new InvalidDataException("ArchivedFileChanged;LeaveUntouched");
                        File.Delete(archived);files++;bytes+=entry.Size;manifest.Entries.Remove(entry);
                        string directory=Path.GetDirectoryName(archived)!;
                        if(!string.Equals(directory,archive,StringComparison.OrdinalIgnoreCase) && !Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory);
                    }
                    catch(Exception ex){failures++;RuntimeLog.TryBackgroundWarning($"historicalDataDeleteWarning=true;file={entry.FileName};reason={ex.GetType().Name}:{ex.Message}");}
                }
                Save(path,manifest);
            }
            catch(Exception ex){failures++;RuntimeLog.TryBackgroundWarning($"historicalDataDeleteWarning=true;reason={ex.GetType().Name}:{ex.Message}");}
            RuntimeLog.TryBackgroundInfo($"historicalDataDeleted=true;files={files};bytes={bytes};failures={failures};currentDataUntouched=true");
            return new(files,bytes,failures,archive);
        }
    }

    private static string Child(string root,string name)
    {
        string path=Path.GetFullPath(Path.Combine(root,name));
        if(!path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ArchivePathEscapesRoot");
        return path;
    }
    private static void CheckLinks(string path)
    {
        for(string? p=Path.GetFullPath(path);p is not null;p=Path.GetDirectoryName(p))
            if((Directory.Exists(p)||File.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("ArchiveReparsePointRejected");
    }
    private static Manifest Load(string path)
    {
        CheckLinks(path);
        if(!File.Exists(path))return new(1,[]);
        var value=JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path),Json);
        return value is {SchemaVersion:1,Entries:not null}?value:throw new InvalidDataException("ArchiveManifestUnknown");
    }
    private static void Save(string path,Manifest value)
    {
        CheckLinks(path);
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,JsonSerializer.Serialize(value,Json));File.Move(temp,path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
    private static string Identify(string name,byte[] content)
    {
        using var doc=JsonDocument.Parse(content);var r=doc.RootElement;
        string? Text(string key)=>r.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString():null;
        bool One(string key)=>r.TryGetProperty(key,out var v)&&v.TryGetInt32(out int n)&&n==1;
        bool Array(string key)=>r.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Array;
        bool known=name switch {
            "family_local_performance_v1.json"=>One("schemaVersion")&&Text("storeAbi")=="FamilyLocalPerformance.20260903.v1"&&Array("entries"),
            "gpu_neow_calibration_p7_v1.json"=>One("SchemaVersion")&&Text("RuntimeTarget")=="Beta110-0.110.1"&&r.TryGetProperty("PlanAbi",out var abi)&&abi.GetInt32()==8,
            "planner_reference_v1.json"=>One("schemaVersion")&&Text("referenceVersion")=="planner-reference-beta110-20260812-v1"&&Text("runtimeTarget")=="Modern110-Physical-v1",
            "search_performance_profile_v1.json"=>One("SchemaVersion")&&Text("RuntimeTarget")=="Modern110-Physical-v1"&&Array("Entries")&&r.TryGetProperty("MeasurementAttributionSchemaVersion",out var a)&&a.GetInt32()==2,
            _=>false
        };
        return known?name+":schema1":throw new InvalidDataException("RetiredFileFormatNotRecognized;LeaveUntouched");
    }
    private const string Readme="""
        # Archived RT2 runtime data / 已归档 RT2 历史数据

        RT2 moved recognized obsolete runtime caches here. Current Production no longer reads them.
        Active settings, presets, cursor, calibration, logs and project evidence were not moved.
        Keep these files safely for debugging. manifest.json records original paths and SHA256.
        An interrupted move may leave a receipt without a destination file; the original is then unchanged.
        To restore a file, close the game and copy it to its original path (never overwrite current data).
        The next startup may archive a restored obsolete cache again; keeping it here is preferable.
        “Delete archived historical data” in Settings permanently removes only unchanged manifested caches.
        Current settings/calibration are unaffected. README/manifest and unrecognized files remain.

        这里保存已识别的旧运行时缓存；当前程序不再读取。可以保留用于排错。
        manifest.json 记录原位置和校验值。中断的移动可能只留下记录，原文件仍在原处。
        手动恢复前请退出游戏，按原路径复制，勿覆盖当前数据；恢复后下次启动可能再次归档。
        设置中的“删除已归档历史数据”只永久删除清单内未被修改的旧缓存，不影响当前设置、校准或日志。
        """;
}
