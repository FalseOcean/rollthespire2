using Godot;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.BossMap;

internal sealed partial class BossMapReadOnlyVariantHeader : PanelContainer
{
    public BossMapReadOnlyVariantHeader(
        BossMapVariantDefinition definition,
        IGameIconResolver icons,
        IGameContentNameResolver names,
        string missingIconText)
    {
        CustomMinimumSize = new Vector2(0, BossMapRowGeometry.CellHeight);
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Card, 3f, 1, 4f);
        string displayName = names.Resolve(definition.ActKey, GameContentKind.Act);
        TooltipText = displayName;

        var holder = new Control
        {
            CustomMinimumSize = new Vector2(BossMapRowGeometry.VariantWidth, BossMapRowGeometry.CellHeight),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        holder.AddChild(BossMapVariantIdentityButton.BuildIdentityContent(
            definition.ActKey,
            displayName,
            icons,
            missingIconText));
        AddChild(holder);
    }
}
