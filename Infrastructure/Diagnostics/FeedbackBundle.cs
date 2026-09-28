using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RolltheSpire2.Infrastructure.Diagnostics;

// All game/Godot values are captured by the caller on the main thread.
internal sealed record FeedbackBundleRequest(string OutputDirectory, string Rt2Directory,
    string CurrentRt2Log, string GodotLog, string EnvironmentJson, string? EditorJson,
    IReadOnlyDictionary<string, string> PrivatePaths, IReadOnlyList<string> CaptureWarnings);
internal sealed record FeedbackBundleResult(string Path, long Bytes, int Logs, int Warnings, bool Oversized,
    FeedbackIssueText ChineseIssue, FeedbackIssueText EnglishIssue);

/// <summary>Local, bounded-at-open log snapshots. No network, save files or game state access.</summary>
internal static class FeedbackBundle
{
    internal const long AttachmentLimit = 25_000_000;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly Regex SteamAccount = new(@"\b7656119\d{10}\b", RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private sealed record LogSource(string Kind, string Path, bool Current);
    private sealed record Receipt(string Entry, bool Current, long SnapshotBytes, long ReadBytes,
        DateTime? LastWriteUtc, string Status);

    internal static FeedbackBundleResult Export(FeedbackBundleRequest request)
    {
        Directory.CreateDirectory(request.OutputDirectory);
        string path = Path.Combine(request.OutputDirectory,
            $"RT2-feedback-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
        string pending = path + ".partial";
        var warnings = request.CaptureWarnings.ToList();
        var receipts = new List<Receipt>();
        var sources = new List<LogSource>();
        FeedbackIssueText chineseIssue = null!, englishIssue = null!;
        string? currentLogSeed = null;
        SelectLogs("rt2", request.Rt2Directory, "rt2_", ".log", request.CurrentRt2Log);
        if (!string.IsNullOrWhiteSpace(request.GodotLog))
            SelectLogs("godot", Path.GetDirectoryName(request.GodotLog)!,
                Path.GetFileNameWithoutExtension(request.GodotLog), Path.GetExtension(request.GodotLog), request.GodotLog);
        else warnings.Add("Godot log path unavailable.");
        var substitutions = request.PrivatePaths.Where(p => !string.IsNullOrWhiteSpace(p.Key))
            .SelectMany(p => new[] { p, new KeyValuePair<string, string>(p.Key.Replace('\\', '/'), p.Value),
                new KeyValuePair<string, string>(JsonSerializer.Serialize(p.Key)[1..^1], p.Value) })
            .OrderByDescending(p => p.Key.Length).ToArray();
        string Redact(string value)
        {
            foreach (var p in substitutions) value = value.Replace(p.Key, p.Value, StringComparison.OrdinalIgnoreCase);
            // Keep numeric JSON fields valid. This is diagnostic evidence, not a replay authority snapshot.
            return SteamAccount.Replace(value, "0");
        }
        try
        {
            using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
            {
                void Write(string name, string text)
                {
                    using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                    writer.Write(Redact(text));
                }
                Write("environment.json", request.EnvironmentJson);
                if (request.EditorJson is not null) Write("current-editor.json", request.EditorJson);
                foreach (LogSource source in sources)
                {
                    string entryName = source.Kind + "/" + Path.GetFileName(source.Path);
                    long captured = 0, read = 0;
                    DateTime? modified = null;
                    string status = "complete", latestWorkload = "", provenance = "";
                    FileStream file;
                    try
                    {
                        file = new FileStream(source.Path, FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        warnings.Add(entryName + ": " + ex.GetType().Name);
                        receipts.Add(new(entryName, source.Current, 0, 0, null, "unavailable"));
                        continue;
                    }
                    using (file)
                    {
                        captured = file.Length; modified = File.GetLastWriteTimeUtc(source.Path);
                        using var snapshot = new SnapshotStream(file, captured);
                        using var reader = new StreamReader(snapshot, Encoding.UTF8, true);
                        using var writer = new StreamWriter(zip.CreateEntry(entryName, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
                        try
                        {
                            while (true)
                            {
                                string? line;
                                try { line = reader.ReadLine(); }
                                catch (IOException ex)
                                { status = "partial"; warnings.Add(entryName + ": " + ex.GetType().Name); break; }
                                if (line is null) break;
                                writer.WriteLine(Redact(line));
                                const string marker = ";workloadJson=";
                                int at = source.Kind == "rt2" && line.Contains("productionSearchWorkload=true", StringComparison.Ordinal)
                                    ? line.IndexOf(marker, StringComparison.Ordinal) : -1;
                                if (at >= 0) { latestWorkload = line[(at + marker.Length)..]; provenance = line[..at]; }
                            }
                        }
                        finally { read = snapshot.ReadBytes; }
                        if (read < captured) { status = "partial"; warnings.Add(entryName + ": file shrank while being read."); }
                    }
                    receipts.Add(new(entryName, source.Current, captured, read, modified, status));
                    if (latestWorkload.Length > 0)
                    {
                        try
                        {
                            using var document = JsonDocument.Parse(latestWorkload);
                            if (source.Current && document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("Options", out var options) &&
                                options.ValueKind == JsonValueKind.Object && options.TryGetProperty("StartSeed", out var seed) && seed.ValueKind == JsonValueKind.String)
                                currentLogSeed = seed.GetString();
                            string stem = "searches/" + Path.GetFileNameWithoutExtension(source.Path);
                            Write(stem + "-latest-workload.json", document.RootElement.GetRawText());
                            Write(stem + "-provenance.txt", provenance);
                        }
                        catch (JsonException) { warnings.Add(entryName + ": latest workload JSON incomplete; inspect the log."); }
                    }
                }
                int rt2Logs = receipts.Count(r => r.Status == "complete" && r.Entry.StartsWith("rt2/", StringComparison.Ordinal));
                int godotLogs = receipts.Count(r => r.Status == "complete" && r.Entry.StartsWith("godot/", StringComparison.Ordinal));
                chineseIssue = FeedbackIssueTemplate.Create("zh", Redact(request.EnvironmentJson),
                    request.EditorJson is null ? null : Redact(request.EditorJson), Path.GetFileName(path), rt2Logs, godotLogs, warnings.Count, currentLogSeed);
                englishIssue = FeedbackIssueTemplate.Create("en", Redact(request.EnvironmentJson),
                    request.EditorJson is null ? null : Redact(request.EditorJson), Path.GetFileName(path), rt2Logs, godotLogs, warnings.Count, currentLogSeed);
                Write("issue-zh.md", chineseIssue.Body);
                Write("issue-en.md", englishIssue.Body);
                Write("README.txt", """
                    RT2 feedback bundle / 问题反馈包

                    This bundle was exported locally. Nothing has been uploaded.
                    该压缩包仅保存在本地，尚未上传。请自行附到 GitHub Issue 或邮件中。
                    Describe your steps, expected result and actual result; add screenshots when useful.
                    请说明操作步骤、预期结果与实际现象，界面问题请附截图。

                    environment.json: environment at export time; RT2 hash captured when the mod loaded.
                    current-editor.json: editor at export time, NOT necessarily the failed search.
                    searches/: latest logged workload per included RT2 log, with its session/start provenance.
                    These workloads are NOT automatically classified as the failing search. Earlier searches
                    and errors remain in the logs; match session IDs before drawing conclusions.
                    当前编辑条件不等于出错时的查询。每份日志的最近搜索也不一定是出错搜索，请按会话 ID 核对。
                    manifest.json: log selection, snapshot lengths, timestamps and missing/partial evidence.
                    issue-zh.md / issue-en.md: prefilled Chinese/English report bodies for this bundle.
                    Current logs are included first, followed by recent logs, up to five per source.
                    RT2 is flushed before capture; Godot contains only data already written to disk.
                    A running log is read only up to its length when opened; later records are not included.

                    No save files are collected. Known local user paths and Steam account numbers are masked;
                    third-party logs may contain other personal information. Review before sharing publicly.
                    不收集存档；已遮盖已知用户路径和 Steam 账号数字，但无法保证第三方日志没有其他个人信息。
                    GitHub attachments on public issues are public. Review the ZIP before attaching it.
                    ZIPs above GitHub's attachment limit are kept intact; no logs are silently removed.
                    """);
                Write("manifest.json", JsonSerializer.Serialize(new { SchemaVersion = 1, ExportedUtc = DateTime.UtcNow,
                    Selection = "Current log first, then newest; at most 5 RT2 + 5 Godot logs",
                    Logs = receipts, Warnings = warnings }, Json));
            }
            File.Move(pending, path);
            long size = new FileInfo(path).Length;
            return new(path, size, receipts.Count(x => x.Status == "complete"), warnings.Count, size > AttachmentLimit,
                chineseIssue, englishIssue);
        }
        catch
        {
            try { File.Delete(pending); } catch { /* Preserve the original export failure. */ }
            throw;
        }

        void SelectLogs(string kind, string directory, string prefix, string extension, string current)
        {
            try
            {
                // Godot uses '/', while FileInfo.FullName uses native separators on Windows.
                string currentFull = string.IsNullOrWhiteSpace(current) ? "" : Path.GetFullPath(current);
                var files = Directory.EnumerateFiles(directory).Select(p => new FileInfo(p))
                    .Where(f => f.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                        f.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => f.LastWriteTimeUtc).ThenBy(f => f.Name, StringComparer.Ordinal).Select(f => f.FullName).ToList();
                if (currentFull.Length > 0)
                {
                    files.RemoveAll(p => string.Equals(p, currentFull, StringComparison.OrdinalIgnoreCase));
                    files.Insert(0, currentFull); // Keep a missing current log in the manifest as missing evidence.
                }
                if (files.Count == 0) warnings.Add(kind + ": no log files found.");
                sources.AddRange(files.Take(5).Select(p => new LogSource(kind, p,
                    string.Equals(p, currentFull, StringComparison.OrdinalIgnoreCase))));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { warnings.Add(kind + ": log directory unavailable (" + ex.GetType().Name + ")."); }
        }
    }

    // StreamReader cannot consume records appended after this snapshot was opened.
    internal sealed class SnapshotStream(Stream source, long length) : Stream
    {
        internal long ReadBytes { get; private set; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            int count = source.Read(buffer[..(int)Math.Min(buffer.Length, Math.Max(0, length - ReadBytes))]);
            ReadBytes += count; return count;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => ReadBytes; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
