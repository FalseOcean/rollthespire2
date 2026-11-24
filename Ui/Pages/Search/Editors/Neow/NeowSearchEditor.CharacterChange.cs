using MegaCrit.Sts2.Core.Models;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Ui.Pages.Search.Neow;

namespace RolltheSpire2.Ui.Pages.Search.Editors;

internal sealed partial class NeowSearchEditor
{
    // Called only by the workbench's explicit character-edit transaction, after
    // refreshing this seat's new catalogs. The caller owns rendering and receipts.
    internal IReadOnlyList<ModelKey> RemoveUnavailableSelectionsAfterCharacterChange()
    {
        if (_catalog is not { } catalog) return [];
        var removed = new List<ModelKey>();
        var known = ModelDb.AllCards.Cast<AbstractModel>().Concat(ModelDb.AllRelics).Concat(ModelDb.AllPotions)
            .Select(model => new ModelKey(model.Id.Category, model.Id.Entry)).ToHashSet(ModelKeyComparer.Instance);
        bool Unavailable(ModelKey key, NeowCandidatePoolKind pool) => known.Contains(key) &&
            catalog.CompleteCandidatePools.Contains(pool) && !Candidates(pool).Contains(key, ModelKeyComparer.Instance);
        IReadOnlyList<ModelKey> Candidates(NeowCandidatePoolKind pool) => pool == NeowCandidatePoolKind.TransformCards
            ? catalog.InitialBasicTransformCards : catalog.Candidates(pool);
        var runtimeRoutes = NeowSearchUiCatalog.AllRuntimeIdentities().ToHashSet(ModelKeyComparer.Instance);
        bool transformInputsKnown = _source != BaseGameModelKeys.Relics.NeowsBones || HasTransformPair;
        if (catalog.RouteEligibilityComplete)
        {
            if (_source is { } source && runtimeRoutes.Contains(source) && !catalog.RouteRelics.Contains(source, ModelKeyComparer.Instance))
            { removed.Add(source); _source = null; }
            foreach (ModelKey child in _bones.Where(key => runtimeRoutes.Contains(key) && !catalog.BonesNeowRelics.Contains(key, ModelKeyComparer.Instance)).ToArray())
            { removed.Add(child); _bones.Remove(child); }
            if (_players > 1 && _partyOffers.TryGetValue(_activeSeat, out var offers))
                foreach (ModelKey option in offers.Where(key => runtimeRoutes.Contains(key) && !catalog.RouteRelics.Contains(key, ModelKeyComparer.Instance)).ToArray())
                { offers.Remove(option); removed.Add(option); }
        }
        foreach (var entry in _slots.ToArray())
        {
            if (entry.Value is not { } key) continue;
            // Slot IDs are source/component/ordinal. Unknown registrations retain
            // their authoring state until their owning source can prove membership.
            var definition = NeowEffectCardRegistry.All.FirstOrDefault(value => entry.Key.StartsWith(value.RouteRelicKey + "/", StringComparison.Ordinal));
            if (definition is null) continue;
            string componentId = entry.Key[(definition.RouteRelicKey.ToString().Length + 1)..].Split('/')[0];
            NeowCandidatePoolKind? pool = definition.Components.FirstOrDefault(value => value.ComponentId == componentId)?.CandidatePool;
            if (!transformInputsKnown && pool is NeowCandidatePoolKind.TransformCards or NeowCandidatePoolKind.NewLeafTransformCards) continue;
            if (definition.RouteRelicKey == BaseGameModelKeys.Relics.ScrollBoxes && componentId == "bundle")
            {
                if (!catalog.CompleteCandidatePools.Contains(NeowCandidatePoolKind.CommonCharacterCards) ||
                    !catalog.CompleteCandidatePools.Contains(NeowCandidatePoolKind.UncommonCharacterCards)) continue;
                if (!known.Contains(key)) continue;
                if (catalog.CharacterKey == BaseGameModelKeys.Characters.Defect)
                {
                    if (catalog.ClawKey is not { } claw || key == claw) continue;
                }
                else if (catalog.CommonCharacterCards.Concat(catalog.UncommonCharacterCards).Contains(key, ModelKeyComparer.Instance)) continue;
            }
            else if (pool is null || !Unavailable(key, pool.Value)) continue;
            _slots.Remove(entry.Key); removed.Add(key);
        }
        bool completeTransforms = transformInputsKnown && catalog.CompleteCandidatePools.Contains(NeowCandidatePoolKind.TransformCards) &&
            catalog.CompleteCandidatePools.Contains(NeowCandidatePoolKind.NewLeafTransformCards);
        var transforms = catalog.InitialBasicTransformCards.Concat(catalog.NewLeafTransformCards).ToHashSet(ModelKeyComparer.Instance);
        for (int i = 0; i < _draft.TransformTargets.Length; i++)
            if (_draft.TransformTargets[i] is { } key && completeTransforms && known.Contains(key) && !transforms.Contains(key))
            { removed.Add(key); _draft.TransformTargets[i] = null; }
        for (int i = 0; i < _draft.CapsuleTargets.Length; i++)
            if (_draft.CapsuleTargets[i] is { } key && Unavailable(key, NeowCandidatePoolKind.OrdinaryRelics))
            { removed.Add(key); _draft.CapsuleTargets[i] = null; }
        if (_bonesFinalCurse is { } curse && Unavailable(curse, NeowCandidatePoolKind.Curses))
        { removed.Add(curse); _bonesFinalCurse = null; }
        if (_draft.ScrollOffer == NeowSpecialOfferKind.ScrollBoxesTripleClaw &&
            catalog.CompleteCandidatePools.Contains(NeowCandidatePoolKind.CharacterCards) && catalog.ClawKey is null)
        { _draft.ScrollOffer = NeowSpecialOfferKind.None; removed.Add(new("CARD", "CLAW")); }
        return removed;
    }

    internal bool TryCharacterChangeTransformationCandidates(int seat, out IReadOnlyList<ModelKey> candidates)
    {
        candidates = [];
        if (!_drafts.TryGetValue(seat, out var draft) || draft.Catalog is not { } catalog ||
            !catalog.CompleteCandidatePools.Contains(NeowCandidatePoolKind.TransformCards) ||
            !catalog.CompleteCandidatePools.Contains(NeowCandidatePoolKind.NewLeafTransformCards)) return false;
        var sources = TransformationSources(seat);
        if (sources.Count != 1) return false;
        if (sources[0].Id is not ("N.LeafyPoultice" or "N.NewLeaf" or "N.BonesLeafyNewLeaf")) return false;
        candidates = sources[0].Id switch
        {
            "N.LeafyPoultice" => catalog.InitialBasicTransformCards,
            "N.NewLeaf" => catalog.NewLeafTransformCards,
            "N.BonesLeafyNewLeaf" => catalog.InitialBasicTransformCards.Concat(catalog.NewLeafTransformCards).Distinct(ModelKeyComparer.Instance).ToArray(),
            _ => []
        };
        // An arbitrary Bones companion can alter the deck before Leafy runs.
        // The initial-deck snapshot cannot prove exclusions for that composition.
        return true;
    }
}
