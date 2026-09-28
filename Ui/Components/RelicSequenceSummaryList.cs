using Godot;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Icons;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Main-thread rendering only. Prediction order, filtering, precision, and
/// evidence are already fixed in the immutable Seed Analysis Document.
/// </summary>
internal sealed partial class RelicSequenceSummaryList : VBoxContainer
{
    private readonly IGameIconResolver _icons;

    public RelicSequenceSummaryList(IGameIconResolver icons)
    {
        _icons = icons;
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 8);
    }

    public void Bind(
        SeedDomainViewModel<RelicSequenceLaneViewModel> domain,
        string unavailableFormat,
        string scopeNote,
        string missingIconText,
        bool advanced)
    {
        Clear();
        AddChild(Ui1Theme.Label(scopeNote, Ui1TextRole.Meta, true));
        if (domain.Status != SeedDomainEvaluationStatus.Evaluated)
        {
            AddChild(Ui1Theme.Label(
                string.Format(unavailableFormat, domain.Status, domain.IssueCode),
                Ui1TextRole.Warning,
                true));
            return;
        }

        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);
        foreach (RelicSequenceLaneViewModel lane in domain.Items)
        {
            var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            Ui1Theme.ApplyPanel(panel, Ui1SurfaceRole.Input, 4f, 1, 10f);
            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 6);
            column.AddChild(Ui1Theme.Label(
                $"{lane.Title} · {lane.PullDirectionLabel}",
                Ui1TextRole.CardTitle,
                true));

            foreach (RelicSequenceEntryViewModel entry in lane.Entries.OrderBy(item => item.Position))
            {
                var identity = new IconWithLabel(34f, Ui1TextRole.Body);
                identity.Bind(
                    _icons.Resolve(entry.RelicDisplay.ModelKey, GameContentKind.Relic, IconVariant.Small),
                    $"{entry.Position}. {entry.RelicDisplay.DisplayName}",
                    entry.RelicDisplay.Tooltip,
                    missingIconText);
                column.AddChild(identity);
            }

            if (lane.Entries.Count == 0)
            {
                column.AddChild(Ui1Theme.Label("—", Ui1TextRole.Muted));
            }
            if (advanced)
            {
                column.AddChild(Ui1Theme.Label(
                    $"{lane.PrecisionLabel} · {lane.Authority} · {lane.Completeness}\n" +
                    $"RNG: {lane.RngStream}:{lane.RngCallCount}\n{lane.EvidenceCode}",
                    Ui1TextRole.Meta,
                    true));
            }
            panel.AddChild(column);
            grid.AddChild(panel);
        }
        AddChild(grid);
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
