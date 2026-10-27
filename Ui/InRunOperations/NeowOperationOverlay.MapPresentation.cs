using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.InRunOperations;

internal sealed partial class NeowOperationOverlay
{
    private readonly List<(HBoxContainer Row, Label Label, float NaturalWidth)> _mapSummaryNames = [];

    private void LayoutMapSummaryNames(float width)
    {
        if (!MapCardPresentation) return;
        foreach (var (row, label, naturalWidth) in _mapSummaryNames)
        {
            // The map owns the outer scroll viewport. Fit identities inside
            // its assigned width, accounting for action text in nested rows.
            float available = Math.Max(1, width - 42);
            Control child = row;
            for (Node? parent = row.GetParent(); parent != null && parent != _summary; parent = parent.GetParent())
            {
                if (parent is HBoxContainer line)
                {
                    var siblings = line.GetChildren().OfType<Control>().Where(item => item != child && item.Visible).ToArray();
                    available -= siblings.Sum(item => item.GetCombinedMinimumSize().X) +
                        siblings.Length * line.GetThemeConstant("separation");
                }
                if (parent is Control control) child = control;
            }
            float identityChrome = row.GetChildren().OfType<Control>().Where(item => item != label && item.Visible)
                .Sum(item => item.GetCombinedMinimumSize().X) + row.GetThemeConstant("separation");
            var minimum = new Vector2(Math.Max(1, Math.Min(naturalWidth, available - identityChrome)), 0);
            if (!label.CustomMinimumSize.IsEqualApprox(minimum)) label.CustomMinimumSize = minimum;
        }
    }

    // Map-owned card previews share native faces and authored result data, while
    // their surrounding opportunity rows already supply identity and card names.
    // Other operation consumers retain their source-specific display policies.
    internal bool MapCardPresentation { get; init; }
    internal bool MapSummaryOnly { get; init; }
    internal bool MapSummaryCardNames { get; init; }

    // Quest objects carry a stable identity/effect rather than a reward offer
    // to compare. Their names and real actions remain in the map summary.
    internal static bool MapUsesCardFace(PredictorCard card) =>
        ModelDb.GetById<CardModel>(new ModelId(card.Key.Category, card.Key.Entry)).Type != CardType.Quest;

    internal int MapCardCount => _stacks.Sum(stack => stack.Cards.Length);

    // A map dock may contain several overlays, one per real opportunity. Its
    // total decides the art layout without merging those opportunities.
    internal void SetMapCardCount(int totalCount)
    {
        if (!MapCardPresentation) return;
        foreach (var stack in _stacks)
        {
            stack.Stacked = totalCount > 5;
            if (stack.Host is not null)
                stack.Host.SetMeta("rt2_stacked", stack.Stacked);
        }
        LayoutStacks();
    }
}
