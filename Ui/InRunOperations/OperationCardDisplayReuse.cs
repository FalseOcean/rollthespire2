using Godot;
using MegaCrit.Sts2.Core.Localization;
using RolltheSpire2.Core.PredictorRuntime;

namespace RolltheSpire2.Ui.InRunOperations;

// Main-thread display ownership only. Retains at most the preceding display's
// cards while a new prediction is pending; it never retains prediction truth.
internal sealed class OperationCardDisplayReuse
{
    internal readonly record struct Key(PredictorCard Card, string Language)
    {
        internal static Key For(PredictorCard card) => new(card with { Id = 0 },
            LocManager.Instance?.Language ?? TranslationServer.GetLocale());
    }

    private readonly Dictionary<Key, Queue<Control>> _cards = [];
    internal int Count => _cards.Values.Sum(cards => cards.Count);

    internal Control? Take(Key key)
    {
        if (!_cards.TryGetValue(key, out var cards)) return null;
        while (cards.TryDequeue(out var card))
            if (GodotObject.IsInstanceValid(card) && !card.IsQueuedForDeletion()) return card;
        return null;
    }

    internal void Keep(Key key, Control card)
    {
        card.GetParent()?.RemoveChild(card);
        if (!_cards.TryGetValue(key, out var cards)) _cards.Add(key, cards = new());
        cards.Enqueue(card);
    }

    internal void Clear()
    {
        foreach (var cards in _cards.Values)
            foreach (var card in cards)
                if (GodotObject.IsInstanceValid(card) && !card.IsQueuedForDeletion()) card.QueueFree();
        _cards.Clear();
    }
}
