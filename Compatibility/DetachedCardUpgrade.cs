using MegaCrit.Sts2.Core.Models;

namespace RolltheSpire2.Compatibility;

internal static class DetachedCardUpgrade
{
    private static readonly Action<CardModel> Upgrade = Bind();

    internal static void Apply(CardModel card)
    {
        Upgrade(card);
        card.FinalizeUpgradeInternal();
    }

    private static Action<CardModel> Bind()
    {
        const string name = nameof(CardModel.UpgradeInternal);
        if (typeof(CardModel).GetMethod(name, [typeof(bool)]) is { } current)
        {
            var upgrade = current.CreateDelegate<Action<CardModel, bool>>();
            return card => upgrade(card, false);
        }
        return (typeof(CardModel).GetMethod(name, Type.EmptyTypes)
            ?? throw new MissingMethodException(typeof(CardModel).FullName, name)).CreateDelegate<Action<CardModel>>();
    }
}
