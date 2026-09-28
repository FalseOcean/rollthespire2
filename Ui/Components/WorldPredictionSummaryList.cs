using Godot;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Main-thread rendering of immutable world-domain presentation models. This component
/// resolves runtime icons only; it never predicts, filters, or compares identities.
/// </summary>
internal sealed partial class WorldPredictionSummaryList : VBoxContainer
{
    private readonly IGameIconResolver _icons;

    public WorldPredictionSummaryList(IGameIconResolver icons)
    {
        _icons = icons;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 8);
    }

    public void BindBosses(
        SeedDomainViewModel<BossPredictionViewModel> domain,
        string actFormat,
        string ordinalFormat,
        string unavailableFormat,
        string missingIconText,
        bool advanced,
        int? actFilter = null)
    {
        Clear();
        if (domain.Status != SeedDomainEvaluationStatus.Evaluated)
        {
            AddChild(Ui1Theme.Label(string.Format(unavailableFormat, domain.Status, domain.IssueCode), Ui1TextRole.Warning, true));
            return;
        }

        var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        BossPredictionViewModel[] bosses = domain.Items
            .Where(item => !actFilter.HasValue || item.Act == actFilter.Value)
            .OrderBy(item => item.Act)
            .ThenBy(item => item.Ordinal)
            .ToArray();
        if (bosses.Length == 0)
        {
            AddChild(Ui1Theme.Label(string.Format(unavailableFormat, SeedDomainEvaluationStatus.Unknown, "ActProjectionMissing"), Ui1TextRole.Warning, true));
            return;
        }
        foreach (BossPredictionViewModel boss in bosses)
        {
            var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            Ui1Theme.ApplyPanel(panel, Ui1SurfaceRole.Input, 4f, 1, 10f);
            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 6);
            string bossMeta = actFilter.HasValue
                ? string.Format(ordinalFormat, boss.Ordinal)
                : string.Format(actFormat, boss.Act) + " · " + string.Format(ordinalFormat, boss.Ordinal);
            column.AddChild(Ui1Theme.Label(bossMeta, Ui1TextRole.Meta));
            var identity = new IconWithLabel(40f, Ui1TextRole.CardTitle);
            identity.Bind(
                _icons.Resolve(boss.BossDisplay.ModelKey, GameContentKind.Encounter, IconVariant.WorldCompendiumBossIcon),
                boss.BossDisplay.DisplayName,
                boss.BossDisplay.Tooltip,
                missingIconText);
            column.AddChild(identity);
            if (advanced || boss.IdentityPrecision != PredictionPrecision.Exact)
            {
                column.AddChild(Ui1Theme.Label(boss.IdentityLabel, Ui1TextRole.Muted, true));
            }
            if (advanced)
            {
                column.AddChild(Ui1Theme.Label(
                    $"{boss.Authority} · {boss.Completeness} · {boss.RngStream}:{boss.RngCallCount}\n{boss.EvidenceCode}",
                    Ui1TextRole.Meta,
                    true));
            }
            panel.AddChild(column);
            grid.AddChild(panel);
        }
        AddChild(grid);
    }

    public void BindAncients(
        SeedDomainViewModel<AncientPredictionViewModel> domain,
        string actFormat,
        string optionsLabel,
        string unavailableFormat,
        string canonicalNeowOptionsNote,
        string missingIconText,
        bool advanced,
        int? actFilter = null)
    {
        Clear();
        if (domain.Status != SeedDomainEvaluationStatus.Evaluated)
        {
            AddChild(Ui1Theme.Label(string.Format(unavailableFormat, domain.Status, domain.IssueCode), Ui1TextRole.Warning, true));
            return;
        }

        AncientPredictionViewModel[] ancients = domain.Items
            .Where(item => !actFilter.HasValue || item.Act == actFilter.Value)
            .OrderBy(item => item.Act)
            .ToArray();
        if (ancients.Length == 0)
        {
            AddChild(Ui1Theme.Label(string.Format(unavailableFormat, SeedDomainEvaluationStatus.Unknown, "ActProjectionMissing"), Ui1TextRole.Warning, true));
            return;
        }
        foreach (AncientPredictionViewModel ancient in ancients)
        {
            var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            Ui1Theme.ApplyPanel(panel, Ui1SurfaceRole.Input, 4f, 1, 10f);
            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 8);
            if (!actFilter.HasValue)
            {
                column.AddChild(Ui1Theme.Label(string.Format(actFormat, ancient.Act), Ui1TextRole.Meta));
            }
            var identity = new IconWithLabel(40f, Ui1TextRole.CardTitle);
            identity.Bind(
                _icons.Resolve(ancient.AncientDisplay.ModelKey, GameContentKind.Ancient, IconVariant.WorldCompendiumAncientIcon),
                ancient.AncientDisplay.DisplayName,
                ancient.AncientDisplay.Tooltip,
                missingIconText);
            column.AddChild(identity);
            if (advanced || ancient.IdentityPrecision != PredictionPrecision.Exact || ancient.OptionPrecision != PredictionPrecision.Exact)
            {
                column.AddChild(Ui1Theme.Label(ancient.IdentityLabel + " · " + ancient.OptionPrecisionLabel, Ui1TextRole.Muted, true));
            }
            if (ancient.OptionsEvaluationStatus == AncientOptionsEvaluationStatus.NotEvaluatedByPolicy &&
                string.Equals(ancient.OptionIssueCode, "CanonicalNeowPredictorOwnsOptions", StringComparison.Ordinal))
            {
                column.AddChild(Ui1Theme.Label(canonicalNeowOptionsNote, Ui1TextRole.Meta, true));
            }

            if (ancient.Options.Count > 0)
            {
                column.AddChild(Ui1Theme.Label(optionsLabel, Ui1TextRole.Meta));
                var options = new GridContainer { Columns = 3, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                options.AddThemeConstantOverride("h_separation", 8);
                options.AddThemeConstantOverride("v_separation", 8);
                foreach (AncientOptionPredictionViewModel option in ancient.Options.OrderBy(item => item.Ordinal))
                {
                    var optionColumn = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                    optionColumn.AddThemeConstantOverride("separation", 4);
                    var optionIdentity = new IconWithLabel(34f, Ui1TextRole.Body);
                    optionIdentity.Bind(
                        _icons.Resolve(option.OptionDisplay.ModelKey, GameContentKind.Relic, IconVariant.Small),
                        $"{option.Ordinal}. {option.OptionDisplay.DisplayName}",
                        option.OptionDisplay.Tooltip,
                        missingIconText);
                    optionColumn.AddChild(optionIdentity);
                    if (option.CharacterTargetDisplay is not null)
                    {
                        var targetIdentity = new IconWithLabel(26f, Ui1TextRole.Meta);
                        targetIdentity.Bind(
                            _icons.Resolve(
                                option.CharacterTargetDisplay.ModelKey,
                                GameContentKind.Character,
                                IconVariant.CharacterPortrait),
                            option.CharacterTargetDisplay.DisplayName,
                            option.CharacterTargetDisplay.Tooltip,
                            missingIconText);
                        optionColumn.AddChild(targetIdentity);
                    }
                    else if (option.CharacterTargetPrecision == PredictionPrecision.Unknown &&
                             !string.IsNullOrWhiteSpace(option.CharacterTargetLabel))
                    {
                        optionColumn.AddChild(Ui1Theme.Label(option.CharacterTargetLabel, Ui1TextRole.Warning, true));
                    }
                    options.AddChild(optionColumn);
                }
                column.AddChild(options);
            }

            if (advanced)
            {
                column.AddChild(Ui1Theme.Label(
                    $"{ancient.Authority} · {ancient.Completeness}\n" +
                    $"Identity RNG: {ancient.IdentityRngStream}:{ancient.IdentityRngCallCount}\n" +
                    $"Option RNG: {ancient.OptionRngStream}:{ancient.OptionRngCallCount}\n" +
                    $"{ancient.IdentityEvidenceCode}\n{ancient.OptionEvidenceCode}",
                    Ui1TextRole.Meta,
                    true));
            }
            panel.AddChild(column);
            AddChild(panel);
        }
    }

    private void Clear()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
    }
}
