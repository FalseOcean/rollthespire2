using RolltheSpire2.Ui.Controls.Pickers;
using RolltheSpire2.Ui.Icons;

namespace RolltheSpire2.Ui.Pages.Search.Neow;

/// <summary>Horizontal card-result slot using the shared Neow result-slot chrome.</summary>
internal sealed partial class SearchCardResultSlot : SearchHorizontalResultSlot
{
    public SearchCardResultSlot(IGameIconResolver icons, Action<RelicPickerRequest> openPicker)
        : base(icons, openPicker)
    {
    }
}
