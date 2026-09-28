using System.Text.Json;

namespace RolltheSpire2.Infrastructure.Diagnostics;

internal sealed record FeedbackIssueText(string Title, string Body);

/// <summary>Two portable templates derived from the same redacted snapshot as the ZIP.</summary>
internal static class FeedbackIssueTemplate
{
    internal const string NewIssueUrl = "https://github.com/FalseOcean/rollthespire2/issues/new";
    // Keep query strings bounded. Long reports remain available verbatim through copy / ZIP.
    internal const int MaxPrefillUrlLength = 7500;

    internal static FeedbackIssueText Create(string language, string environmentJson, string? editorJson,
        string bundleName, int rt2Logs, int godotLogs, int warnings, string? latestSearchSeed)
    {
        bool zh = language == "zh";
        string unknown = zh ? "未取得" : "Unavailable";
        using var environment = JsonDocument.Parse(environmentJson);
        JsonElement env = environment.RootElement;
        string Field(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item)
            && item.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) ? Scalar(item.ToString()) : unknown;
        string build = Field(env, "Rt2Build");
        string mods = env.TryGetProperty("Mods", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.GetArrayLength().ToString(System.Globalization.CultureInfo.InvariantCulture) : unknown;
        string running = env.TryGetProperty("SearchRunning", out var search) && search.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? search.GetBoolean() ? zh ? "正在搜索" : "Running" : zh ? "未在搜索" : "Not running" : unknown;
        string editorSummary = unknown;
        if (editorJson is not null)
        {
            using var editor = JsonDocument.Parse(editorJson);
            JsonElement draft = editor.RootElement;
            string Character(JsonElement player)
            {
                if (!player.TryGetProperty("Character", out var character)) return unknown;
                return character.ValueKind == JsonValueKind.Object ? Field(character, "Serialized") : Scalar(character.ToString());
            }
            if (draft.ValueKind == JsonValueKind.Object)
            {
                string ascension = Field(draft, "Ascension");
                if (draft.TryGetProperty("Players", out var players) && players.ValueKind == JsonValueKind.Array && players.GetArrayLength() > 0)
                    editorSummary = (zh ? "多人" : "Multiplayer") + " / A" + ascension + " / " +
                        string.Join(", ", players.EnumerateArray().Take(4).Select((p, i) => $"P{i + 1}: {Character(p)}"));
                else editorSummary = (zh ? "单人" : "Single-player") + " / " + Character(draft) + " / A" + ascension;
            }
        }
        string seed = string.IsNullOrWhiteSpace(latestSearchSeed) ? unknown : Scalar(latestSearchSeed);
        string collected = zh ? $"RT2 {rt2Logs} 份完整日志，Godot {godotLogs} 份；收集提示 {warnings} 项（见 manifest.json）。"
            : $"{rt2Logs} complete RT2 logs, {godotLogs} Godot logs; {warnings} collection notices (see manifest.json).";
        string titleVersion = build.Split('+')[0];
        if (titleVersion.Length > 64) titleVersion = titleVersion[..64] + "…";
        string title = zh ? $"[Bug] RT2 {titleVersion} - 请简述问题" : $"[Bug] RT2 {titleVersion} - Describe the issue";
        string body = zh ? $"""
            ## 环境与上下文（自动填写）
            以下是导出时的信息，不一定是故障发生时的状态。完整环境、模组列表及查询见 ZIP。

            - RT2 构建：{build}
            - DLL SHA256：{Field(env, "Rt2LoadedAssemblySha256")}
            - 游戏 / Godot：{Field(env, "GameVersion")} / {Field(env, "Godot")}
            - 系统 / 进程架构：{Field(env, "OS")} / {Field(env, "Architecture")}
            - GPU / 图形后端：{Field(env, "GPU")} / {Field(env, "RenderingDriver")}
            - 模组记录：{mods} 项，加载状态与版本见 environment.json
            - 导出时间（UTC）：{Field(env, "CapturedUtc")}
            - 导出时搜索状态：{running}
            - 当前筛选编辑器：{editorSummary}
            - 当前日志最近记录的搜索起点：{seed}（不代表已定位到出错查询）
            - 反馈包：{Scalar(bundleName)}

            ## 问题描述（请填写）
            发生了什么？你原本期望什么？


            ## 复现步骤（请填写）
            出问题前做了哪些操作？是否每次都能复现？


            ## 诊断附件
            请将上述 ZIP 拖入编辑框，或通过附件按钮添加。附件不会自动上传。
            {collected}
            当前编辑条件与历史搜索分别保存，请按日志会话核对。界面问题请附截图。
            公开 Issue 的附件也会公开，请先检查内容。
            """ : $"""
            ## Environment and context (filled automatically)
            These values were captured at export time, not necessarily when the problem occurred. Full environment, mod and query details are in the ZIP.

            - RT2 build: {build}
            - DLL SHA256: {Field(env, "Rt2LoadedAssemblySha256")}
            - Game / Godot: {Field(env, "GameVersion")} / {Field(env, "Godot")}
            - OS / process architecture: {Field(env, "OS")} / {Field(env, "Architecture")}
            - GPU / rendering backend: {Field(env, "GPU")} / {Field(env, "RenderingDriver")}
            - Mod records: {mods}; versions and load states are in environment.json
            - Captured at (UTC): {Field(env, "CapturedUtc")}
            - Search status at export: {running}
            - Current filter editor: {editorSummary}
            - Latest search start recorded in the current log: {seed} (not automatically identified as the failing query)
            - Bundle: {Scalar(bundleName)}

            ## Problem description (please fill in)
            What happened? What did you expect to happen?


            ## Steps to reproduce (please fill in)
            What did you do before the problem occurred? Does it happen every time?


            ## Diagnostic attachment
            Drag the ZIP above into the editor, or use the attachment button. It is not uploaded automatically.
            {collected}
            Current editor settings and historical searches are stored separately; match log sessions. Include screenshots for visual issues.
            Attachments on public issues are public. Review the contents before attaching.
            """;
        return new(title, body);
    }

    internal static string? PrefillUrl(FeedbackIssueText issue)
    {
        string url = NewIssueUrl + "?title=" + Uri.EscapeDataString(issue.Title) + "&body=" + Uri.EscapeDataString(issue.Body);
        return url.Length <= MaxPrefillUrlLength ? url : null;
    }

    private static string Scalar(string value)
    {
        // Values can originate from mod manifests; prevent new Markdown blocks/links in the generated report.
        string text = new(value.Select(c => char.IsControl(c) || c is '[' or ']' or '<' or '>' or '`' ? ' ' : c).ToArray());
        text = text.Trim();
        return text.Length > 240 ? text[..240] + "…" : text;
    }
}
