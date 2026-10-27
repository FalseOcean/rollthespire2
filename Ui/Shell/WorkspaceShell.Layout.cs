using Godot;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Shell;

internal sealed partial class WorkspaceShell
{
    private void BuildWorkspaceLayout()
    {
        // Preferences have been selected before any styled controls are built.
        _search = MakeButton(); _analysis = MakeButton(); _currentPrediction = MakeButton();
        _seeds = MakeButton(); _encyclopedia = MakeButton(); _status = MakeButton();
        _notes = MakeButton(); _feedback = MakeButton(); _settings = MakeButton(); _close = MakeButton("×");
        Theme = MainPanelTheme.Create();
        _background = new ColorRect { MouseFilter = MouseFilterEnum.Ignore };
        MainPanelTheme.SetBackground(_background, _palette.Canvas);
        AddChild(_background);
        _background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddBackgroundLight(new(.28f, .34f), new(.48f, .64f), new Color("254A85"), .15f);
        AddBackgroundLight(new(.86f, .86f), new(.42f, .56f), new Color("1B4073"), .16f);
        var divider = new ColorRect { Name = "WorkspaceHeaderDivider", Position = new(0, 84),
            Size = new(CanvasSize.X, 1), MouseFilter = MouseFilterEnum.Ignore };
        MainPanelTheme.SetBackground(divider, _palette.Line);
        AddChild(divider);

        var header = new HBoxContainer { Name = "WorkspaceHeader", Position = new(32, 20), Size = new(1536, 48) };
        header.AddThemeConstantOverride("separation", 8);
        AddChild(header);
        _title = _palette.Label("RolltheSpire2", 24, heading: true);
        _title.CustomMinimumSize = new(174, 0);
        _title.VerticalAlignment = VerticalAlignment.Center;
        header.AddChild(_title);
        header.AddChild(new Control { Name = "WorkspaceIdentityGap", CustomMinimumSize = new(20, 0),
            MouseFilter = MouseFilterEnum.Ignore });
        foreach (var (button, icon) in new[] { (_search, "search"), (_analysis, "analysis"),
            (_currentPrediction, "crystal"), (_seeds, "library"), (_encyclopedia, "book"), (_status, "status") })
        {
            SetNavigationIcon(button, icon);
            header.AddChild(button);
        }
        _currentPrediction.Visible = _liveRun is not null;
        header.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore });
        foreach (var (button, icon) in new[] { (_notes, "about"), (_feedback, "feedback"), (_settings, "settings") })
        {
            SetNavigationIcon(button, icon);
            header.AddChild(button);
        }
        _close.CustomMinimumSize = new(48, 48);
        header.AddChild(_close);
        _content.Position = new(32, 96);
        _content.Size = new(1536, 772);
        AddChild(_content);
    }

    // Static broad light fields, kept below every interactive surface. Their
    // color, strength, center and extent can be tuned without touching layout.
    private void AddBackgroundLight(Vector2 center, Vector2 extent, Color color, float strength)
    {
        var light = new TextureRect
        {
            Name = "WorkspaceAmbientLight", MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Texture = new GradientTexture2D
            {
                Width = 512, Height = 512, Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new(.5f, .5f), FillTo = new(1, .5f),
                Gradient = new Gradient
                {
                    Colors = [new Color(color, strength), new Color(color, 0)],
                    Offsets = [0, 1]
                }
            },
            Position = (center - extent) * CanvasSize,
            Size = extent * CanvasSize * 2
        };
        AddChild(light);
    }

    private IEnumerable<Button> HeaderButtons() =>
        [_search, _analysis, _currentPrediction, _seeds, _encyclopedia, _status, _notes, _feedback, _settings, _close];

    private void SetNavigationIcon(Button button, string key)
    {
        button.Icon = WorkspaceToolIcons.Get(key);
        // Expanding icons do not reserve width in an auto-sized header button.
        // Keep their natural measure so text cannot squeeze the icon to zero.
        button.ExpandIcon = false;
        button.AddThemeConstantOverride("icon_max_width", 20);
        button.AddThemeConstantOverride("h_separation", 7);
        ApplyNavigationIconColor(button);
    }

    private void ApplyNavigationIconColor(Button button)
    {
        foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
            button.AddThemeColorOverride("icon_" + state + "_color", _palette.Color(_palette.Text));
        button.AddThemeColorOverride("icon_disabled_color", _palette.Color(_palette.Disabled));
    }

    private static Button MakeButton(string text = "")
    {
        var button = WorkspacePalette.MainPanel.Button(text);
        button.CustomMinimumSize = new(0, 48);
        button.AddThemeFontSizeOverride("font_size", 18);
        WorkspacePalette.MainPanel.SetActive(button, false, horizontalPadding: 10);
        return button;
    }
}
