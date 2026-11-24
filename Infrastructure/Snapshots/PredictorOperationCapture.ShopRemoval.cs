using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorOperationCapture
{
    private static void InstallShopRemovalObserver(Harmony harmony)
    {
        if (MerchantRuntimeBinding.SynchronizedPurchase == null)
        {
            var legacy = AccessTools.DeclaredMethod(typeof(OneOffSynchronizer), "DoMerchantCardRemoval", [typeof(Player), typeof(int), typeof(bool)])
                ?? throw new MissingMethodException(typeof(OneOffSynchronizer).FullName, "DoMerchantCardRemoval");
            harmony.Patch(legacy,
                prefix: new(typeof(PredictorOperationCapture), nameof(BeforeLegacyRemoval)),
                postfix: new(typeof(PredictorOperationCapture), nameof(AfterRemoval)));
            return;
        }

        var purchase = AccessTools.DeclaredMethod(typeof(MerchantCardRemovalEntry), "OnTryPurchaseInternal", [typeof(MerchantInventory), typeof(bool)])
            ?? throw new MissingMethodException(typeof(MerchantCardRemovalEntry).FullName, "OnTryPurchaseInternal");
        var moveNext = AccessTools.AsyncMoveNext(purchase)
            ?? throw new MissingMethodException(typeof(MerchantCardRemovalEntry).FullName, "OnTryPurchaseInternal.MoveNext");
        harmony.Patch(moveNext, transpiler: new(typeof(PredictorOperationCapture), nameof(ObserveNativeRemovalCall)));
    }

    private static readonly MethodInfo? NativeRemoval = AccessTools.DeclaredMethod(typeof(MerchantCardRemovalEntry), "DoCardRemoval", [typeof(Player), typeof(bool)]);
    private static readonly Func<Player, bool, Task<bool>>? RemoveCard = NativeRemoval?.CreateDelegate<Func<Player, bool, Task<bool>>>();

    // The new purchase computes the final quote before invoking the picker and
    // stores it across the await. Observe that value without running Cost or its hooks again.
    private static IEnumerable<CodeInstruction> ObserveNativeRemovalCall(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var stateMachine = __originalMethod.DeclaringType!;
        var quote = AccessTools.DeclaredField(stateMachine, "<goldToSpend>5__2");
        var entry = AccessTools.DeclaredField(stateMachine, "<>4__this");
        if (quote?.FieldType != typeof(int) || entry?.FieldType != typeof(MerchantCardRemovalEntry) || RemoveCard == null)
            throw new InvalidOperationException("Shop removal purchase no longer exposes the audited quote and entry");

        var body = instructions.ToList();
        int call = body.FindIndex(instruction => instruction.Calls(NativeRemoval!));
        if (call < 0 || body.FindLastIndex(instruction => instruction.Calls(NativeRemoval!)) != call)
            throw new InvalidOperationException("Shop removal purchase no longer calls the audited picker once");

        // Player and cancelability are already on the native call's stack.
        // Keep its labels and exception boundaries on the first inserted instruction.
        body[call].opcode = OpCodes.Ldarg_0;
        body[call].operand = null;
        body.InsertRange(call + 1,
        [
            new(OpCodes.Ldfld, quote),
            new(OpCodes.Ldarg_0),
            new(OpCodes.Ldfld, entry),
            new(OpCodes.Call, AccessTools.DeclaredMethod(typeof(PredictorOperationCapture), nameof(ObserveRemovalSelection)))
        ]);
        return body;
    }

    private static Task<bool> ObserveRemovalSelection(Player player, bool cancelable, int quote, MerchantCardRemovalEntry entry)
    {
        var previous = Removing.Value;
        var observation = ObserveRemoval(player, quote, cancelable, entry);
        Removing.Value = observation;
        try
        {
            var task = RemoveCard!(player, cancelable);
            if (observation != null) observation.Task = task;
            return task;
        }
        finally { Removing.Value = previous; }
    }
}
