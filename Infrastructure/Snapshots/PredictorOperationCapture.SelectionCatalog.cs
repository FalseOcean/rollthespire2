using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.PredictorRuntime;
using static RolltheSpire2.Infrastructure.Snapshots.PredictorLiveRunCapture;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorOperationCapture
{
    private sealed record SelectionCardCatalog(CrystalSphereCatalogCapture.CardInputs Inputs,
        Dictionary<CardPoolModel, ImmutableArray<CardModel>> Pools, PredictorCatalog Projection)
    {
        internal bool Matches(CrystalSphereCatalogCapture.CardInputs inputs,
            Dictionary<CardPoolModel, ImmutableArray<CardModel>> pools) =>
            ReferenceEquals(Inputs.Own, inputs.Own) && ReferenceEquals(Inputs.Colorless, inputs.Colorless) &&
            Enumerable.SequenceEqual<CardPoolModel>(Inputs.Unlocked, inputs.Unlocked, ReferenceEqualityComparer.Instance) &&
            Enumerable.SequenceEqual<CardModel>(Inputs.Definitions, inputs.Definitions, ReferenceEqualityComparer.Instance) &&
            Inputs.Characters.Length == inputs.Characters.Length &&
            Inputs.Characters.Zip(inputs.Characters).All(pair => pair.First.Character == pair.Second.Character &&
                ReferenceEquals(pair.First.Pool, pair.Second.Pool)) &&
            Pools.Count == pools.Count && Pools.All(pair => pools.TryGetValue(pair.Key, out var current) &&
                Enumerable.SequenceEqual<CardModel>(pair.Value, current, ReferenceEqualityComparer.Instance));
    }

    // Only the immutable native card definitions and their detached projection
    // are reusable. The actual unlocked pools, including order and identities,
    // are copied again into the current capture. Modded or mutable pool members
    // keep their normal property reads rather than inheriting old metadata.
    private static readonly ConditionalWeakTable<PredictorOperationSnapshot, SelectionCardCatalog> SelectionCardCatalogs = new();

    private static PredictorCatalog CaptureSelectionCardCatalog(RunState run, Player player,
        CrystalSphereCatalogCapture.CapturePools pools, CatalogLevels levels, CaptureTiming? timing,
        PredictorOperationSnapshot? previous, out SelectionCardCatalog? observed, out bool reused)
    {
        observed = null;
        reused = false;
        // A loaded mod may patch a property on an existing canonical object.
        // Rebuild its detached projection and compare the values instead of
        // relying on that object's unchanged identity.
        if (ModManager.State != ModManagerState.Initialized || ModManager.Mods.Any(mod => mod.state == ModLoadState.Loaded &&
            !string.Equals(mod.manifest?.id, ModEntry.ModId, StringComparison.OrdinalIgnoreCase)))
            return CrystalSphereCatalogCapture.Capture(run, player, pools, levels.Capture).Catalog;

        var inputs = CrystalSphereCatalogCapture.ReadCardInputs(player, pools);
        var current = new Dictionary<CardPoolModel, ImmutableArray<CardModel>>(ReferenceEqualityComparer.Instance);
        foreach (var pool in inputs.Unlocked.Concat(inputs.Characters.Select(character => character.Pool)).Append(inputs.Own).Append(inputs.Colorless))
            if (!current.ContainsKey(pool)) current.Add(pool, pools.Read(pool));

        bool reusable = IsNativeCanonical(player.Character) && inputs.Definitions.All(IsNativeCanonical) &&
            current.All(pair => IsNativeCanonical(pair.Key) && pair.Value.All(IsNativeCanonical));
        reused = reusable && previous != null && SelectionCardCatalogs.TryGetValue(previous, out observed) && observed.Matches(inputs, current);
        if (reused)
        {
            if (timing != null) timing.SelectionCatalogReuses++;
            return observed!.Projection;
        }

        var (catalog, _) = CrystalSphereCatalogCapture.Capture(run, player, pools, levels.Capture, inputs);
        observed = reusable ? new(inputs, current, catalog) : null;
        return catalog;
    }

    private static bool IsNativeCanonical(AbstractModel model) =>
        model.IsCanonical && !model.IsMock && model.GetType().Assembly == typeof(CardModel).Assembly;

    // Reuse the digest only after comparing the detached values themselves.
    // Unlisted record properties still participate in record equality. For
    // nested immutable arrays, compare the ordered values instead of storage.
    private static bool SameSelectionCatalog(PredictorCatalog before, PredictorCatalog after)
    {
        static PredictorCatalog Scalars(PredictorCatalog catalog) => catalog with
        {
            CharacterCards = [], ColorlessCards = [], AllCharacterCards = [], KaleidoscopePools = null, CardDefinitions = [], Encounters = [],
            TransformationPools = [], UnlockedCurseCards = [], ModifierCurseCards = [], ShopRelics = [], CharacterPools = [],
            ShopGainsBlock = [], EventPetRelics = [], StarterRelics = [], DustyTomeCards = [], TradableRelicTypes = []
        };
        bool sameKaleidoscope = before.KaleidoscopePools is { } oldPools
            ? after.KaleidoscopePools is { } newPools && SameOrder(oldPools, newPools, static (left, right) =>
                left.Source == right.Source && left.Issue == right.Issue && SameOrder(left.Cards, right.Cards))
            : after.KaleidoscopePools is null;
        return Scalars(before) == Scalars(after) && sameKaleidoscope &&
            SameOrder(before.CharacterCards, after.CharacterCards) && SameOrder(before.ColorlessCards, after.ColorlessCards) &&
            SameOrder(before.AllCharacterCards, after.AllCharacterCards) && SameOrder(before.CardDefinitions, after.CardDefinitions,
                static (left, right) => left == right || (left with { Levels = [] }) == (right with { Levels = [] }) && SameOrder(left.Levels, right.Levels)) &&
            SameOrder(before.Encounters, after.Encounters, static (left, right) =>
                left == right || (left with { EnemyCreationSources = [] }) == (right with { EnemyCreationSources = [] }) &&
                    SameOrder(left.EnemyCreationSources, right.EnemyCreationSources)) &&
            SameOrder(before.TransformationPools, after.TransformationPools, static (left, right) =>
                left == right || (left with { Cards = [] }) == (right with { Cards = [] }) && SameOrder(left.Cards, right.Cards)) &&
            SameOrder(before.CharacterPools, after.CharacterPools, static (left, right) =>
                left == right || (left with { Cards = [] }) == (right with { Cards = [] }) && SameOrder(left.Cards, right.Cards)) &&
            SameOrder(before.UnlockedCurseCards, after.UnlockedCurseCards) && SameOrder(before.ModifierCurseCards, after.ModifierCurseCards) &&
            SameOrder(before.ShopRelics, after.ShopRelics) && SameOrder(before.ShopGainsBlock, after.ShopGainsBlock) &&
            SameOrder(before.EventPetRelics, after.EventPetRelics) && SameOrder(before.StarterRelics, after.StarterRelics) &&
            SameOrder(before.DustyTomeCards, after.DustyTomeCards) && SameOrder(before.TradableRelicTypes, after.TradableRelicTypes);
    }

    private static bool SameOrder<T>(ImmutableArray<T> left, ImmutableArray<T> right, Func<T, T, bool>? matches = null)
    {
        if (left == right) return true;
        if (left.IsDefault || right.IsDefault || left.Length != right.Length) return false;
        matches ??= EqualityComparer<T>.Default.Equals;
        for (int i = 0; i < left.Length; i++)
            if (!matches(left[i], right[i])) return false;
        return true;
    }
}
