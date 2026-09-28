using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Icons;

namespace RolltheSpire2.Ui.Pages.Search.Neow;

/// <summary>
/// Horizontal relic/curse/other ModelKey result slot using the shared Neow
/// result-slot chrome. Search state remains a stable ModelKey.
/// </summary>
internal sealed partial class NeowModelKeySlot : SearchHorizontalResultSlot
{
    public NeowModelKeySlot(IGameIconResolver icons, Action<RelicPickerRequest> openPicker)
        : base(icons, openPicker)
    {
    }
}
