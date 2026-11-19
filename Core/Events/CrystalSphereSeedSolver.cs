using System.Collections.Immutable;
using System.Numerics;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Core.Rng;
using static RolltheSpire2.Core.Events.CrystalSphereSeedSearch;
using static RolltheSpire2.Core.Events.CrystalSphereSeedCardOffers;
using static RolltheSpire2.Core.PredictorRuntime.PredictorCrystalBoard;

namespace RolltheSpire2.Core.Events;

internal sealed class CrystalSphereSeedSolver(
    PredictorCrystalBoard board, Xoshiro256StarStar rng, int divinations, CrystalSphereSeedTargets targets,
    CancellationToken token, int stateLimit, CrystalSphereSeedCardTargets? cardTargets = null, bool driftwood = false)
{
    private readonly record struct Move(UInt128 CenterBit, UInt128 Mask, int[] OrderedCells,
        PredictorCrystalStep Step);

    // Offsets use plus one so zero means unrevealed. The board has at most
    // ten tiles of each card kind and six relic tiles. Each tile contributes
    // at most 15 Populate calls per occupied cell, so 12 bits cover an offset.
    private const int OffsetBits = 12;
    private const int OffsetMask = (1 << OffsetBits) - 1;
    private readonly record struct RewardProgress(int PotionCalls, int CoreCalls, UInt128 RelicOffsets,
        UInt128 UncommonOffsets, UInt128 RareOffsets, int CardCounts);

    private readonly record struct CardMenus(int PotionCalls, UInt128 UncommonOffsets, UInt128 RareOffsets,
        int RerollStart, int RerollableCounts);

    private static readonly Move[] Moves = MakeMoves();
    private static readonly Move[][] Covering = Enumerable.Range(0, CellCount)
        .Select(cell => Moves.Where(move => (move.Mask & ((UInt128)1 << cell)) != 0).ToArray()).ToArray();
    private static readonly UInt128[] PackingExcluded = Enumerable.Range(0, CellCount)
        .Select(cell =>
        {
            int x = cell / Side, y = cell % Side;
            int left = Math.Max(0, x - 2), top = Math.Max(0, y - 2);
            return Rectangle(left, top, Math.Min(Side, x + 3) - left, Math.Min(Side, y + 3) - top);
        }).ToArray();

    private readonly Move[] _path = new Move[divinations];
    private readonly int[] _occupancy = new int[CellCount];
    private readonly int[] _relics = Enumerable.Range(0, board.Items.Length)
        .Where(index => board.Items[index].Kind == PredictorCrystalItemKind.Relic && board.Items[index].Cells != 0).ToArray();
    private readonly int _requestedRarities = targets.RequiredRelicRarities;
    private readonly List<EffectRelicRarity> _rolls = [];
    private readonly Xoshiro256StarStar _rollRng = rng.Clone();
    private readonly CrystalSphereSeedCardOffers? _uncommon = cardTargets is { Uncommon.Length: > 0 }
        ? new(cardTargets.UncommonPool, cardTargets.Uncommon, rng) : null;
    private readonly CrystalSphereSeedCardOffers? _rare = cardTargets is { Rare.Length: > 0 }
        ? new(cardTargets.RarePool, cardTargets.Rare, rng) : null;
    private readonly int[] _cardOffsetColumns = new int[board.Items.Length];
    private readonly HashSet<(UInt128 Fog, int Left, RewardProgress Progress)> _dead = [];
    private readonly HashSet<CardMenus> _checkedMenus = [];
    private CrystalSphereSeedWitness? _witness;
    private int _remainingStates = stateLimit;
    private bool _limited;
    private UInt128 _occupied;
    private bool _canOmitBlankMoves;

    internal CrystalSphereSeedResult Solve()
    {
        token.ThrowIfCancellationRequested();
        board.WriteOccupancy(_occupancy);
        _occupied = board.Items.Aggregate((UInt128)0, (cells, item) => cells | item.Cells);
        // Omitting a blank-only click cannot hide another center required by
        // the productive subsequence or change any earlier item completion.
        // Each productive click can clear at most eight blanks. When enough
        // blanks remain for all padding, never enumerate blank-only clicks.
        _canOmitBlankMoves = (int)UInt128.PopCount(InitialFog & ~_occupied) >= (divinations - 1) * 8 + 1;
        var cardGroups = new List<((UInt128 Cells, int Copies)[] Placements, int Required)>();
        foreach (CrystalSphereSeedCards card in new[]
            { CrystalSphereSeedCards.Common, CrystalSphereSeedCards.Uncommon, CrystalSphereSeedCards.Rare })
        {
            int required = card switch
            {
                CrystalSphereSeedCards.Uncommon => _uncommon?.Required ?? 0,
                CrystalSphereSeedCards.Rare => _rare?.Required ?? 0,
                _ => 0
            };
            if ((targets.Cards & card) != 0) required = Math.Max(1, required);
            if (required == 0) continue;
            var placements = board.Items.Where(item => item.Cells != 0 && CardKind(item.Kind) == card)
                .Select(item => (item.Cells, item.Subscriptions)).ToArray();
            if (placements.Sum(placement => placement.Subscriptions) < required) return new(CrystalSphereSeedVerdict.NoMatch);
            cardGroups.Add((placements, required));
        }
        if (cardTargets != null)
        {
            int uncommonColumn = 0, rareColumn = 0, maximumDraws = 0;
            for (int i = 0; i < board.Items.Length; i++)
            {
                var item = board.Items[i];
                if (item.Cells == 0) continue;
                if (item.Kind == PredictorCrystalItemKind.UncommonCard) _cardOffsetColumns[i] = uncommonColumn++;
                if (item.Kind == PredictorCrystalItemKind.RareCard) _cardOffsetColumns[i] = rareColumn++;
                maximumDraws += item.Subscriptions * (IsPotion(item.Kind) ? 1 : PopulationCalls(item.Kind));
                if (driftwood && CardKind(item.Kind) != 0) maximumDraws += item.Subscriptions * 6;
            }
            if ((_uncommon != null && !_uncommon.AllPossible(maximumDraws - 6)) ||
                (_rare != null && !_rare.AllPossible(maximumDraws - 6))) return new(CrystalSphereSeedVerdict.NoMatch);
        }

        if (_requestedRarities == 0)
            ChooseCards(0, 0);
        else
        {
            if (_relics.Sum(index => board.Items[index].Subscriptions) < BitOperations.PopCount((uint)_requestedRarities))
                return new(CrystalSphereSeedVerdict.NoMatch);
            foreach (int relic in _relics)
            {
                ChooseCards(0, board.Items[relic].Cells);
                if (_witness != null || _limited) break;
            }
        }

        if (_witness != null) return new(CrystalSphereSeedVerdict.Match, _witness);
        return _limited ? new(CrystalSphereSeedVerdict.Unknown, Reason: "CrystalSphereSeedStateLimit")
            : new(CrystalSphereSeedVerdict.NoMatch);

        void ChooseCards(int group, UInt128 required)
        {
            if (group < cardGroups.Count)
            {
                var (placements, needed) = cardGroups[group];
                ChoosePlacements(group, placements, needed, required, 0);
                return;
            }
            if (!FindCover(required, 0, 0, out int used)) return;
            UInt128 fog = InitialFog;
            for (int i = 0; i < used; i++) fog &= ~_path[i].Mask;
            bool orderedGoals = _requestedRarities != 0 || cardTargets != null;
            if (TryPad(fog, used, keepRewards: orderedGoals) && TryRecordWitness()) return;
            if (_limited) return;
            if (!orderedGoals) throw new InvalidOperationException("CrystalSphereSeedCardCoverDidNotReplay");
            if (cardTargets == null)
            {
                _dead.Clear();
                FindOrdered(required, InitialFog, 0, default, divinations, Moves);
                return;
            }

            // Start with routes that need the fewest productive clicks.
            // Unused clicks can still be spent without disturbing the menus.
            var prioritized = Moves.OrderByDescending(move =>
                    board.Items.Count(item => item.Cells != 0 && (item.Cells & ~move.Mask) == 0))
                .ThenByDescending(move => UInt128.PopCount(required & move.Mask)).ToArray();
            for (int maxDepth = 1; maxDepth <= divinations && !_limited; maxDepth++)
            {
                _dead.Clear();
                if (FindOrdered(required, InitialFog, 0, default, maxDepth, prioritized)) return;
            }
        }

        void ChoosePlacements(int group, (UInt128 Cells, int Copies)[] placements, int needed, UInt128 required, int start)
        {
            if (needed <= 0)
            {
                ChooseCards(group + 1, required);
                return;
            }
            for (int i = start; i < placements.Length; i++)
            {
                var (cells, copies) = placements[i];
                ChoosePlacements(group, placements, needed - copies, required | cells, i + 1);
                if (_witness != null || _limited) return;
            }
        }
    }

    private bool Continue()
    {
        token.ThrowIfCancellationRequested();
        if (_remainingStates > 0)
        {
            _remainingStates--;
            return true;
        }
        _limited = true;
        return false;
    }

    // A minimal geometric cover never needs a Small click covered by a Big
    // click. Two Big clicks within each other's footprints cannot both occur.
    // The remaining clicks cannot hide each other's centers and may be
    // enumerated as an unordered cover before replaying an actual sequence.
    private bool FindCover(UInt128 need, UInt128 cleared, int depth, out int used)
    {
        used = depth;
        if (need == 0) return true;
        int left = divinations - depth;
        if (!MayCover(need, left) || !Continue()) return false;

        int pivot = FirstCell(need);
        foreach (Move move in Covering[pivot])
        {
            if ((move.CenterBit & cleared) != 0) continue;
            bool blocksPrevious = false;
            for (int i = 0; i < depth && !blocksPrevious; i++)
                blocksPrevious = (move.Mask & _path[i].CenterBit) != 0;
            if (blocksPrevious) continue;
            _path[depth] = move;
            if (FindCover(need & ~move.Mask, cleared | move.Mask, depth + 1, out used)) return true;
            if (_limited) return false;
        }
        return false;
    }

    private bool FindOrdered(UInt128 required, UInt128 fog, int depth, RewardProgress progress, int maxDepth, Move[] moves)
    {
        int left = maxDepth - depth;
        UInt128 need = required & fog;
        if (!MayCover(need, left)) return false;
        if (need == 0 && RelicsMatch(progress) && TryPad(fog, depth, keepRewards: true) &&
            (cardTargets == null || _checkedMenus.Add(new(progress.PotionCalls, progress.UncommonOffsets, progress.RareOffsets,
                driftwood ? progress.PotionCalls + progress.CoreCalls : 0, progress.CardCounts))))
        {
            if (TryRecordWitness()) return true;
            if (_limited) return false;
            if (cardTargets == null) throw new InvalidOperationException("CrystalSphereSeedRelicRouteDidNotReplay");
        }
        if (left == 0 || !_dead.Add((fog, left, progress)) || !Continue()) return false;

        // Try target coverage first. Optional moves can change the eventual
        // relic roll, including a potion revealed after the relic itself.
        for (int pass = 0; pass < 2; pass++)
            foreach (Move move in moves)
            {
                if ((fog & move.CenterBit) == 0 || ((need & move.Mask) != 0) != (pass == 0)) continue;
                if (_canOmitBlankMoves && (fog & move.Mask & _occupied) == 0) continue;
                if (!MayCover(need & ~move.Mask, left - 1)) continue;
                UInt128 nextFog = fog;
                RewardProgress next = progress;
                foreach (int cell in move.OrderedCells)
                {
                    UInt128 bit = (UInt128)1 << cell;
                    if ((nextFog & bit) == 0) continue;
                    nextFog &= ~bit;
                    int index = _occupancy[cell];
                    if (index < 0 || (board.Items[index].Cells & nextFog) != 0) continue;
                    next = AddReward(next, index);
                }
                _path[depth] = move;
                if (FindOrdered(required, nextFog, depth + 1, next, maxDepth, moves)) return true;
                if (_limited) return false;
            }
        return false;
    }

    private RewardProgress AddReward(RewardProgress progress, int index)
    {
        var item = board.Items[index];
        UInt128 offset = (UInt128)(progress.CoreCalls + 1);
        if (item.Kind == PredictorCrystalItemKind.Relic && _requestedRarities != 0)
            progress = progress with { RelicOffsets = progress.RelicOffsets | (offset << (Array.IndexOf(_relics, index) * OffsetBits)) };
        if (item.Kind == PredictorCrystalItemKind.UncommonCard && _uncommon != null)
            progress = progress with { UncommonOffsets = progress.UncommonOffsets | (offset << (_cardOffsetColumns[index] * OffsetBits)) };
        if (item.Kind == PredictorCrystalItemKind.RareCard && _rare != null)
            progress = progress with { RareOffsets = progress.RareOffsets | (offset << (_cardOffsetColumns[index] * OffsetBits)) };
        if (driftwood)
        {
            // Ten boards with cumulative subscriptions give at most 55
            // menus of each rarity, so counts can occupy separate bytes.
            int count = item.Kind switch
            {
                PredictorCrystalItemKind.CommonCard => item.Subscriptions,
                PredictorCrystalItemKind.UncommonCard => item.Subscriptions << 8,
                PredictorCrystalItemKind.RareCard => item.Subscriptions << 16,
                _ => 0
            };
            progress = progress with { CardCounts = progress.CardCounts + count };
        }
        return IsPotion(item.Kind) ? progress with { PotionCalls = progress.PotionCalls + item.Subscriptions } :
            progress with { CoreCalls = progress.CoreCalls + item.Subscriptions * PopulationCalls(item.Kind) };
    }

    private bool RelicsMatch(RewardProgress progress)
    {
        int observed = 0;
        for (int slot = 0; slot < _relics.Length; slot++)
        {
            int offset = (int)(progress.RelicOffsets >> (slot * OffsetBits)) & OffsetMask;
            if (offset == 0) continue;
            int first = progress.PotionCalls + offset - 1;
            for (int i = 0; i < board.Items[_relics[slot]].Subscriptions; i++)
                observed |= CrystalSphereSeedTargets.RelicBit(RollAt(first + i));
            if ((observed & _requestedRarities) == _requestedRarities) return true;
        }
        return false;
    }

    private EffectRelicRarity RollAt(int offset)
    {
        while (_rolls.Count <= offset) _rolls.Add(NeowRewardGenerator.RollRelicRarity(_rollRng));
        return _rolls[offset];
    }

    private static bool MayCover(UInt128 need, int left)
    {
        if (need == 0) return true;
        if (left <= 0 || (int)UInt128.PopCount(need) > left * 9) return false;
        // Any two chosen cells farther than two along either axis need
        // distinct moves, even allowing all large-click centers.
        for (int clicks = 0; need != 0; clicks++)
        {
            if (clicks == left) return false;
            need &= ~PackingExcluded[FirstCell(need)];
        }
        return true;
    }

    private bool TryPad(UInt128 fog, int depth, bool keepRewards)
    {
        for (; depth < divinations; depth++)
        {
            UInt128 available = fog;
            if (keepRewards)
                foreach (var item in board.Items)
                {
                    UInt128 rest = fog & item.Cells;
                    if ((rest & (rest - 1)) == 0) available &= ~rest;
                }
            if (available == 0) return false;
            Move move = Small[FirstCell(available)];
            _path[depth] = move;
            fog &= ~move.Mask;
        }
        return true;
    }

    // Recreate the native reveal and two preparation phases from the witness.
    // Potions select their identities in ToReward before any reward Populate.
    // The remaining Populate calls follow reveal order, before reward sorting.
    private bool TryRecordWitness()
    {
        UInt128 fog = InitialFog;
        var revealed = new List<int>();
        foreach (Move move in _path)
        {
            if ((fog & move.CenterBit) == 0) throw new InvalidOperationException("CrystalSphereSeedHiddenCenterRequired");
            foreach (int cell in move.OrderedCells)
            {
                UInt128 bit = (UInt128)1 << cell;
                if ((fog & bit) == 0) continue;
                fog &= ~bit;
                int item = _occupancy[cell];
                if (item >= 0 && (fog & board.Items[item].Cells) == 0) revealed.Add(item);
            }
        }

        var cards = CrystalSphereSeedCards.None;
        int rolledRarities = 0;
        int offset = revealed.Where(index => IsPotion(board.Items[index].Kind)).Sum(index => board.Items[index].Subscriptions);
        int uncommonCount = CountSlots(CrystalSphereSeedCards.Uncommon);
        int rareCount = CountSlots(CrystalSphereSeedCards.Rare);
        int commonCount = driftwood ? CountSlots(CrystalSphereSeedCards.Common) : 0;
        Span<RewardSlot> uncommonSlots = stackalloc RewardSlot[uncommonCount];
        Span<RewardSlot> rareSlots = stackalloc RewardSlot[rareCount];
        Span<RewardSlot> commonSlots = stackalloc RewardSlot[commonCount];
        int uncommonIndex = 0, rareIndex = 0, commonIndex = 0;
        var rewards = ImmutableArray.CreateBuilder<CrystalSphereSeedReward>(revealed.Count);
        foreach (int index in revealed)
        {
            var item = board.Items[index];
            var rarity = CardKind(item.Kind);
            cards |= rarity;
            ImmutableArray<EffectRelicRarity> rolls = [];
            if (item.Kind == PredictorCrystalItemKind.Relic)
            {
                rolls = Enumerable.Range(offset, item.Subscriptions).Select(RollAt).ToImmutableArray();
                foreach (var roll in rolls) rolledRarities |= CrystalSphereSeedTargets.RelicBit(roll);
            }
            for (int copy = 0; copy < item.Subscriptions && cardTargets != null; copy++)
            {
                var slot = new RewardSlot(index, copy, offset + copy * 6);
                if (rarity == CrystalSphereSeedCards.Uncommon && uncommonCount > 0) uncommonSlots[uncommonIndex++] = slot;
                if (rarity == CrystalSphereSeedCards.Rare && rareCount > 0) rareSlots[rareIndex++] = slot;
                if (rarity == CrystalSphereSeedCards.Common && commonCount > 0) commonSlots[commonIndex++] = slot;
            }
            offset += PopulationCalls(item.Kind) * item.Subscriptions;
            rewards.Add(new(index, item.Code, item.Subscriptions, rolls));
        }
        if ((rolledRarities & _requestedRarities) != _requestedRarities || (cards & targets.Cards) != targets.Cards) return false;
        ImmutableArray<CrystalSphereSeedCardTake> takes = [];
        ImmutableArray<CrystalSphereSeedCardReroll> rerolls = [];
        if (cardTargets != null)
        {
            Span<RewardSlot> suffix = stackalloc RewardSlot[commonCount + uncommonCount + rareCount];
            Span<bool> used = stackalloc bool[suffix.Length];
            used.Clear();
            if (!FindCardTakes(uncommonSlots, rareSlots, commonSlots, offset, used, suffix, 0, out takes, out rerolls)) return false;
        }
        _witness = new(rng.CallCount, _path.Select(move => move.Step).ToImmutableArray(), rewards.MoveToImmutable())
        {
            CardTakes = takes,
            CardRerolls = rerolls
        };
        return true;

        int CountSlots(CrystalSphereSeedCards rarity) => cardTargets == null ? 0 :
            revealed.Where(index => CardKind(board.Items[index].Kind) == rarity).Sum(index => board.Items[index].Subscriptions);
    }

    private bool FindCardTakes(Span<RewardSlot> uncommon, Span<RewardSlot> rare, Span<RewardSlot> common, int offset,
        Span<bool> used, Span<RewardSlot> suffix, int depth, out ImmutableArray<CrystalSphereSeedCardTake> takes,
        out ImmutableArray<CrystalSphereSeedCardReroll> rerolls)
    {
        takes = [];
        rerolls = [];
        if (uncommon.Length < (_uncommon?.Required ?? 0) || rare.Length < (_rare?.Required ?? 0)) return false;
        bool uncommonMatch = _uncommon?.TryAssign(uncommon, 0) ?? true;
        bool rareMatch = _rare?.TryAssign(rare, 0) ?? true;
        if (uncommonMatch && rareMatch)
        {
            var assigned = ImmutableArray.CreateBuilder<CrystalSphereSeedCardTake>();
            _uncommon?.TryAssign(uncommon, 0, assigned);
            _rare?.TryAssign(rare, 0, assigned);
            takes = assigned.ToImmutable();
            var ordered = ImmutableArray.CreateBuilder<CrystalSphereSeedCardReroll>(depth);
            foreach (var step in suffix[..depth]) ordered.Add(new(step.Item, step.Subscription));
            rerolls = ordered.MoveToImmutable();
            return true;
        }

        if (!driftwood || depth == suffix.Length ||
            (!uncommonMatch && !used[..uncommon.Length].Contains(false)) ||
            (!rareMatch && !used.Slice(uncommon.Length, rare.Length).Contains(false)) || !Continue()) return false;
        // A reward can reroll only once. Previously generated card menus can
        // be retained, replaced by a useful reroll, or advance the event RNG
        // before another rarity rerolls. Taking the chosen cards can wait.
        bool triedOffsetOnly = false;
        for (int i = 0; i < used.Length; i++)
        {
            if (used[i]) continue;
            bool isUncommon = i < uncommon.Length;
            bool isRare = !isUncommon && i < uncommon.Length + rare.Length;
            bool offsetOnly = !(isUncommon && _uncommon != null || isRare && _rare != null);
            if (offsetOnly && triedOffsetOnly) continue;
            triedOffsetOnly |= offsetOnly;

            ref var slot = ref (isUncommon ? ref uncommon[i] : ref (isRare ? ref rare[i - uncommon.Length] :
                ref common[i - uncommon.Length - rare.Length]));
            var initial = slot;
            slot = slot with { Offset = offset };
            used[i] = true;
            suffix[depth] = slot;
            if (FindCardTakes(uncommon, rare, common, offset + 6, used, suffix, depth + 1, out takes, out rerolls)) return true;
            used[i] = false;
            slot = initial;
            if (_limited) return false;
        }
        return false;
    }

    private static readonly Move[] Small = Enumerable.Range(0, CellCount)
        .Select(center => MakeMove(center, PredictorCrystalTool.Small)).ToArray();

    private static Move[] MakeMoves() => Enumerable.Range(0, CellCount)
        .Where(center => (InitialFog & ((UInt128)1 << center)) != 0)
        .SelectMany(center => new[] { MakeMove(center, PredictorCrystalTool.Big), MakeMove(center, PredictorCrystalTool.Small) })
        .ToArray();

    private static Move MakeMove(int center, PredictorCrystalTool tool)
    {
        int x = center / Side, y = center % Side;
        int[] cells = PredictorCrystalSearch.Cells(x, y, tool).Select(cell => Index(cell.X, cell.Y)).ToArray();
        UInt128 mask = 0;
        foreach (int cell in cells) mask |= (UInt128)1 << cell;
        return new((UInt128)1 << center, mask, cells, new(x, y, tool));
    }
}
