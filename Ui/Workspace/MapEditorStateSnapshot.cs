using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Ui.Workspace;

// Editor-only drafts. SearchQuery remains the authority for every active condition.
internal sealed record MapEditorStateSnapshot(int SelectedScope, IReadOnlyList<MapActEditorStateSnapshot> Acts);

internal sealed record MapActEditorStateSnapshot(int Act, bool DrawingMode,
    MapDailyRouteDraftSnapshot Daily, IReadOnlyList<MapDrawingNodeSnapshot> Nodes);

internal sealed record MapDailyRouteDraftSnapshot(StandardMapMetric Metric, int? Minimum);

internal sealed record MapDrawingNodeSnapshot(long Id, StandardMapRoomType Room, long GroupId, long AddedOrder);
