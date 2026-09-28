using Godot;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Localization;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Pages.Search.BossMap;

internal sealed partial class BossMapActSection : PanelContainer
{
    private readonly List<BossMapVariantEditor> _rows = new();

    public BossMapActSection(
        BossMapActSectionDefinition definition,
        IGameIconResolver icons,
        IGameContentNameResolver names,
        IUiTextProvider text,
        string missingIconText)
    {
        Act = definition.Act;
        ShowSecondBoss = definition.ShowSecondBoss;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Ui1Theme.ApplyPanel(this, Ui1SurfaceRole.Page, 3f, 1, 6f);

        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 3);
        column.AddChild(Ui1Theme.Label(ActTitle(definition.Act, text), Ui1TextRole.SectionTitle, true));

        bool selectableVariants = definition.Variants.Count > 1;
        for (int index = 0; index < definition.Variants.Count; index++)
        {
            var row = new BossMapVariantEditor(
                definition.Variants[index],
                selectableVariants,
                definition.ShowSecondBoss,
                icons,
                names,
                text,
                missingIconText);
            row.Changed += () => Changed?.Invoke();
            _rows.Add(row);
            column.AddChild(row);
            if (index < definition.Variants.Count - 1)
            {
                var separator = new HSeparator();
                Ui1Theme.ApplySeparator(separator);
                column.AddChild(separator);
            }
        }

        AddChild(column);
    }

    public event Action? Changed;

    public int Act { get; }
    public bool ShowSecondBoss { get; }
    public IReadOnlyList<BossMapVariantEditor> Rows => _rows;
    public int EnabledConditionCount => _rows.Sum(row => row.EnabledConditionCount);

    public void SetEnabled(bool enabled)
    {
        foreach (BossMapVariantEditor row in _rows)
        {
            row.SetEnabled(enabled);
        }
    }

    public void Clear(bool notify)
    {
        foreach (BossMapVariantEditor row in _rows)
        {
            row.Clear(notify: false);
        }
        if (notify)
        {
            Changed?.Invoke();
        }
    }

    private static string ActTitle(int act, IUiTextProvider text) => act switch
    {
        1 => text.Get(Ui1TextKey.SearchBossMapAct1Title),
        2 => text.Get(Ui1TextKey.SearchBossMapAct2Title),
        3 => text.Get(Ui1TextKey.SearchBossMapAct3Title),
        _ => $"Act {act}"
    };
}
