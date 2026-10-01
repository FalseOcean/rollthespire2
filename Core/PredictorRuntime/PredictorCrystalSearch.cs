using System.Collections.Immutable;
using System.Diagnostics;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Rng;

namespace RolltheSpire2.Core.PredictorRuntime;

internal sealed record PredictorCrystalItem(string Kind, int Width, int Height, int X, int Y, int Subscriptions, bool Revealed);
internal sealed record PredictorCrystalSnapshot(PredictorContext Context, PredictorState State,
    PredictorStreamState EventRng, int Remaining, PredictorCrystalTool Tool, bool PlacedAllItems,
    ImmutableArray<bool> Hidden, ImmutableArray<int> Occupancy, ImmutableArray<PredictorCrystalItem> Items,
    ImmutableArray<int> Revealed);
internal sealed record PredictorCrystalOptions(bool AvoidCurse = true, bool RequireRelic = false);
internal sealed record PredictorCrystalStep(int X, int Y, PredictorCrystalTool Tool);
internal sealed record PredictorCrystalTake(int RewardIndex, ModelKey Card, int? UpgradeLevel = null);
internal sealed record PredictorCrystalSolution(string Status, long Examined,
    ImmutableArray<PredictorCrystalStep> Steps, ImmutableArray<PredictorCrystalTake> Takes)
{
    internal long RewardRows { get; init; }
    internal long RewardCacheHits { get; init; }
    internal long GeometryPruned { get; init; }
}

internal sealed partial class PredictorRun
{
    internal PredictorCrystalSnapshot ExportCrystal() => new(Context, Working,
        PredictorStreamState.Capture(PredictorStream.Rewards, _eventRng!), _crystalRemaining, _crystalTool,
        _crystalPlacedAllItems, _crystalHidden.ToImmutableArray(), _crystalOccupancy.ToImmutableArray(),
        _crystalItems.Select(i => new PredictorCrystalItem(i.Kind, i.Width, i.Height, i.X, i.Y, i.Subscriptions, i.Revealed)).ToImmutableArray(),
        _crystalRevealed.ToImmutableArray());

    internal static PredictorRun FromCrystal(PredictorCrystalSnapshot snapshot)
    {
        var run = new PredictorRun(snapshot.Context, snapshot.State);
        run._eventKey = new("EVENT", "CRYSTAL_SPHERE"); run._eventRng = snapshot.EventRng.Restore();
        run._crystalActive = true; run._crystalRemaining = snapshot.Remaining;
        run._crystalTool = snapshot.Tool; run._crystalPlacedAllItems = snapshot.PlacedAllItems;
        snapshot.Hidden.CopyTo(run._crystalHidden); snapshot.Occupancy.CopyTo(run._crystalOccupancy);
        run._crystalItems.AddRange(snapshot.Items.Select(i => new CrystalItem(i.Kind, i.Width, i.Height)
            { X = i.X, Y = i.Y, Subscriptions = i.Subscriptions, Revealed = i.Revealed }));
        run._crystalRevealed.AddRange(snapshot.Revealed); run.Phase = PredictorPhase.EventChoice;
        return run;
    }

    // Counterfactual reward row only: no claim that this reveal order can be
    // reached on the board. Use the same reveal/curse and settlement handlers.
    internal static PredictorRun PreviewCrystalRewards(PredictorCrystalSnapshot snapshot, ImmutableArray<int> order,
        CancellationToken token)
    {
        if (!order.Take(snapshot.Revealed.Length).SequenceEqual(snapshot.Revealed))
            throw new ArgumentException("CrystalRewardPrefixMismatch");
        var run = FromCrystal(snapshot);
        foreach (int index in order.Skip(snapshot.Revealed.Length).Distinct())
        {
            token.ThrowIfCancellationRequested();
            var item = run._crystalItems[index];
            for (int x = item.X; x < item.X + item.Width; x++)
                for (int y = item.Y; y < item.Y + item.Height; y++) run.ClearCrystalCell(x, y);
        }
        if (!run._crystalRevealed.SequenceEqual(order)) throw new ArgumentException("CrystalRewardOrderMismatch");
        run._crystalRemaining = 0;
        run.CompleteCrystalReveals(token);
        return run;
    }

