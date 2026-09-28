using Godot;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;
using RolltheSpire2.Ui.Tooltips;

namespace RolltheSpire2.Ui.Pages.Search.Ancient;

internal sealed partial class AncientActSection : PanelContainer
{
    private readonly List<AncientConditionRow> _rows = new();

    public AncientActSection(
        AncientActSectionDefinition definition,
        IReadOnlyList<ModelKey> seaGlassTargets,
        bool seaGlassTargetAuthorityExact,
        IGameIconResolver icons,
        ICharacterPoolIconProvider characterIcons,
        IGameContentNameResolver names,
        IUiTextProvider text,
        AnchoredTooltipHost tooltipHost,
        string missingIconText)
    {
        Act = definition.Act;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        SizeFlagsStretchRatio = 1f;
        ClipContents = true;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Page, 3f, 1, 8f);

        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 7);
        string title = definition.Act == 2
            ? text.Get(Ui1TextKey.SearchAncientAct2Title)
            : text.Get(Ui1TextKey.SearchAncientAct3Title);
        column.AddChild(Ui1Theme.Label(title, Ui1TextRole.SectionTitle, true));

        var grid = new GridContainer
        {
            Columns = 2,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            ClipContents = true
        };
        grid.AddThemeConstantOverride("h_separation", AncientRowGeometry.CardGap);
        grid.AddThemeConstantOverride("v_separation", AncientRowGeometry.CardGap);
        foreach (AncientRowDefinition rowDefinition in PresentationRows(definition))
        {
            var row = new AncientConditionRow(
                rowDefinition,
                seaGlassTargets,
                seaGlassTargetAuthorityExact,
                icons,
                characterIcons,
                names,
                text,
                tooltipHost,
                missingIconText);
            row.Changed += () => Changed?.Invoke();
            _rows.Add(row);
            grid.AddChild(row);
        }
        column.AddChild(grid);
        AddChild(column);
    }

    public event Action? Changed;

    public int Act { get; }
    public IReadOnlyList<AncientConditionRow> Rows => _rows;
    public int EnabledConditionCount => _rows.Sum(row => row.EnabledConditionCount);

    public void SetEnabled(bool enabled)
    {
        foreach (AncientConditionRow row in _rows) row.SetEnabled(enabled);
    }

    public void Clear(bool notify)
    {
        foreach (AncientConditionRow row in _rows) row.Clear(notify: false);
        if (notify) Changed?.Invoke();
    }

    private static IEnumerable<AncientRowDefinition> PresentationRows(AncientActSectionDefinition definition)
    {
        if (definition.Act != 2)
        {
            return definition.Rows;
        }

        return definition.Rows.OrderBy(row => row.AncientKey.Entry switch
        {
            "PAEL" => 0,
            "TEZCATARA" => 1,
            "OROBAS" => 2,
            "DARV" => 3,
            _ => 4
        });
    }
}
