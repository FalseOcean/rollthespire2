using RolltheSpire2.Core.Identity;
using RolltheSpire2.Search.Contracts;

namespace RolltheSpire2.Search.Selectivity;

/// <summary>
/// Small exact solvers for the two Normal Merchant queue shapes. Relic Shop is a
/// prefix of a uniform permutation; colorless lanes are independent draws with
/// replacement. Only the five authored positions are tracked.
/// </summary>
internal static class ShopSequenceProbabilityEstimator
{
    public static bool TrySolveColorlessConjunction(
        IReadOnlyList<ModelKey> pool,
        IReadOnlyList<MerchantColorlessSlotCondition> slotConditions,
        IReadOnlyList<MerchantColorlessSequenceSearchCondition> sequenceConditions,
        out double probability,
        out string detail)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(slotConditions);
        ArgumentNullException.ThrowIfNull(sequenceConditions);
        probability = 0d;
        detail = string.Empty;
        if (pool.Count == 0 || pool.Distinct(ModelKeyComparer.Instance).Count() != pool.Count)
        {
            detail = "ColorlessPoolNotUnique";
            return false;
        }

        var orderedByPosition = new Dictionary<int, ModelKey>();
        var unordered = new List<(int Horizon, Dictionary<ModelKey, int> Requested)>();
        int maximumPosition = 0;
        string orderedFailure = string.Empty;
        bool orderedFailureIsResolvedZero = false;

        bool AddOrdered(int position, ModelKey target)
        {
            if (!target.IsValid)
            {
                orderedFailure = "ColorlessInvalidTarget";
                return false;
            }
            if (!pool.Contains(target, ModelKeyComparer.Instance))
            {
                orderedFailure = "ColorlessTargetAbsentFromRuntimePool";
                orderedFailureIsResolvedZero = true;
                return false;
            }
            if (orderedByPosition.TryGetValue(position, out ModelKey existing) && existing != target)
            {
                orderedFailure = "ColorlessOrderedPositionConflict";
                orderedFailureIsResolvedZero = true;
                return false;
            }
            orderedByPosition[position] = target;
            return true;
        }

        foreach (MerchantColorlessSlotCondition condition in slotConditions)
        {
            if (!condition.IsValid || condition.MerchantOrdinal > 5)
            {
                detail = "ColorlessSlotConditionInvalid";
                return false;
            }
            maximumPosition = Math.Max(maximumPosition, condition.MerchantOrdinal);
            if (!AddOrdered(condition.MerchantOrdinal - 1, condition.TargetCardKey))
            {
                detail = orderedFailure;
                return orderedFailureIsResolvedZero;
            }
        }

        foreach (MerchantColorlessSequenceSearchCondition condition in sequenceConditions)
        {
            if (condition.IsEmpty) continue;
            if (condition.Count is < 1 or > 5 || condition.Slots.Count < condition.Count ||
                condition.OrderMode is not (CombatRewardSequenceOrderMode.Ordered or CombatRewardSequenceOrderMode.Unordered))
            {
                detail = "ColorlessSequenceShapeUnsupported";
                return false;
            }
            ModelKey?[] slots = condition.Slots.Take(condition.Count).ToArray();
            if (slots.Any(target => target.HasValue && !target.Value.IsValid))
            {
                detail = "ColorlessInvalidTarget";
                return false;
            }
            maximumPosition = Math.Max(maximumPosition, condition.Count);
            if (condition.OrderMode == CombatRewardSequenceOrderMode.Ordered)
            {
                for (int position = 0; position < slots.Length; position++)
                    if (slots[position] is ModelKey target && !AddOrdered(position, target))
                    {
                        detail = orderedFailure;
                        return orderedFailureIsResolvedZero;
                    }
                continue;
            }

            var requested = new Dictionary<ModelKey, int>(ModelKeyComparer.Instance);
            foreach (ModelKey target in slots.Where(target => target.HasValue).Select(target => target!.Value))
            {
                if (!pool.Contains(target, ModelKeyComparer.Instance))
                {
                    probability = 0d;
                    detail = "ColorlessTargetAbsentFromRuntimePool";
                    return true;
                }
                requested[target] = requested.GetValueOrDefault(target) + 1;
            }
            if (requested.Count != 0) unordered.Add((condition.Count, requested));
        }

        if (maximumPosition == 0)
        {
            probability = 1d;
            detail = "NoActiveColorlessConstraint";
            return true;
        }

        ModelKey[] unorderedTargets = unordered.SelectMany(item => item.Requested.Keys)
            .Distinct(ModelKeyComparer.Instance).ToArray();
        var targetIndex = unorderedTargets.Select((key, index) => (key, index))
            .ToDictionary(item => item.key, item => item.index, ModelKeyComparer.Instance);
        int[] caps = unorderedTargets.Select(target =>
            unordered.Max(item => item.Requested.GetValueOrDefault(target))).ToArray();
        if (caps.Sum() > maximumPosition)
        {
            probability = 0d;
            detail = "ColorlessJointMultiplicityImpossible";
            return true;
        }

