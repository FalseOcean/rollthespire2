using System.IO.Compression;
using System.Text;
using System.Text.Json;
using RolltheSpire2.Infrastructure.Diagnostics;

internal static class FeedbackBundleChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string name)
        { if (!value) throw new InvalidOperationException("Feedback: " + name); checks++; }
        string root = Path.GetFullPath(".build-evidence/feedback-20260928/files-" + Guid.NewGuid().ToString("N"));
        string rt2 = Path.Combine(root, "rt2"), godot = Path.Combine(root, "godot");
        Directory.CreateDirectory(rt2); Directory.CreateDirectory(godot);
        string home = @"C:\Users\FeedbackTest";
        for (int i = 0; i < 7; i++)
        {
            foreach (string directory in new[] { rt2, godot })
            {
                string log = Path.Combine(directory, directory == rt2 ? $"rt2_{i}.log" : $"godot2026-09-{i:00}.log");
                File.WriteAllText(log, home + "\\log.txt\nsteam=76561191234567890\n你好\n");
                File.SetLastWriteTimeUtc(log, DateTime.UtcNow.AddDays(-i));
            }
        }
        string current = Path.Combine(rt2, "rt2_6.log"); // Deliberately old timestamp: current must still be selected.
        using var live = new FileStream(current, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        byte[] workload = Encoding.UTF8.GetBytes("productionSearchWorkload=true;sessionId=original;workloadJson={\"Options\":{\"StartSeed\":\"AAA\"}}\n");
        live.Write(workload); live.Flush();
        string native = Path.Combine(godot, "godot.log"); File.WriteAllText(native, "current godot");
        var request = new FeedbackBundleRequest(Path.Combine(root, "out"), rt2, current.Replace('\\', '/'), native.Replace('\\', '/'),
            JsonSerializer.Serialize(new { Path = home + "\\test", Build = "test" }), "{\"CurrentSeed\":\"BBB\"}",
            new Dictionary<string, string> { [home] = "<USER_HOME>" }, []);
        FeedbackBundleResult result = FeedbackBundle.Export(request);
        Check(result.Logs == 10 && result.Warnings == 0 && result.Bytes > 0, "5+5 complete logs including active writer");
        using (var archive = ZipFile.OpenRead(result.Path))
        {
            string Text(string path) { using var reader = new StreamReader(archive.GetEntry(path)!.Open()); return reader.ReadToEnd(); }
            Check(archive.GetEntry("rt2/rt2_6.log") is not null && archive.GetEntry("rt2/rt2_4.log") is null, "current wins over older rotation");
            Check(archive.Entries.Select(e => e.FullName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == archive.Entries.Count,
                "Godot/native path separators do not duplicate the current log");
            Check(archive.GetEntry("godot/godot.log") is not null && archive.GetEntry("godot/godot2026-09-04.log") is null, "Godot current and 4 latest");
            string text = Text("rt2/rt2_6.log");
            Check(!text.Contains(home) && !text.Contains("76561191234567890") && text.Contains("你好"), "paths/accounts masked, Unicode retained");
            using var env = JsonDocument.Parse(Text("environment.json"));
            Check(env.RootElement.GetProperty("Path").GetString() == "<USER_HOME>\\test", "escaped JSON path remains valid");
            Check(Text("current-editor.json").Contains("BBB") && Text("searches/rt2_6-latest-workload.json").Contains("AAA"), "current editor and historical search distinct");
            Check(Text("searches/rt2_6-provenance.txt").Contains("sessionId=original"), "search session provenance retained");
            using var manifest = JsonDocument.Parse(Text("manifest.json"));
            Check(manifest.RootElement.GetProperty("Logs").GetArrayLength() == 10, "manifest accounts for every log");
            Check(!archive.Entries.Any(x => x.FullName.EndsWith(".save")), "only explicitly selected evidence collected");
            Check(Text("issue-zh.md") == result.ChineseIssue.Body && Text("issue-en.md") == result.EnglishIssue.Body,
                "both cached issue bodies match their ZIP snapshot");
            Check(result.ChineseIssue.Body.Contains(Path.GetFileName(result.Path)) && result.EnglishIssue.Body.Contains("AAA"),
                "templates identify their bundle and current-log search start");
        }
        using (var bytes = new MemoryStream())
        {
            bytes.Write(Encoding.UTF8.GetBytes("before")); bytes.Position = 0;
            using var snapshot = new FeedbackBundle.SnapshotStream(bytes, bytes.Length);
            bytes.Position = bytes.Length; bytes.Write(Encoding.UTF8.GetBytes("after")); bytes.Position = 0;
            using var reader = new StreamReader(snapshot);
            Check(reader.ReadToEnd() == "before" && snapshot.ReadBytes == 6, "appends excluded from captured length");
        }
        using (var locked = new FileStream(native, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var partial = FeedbackBundle.Export(request);
            Check(partial.Warnings > 0 && partial.Logs == 9 && partial.Path != result.Path, "unavailable current log recorded, export stays unique");
            using var archive = ZipFile.OpenRead(partial.Path);
            using var reader = new StreamReader(archive.GetEntry("manifest.json")!.Open());
            Check(reader.ReadToEnd().Contains("unavailable"), "partial result explicitly marked");
        }
        var missing = FeedbackBundle.Export(request with { Rt2Directory = Path.Combine(root, "missing"), CurrentRt2Log = "", GodotLog = "" });
        Check(missing.Logs == 0 && missing.Warnings >= 2, "no evidence does not claim complete logs");
        string blocked = Path.Combine(root, "not-a-directory"); File.WriteAllText(blocked, "keep");
        bool failed = false;
        try { FeedbackBundle.Export(request with { OutputDirectory = blocked }); } catch (IOException) { failed = true; }
        Check(failed && File.ReadAllText(blocked) == "keep", "output failure preserves existing files");
        Check(!Directory.EnumerateFiles(request.OutputDirectory, "*.partial").Any(), "no unfinished archive after successful exports");
        string environment = JsonSerializer.Serialize(new { Rt2Build = "1.3.0+Release.test", Rt2LoadedAssemblySha256 = new string('A', 64),
            GameVersion = "0.111.0", Godot = "4.5.1", OS = "Microsoft Windows", Architecture = "X64",
            GPU = "GPU & test / 中文 #1", RenderingDriver = "d3d12", Mods = new[] { new { Name = "fixture", State = "Loaded" } },
            SearchRunning = false, CapturedUtc = "2026-09-28T08:00:00Z" });
        string editorJson = """{"Ascension":10,"Character":{"Serialized":"CHARACTER:IRONCLAD"},"Players":[]}""";
        foreach (string locale in new[] { "zh", "en" })
        {
            var issue = FeedbackIssueTemplate.Create(locale, environment, editorJson, "RT2-feedback-snapshot.zip", 5, 4, 1, "ABC123");
            string? url = FeedbackIssueTemplate.PrefillUrl(issue);
            Check(url is not null && url.StartsWith(FeedbackIssueTemplate.NewIssueUrl + "?title="), locale + " normal template fits prefill URL");
            string encodedBody = url![(url!.IndexOf("&body=", StringComparison.Ordinal) + 6)..];
            Check(Uri.UnescapeDataString(encodedBody) == issue.Body, locale + " Unicode and reserved characters round-trip");
            Check(issue.Body.Contains("CHARACTER:IRONCLAD") && issue.Body.Contains("A10") && issue.Body.Contains("ABC123"), locale + " recorded context populated");
            Check(!issue.Body.Contains("CPU") && !issue.Body.Contains("Memory"), locale + " uncaptured metrics are not invented");
            File.WriteAllText(Path.Combine(root, "issue-" + locale + ".md"), issue.Body);
            Console.WriteLine($"FeedbackIssueTemplate locale={locale};urlChars={url.Length}");
        }
        string party = """{"Ascension":7,"Players":[{"Character":{"Serialized":"CHARACTER:IRONCLAD"}},{"Character":{"Serialized":"CHARACTER:SILENT"}}]}""";
        var partyIssue = FeedbackIssueTemplate.Create("en", environment, party, "party.zip", 0, 0, 2, null);
        Check(partyIssue.Body.Contains("Multiplayer / A7 / P1: CHARACTER:IRONCLAD, P2: CHARACTER:SILENT"), "party slots retained");
        Check(FeedbackIssueTemplate.PrefillUrl(new("Report", new string('长', 5000))) is null, "long report uses explicit clipboard path");
        Check(missing.EnglishIssue.Body.Contains("Unavailable") && missing.EnglishIssue.Body.Contains("0 complete RT2 logs"), "missing evidence remains explicit");
        CheckIssueLedger(Check);
        Console.WriteLine($"FEEDBACK_EXPORT_PASS checks={checks};evidence={root}");
        return 0;
    }

    private static void CheckIssueLedger(Action<bool,string> check)
    {
        int fullLogs=0;
        var ledger=new RuntimeIssueLedger(4, (_,first) => { if(first) Interlocked.Increment(ref fullLogs); });
        Exception Failure()
        {
            try { throw new InvalidOperationException("ledger outer root",new IOException("ledger inner root")); }
            catch(Exception ex) { return ex; }
        }
        var one=Failure();
        Parallel.For(0,64,_=>ledger.Record(one,"LedgerFixture","FeedbackChecks","Worker","fixture.message",correlationId:"fixture-run"));
        var initial=ledger.Snapshot().Single();
        check(initial.OccurrenceCount==1 && fullLogs==1 && ledger.Revision==1,
            "concurrent observers of one thrown instance publish full evidence once");
        check(initial.ExceptionDetail.Contains("ledger outer root") && initial.ExceptionDetail.Contains("ledger inner root") &&
            initial.ExceptionDetail.Contains(nameof(CheckIssueLedger)) && initial.CorrelationId=="fixture-run" && initial.HasException,
            "complete stack, inner cause and correlation survive snapshot");
        Parallel.For(0,32,_=>ledger.Record(Failure(),"LedgerFixture","FeedbackChecks","Worker","fixture.message",correlationId:"fixture-run"));
        var repeated=ledger.Snapshot().Single();
        check(repeated.OccurrenceCount==33 && fullLogs==1 && repeated.FirstOccurredUtc==initial.FirstOccurredUtc &&
            repeated.LastOccurredUtc>=initial.LastOccurredUtc && ledger.Revision==33,
            "independent repeated occurrences aggregate atomically without repeating full log");
        check(initial.OccurrenceCount==1,"issued snapshots remain immutable after later occurrences");
        for(int i=0;i<7;i++) ledger.RecordDegradation("Capacity"+i,"FeedbackChecks","Startup","fixture.degradation","missing optimization "+i,"startup-run");
        var bounded=ledger.Snapshot();
        check(bounded.Count==4 && bounded[0].Code=="Capacity6" && bounded[^1].Code=="Capacity3",
            "ledger retains bounded most recent evidence and evicts old groups");
        check(bounded.All(i=>!i.HasException && i.Severity==RuntimeIssueSeverity.Warning) &&
            bounded[0].ExceptionDetail=="missing optimization 6",
            "explicit degradation stores copied warning detail without a fake exception");
        check(ledger.Record(one,"LedgerFixture","FeedbackChecks","Poll","fixture.message",correlationId:"fixture-run") is null && fullLogs==8,
            "eviction does not allow a still-live thrown instance to publish twice");
        var failingSink=new RuntimeIssueLedger(1,(_,_)=>throw new IOException("sink unavailable"));
        var original=Failure();
        try
        {
            try { throw original; }
            catch(Exception ex) { failingSink.Record(ex,"SinkFailure","FeedbackChecks","Worker","fixture.message"); throw; }
        }
        catch(Exception caught)
        {
            check(ReferenceEquals(caught,original) && failingSink.Snapshot().Single().ExceptionDetail.Contains("ledger inner root"),
                "logging sink failure never replaces the original exception rethrown by producer");
        }
        var evidence=failingSink.RecordEvidence("CopiedFault","FeedbackChecks","Document","fixture.message",initial.ExceptionDetail);
        check(evidence.Severity==RuntimeIssueSeverity.Error && evidence.ExceptionDetail==initial.ExceptionDetail && !evidence.HasException,
            "copied stack evidence records as Error without a synthetic exception object");
    }
}
