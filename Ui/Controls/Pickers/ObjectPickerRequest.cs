using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;

namespace RolltheSpire2.Ui.Controls.Pickers;

// Invocation-owned authorities and presentation services. The picker never captures
// an editor catalog or broadens the supplied candidate set.
internal sealed record ObjectPickerRequest(
    IReadOnlyList<ModelKey> Candidates,
    Action<ModelKey> Selected,
    IGameContentNameResolver Names,
    IUiTextProvider Text,
    IGameIconResolver Icons,
    Func<ModelKey, string> ObjectRarity,
    CardPickerContext? CardContext = null,
    IReadOnlySet<ModelKey>? MultiplayerOnlyCards = null,
    int Players = 1,
    IReadOnlyList<ModelKey>? Curses = null);