        int[] multipliers = new int[caps.Length];
        int multiplier = 1;
        for (int index = 0; index < caps.Length; index++)
        {
            multipliers[index] = multiplier;
            multiplier = checked(multiplier * (caps[index] + 1));
        }
        int otherCount = pool.Count - unorderedTargets.Length;
        var memo = new Dictionary<(int Position, int Code), double>();

        int AddObserved(int code, ModelKey target)
        {
            if (!targetIndex.TryGetValue(target, out int index)) return code;
            int digit = code / multipliers[index] % (caps[index] + 1);
            return digit < caps[index] ? code + multipliers[index] : code;
        }

        bool TriggeredConstraintsPass(int completedPositions, int code)
        {
            foreach ((int horizon, Dictionary<ModelKey, int> requested) in unordered)
            {
                if (horizon != completedPositions) continue;
                foreach ((ModelKey target, int count) in requested)
                {
                    int index = targetIndex[target];
                    int actual = code / multipliers[index] % (caps[index] + 1);
                    if (actual < count) return false;
                }
            }
            return true;
        }

        double Solve(int position, int code)
        {
            if (position >= maximumPosition) return 1d;
            var state = (position, code);
            if (memo.TryGetValue(state, out double cached)) return cached;

            int completed = position + 1;
            double value;
            if (orderedByPosition.TryGetValue(position, out ModelKey orderedTarget))
            {
                int nextCode = AddObserved(code, orderedTarget);
                value = TriggeredConstraintsPass(completed, nextCode)
                    ? Solve(completed, nextCode) / pool.Count
                    : 0d;
            }
            else
            {
                value = 0d;
                if (otherCount > 0 && TriggeredConstraintsPass(completed, code))
                    value += otherCount / (double)pool.Count * Solve(completed, code);
                foreach (ModelKey target in unorderedTargets)
                {
                    int nextCode = AddObserved(code, target);
                    if (TriggeredConstraintsPass(completed, nextCode))
                        value += Solve(completed, nextCode) / pool.Count;
                }
            }
            memo[state] = value;
            return value;
        }

