using Godot;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Ui.Pages;

internal interface IAppPage
{
    AppPageKey PageKey { get; }
    Control View { get; }
    void ApplyLocalization(IUiTextProvider uiText, IGameContentNameResolver contentNames);
    void ApplyDisplayMode(AppDisplayMode mode);
}
