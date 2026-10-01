using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Runtime;
using RolltheSpire2.Search.FamilyExecution;

namespace RolltheSpire2.Ui.Persistence;

internal sealed partial class SearchWorkspacePersistence
{
    // Shells in the menu and a run must not overwrite one another's final cursor.
    // Access and polling are confined to the Godot main thread.
    private static readonly Dictionary<string, WeakReference<SearchWorkspacePersistence>> OpenStores =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    internal static SearchWorkspacePersistence Open(string directory, RuntimeProfileId profile)
    {
        string key = Path.GetFullPath(directory);
        if (OpenStores.TryGetValue(key, out var reference) && reference.TryGetTarget(out var existing)) return existing;
        var store = new SearchWorkspacePersistence(directory, profile, initializeSearchCursor: false);
        OpenStores[key] = new(store);
        return store;
    }

    private IProductionSearchSession? _finishingSearch;
    private string _finishingFingerprint = "";
    private SceneTree? _finishingTree;
    internal bool HasFinishingSearch => _finishingSearch is not null;
    private IProductionSearchSession? _activeSearch;
    internal bool HasSearchInFlight => _activeSearch is not null || HasFinishingSearch;
    internal void TrackSearch(IProductionSearchSession session) => _activeSearch = session;
    internal void ReleaseSearch(IProductionSearchSession session)
    {
        if (ReferenceEquals(_activeSearch, session)) _activeSearch = null;
    }

    internal void FinishDetachedSearch(IProductionSearchSession session, string fingerprint, SceneTree tree)
    {
        if (_finishingSearch is not null) throw new InvalidOperationException("SearchHandoffAlreadyActive");
        _finishingSearch = session; _finishingFingerprint = fingerprint;
        session.Cancel();
        _finishingTree = tree;
        tree.ProcessFrame += PollDetachedSearch;
        PollDetachedSearch();
        FlushAll();
    }

    private void PollDetachedSearch()
    {
        if (_finishingSearch is not { } session) return;
        if (!DrainSessionResults(session, _finishingFingerprint)) return;
        FlushAll();
        if (_finishingTree is { } tree) tree.ProcessFrame -= PollDetachedSearch;
        _finishingTree = null; _finishingSearch = null;
        ReleaseSearch(session);
        RuntimeLog.Info("searchDetachedHandoffCompleted=true;state=" + session.GetProgress().State);
        _ = DisposeDetachedSearch(session);
    }

    // Shared terminal handoff logic can be checked without constructing a Godot node.
    internal bool DrainSessionResults(IProductionSearchSession session, string fingerprint)
    {
        void Drain()
        {
            while (session.TryReadCandidate(out var candidate))
                if (candidate is not null) AppendResult(candidate, fingerprint);
        }
        Drain();
        bool complete = session.Completion.IsCompleted;
        if (complete) Drain();
        if (session.TryGetSafeNextOrdinal(out var next))
        {
            if (next < Beta110SeedCodec.SpaceSize) ObserveSafeNextCursor(next);
            else if (complete && next == Beta110SeedCodec.SpaceSize) CommitEndOfSpaceWrap();
        }
        return complete;
    }

    private static async Task DisposeDetachedSearch(IProductionSearchSession session)
    {
        try { await session.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) { RuntimeLog.TryBackgroundWarning("searchDetachedDisposeFailed=" + ex); }
    }
}
