namespace RolltheSpire2.Ui.Pages;

/// <summary>
/// Narrow AppShell/Page contract for Phase A2 UI-surface lifecycle.
///
/// This is intentionally not a generic modal framework. A Page remains the
/// business owner of its own workspaces/transactions/pickers; AppShell only
/// asks the active Page to cancel transient children on first-level navigation
/// and to consume one Escape layer when appropriate.
/// </summary>
internal interface IPageSurfaceLifecycle
{
    /// <summary>
    /// Called before switching away from this first-level Page. Transient
    /// transactions/pickers must be cancelled, while page-local workspace state
    /// that is explicitly preservable across Page switches must remain intact.
    /// </summary>
    void CancelTransientSurfacesForPageSwitch();

    /// <summary>
    /// Attempts to consume exactly one Page-owned Escape layer. Returns true
    /// only when a Page-owned surface was actually closed/cancelled.
    /// </summary>
    bool TryHandleEscape();

    /// <summary>
    /// Top-level RT2 exit cleanup. This may close page-local workspaces as well
    /// because cross-RT2-session workspace persistence is not part of Phase A2.
    /// It must not affect Search semantic/execution authority beyond existing
    /// AppShell top-level cleanup behavior.
    /// </summary>
    void ResetSurfacesForTopLevelClose();
}
