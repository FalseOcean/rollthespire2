namespace RolltheSpire2.Core.Prediction.Maps;

internal sealed partial class BoundedActMap
{
    private readonly int[] _assignmentPropertyScratch = new int[MapPropertyProgram.ScratchLength];

    internal int EvaluateAssignmentProperty(int metric, CancellationToken token = default) =>
        MapPropertyProgram.Evaluate(_type, _degree, _children, _boss, metric,
            _assignmentPropertyScratch, projectUnassignedToMonster: true, token);
}
