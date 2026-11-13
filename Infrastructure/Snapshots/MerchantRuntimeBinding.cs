using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static class MerchantRuntimeBinding
{
    private static readonly MethodInfo? LegacyInventory = AccessTools.DeclaredMethod(typeof(MerchantRoom), "GetLocalInventory", Type.EmptyTypes);
    private static readonly PropertyInfo? Synchronizer = AccessTools.DeclaredProperty(typeof(RunManager), "MerchantSynchronizer");
    private static readonly MethodInfo? SynchronizedInventory = Synchronizer == null ? null :
        AccessTools.DeclaredMethod(Synchronizer.PropertyType, "GetLocalInventory", Type.EmptyTypes);

    internal static MethodInfo? SynchronizedPurchase { get; } =
        AccessTools.DeclaredMethod(typeof(MerchantEntry), "OnTryPurchaseUnsynchronized", [typeof(MerchantInventory), typeof(bool)]);

    internal static MethodInfo Purchase => SynchronizedPurchase ??
        AccessTools.DeclaredMethod(typeof(MerchantEntry), "OnTryPurchaseWrapper", [typeof(MerchantInventory), typeof(bool)]) ??
        throw new MissingMethodException(typeof(MerchantEntry).FullName, "OnTryPurchaseUnsynchronized / OnTryPurchaseWrapper");

    // AllEntries returns a different CLR type in each supported assembly.
    private static readonly MethodInfo InventoryEntries = AccessTools.DeclaredPropertyGetter(typeof(MerchantInventory), "AllEntries")
        ?? throw new MissingMemberException(typeof(MerchantInventory).FullName, "AllEntries");

    internal static IEnumerable<MerchantEntry> Entries(MerchantInventory inventory) =>
        (IEnumerable<MerchantEntry>)InventoryEntries.Invoke(inventory, BindingFlags.DoNotWrapExceptions, null, null, null)!;

    internal static MerchantInventory? LocalInventory(RunState run, MerchantRoom room)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (!ReferenceEquals(run.CurrentRoom, room) || LocalContext.GetMe(run) is not { } player) return null;
        object? owner = room;
        MethodInfo? method = LegacyInventory;
        if (Synchronizer != null)
        {
            if (!ReferenceEquals(OperationSurfaceHoverCapture.CurrentRun(), run)) return null;
            owner = Synchronizer.GetValue(RunManager.Instance);
            method = SynchronizedInventory;
        }
        if (method == null) throw new MissingMethodException("MerchantRoom / MerchantSynchronizer", "GetLocalInventory");
        if (owner == null) return null;

        // Both native getters index a list populated during room entry.
        MerchantInventory? inventory;
        try { inventory = (MerchantInventory?)method.Invoke(owner, BindingFlags.DoNotWrapExceptions, null, null, null); }
        catch (ArgumentOutOfRangeException) { return null; }
        return ReferenceEquals(inventory?.Player, player) ? inventory : null;
    }
}