    internal static PredictorCrystalSnapshot CreateCrystalBranch(PredictorContext context, PredictorState state,
        PredictorStreamState eventRng, bool debt, int price)
    {
        var run = new PredictorRun(context, state);
        run._eventKey = new("EVENT", "CRYSTAL_SPHERE"); run._eventRng = eventRng.Restore();
        if (debt) run.Working = PredictorObtainSources.AddCard(context, state, new ModelKey("CARD", "DEBT"));
        // Native option has no affordability lock after occurrence; LoseGold
        // clamps the current balance, even if it changed since entering.
        else run.SpendEventGold(price);
        run.BeginCrystalSphere(debt ? 6 : 3);
        return run.ExportCrystal();
    }
}

internal static class PredictorCrystalSearch
{
    internal static ImmutableArray<(int X, int Y)> Cells(int x, int y, PredictorCrystalTool tool)
    {
        if (tool == PredictorCrystalTool.Small) return [(x, y)];
        var cells = ImmutableArray.CreateBuilder<(int, int)>();
        foreach (int dx in new[] { -1, 1 }) if (x + dx is >= 0 and < 11) cells.Add((x + dx, y));
        foreach (int dy in new[] { -1, 1 }) if (y + dy is >= 0 and < 11) cells.Add((x, y + dy));
        foreach (int dx in new[] { -1, 1 }) foreach (int dy in new[] { -1, 1 })
            if (x + dx is >= 0 and < 11 && y + dy is >= 0 and < 11) cells.Add((x + dx, y + dy));
        cells.Add((x, y)); return cells.ToImmutable();
    }

