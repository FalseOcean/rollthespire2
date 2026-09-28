using Godot;
using RolltheSpire2.Core.World;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Ui.Theme;

namespace RolltheSpire2.Ui.Components;

/// <summary>
/// Main-thread rendering only. It consumes the immutable Document projection and
/// never reconstructs catalogs, epoch filters, static eligibility, or RNG order.
/// </summary>
internal sealed partial class EventPoolSequenceSummaryList : VBoxContainer
{
    public EventPoolSequenceSummaryList()
    {
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        AddThemeConstantOverride("separation", 8);
    }

    public void Bind(
        SeedDomainViewModel<EventPoolActSequenceViewModel> domain,
        string unavailableFormat,
        string scopeNote,
        string effectiveQueueTitle,
        string rawQueueTitle,
        string rawQueueNote,
        bool showRawQueue,
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

        foreach (EventPoolActSequenceViewModel act in domain.Items.OrderBy(item => item.Act))
        {
            var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            Ui1Theme.ApplyPanel(panel, Ui1SurfaceRole.Input, 4f, 1, 10f);
            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 4);
            column.AddChild(Ui1Theme.Label(act.ActLabel, Ui1TextRole.CardTitle, true));

            if (showRawQueue)
            {
                column.AddChild(Ui1Theme.Label(effectiveQueueTitle, Ui1TextRole.Meta));
            }

            AddEffectiveRows(column, act.Entries);

            if (showRawQueue)
            {
                var separator = new HSeparator();
                Ui1Theme.ApplySeparator(separator);
                column.AddChild(separator);
                column.AddChild(Ui1Theme.Label(rawQueueTitle, Ui1TextRole.Meta));
                column.AddChild(Ui1Theme.Label(rawQueueNote, Ui1TextRole.Muted, true));
                AddRawRows(column, act.RawEntries);
            }

            if (advanced)
            {
                string epochEvidence = string.Join("; ", act.EpochFilters.Select(epoch =>
                    $"{epoch.EpochId}:revealed={epoch.IsRevealed},members={epoch.MemberCount},removed={epoch.RemovedCount}"));
                column.AddChild(Ui1Theme.Label(
                    $"{act.PrecisionLabel} · {act.Authority} · {act.Completeness}\n" +
                    $"rawLocal={act.RawActLocalCount}; rawShared={act.RawSharedCount}; epochEligible={act.EligibleCount}; effective={act.EffectiveCount}\n" +
                    $"epochFiltered={act.FilteredOutCount}; ancientOffset={act.OpeningAncientCursorOffset}; ancientSkipped={act.OpeningAncientSkippedCount}; staticFiltered={act.StaticFilteredOutCount}; duplicateFiltered={act.DuplicateFilteredOutCount}\n" +
                    $"RNG: {act.RngStream}:{act.RngCallCountBefore}→{act.RngCallCountAfter}\n" +
                    $"{epochEvidence}\n{act.EvidenceCode}",
                    Ui1TextRole.Meta,
                    true));
            }
            panel.AddChild(column);
            AddChild(panel);
        }
    }


    public void BindAct(
        SeedDomainViewModel<EventPoolActSequenceViewModel> domain,
        int actNumber,
        string unavailableFormat,
        string reportScopeNote)
    {
        Clear();
        AddChild(Ui1Theme.Label(reportScopeNote, Ui1TextRole.Meta, true));
        if (domain.Status != SeedDomainEvaluationStatus.Evaluated)
        {
            AddChild(Ui1Theme.Label(
                string.Format(unavailableFormat, domain.Status, domain.IssueCode),
                Ui1TextRole.Warning,
                true));
            return;
        }

        EventPoolActSequenceViewModel? act = domain.Items.FirstOrDefault(item => item.Act == actNumber);
        if (act is null)
        {
            AddChild(Ui1Theme.Label(
                string.Format(unavailableFormat, SeedDomainEvaluationStatus.Unknown, "ActProjectionMissing"),
                Ui1TextRole.Warning,
                true));
            return;
        }

        AddEffectiveRows(this, act.Entries);
    }
    private static void AddEffectiveRows(
        VBoxContainer column,
        IReadOnlyList<EventPoolSequenceEntryViewModel> entries)
    {
        foreach (EventPoolSequenceEntryViewModel entry in entries.OrderBy(item => item.Ordinal))
        {
            AddCompactRow(
                column,
                entry.Ordinal,
                entry.EventDisplay.DisplayName,
                entry.EventDisplay.Tooltip,
                string.IsNullOrWhiteSpace(entry.EligibilityLabel)
                    ? entry.SourceLabel
                    : entry.SourceLabel + " · " + entry.EligibilityLabel);
        }

        if (entries.Count == 0)
        {
            column.AddChild(Ui1Theme.Label("—", Ui1TextRole.Muted));
        }
    }

    private static void AddRawRows(
        VBoxContainer column,
        IReadOnlyList<EventPoolRawSequenceEntryViewModel> entries)
    {
        foreach (EventPoolRawSequenceEntryViewModel entry in entries.OrderBy(item => item.RawOrdinal))
        {
            AddCompactRow(
                column,
                entry.RawOrdinal,
                entry.EventDisplay.DisplayName,
                entry.EventDisplay.Tooltip,
                entry.SourceLabel);
        }

        if (entries.Count == 0)
        {
            column.AddChild(Ui1Theme.Label("—", Ui1TextRole.Muted));
        }
    }

    private static void AddCompactRow(
        VBoxContainer column,
        int ordinal,
        string displayName,
        string tooltip,
        string sourceLabel)
    {
        var row = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 28f)
        };
        row.AddThemeConstantOverride("separation", 12);

        Label identity = Ui1Theme.Label($"{ordinal}. {displayName}", Ui1TextRole.Body);
        identity.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        identity.CustomMinimumSize = new Vector2(0f, 28f);
        identity.ClipText = true;
        identity.TooltipText = tooltip;
        row.AddChild(identity);

        Label source = Ui1Theme.Label(sourceLabel, Ui1TextRole.Muted);
        source.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
        source.CustomMinimumSize = new Vector2(84f, 28f);
        source.HorizontalAlignment = HorizontalAlignment.Right;
        source.TooltipText = sourceLabel;
        row.AddChild(source);

        column.AddChild(row);
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
