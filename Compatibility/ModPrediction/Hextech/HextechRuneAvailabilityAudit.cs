using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace RolltheSpire2.Compatibility.ModPrediction.Hextech;

// Candidate filtering may invoke availability predicates on the live player.
// Audit those entry points before calling the mod's otherwise read-only pool
// builder. This does not claim that the runes' gameplay effects are simulated.
internal static class HextechRuneAvailabilityAudit
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Static | BindingFlags.Instance;
    private static readonly HashSet<string> WatcherPredicates = new(StringComparer.Ordinal)
    {
        "HextechWatcherPack.WatcherHexBlasphemyPardonRune",
        "HextechWatcherPack.WatcherHexBorrowedStonesRune",
        "HextechWatcherPack.WatcherHexCalmDexterityRune",
        "HextechWatcherPack.WatcherHexCalmSurgeRune",
        "HextechWatcherPack.WatcherHexCardRuneBase`1",
        "HextechWatcherPack.WatcherHexDailyMantraRune",
        "HextechWatcherPack.WatcherHexDivineDescentRune",
        "HextechWatcherPack.WatcherHexDivineEchoRune",
        "HextechWatcherPack.WatcherHexEndlessLiftRune",
        "HextechWatcherPack.WatcherHexEndlessMiracleRune",
        "HextechWatcherPack.WatcherHexEternalDivinityRune",
        "HextechWatcherPack.WatcherHexFourHandsRune",
        "HextechWatcherPack.WatcherHexHalfMantraRune",
        "HextechWatcherPack.WatcherHexIronStanceRune",
        "HextechWatcherPack.WatcherHexLoneBladeRune",
        "HextechWatcherPack.WatcherHexMantraFistRune",
        "HextechWatcherPack.WatcherHexMantraResonanceRune",
        "HextechWatcherPack.WatcherHexMiracleWorkerRune",
        "HextechWatcherPack.WatcherHexSecondStanceRune",
        "HextechWatcherPack.WatcherHexTripleVaultRune",
        "HextechWatcherPack.WatcherHexUltimateBladeRune",
        "HextechWatcherPack.WatcherHexWarriorAngerRune",
        "HextechWatcherPack.WatcherHexWrathGuardRune",
        "HextechWatcherPack.WatcherHexWrathStrengthRune"
    };

    internal static void Validate(Assembly nativeAssembly, IEnumerable<Type> runeTypes)
    {
        var registry = nativeAssembly.GetType("HextechRunes.HextechExternalContentRegistry", true)!;
        var callback = registry.GetMethod("GetPlayerRuneAvailability", Members, null, [typeof(ModelId)], null)
            ?? throw new MissingMethodException(registry.FullName, "GetPlayerRuneAvailability");
        var nativeBase = nativeAssembly.GetType("HextechRunes.HextechRelicBase", true)!;
        foreach (var type in runeTypes)
        {
            if (!nativeBase.IsAssignableFrom(type))
                throw new NotSupportedException("HextechRuneAvailabilityNotAudited:" + type.FullName);
            // Current main-package predicates inspect character, multiplayer
            // context or deck membership. The installed Watcher Pack predicates
            // inspect Watcher identity and (Borrowed Stones) strike membership.
            Check(type.GetMethod("IsAvailableForPlayer", Members, null, [typeof(Player)], null));
            // The main generic card-rune predicate dispatches through these
            // virtual methods; inherited ownership alone is insufficient.
            Check(type.GetMethod("IsAvailableForCharacter", Members, null, [typeof(Player)], null));
            Check(type.GetMethod("MeetsCardAvailabilityRequirement", Members, null,
                [typeof(IEnumerable<CardModel>)], null));
            if (callback.Invoke(null, [ModelDb.GetId(type)]) != null)
                throw new NotSupportedException("HextechRuneAvailabilityCallbackNotAudited:" + type.FullName);
        }

        void Check(MethodInfo? method)
        {
            if (method == null) return;
            var owner = method.DeclaringType!;
            if (owner.Assembly == nativeAssembly) return;
            var definition = owner.IsGenericType ? owner.GetGenericTypeDefinition() : owner;
            if (owner.Assembly.GetName().Name == "HextechWatcherPack" &&
                WatcherPredicates.Contains(definition.FullName!)) return;
            throw new NotSupportedException("HextechRuneAvailabilityNotAudited:" + owner.FullName + "." + method.Name);
        }
    }
}
