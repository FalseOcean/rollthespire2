using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Tooltips;
using Godot;
using RolltheSpire2.Ui.Persistence;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class QueryWorkbenchFrame
{
    internal event Action<SearchResultAction>? FavoriteSeedRequested;
    internal event Action<SearchResultAction>? OpenResultRequested;
    internal void ShowLibraryReceipt(string message) => ReceiptText(message);

    private Button FavoriteResultButton(SearchResultAction action)
    {
        var button = _p.Button(_language == "zh" ? "收藏" : "Favorite");
        button.CustomMinimumSize = new(72, 32); button.AddThemeFontSizeOverride("font_size", 14);
        button.Disabled = action.Context is null;
        if (button.Disabled) SearchTooltip.Set(button, _text.Get("workflow.results.missing_context"));
        button.Pressed += () =>
        {
            try { FavoriteSeedRequested?.Invoke(action); }
            catch (Exception ex) { ReceiptText(ex.Message); }
        };
        return button;
    }
}
