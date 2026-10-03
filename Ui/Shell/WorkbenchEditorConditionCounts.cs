using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Ui.Shell;

// Navigation counts describe authored UI requirements, not backend predicates or RNG factors.
internal sealed partial class NeowEditorPrototype
{
    internal int AuthoredConditionCount()
    {
        if (_draft.Source is not { } source) return 0;
        int count = 1; // Opening identity.
        bool bones = source == BaseGameModelKeys.Relics.NeowsBones;
        ModelKey[] roots = bones ? _draft.Bones.ToArray() : [source];
        if (bones && roots.Length > 0) count++; // Nested relics and their pickup order form one requirement.

        var resultGroups = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string slot, ModelKey? value) in _draft.Slots)
        {
            if (!value.HasValue || !roots.Any(root => slot.StartsWith(root + "/", StringComparison.Ordinal))) continue;
            int ordinal = slot.LastIndexOf('/');
            if (ordinal > 0) resultGroups.Add(slot[..ordinal]);
        }
        count += resultGroups.Count;
        if (bones && _draft.JointCapsules && _draft.CapsuleTargets.Any(key => key.HasValue)) count++;
        if (bones && _draft.JointTransforms && _draft.TransformTargets.Any(key => key.HasValue) && !TManagesTransform()) count++;
        if (bones && _draft.Curse.HasValue) count++;
        if (!bones && source == BaseGameModelKeys.Relics.ScrollBoxes &&
            _draft.ScrollOffer == NeowSpecialOfferKind.ScrollBoxesTripleClaw) count++;
        return count;
    }
}

internal sealed partial class AncientEditorPrototype
{
    internal int AuthoredConditionCount()
    {
        ModeDraft mode = _draft.Current;
        int count = _draft.Mode != 0 && mode.NeowEnabled ? 1 + (mode.NeowOptions.Count > 0 ? 1 : 0) : 0;
        if (_multiplayer)
        {
            foreach (int act in new[] { 2, 3 })
            {
                if (SharedAncients(act).Count > 0) count++;
                count += SharedAncients(act).Count(key => mode.Acts[act]
                    .Any(row => row.Ancient == key && (row.Options.Count > 0 || row.SeaGlassTarget.HasValue)));
            }
            return count;
        }
        foreach (List<AncientDraft> rows in mode.Acts.Values)
            foreach (AncientDraft row in rows)
                count += 1 + (row.Options.Count > 0 ? 1 : 0);
        return count;
    }
}

internal sealed partial class ShopEditorPrototype
{
    internal int AuthoredConditionCount() => CountRow(_draft.Relic) + CountRow(_draft.Uncommon) + CountRow(_draft.Rare);

    private int CountRow(RowDraft row)
    {
        int filled = row.Slots.Take(_draft.ShopRange).Count(key => key.HasValue);
        return row.OrderMode == CombatRewardSequenceOrderMode.Ordered ? filled : filled > 0 ? 1 : 0;
    }
}

internal sealed partial class CombatRewardEditorPrototype
{
    internal int AuthoredConditionCount() => _draft.Slots.Take(_draft.BattleRange).Any(key => key.HasValue) ||
        _draft.Potions.Take(_draft.PotionRange).Any(p => !p.IsNeutral) ? 1 : 0;
}

internal sealed partial class RelicSequenceEditorPrototype
{
    internal int AuthoredConditionCount() => _draft.Conditions.Count;
}

internal sealed partial class EventEditorPrototype
{
    internal int AuthoredConditionCount() => QueueConditions.Count + AuthoredResultEvents().Count;

    private HashSet<string> AuthoredResultEvents()
    {
        var resultEvents = new HashSet<string>(StringComparer.Ordinal);
        foreach ((EventResultConditionKind kind, ModelKey?[] slots) in _draft.Results)
        {
            if (!slots.Any(key => key.HasValue)) continue;
            string eventId = ResultEventId(kind);
            if (TransformTakenOver?.Invoke(_activeSeat, "E." + eventId) != true) resultEvents.Add(eventId);
        }
        if (_draft.CharacterColor >= 0) resultEvents.Add("COLORFUL_PHILOSOPHERS");
        if (_draft.TrialCase >= 0 && TransformTakenOver?.Invoke(_activeSeat, "E.TRIAL") != true)
            resultEvents.Add("TRIAL");
        if (_draft.TinkerType >= 0) resultEvents.Add("TINKER_TIME");
        return resultEvents;
    }
}

internal sealed partial class ActInformationEditorPrototype
{
    internal int BossVariantConditionCount() => Enumerable.Range(0, 3).Sum(act =>
        (_draft.SelectedVariants[act].Count > 0 ? 1 : 0) + (_draft.SelectedBosses[act].Count > 0 ? 1 : 0));

    internal int MapConditionCount() => _draft.Routes.Count + _draft.Properties.Values.Sum(conditions => conditions.Count);
}

internal sealed partial class TransformationEditorPrototype
{
    internal int AuthoredConditionCount() => _draft.TakenOver.Count(id => _sources.Any(source => source.Id == id));
}
