using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace RolltheSpire2.Compatibility;

// Native UI eligibility queries need a detached player and its run mode.
internal static class DetachedStandardRunBinding
{
    private delegate RunState LegacyFactory(IReadOnlyList<Player> players, IReadOnlyList<ActModel>? acts,
        IReadOnlyList<ModifierModel>? modifiers, GameMode mode, int ascension, string seed);

    private delegate RunState CurrentFactory(IReadOnlyList<Player> players, IReadOnlyList<ActModel>? acts,
        IReadOnlyList<ModifierModel>? modifiers, GameMode mode, bool custom, int ascension, string seed);

    private static readonly Lazy<Func<IReadOnlyList<Player>, int, string, RunState>> Factory = new(Bind);

    internal static RunState Create(IReadOnlyList<Player> players, int ascension, string seed) =>
        Factory.Value(players, ascension, seed);

    private static Func<IReadOnlyList<Player>, int, string, RunState> Bind()
    {
        const string name = "CreateForTest";
        Type players = typeof(IReadOnlyList<Player>);
        Type acts = typeof(IReadOnlyList<ActModel>);
        Type modifiers = typeof(IReadOnlyList<ModifierModel>);
        Type mode = typeof(GameMode);
        if (typeof(RunState).GetMethod(name, [players, acts, modifiers, mode, typeof(bool), typeof(int), typeof(string)]) is { } current)
        {
            var create = current.CreateDelegate<CurrentFactory>();
            return (roster, ascension, seed) => create(roster, null, null, GameMode.Standard, false, ascension, seed);
        }
        if (typeof(RunState).GetMethod(name, [players, acts, modifiers, mode, typeof(int), typeof(string)]) is { } legacy)
        {
            var create = legacy.CreateDelegate<LegacyFactory>();
            return (roster, ascension, seed) => create(roster, null, null, GameMode.Standard, ascension, seed);
        }
        throw new MissingMethodException(typeof(RunState).FullName, name);
    }
}
