using Godot;
using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class SeedLibraryCanvas
{
    private void RenderDeveloperSeed(SeedLibraryEntry entry)
    {
        string title = entry.TitleFor(_language);
        var actions = DetailHeading(title, T("开发者推荐", "Developer pick") + " · " + entry.Seed + "\n" +
            (entry.Context is { } context ? FormatContext(context) : T("上下文不可用", "Context unavailable")), entry.DeveloperDetails?.Icons);
        AddAction(actions, T("复制种子", "Copy seed"), () =>
        { DisplayServer.ClipboardSet(entry.Seed); ShowReceipt(T("已复制种子。", "Seed copied.")); }).Name = "CopyDeveloperSeed";
        var predict = AddAction(actions, T("进行预测", "Predict"), () => OpenRequested?.Invoke(entry), true);
        predict.Name = "PredictDeveloperSeed"; predict.Disabled = !entry.CanOpen;
        var favorite = AddAction(ManagementActions(), T("加入我的收藏", "Add to my favorites"), () =>
            OpenSave(entry.Seed, entry.Context!, entry.FavoriteNote(_language), initialTitle: title));
        favorite.Name = "FavoriteDeveloperSeed"; favorite.Disabled = !entry.CanOpen;
        if (!entry.CanOpen)
            _detail.AddChild(Text(T("当前无法按此配置预测，仍可复制种子。", "This configuration cannot be predicted; you can still copy the seed.") + "\n" + entry.Issue, 16, true));
        if (!string.IsNullOrWhiteSpace(entry.NoteFor(_language))) _detail.AddChild(Text(entry.NoteFor(_language), 20));
        string instructions = entry.DeveloperDetails?.Instructions.Resolve(_language) ?? "";
        if (instructions.Length > 0)
        {
            _detail.AddChild(Text(T("游玩说明", "How to play"), 18, true));
            _detail.AddChild(Text(instructions, 19));
        }
        _detail.AddChild(Text(T("按上方角色、进阶和版本使用；具体选择见游玩说明。预测会按当前游戏环境重新计算。",
            "Use the characters, ascension and game version shown above; follow any choices in the instructions. Predictions are recalculated in the current game environment."), 16, true));
    }
}
