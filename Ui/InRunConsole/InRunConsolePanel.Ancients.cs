using Godot;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Icons;

namespace RolltheSpire2.Ui.InRunConsole;

internal sealed partial class InRunConsolePanel
{
    private HFlowContainer _ancientOptionGrid = null!;
    private Label _ancientOptionSummary = null!;
    private Button _normalAncientOption = null!;
    private string? _ancientOptionsIdentity;
    private string? _selectedAncientOptionKey;
    private readonly List<EventOption> _ancientChoices = [];

    private void BuildAncientOptions(Node parent)
    {
        _ancientOptions = Section(parent);
        _ancientOptions.SizeFlagsVertical = SizeFlags.ExpandFill;
        _ancientOptions.Name = "ConsoleAncientOptions";
        var heading = _p.Label(T("console.ancient.forceOption"), 16, true);
        heading.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _ancientOptions.AddChild(heading);
        _normalAncientOption = Button(_ancientOptions, T("console.ancient.normalGeneration"), () =>
        {
            _selectedAncientOptionKey = null;
            RenderAncientOptions();
        }, "ConsoleAncientNormalOption");
        _ancientOptionGrid = new HFlowContainer { Name = "ConsoleAncientOptionIcons", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _ancientOptionGrid.AddThemeConstantOverride("h_separation", 6);
        _ancientOptionGrid.AddThemeConstantOverride("v_separation", 6);
        _ancientOptionSummary = _p.Label("", 15, true);
        _ancientOptionSummary.Name = "ConsoleAncientOptionSummary";
        _ancientOptionSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _ancientOptions.AddChild(_ancientOptionSummary);
        var scroll = new ScrollContainer { Name = "ConsoleAncientOptionScroll", SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _ancientOptions.AddChild(scroll);
        scroll.AddChild(_ancientOptionGrid);
    }

    // AllPossibleOptions is the native console's identity catalog. Merely read the
    // option and its associated relic; never generate offers or invoke a choice.
    private void ResetAncientOptions()
    {
        if (_ancientOptionGrid is null) return;
        var ancient = _tab == 3 ? _selected.SingleOrDefault() as AncientEventModel : null;
        string? identity = ancient?.Id.ToString();
        if (!StringComparer.Ordinal.Equals(identity, _ancientOptionsIdentity)) _selectedAncientOptionKey = null;
        _ancientOptionsIdentity = identity;
        _ancientChoices.Clear();
        if (ancient is not null)
        {
            try
            {
                _ancientChoices.AddRange(ancient.AllPossibleOptions.DistinctBy(option => option.TextKey));
            }
            catch (Exception ex)
            {
                RuntimeLog.WarnException($"inRunConsoleAncientOptionsFailed=true;ancient={ancient.Id}", ex);
                _status.Text = F("console.ancient.readFailed", ex.Message);
            }
        }
        if (!_ancientChoices.Any(option => option.TextKey == _selectedAncientOptionKey)) _selectedAncientOptionKey = null;
        RenderAncientOptions();
    }

    // Call after restoring the selected Ancient, so a stale key cannot target a
    // different identity. The executor still receives the complete native TextKey.
    private void RestoreAncientOptionSelection(string? textKey)
    {
        ResetAncientOptions();
        _selectedAncientOptionKey = _ancientChoices.Any(option => option.TextKey == textKey) ? textKey : null;
        RenderAncientOptions();
    }

    private void RenderAncientOptions()
    {
        foreach (var child in _ancientOptionGrid.GetChildren())
        { _ancientOptionGrid.RemoveChild(child); child.QueueFree(); }
        bool hasAncient = _ancientOptionsIdentity is not null;
        _normalAncientOption.Visible = hasAncient;
        _normalAncientOption.Disabled = _busy;
        _p.SetModeActive(_normalAncientOption, _selectedAncientOptionKey is null);
        foreach (var option in _ancientChoices)
        {
            string title = AncientOptionTitle(option);
            Texture2D? texture = null;
            if (option.Relic is { } relic)
            {
                try { texture = _icons.Resolve(KeyOf(relic), GameContentKind.Relic, IconVariant.RelicLarge).Texture; }
                catch (Exception ex)
                {
                    RuntimeLog.WarnException($"inRunConsoleAncientOptionIconFailed=true;ancient={_ancientOptionsIdentity};option={option.TextKey};relic={relic.Id}", ex);
                }
            }
            bool selected = option.TextKey == _selectedAncientOptionKey;
            var tile = _p.Button("");
            tile.Name = "ConsoleAncientOption" + _ancientOptionGrid.GetChildCount();
            tile.AccessibilityName = title;
            tile.SetMeta("ancient_option_text_key", option.TextKey);
            tile.Disabled = _busy;
            _p.ApplyButton(tile, selected: selected);
            _p.ApplyCandidateSurface(tile, selected);
            if (texture is not null)
            {
                tile.CustomMinimumSize = new(62, 62);
                tile.AddChild(new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    Texture = texture, Position = new(7, 7),
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = MouseFilterEnum.Ignore, Size = new(48, 48) });
                if (selected)
                {
                    var badge = _p.Label("✓", 18); badge.Position = new(41, 0); badge.Size = new(20, 22);
                    badge.MouseFilter = MouseFilterEnum.Ignore; tile.AddChild(badge);
                }
            }
            else
            {
                // Missing associations/art are actual text choices, never guessed relics.
                float height = Math.Max(62, 14 + (float)Math.Ceiling(title.Length / (_zh ? 12d : 23d)) * 19);
                tile.CustomMinimumSize = new(198, height);
                var label = _p.Label((selected ? "✓ " : "") + title, 14);
                label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                label.MouseFilter = MouseFilterEnum.Ignore;
                tile.AddChild(label); label.Position = new(7, 7); label.Size = new(184, height - 14);
            }
            tile.Pressed += () =>
            {
                if (_busy) return;
                _selectedAncientOptionKey = selected ? null : option.TextKey;
                RenderAncientOptions();
            };
            if (option.Relic is { } tooltipRelic) BindContentTooltip(tile, tooltipRelic, title);
            _ancientOptionGrid.AddChild(tile);
        }
        var chosen = _ancientChoices.FirstOrDefault(option => option.TextKey == _selectedAncientOptionKey);
        _ancientOptionSummary.Text = !hasAncient ? T("console.ancient.select")
            : chosen is null ? T("console.ancient.noForcedOption")
            : F("console.ancient.selectedOption", AncientOptionTitle(chosen));
    }

    private string AncientOptionTitle(EventOption option)
    {
        try { return option.Title.GetFormattedText(); }
        catch (Exception ex)
        {
            RuntimeLog.WarnException($"inRunConsoleAncientOptionTitleFailed=true;ancient={_ancientOptionsIdentity};option={option.TextKey}", ex);
            return option.TextKey;
        }
    }
}