    internal static PredictorCrystalSolution Solve(PredictorCrystalSnapshot snapshot, ImmutableArray<ModelKey> targets,
        TimeSpan budget, CancellationToken token = default, PredictorCrystalOptions? options = null)
    {
        options ??= new();
        if (targets.Length > 2 || targets.Length == 0 && !options.RequireRelic || targets.Any(k => k.Category != "CARD") || targets.Distinct().Count() != targets.Length)
            throw new ArgumentException("CrystalTargetInvalid");
        var watch = Stopwatch.StartNew(); long examined = 0; bool limited = false;
        var path = new List<PredictorCrystalStep>(); PredictorCrystalSolution? found = null;
        var memo = new HashSet<(UInt128 Hidden, string Order, int Remaining)>();
        var outcomes = new Dictionary<string, bool>();
        long rows = 0, cacheHits = 0, pruned = 0;
        PredictorCrystalSolution Receipt(PredictorCrystalSolution value) => value with
            { RewardRows = rows, RewardCacheHits = cacheHits, GeometryPruned = pruned };
        UInt128 hidden = 0;
        for (int i = 0; i < 121; i++) if (snapshot.Hidden[i]) hidden |= (UInt128)1 << i;
        var masks = snapshot.Items.Select(item =>
        {
            UInt128 mask = 0;
            if (item.X >= 0) for (int dx = 0; dx < item.Width; dx++) for (int dy = 0; dy < item.Height; dy++)
                mask |= (UInt128)1 << ((item.X + dx) * 11 + item.Y + dy);
            return mask;
        }).ToArray();
        var geometries = (from x in Enumerable.Range(0, 11)
            from y in Enumerable.Range(0, 11)
            where snapshot.Hidden[x * 11 + y]
            from tool in new[] { PredictorCrystalTool.Big, PredictorCrystalTool.Small }
            let cells = Cells(x, y, tool).Select(c => c.X * 11 + c.Y).ToArray()
            select (Step: new PredictorCrystalStep(x, y, tool), Cells: cells,
                Mask: cells.Aggregate((UInt128)0, (m, i) => m | (UInt128)1 << i))).ToArray();
        // Fixed-rarity Crystal card rewards, no combat additions. Derive legal
        // memberships from the actual reward pool, including current modifiers.
        var eligible = targets.Select(target => snapshot.Items.Select((item, i) => (item, i))
            .Where(x => x.item.X >= 0 && x.item.Subscriptions > 0 && x.item.Kind.StartsWith("CARD_") &&
                CanContain(snapshot, x.item.Kind, target)).Select(x => x.i).ToArray()).ToList();
        if (options.RequireRelic) eligible.Add(snapshot.Items.Select((item,i) => (item,i))
            .Where(x => x.item.X >= 0 && x.item.Subscriptions > 0 && x.item.Kind == "RELIC").Select(x => x.i).ToArray());
        var required = new HashSet<UInt128>(); var used = new Dictionary<int,int>();
        void Assign(int slot, UInt128 mask)
        {
            if (slot == eligible.Count) { required.Add(mask); return; }
            foreach (int i in eligible[slot])
            {
                int count = used.GetValueOrDefault(i);
                if (count >= snapshot.Items[i].Subscriptions) continue;
                used[i] = count + 1; Assign(slot + 1, mask | masks[i]); used[i] = count;
            }
        }
        Assign(0, 0);
        // Optimistic geometric lower bound: any original Big center may be
        // reused; curse/order/click-center restrictions are deliberately omitted.
        // Failure even here proves insufficient clicks; success is not a witness.
        var covers = geometries.Where(g => g.Step.Tool == PredictorCrystalTool.Big).Select(g => g.Mask).Distinct().ToArray();
        var coverMemo = new Dictionary<(UInt128 Need, int Left), bool>();
        bool CanCover(UInt128 need, int left)
        {
            token.ThrowIfCancellationRequested();
            if (need == 0) return true;
            if (left <= 0) return false;
            if (watch.Elapsed >= budget) { limited = true; return true; }
            if (coverMemo.TryGetValue((need,left), out bool cached)) return cached;
            var gains = covers.Select(c => c & need).Where(c => c != 0).Distinct().OrderByDescending(Bits).ToArray();
            if (gains.Length == 0 || Bits(need) > left * Bits(gains[0])) return false;
            if (gains[0] == need) return true;
            UInt128 cell = need & (~need + 1);
            bool possible = gains.Where(g => (g & cell) != 0).Any(g => CanCover(need & ~g, left - 1));
            if (coverMemo.Count < 100_000 && !limited) coverMemo[(need,left)] = possible;
            return possible;
        }
        bool Feasible(UInt128 fog, int left) => required.Any(mask => CanCover(mask & fog, left));
        UInt128 relevant = required.Aggregate((UInt128)0, (a,b) => a | b);
        bool RewardMatch(ImmutableArray<int> order)
        {
            // Same-kind gold/potion objects have identical settlement semantics.
            // Keep curse instance identity: repeated callbacks are not new curses.
            string key = string.Join(',', order.Select(i => snapshot.Items[i].Kind == "CURSE" ? "CURSE:" + i : snapshot.Items[i].Kind));
            if (outcomes.TryGetValue(key, out bool cached)) { cacheHits++; return cached; }
            token.ThrowIfCancellationRequested(); rows++;
            var preview = PredictorRun.PreviewCrystalRewards(snapshot, order, token);
            bool result = preview.Phase == PredictorPhase.Rewards &&
                (!options.RequireRelic || preview.Rewards.Any(r => r.Kind == PredictorRewardKind.Relic && r.Key.IsValid)) &&
                MatchTargets(preview.Rewards, targets) != null;
            // Cache is scoped to this immutable snapshot and query. Never reuse
            // across live refresh, bag/counter/pool changes or RNG positions.
            if (outcomes.Count < 50_000) outcomes[key] = result;
            return result;
        }
        List<(PredictorCrystalStep Step, UInt128 Fog, ImmutableArray<int> Revealed, int Score)> Moves(
            UInt128 fog, ImmutableArray<int> revealed)
        {
            var moves = new List<(PredictorCrystalStep Step, UInt128 Fog, ImmutableArray<int> Revealed, int Score)>();
            var distinct = new HashSet<(UInt128, string)>();
            foreach (var geometry in geometries)
            {
                if ((fog & ((UInt128)1 << (geometry.Step.X * 11 + geometry.Step.Y))) == 0) continue;
                UInt128 next = fog; var nextRevealed = revealed; int score = 0;
                foreach (int index in geometry.Cells)
                {
                    if ((next & ((UInt128)1 << index)) == 0) continue;
                    next &= ~((UInt128)1 << index);
                    int item = snapshot.Occupancy[index];
                    if (item < 0) continue;
                    bool card = (masks[item] & relevant) != 0 && snapshot.Items[item].Kind.StartsWith("CARD_", StringComparison.Ordinal);
                    bool relic = options.RequireRelic && snapshot.Items[item].Kind == "RELIC";
                    score += relic ? 12 : card ? 4 : 1;
                    if ((next & masks[item]) == 0 && !nextRevealed.Contains(item))
                    {
                        for (int n = 0; n < snapshot.Items[item].Subscriptions; n++) nextRevealed = nextRevealed.Add(item);
                        score += relic ? 120 : card ? 40 : 5;
                    }
                }
                if (options.AvoidCurse && nextRevealed.Any(i => snapshot.Items[i].Kind == "CURSE" && !snapshot.Revealed.Contains(i))) continue;
                if (distinct.Add((next, string.Join(',', nextRevealed)))) moves.Add((geometry.Step, next, nextRevealed, score));
            }
            return moves;
        }
        bool TryFinish(UInt128 fog, ImmutableArray<int> revealed, int remaining)
        {
            token.ThrowIfCancellationRequested();
            examined++;
            if (watch.Elapsed >= budget) { limited = true; return false; }
            if (!Feasible(fog, remaining)) { pruned++; return false; }
            if (limited) return false;
            // Finish unused moves with legal Small clicks that reveal no new
            // object. This skips enormous families of equivalent padding paths.
            if (remaining > 0 && required.Any(mask => (mask & fog) == 0) && RewardMatch(revealed))
            {
                UInt128 paddingFog = fog; var padding = new List<PredictorCrystalStep>();
                for (int n = 0; n < remaining; n++)
                {
                    int cell = -1;
                    for (int i = 0; i < 121; i++)
                    {
                        if ((paddingFog & ((UInt128)1 << i)) == 0) continue;
                        int item = snapshot.Occupancy[i];
                        var covered = item < 0 ? (UInt128)0 : paddingFog & masks[item];
                        if (item < 0 || (covered & (covered - 1)) != 0) { cell = i; break; }
                    }
                    if (cell < 0) break;
                    paddingFog &= ~((UInt128)1 << cell);
                    padding.Add(new(cell / 11, cell % 11, PredictorCrystalTool.Small));
                }
                if (padding.Count == remaining)
                {
                    var steps = path.Concat(padding).ToImmutableArray();
                    var takes = Verify(snapshot, steps, targets, token, options);
                    if (takes != null) { found = new("Found", examined, steps, takes.Value); return true; }
                }
            }
            if (remaining == 0)
            {
                if (!RewardMatch(revealed)) return false;
                var witness = Verify(snapshot, path.ToImmutableArray(), targets, token, options);
                if (witness is { } takes)
                { found = new("Found", examined, path.ToImmutableArray(), takes); return true; }
                return false;
            }
            return false;
        }
        bool Search(UInt128 fog, ImmutableArray<int> revealed, int remaining)
        {
            if (TryFinish(fog, revealed, remaining)) return true;
            if (limited || remaining == 0 || !Feasible(fog, remaining)) return false;
            if (memo.Count > 100_000) memo.Clear();
            if (!memo.Add((fog, string.Join(',', revealed), remaining))) return false;
            var moves = Moves(fog, revealed);
            foreach (var move in moves.OrderByDescending(m => m.Score))
            {
                path.Add(move.Step);
                if (Search(move.Fog, move.Revealed, remaining - 1)) return true;
                path.RemoveAt(path.Count - 1);
                if (limited) break;
            }
            return false;
        }
        // A bounded breadth pass avoids spending the whole budget below one
        // unlucky first click. Pruning here is only a witness-finding heuristic;
        // only the subsequent complete DFS can report Unreachable.
        var beam = new List<(UInt128 Fog, ImmutableArray<int> Revealed, ImmutableArray<PredictorCrystalStep> Path, int Score)>
            { (hidden, snapshot.Revealed, [], 0) };
        for (int depth = 0; depth <= snapshot.Remaining && beam.Count > 0 && !limited; depth++)
        {
            var next = new List<(UInt128 Fog, ImmutableArray<int> Revealed, ImmutableArray<PredictorCrystalStep> Path, int Score)>();
            var seen = new HashSet<(UInt128, string)>();
            foreach (var node in beam)
            {
                path.Clear(); path.AddRange(node.Path);
                if (TryFinish(node.Fog, node.Revealed, snapshot.Remaining - depth)) return Receipt(found!);
                if (limited) break;
                if (depth == snapshot.Remaining || !Feasible(node.Fog, snapshot.Remaining - depth)) continue;
                foreach (var move in Moves(node.Fog, node.Revealed))
                    if (!Feasible(move.Fog, snapshot.Remaining - depth - 1)) { pruned++; }
                    else if (seen.Add((move.Fog, string.Join(',', move.Revealed))))
                        next.Add((move.Fog, move.Revealed, node.Path.Add(move.Step), node.Score + move.Score));
            }
            // Retain different reward orders as well as different geometries.
            beam = next.OrderByDescending(n => n.Score).GroupBy(n => string.Join(',', n.Revealed))
                .SelectMany(g => g.Take(4)).Take(192).ToList();
        }
        path.Clear();
        if (!limited) Search(hidden, snapshot.Revealed, snapshot.Remaining);
        return Receipt(found ?? new(limited ? "Incomplete" : "Unreachable", examined, [], []));
    }