        probability = Math.Clamp(Solve(0, 0), 0d, 1d);
        detail = $"ExactColorlessReplacementConjunction;pool={pool.Count};horizon={maximumPosition};" +
                 $"orderedPositions={orderedByPosition.Count};unorderedConstraints={unordered.Count};states={memo.Count}";
        return true;
    }

    public static bool TrySolveRelic(
        IReadOnlyList<ModelKey> pool,
        RelicShopSequenceSearchCondition condition,
        out double probability,
        out string detail)
    {
        probability = 0d;
        detail = string.Empty;
        if (!TryGetTargets(condition.Count, condition.OrderMode, condition.Slots, out ModelKey?[] slots, out detail)) return false;
        ModelKey[] targets = slots.Where(key => key.HasValue).Select(key => key!.Value).ToArray();
        if (targets.Length == 0) { probability = 1d; detail = "NoActiveRelicShopTargets"; return true; }
        if (pool.Count == 0 || pool.Distinct(ModelKeyComparer.Instance).Count() != pool.Count)
        {
            detail = "RelicShopPoolNotUnique";
            return false;
        }
        if (targets.Distinct(ModelKeyComparer.Instance).Count() != targets.Length)
        {
            probability = 0d;
            detail = "RelicShopDuplicateTargetImpossible";
            return true;
        }
        if (targets.Any(target => !pool.Contains(target, ModelKeyComparer.Instance)))
        {
            probability = 0d;
            detail = "RelicShopTargetAbsentFromAllowedPool";
            return true;
        }

        if (condition.OrderMode == CombatRewardSequenceOrderMode.Ordered)
        {
            int activeTargets = slots.Count(key => key.HasValue);
            double value = 1d;
            for (int index = 0; index < activeTargets; index++)
                value /= pool.Count - index;
            probability = Math.Clamp(value, 0d, 1d);
            string positions = string.Join(",", slots
                .Select((key, index) => (key, index))
                .Where(item => item.key.HasValue)
                .Select(item => (item.index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            detail = $"ExactRelicShopPositional;pool={pool.Count};horizon={condition.Count};active={activeTargets};positions={positions};mode={condition.OrderMode}";
            return true;
        }

        ulong initialMask = targets.Length == 64 ? ulong.MaxValue : (1UL << targets.Length) - 1UL;
        var memo = new Dictionary<(int Position, ulong Remaining), double>();
        int horizon = Math.Min(condition.Count, pool.Count);

        double SolveUnordered(int position, ulong remaining)
        {
            if (position >= horizon) return remaining == 0UL ? 1d : 0d;
            var state = (position, remaining);
            if (memo.TryGetValue(state, out double cached)) return cached;
            int remainingCount = pool.Count - position;
            double value = 0d;
            int remainingTargets = BitCount(remaining);
            int otherCount = remainingCount - remainingTargets;
            for (int targetPosition = 0; targetPosition < targets.Length; targetPosition++)
            {
                ulong targetBit = 1UL << targetPosition;
                if ((remaining & targetBit) == 0UL) continue;
                value += (1d / remainingCount) * SolveUnordered(position + 1, remaining ^ targetBit);
            }
            if (otherCount > 0)
                value += (otherCount / (double)remainingCount) * SolveUnordered(position + 1, remaining);
            memo[state] = value;
            return value;
        }

        probability = Math.Clamp(SolveUnordered(0, initialMask), 0d, 1d);
        detail = $"ExactRelicShopUnorderedPermutationDP;pool={pool.Count};horizon={horizon};states={memo.Count};mode={condition.OrderMode}";
        return true;
    }

    public static bool TrySolveColorless(
        IReadOnlyList<ModelKey> pool,
        MerchantColorlessSequenceSearchCondition condition,
        out double probability,
        out string detail)
    {
        probability = 0d;
        detail = string.Empty;
        if (!TryGetTargets(condition.Count, condition.OrderMode, condition.Slots, out ModelKey?[] slots, out detail)) return false;
        ModelKey[] targets = slots.Where(key => key.HasValue).Select(key => key!.Value).ToArray();
        if (targets.Length == 0) { probability = 1d; detail = "NoActiveColorlessTargets"; return true; }
        if (pool.Count == 0 || pool.Distinct(ModelKeyComparer.Instance).Count() != pool.Count)
        {
            detail = "ColorlessPoolNotUnique";
            return false;
        }
        if (targets.Any(target => !pool.Contains(target, ModelKeyComparer.Instance)))
        {
            probability = 0d;
            detail = "ColorlessTargetAbsentFromRuntimePool";
            return true;
        }

        var targetIndex = targets.Distinct(ModelKeyComparer.Instance)
            .Select((key, index) => (key, index))
            .ToDictionary(item => item.key, item => item.index, ModelKeyComparer.Instance);
        int[] requested = targetIndex.Keys.Select(key => targets.Count(item => item == key)).ToArray();
        int[] multipliers = new int[requested.Length];
        int baseValue = condition.Count + 1;
        int multiplier = 1;
        for (int index = 0; index < multipliers.Length; index++) { multipliers[index] = multiplier; multiplier *= baseValue; }
        int targetCount = targetIndex.Count;
        int otherCount = pool.Count - targetCount;
        var memoUnordered = new Dictionary<(int Position, int Code), double>();

        bool Complete(int code)
        {
            for (int index = 0; index < requested.Length; index++)
                if (code / multipliers[index] % baseValue < requested[index]) return false;
            return true;
        }

        double SolveUnordered(int position, int code)
        {
            if (position >= condition.Count) return Complete(code) ? 1d : 0d;
            var state = (position, code);
            if (memoUnordered.TryGetValue(state, out double cached)) return cached;
            double value = otherCount / (double)pool.Count * SolveUnordered(position + 1, code);
            foreach ((ModelKey target, int index) in targetIndex)
            {
                int digit = code / multipliers[index] % baseValue;
                int nextCode = digit < requested[index] ? code + multipliers[index] : code;
                value += (1d / pool.Count) * SolveUnordered(position + 1, nextCode);
            }
            memoUnordered[state] = value;
            return value;
        }

        if (condition.OrderMode == CombatRewardSequenceOrderMode.Ordered)
        {
            int activeTargets = slots.Count(key => key.HasValue);
            probability = Math.Clamp(Math.Pow(1d / pool.Count, activeTargets), 0d, 1d);
            string positions = string.Join(",", slots
                .Select((key, index) => (key, index))
                .Where(item => item.key.HasValue)
                .Select(item => (item.index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            detail = $"ExactColorlessPositional;pool={pool.Count};horizon={condition.Count};active={activeTargets};positions={positions};mode={condition.OrderMode}";
            return true;
        }

        probability = Math.Clamp(SolveUnordered(0, 0), 0d, 1d);
        detail = $"ExactColorlessUnorderedReplacementDP;pool={pool.Count};horizon={condition.Count};states={memoUnordered.Count};mode={condition.OrderMode}";
        return true;
    }

    private static bool TryGetTargets(
        int count,
        CombatRewardSequenceOrderMode order,
        IReadOnlyList<ModelKey?> source,
        out ModelKey?[] slots,
        out string issue)
    {
        slots = Array.Empty<ModelKey?>();
        issue = string.Empty;
        if (count is < 1 or > 5 || source.Count < count || order is not (CombatRewardSequenceOrderMode.Ordered or CombatRewardSequenceOrderMode.Unordered))
        {
            issue = "ShopSequenceShapeUnsupported";
            return false;
        }
        slots = source.Take(count).ToArray();
        if (slots.Any(key => key.HasValue && !key.Value.IsValid))
        {
            issue = "ShopSequenceInvalidTarget";
            return false;
        }
        return true;
    }

    private static int BitCount(ulong value)
    {
        int count = 0;
        while (value != 0UL) { value &= value - 1UL; count++; }
        return count;
    }
}
