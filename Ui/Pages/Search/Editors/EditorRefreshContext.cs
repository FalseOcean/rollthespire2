using RolltheSpire2.Core.Authority;
using MegaCrit.Sts2.Core.Unlocks;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

// One main-thread observation shared by every editor in a refresh/restore transaction.
// The authority is immutable RT2 data; the copied unlock DTO never retains a player/run.
internal sealed record EditorRefreshContext(string Key, SerializableUnlockState Unlocks,
    RuntimeContextAuthoritySnapshot Authority);