    // Final truth is actual source replay and two distinct reward selections,
    // never a union of candidates from mutually exclusive offers.
    internal static ImmutableArray<PredictorCrystalTake>? Verify(PredictorCrystalSnapshot snapshot,
        ImmutableArray<PredictorCrystalStep> steps, ImmutableArray<ModelKey> targets, CancellationToken token = default, PredictorCrystalOptions? options = null)
    {
        options ??= new();
        var run = PredictorRun.FromCrystal(snapshot);
        foreach (var step in steps)
        {
            if (run.Submit(run.Request!, new SelectCrystalTool(step.Tool), token) != PredictorInputResult.Accepted ||
                run.Submit(run.Request!, new RevealCrystalCell(step.X, step.Y), token) != PredictorInputResult.Accepted) return null;
        }
        if (run.Phase != PredictorPhase.Rewards) return null;
        if (options.AvoidCurse && run.ExportCrystal().Revealed.Any(i => snapshot.Items[i].Kind == "CURSE" && !snapshot.Revealed.Contains(i))) return null;
        var rewards = run.Rewards;
        if (options.RequireRelic && !rewards.Any(r => r.Kind == PredictorRewardKind.Relic && r.Key.IsValid)) return null;
        var assignment = MatchTargets(rewards, targets);
        if (assignment == null) return null;
        foreach (int t in Enumerable.Range(0, targets.Length))
        {
            var reward = run.Rewards[assignment[t]];
            var card = reward.Cards.First(c => c.Key == targets[t]);
            if (run.Submit(run.Request!, new EnterCardSelection(reward.Id), token) != PredictorInputResult.Accepted ||
                run.Submit(run.Request!, new TakeReward(reward.Id, card.Id), token) != PredictorInputResult.Accepted) return null;
        }
        return targets.Select((card, i) => new PredictorCrystalTake(assignment[i], card)).ToImmutableArray();
    }
    private static int Bits(UInt128 value) => System.Numerics.BitOperations.PopCount((ulong)value) +
        System.Numerics.BitOperations.PopCount((ulong)(value >> 64));
    private static bool CanContain(PredictorCrystalSnapshot snapshot, string kind, ModelKey target)
    {
        var rarity = Enum.Parse<RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity>(kind[5..], true);
        var plan = new PredictorRewardPlan(PredictorRewardKind.Card, CardCount: 3, Odds: PredictorCardOdds.Uniform, Rarity: rarity);
        return PredictorRewardGeneration.ResolveCardPool(snapshot.Context, snapshot.State, ref plan, true).Any(c => c.CardKey == target);
    }
    internal static int[]? MatchTargets(ImmutableArray<PredictorReward> rewards, ImmutableArray<ModelKey> targets)
    {
        int[] assignment = new int[targets.Length];
        bool Match(int t)
        {
            if (t == targets.Length) return true;
            for (int i = 0; i < rewards.Length; i++)
                if (!assignment.Take(t).Contains(i) && rewards[i].Kind == PredictorRewardKind.Card && rewards[i].Cards.Any(c => c.Key == targets[t]))
                { assignment[t] = i; if (Match(t + 1)) return true; }
            return false;
        }
        return Match(0) ? assignment : null;
    }

}
