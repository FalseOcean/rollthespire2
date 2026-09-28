using Godot;

namespace RolltheSpire2.Ui.Pages.Analysis;

internal sealed partial class AnalysisPage
{
    private Button? _favoriteSeed;
    private Label? _favoriteReceipt;
    private Action? _favoriteSeedRequested;
    internal event Action? FavoriteSeedRequested
    {
        add { _favoriteSeedRequested += value; RefreshFavoriteSeed(); }
        remove { _favoriteSeedRequested -= value; RefreshFavoriteSeed(); }
    }
    internal bool CanFavoriteVisibleSeed => _wbHasResult && _requestBar.HasValidVisibleSeed && LastDocument is not null &&
        _runtime.Profile.TryCanonicalizeSeed(CurrentDraft.RawSeed, out var canonical, out _) && LastDocument.CanonicalSeed == canonical;

    private void BuildFavoriteSeed(Control rail)
    {
        _favoriteSeed = _palette.Button(""); _favoriteSeed.Name = "FavoritePredictionSeed";
        _favoriteSeed.Pressed += () => { if (CanFavoriteVisibleSeed) _favoriteSeedRequested?.Invoke(); };
        rail.AddChild(_favoriteSeed);
        _favoriteReceipt = WLabel(""); _favoriteReceipt.Visible = false; rail.AddChild(_favoriteReceipt);
        RefreshFavoriteSeed();
    }

    private void RefreshFavoriteSeed()
    {
        if (_favoriteSeed is null) return;
        _favoriteSeed.Visible = _favoriteSeedRequested is not null;
        _favoriteSeed.Text = _uiText?.LanguageCode == "en" ? "Favorite seed" : "收藏种子";
        _favoriteSeed.Disabled = !CanFavoriteVisibleSeed;
    }

    internal void ShowLibraryReceipt(string message)
    {
        if (_favoriteReceipt is not null) { _favoriteReceipt.Text = message; _favoriteReceipt.Visible = message.Length > 0; }
    }
}
