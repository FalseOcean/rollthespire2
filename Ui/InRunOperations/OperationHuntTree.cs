using System.Collections.Immutable;

namespace RolltheSpire2.Ui.InRunOperations;

// A trie of complete, ordered card offers from independently settled counts.
// A node is shared only under the same parent and outer combat hypothesis.
internal sealed record OperationHuntTreeNode(int Id, int ParentId,
    OperationPreviewSection? Source, OperationPreviewCardGroup? CardGroup,
    ImmutableArray<int> TriggerCounts, ImmutableArray<int> EndingTriggerCounts,
    ImmutableArray<int> Children);

internal sealed record OperationHuntTree(ImmutableArray<OperationHuntTreeNode> Nodes)
{
    private sealed class Node(int id, int parent, OperationPreviewSection? source = null,
        OperationPreviewCardGroup? group = null)
    {
        internal readonly int Id = id, Parent = parent;
        internal readonly OperationPreviewSection? Source = source;
        internal readonly OperationPreviewCardGroup? Group = group;
        internal readonly List<int> Counts = [], Ends = [], Children = [];
    }

    internal static OperationHuntTree Build(OperationPreviewBranch branch)
    {
        var nodes = new List<Node> { new(0, -1) };
        foreach (var outcome in branch.HuntOutcomes.OrderBy(outcome => outcome.Triggers))
        {
            var current = nodes[0];
            current.Counts.Add(outcome.Triggers);
            foreach (var source in outcome.Sections)
            foreach (var group in source.CardGroups.Where(group => !group.Cards.IsEmpty))
            {
                var child = current.Children.Select(id => nodes[id])
                    .FirstOrDefault(node => SameOffer(node.Source!, node.Group!, source, group));
                if (child == null)
                {
                    child = new(nodes.Count, current.Id, source, group);
                    current.Children.Add(child.Id);
                    nodes.Add(child);
                }
                child.Counts.Add(outcome.Triggers);
                current = child;
            }
            current.Ends.Add(outcome.Triggers);
        }
        return new(nodes.Select(node => new OperationHuntTreeNode(node.Id, node.Parent, node.Source, node.Group,
            node.Counts.Distinct().ToImmutableArray(), node.Ends.Distinct().ToImmutableArray(),
            node.Children.ToImmutableArray())).ToImmutableArray());
    }

    private static bool SameOffer(OperationPreviewSection a, OperationPreviewCardGroup x,
        OperationPreviewSection b, OperationPreviewCardGroup y) =>
        a.Source == b.Source && a.Category == b.Category && a.FeatureId == b.FeatureId &&
        a.Label == b.Label && a.EmptyReason == b.EmptyReason && a.Style == b.Style &&
        x.MinimumSelect == y.MinimumSelect && x.MaximumSelect == y.MaximumSelect &&
        x.CanSkip == y.CanSkip && x.Possible == y.Possible && x.Label == y.Label &&
        x.Highlighted == y.Highlighted && x.EarliestFirst == y.EarliestFirst &&
        x.ForceSingleRow == y.ForceSingleRow && x.SuppressPossiblePrefix == y.SuppressPossiblePrefix &&
        x.IconPaths.SequenceEqual(y.IconPaths) &&
        x.Cards.Select(card => card with { Id = 0 }).SequenceEqual(y.Cards.Select(card => card with { Id = 0 }));
}
