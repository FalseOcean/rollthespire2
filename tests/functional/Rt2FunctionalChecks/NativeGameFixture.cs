using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;

// The supported game builds expose different setup and test-support APIs.
// Keep the fixtures running the native implementation belonging to the loaded game.
internal static class NativeGameFixture
{
    public static AscensionManager StandardAscension(int level)
    {
        var current = typeof(AscensionManager).GetConstructor([typeof(GameMode), typeof(int)]);
        return (AscensionManager)(current != null ? current.Invoke([GameMode.Standard, level])
            : Activator.CreateInstance(typeof(AscensionManager), level)!);
    }

    public static IDisposable UseSelector(ICardSelector selector)
    {
        var stack = typeof(CardSelectCmd).Assembly.GetType("MegaCrit.Sts2.Core.CardSelection.CardSelectorStack");
        var method = stack?.GetMethod("SetTestSelector", [typeof(ICardSelector)])
            ?? typeof(CardSelectCmd).GetMethod("UseSelector", [typeof(ICardSelector)])
            ?? throw new MissingMethodException("Native card selector fixture binding");
        return (IDisposable)method.Invoke(null, BindingFlags.DoNotWrapExceptions, null, [selector], null)!;
    }

    public static MerchantCardEntry MerchantCard(Player player, MerchantInventory inventory,
        IEnumerable<CardModel> pool, CardType type)
    {
        var current = typeof(MerchantCardEntry).GetConstructor([typeof(Player), typeof(IEnumerable<CardModel>), typeof(CardType)]);
        return (MerchantCardEntry)(current != null ? current.Invoke([player, pool, type])
            : Activator.CreateInstance(typeof(MerchantCardEntry), player, inventory, pool, type)!);
    }

    public static void SetRoomInventory(RunState run, MerchantRoom room, MerchantInventory inventory)
    {
        var property = typeof(RunManager).GetProperty("MerchantSynchronizer");
        if (property == null)
        {
            ((List<MerchantInventory>)typeof(MerchantRoom).GetProperty("Inventories")!.GetValue(room)!).Add(inventory);
            return;
        }
        var synchronizer = RuntimeHelpers.GetUninitializedObject(property.PropertyType);
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        property.PropertyType.GetField("_runState", fields)!.SetValue(synchronizer, run);
        property.PropertyType.GetField("_inventories", fields)!.SetValue(synchronizer, new List<MerchantInventory> { inventory });
        property.SetValue(RunManager.Instance, synchronizer);
    }

    public static RelicModel PullRewardRelic(Player player)
    {
        var method = typeof(RelicFactory).GetMethod("PullNextRelicForRewardFromFront", [typeof(Player)])
            ?? typeof(RelicFactory).GetMethod("PullNextRelicFromFront", [typeof(Player)])
            ?? throw new MissingMethodException(typeof(RelicFactory).FullName, "PullNextRelicForRewardFromFront");
        return (RelicModel)method.Invoke(null, BindingFlags.DoNotWrapExceptions, null, [player], null)!;
    }
}
