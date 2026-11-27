using System.Collections.Immutable;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Core.PredictorRuntime;

internal enum PredictorCrystalTool { Small, Big }
internal sealed record SelectCrystalTool(PredictorCrystalTool Tool) : PredictorInput;
internal sealed record RevealCrystalCell(int X, int Y) : PredictorInput;
internal sealed record PredictorCrystalCellView(int X, int Y, bool Hidden, string? RevealedItem);
internal sealed record PredictorCrystalView(int Remaining, PredictorCrystalTool Tool, bool PlacedAllItems,
    ImmutableArray<PredictorCrystalCellView> Cells);

internal sealed partial class PredictorRun
{
    private sealed class CrystalItem(string kind, int width, int height)
    {
        internal string Kind = kind;
        internal int Width = width;
        internal int Height = height;
        internal int X = -1;
        internal int Y = -1;
        internal int Subscriptions;
        internal bool Revealed;
        internal bool Placed => X >= 0;
    }

    private bool _crystalActive;
    private bool _crystalPlacedAllItems;
    private int _crystalRemaining;
    private PredictorCrystalTool _crystalTool;
    private readonly bool[] _crystalHidden = new bool[121];
    private readonly int[] _crystalOccupancy = new int[121];
    private readonly List<CrystalItem> _crystalItems = [];
    private readonly List<int> _crystalRevealed = [];

    internal PredictorCrystalView? CrystalView => !_crystalActive || Phase != PredictorPhase.EventChoice ? null :
        new(_crystalRemaining, _crystalTool, _crystalPlacedAllItems,
            Enumerable.Range(0, 11).SelectMany(x => Enumerable.Range(0, 11).Select(y =>
            {
                int index = CrystalIndex(x, y);
                int itemIndex = _crystalOccupancy[index];
                return new PredictorCrystalCellView(x, y, _crystalHidden[index],
                    _crystalHidden[index] || itemIndex < 0 ? null : _crystalItems[itemIndex].Kind);
            })).ToImmutableArray());

    private static int CrystalIndex(int x, int y) => PredictorCrystalBoard.Index(x, y);

    private bool CrystalClickCompletes(CrystalItem item, int x, int y)
    {
        if (!item.Placed || item.Revealed) return false;
        int radius = _crystalTool == PredictorCrystalTool.Big ? 1 : 0;
        for (int i = item.X; i < item.X + item.Width; i++)
            for (int j = item.Y; j < item.Y + item.Height; j++)
                if (_crystalHidden[CrystalIndex(i, j)] && (Math.Abs(i - x) > radius || Math.Abs(j - y) > radius))
                    return false;
        return true;
    }

    private ImmutableArray<PredictorCombatPendingCause> CrystalClickReadCauses(int x, int y, List<RouteDependency> dependencies)
    {
        var revealed = _crystalItems.Where(item => CrystalClickCompletes(item, x, y)).Select(item => item.Kind).ToArray();
        var causes = ImmutableArray.CreateBuilder<PredictorCombatPendingCause>();
        if (revealed.Contains("CURSE"))
            causes.AddRange(RouteEventFixedCardReadCauses(this, PredictorCardChanges.Definition(Context, new("CARD", "DOUBT")).Prototype, dependencies));
        if (_crystalRemaining == 1) causes.AddRange(CrystalSettlementReadCauses(revealed, dependencies));
        return PredictorRouteCauseEvidence.Normalize(causes);
    }

    private ImmutableArray<PredictorCombatPendingCause> CrystalSettlementReadCauses(IEnumerable<string> newlyRevealed,
        List<RouteDependency> dependencies)
    {
        var rewards = newlyRevealed.Concat(_crystalRevealed.Select(index => _crystalItems[index].Kind)).ToHashSet();
        var causes = ImmutableArray.CreateBuilder<PredictorCombatPendingCause>();
        if (rewards.Contains("RELIC")) causes.AddRange(RouteRelicBagCauses(dependencies, shared: false));
        if (rewards.Any(kind => kind.StartsWith("CARD_", StringComparison.Ordinal)))
        {
            foreach (string relic in new[] { "DINGY_RUG", "PRISMATIC_GEM", "MOLTEN_EGG", "TOXIC_EGG", "FROZEN_EGG",
                "SILVER_CRUCIBLE", "SILKEN_TRESS", "WING_CHARM", "FRESNEL_LENS", "GLITTER", "DRIFTWOOD" })
                causes.AddRange(RouteRelicMembershipCauses(this, dependencies, relic));
            foreach (string relic in new[] { "SILVER_CRUCIBLE", "SILKEN_TRESS" })
                causes.AddRange(RouteRelicStateCauses(this, dependencies, relic));
            if (PredictorSettlementEffects.Has(Working, "WING_CHARM"))
                causes.AddRange(CausesFor(dependencies, StreamKnowledgeDomain(PredictorCardRewardRelicHooks.WingCharmStream(Context))));
        }
        return PredictorRouteCauseEvidence.Normalize(causes);
    }

    private PredictorCrystalView? FinishDrawingCrystalWithoutNewItems(List<RouteDependency> dependencies,
        List<PredictorCombatPendingCause> local, CancellationToken token)
    {
        if (_preview.Settings.Policy != PredictorRoutePreviewPolicy.LiveMapDefaults || local.Count != 0 || CrystalView is not { } board)
            return null;
        // Leave one fogged cell on every incomplete item. Small-tool clicks
        // on the remaining cells legally spend the divinations with no new item.
        var reserved = new HashSet<int>();
        bool CanSpendReveal(PredictorCrystalCellView cell)
        {
            int item = _crystalOccupancy[CrystalIndex(cell.X, cell.Y)];
            return cell.Hidden && (item < 0 || !reserved.Add(item));
        }
        var clicks = board.Cells.Where(CanSpendReveal).Take(board.Remaining).ToArray();
        if (clicks.Length != board.Remaining || !CrystalSettlementReadCauses([], dependencies).IsEmpty) return null;
        Submit(Request!, new SelectCrystalTool(PredictorCrystalTool.Small), token);
        foreach (var cell in clicks)
            if (Submit(Request!, new RevealCrystalCell(cell.X, cell.Y), token) != PredictorInputResult.Accepted)
                throw new InvalidOperationException("PredictorCrystalDefaultClickInvalid");
        _preview.Partial?.Facts.RecordResult("event.crystal.remainingClicks", Working.Position.NodeId,
            string.Join(" | ", clicks.Select(cell => $"{cell.X},{cell.Y}")), origin: PredictorKnowledgeOrigin.ApprovedDefault,
            operation: "Spent remaining divinations with the small tool without completing another item");
        return board;
    }

    private void BeginCrystalSphere(int reveals)
    {
        _crystalActive = true;
        _crystalRemaining = reveals;
        _crystalTool = PredictorCrystalTool.Big;
        var board = PredictorCrystalBoard.Generate(_eventRng!);
        _crystalPlacedAllItems = board.PlacedAllItems;
        for (int cell = 0; cell < _crystalHidden.Length; cell++)
            _crystalHidden[cell] = (PredictorCrystalBoard.InitialFog & ((UInt128)1 << cell)) != 0;
        board.WriteOccupancy(_crystalOccupancy);
        _crystalItems.Clear();
        foreach (var item in board.Items)
            _crystalItems.Add(new(item.Code, item.Width, item.Height)
            {
                X = item.X, Y = item.Y, Subscriptions = item.Subscriptions
            });
        _crystalRevealed.Clear();
        EventOptions = [];
        Phase = PredictorPhase.EventChoice;
        _requestId++;
    }

    private PredictorInputResult? TrySubmitCrystal(PredictorInput input, CancellationToken token)
    {
        if (!_crystalActive || Phase != PredictorPhase.EventChoice) return null;
        if (input is SelectCrystalTool tool)
        {
            if (!Enum.IsDefined(tool.Tool)) return PredictorInputResult.Invalid;
            _crystalTool = tool.Tool;
            Accept(new("CrystalTool:" + tool.Tool, 0, [], null, null));
            return PredictorInputResult.Accepted;
        }
        if (input is not RevealCrystalCell click || click.X is < 0 or >= 11 || click.Y is < 0 or >= 11 ||
            _crystalRemaining <= 0 || !_crystalHidden[CrystalIndex(click.X, click.Y)])
            return PredictorInputResult.Invalid;
        _crystalRemaining--;
        Accept(new("CrystalReveal", CrystalIndex(click.X, click.Y), [], null, null));
        if (_crystalTool == PredictorCrystalTool.Small) ClearCrystalCell(click.X, click.Y);
        else
        {
            foreach (int dx in new[] { -1, 1 })
                if (click.X + dx is >= 0 and < 11) ClearCrystalCell(click.X + dx, click.Y);
            foreach (int dy in new[] { -1, 1 })
                if (click.Y + dy is >= 0 and < 11) ClearCrystalCell(click.X, click.Y + dy);
            foreach (int dx in new[] { -1, 1 })
                foreach (int dy in new[] { -1, 1 })
                    if (click.X + dx is >= 0 and < 11 && click.Y + dy is >= 0 and < 11)
                        ClearCrystalCell(click.X + dx, click.Y + dy);
            ClearCrystalCell(click.X, click.Y);
        }
        if (_crystalRemaining == 0) CompleteCrystalReveals(token);
        return PredictorInputResult.Accepted;
    }

    private void ClearCrystalCell(int x, int y)
    {
        int cell = CrystalIndex(x, y);
        if (!_crystalHidden[cell]) return;
        _crystalHidden[cell] = false;
        int index = _crystalOccupancy[cell];
        if (index < 0) return;
        var item = _crystalItems[index];
        if (item.Revealed) return;
        for (int dx = 0; dx < item.Width; dx++)
            for (int dy = 0; dy < item.Height; dy++)
                if (_crystalHidden[CrystalIndex(item.X + dx, item.Y + dy)]) return;
        item.Revealed = true;
        for (int i = 0; i < item.Subscriptions; i++) _crystalRevealed.Add(index);
        if (item.Kind == "CURSE")
        {
            var key = new ModelKey("CARD", "DOUBT");
            PredictorSettlementEffects.RequireImplementedCard(PredictorCardChanges.Definition(Context, key).Prototype, Context.Crystal != null, context: Context);
            Working = PredictorObtainSources.AddCard(Context, Working, key);
            Accept(new("CrystalDoubtRevealed", Working.Deck[^1], [], null, null));
        }
    }

    private void CompleteCrystalReveals(CancellationToken token)
    {
        var plans = new List<PredictorRewardPlan>();
        foreach (int index in _crystalRevealed)
        {
            var item = _crystalItems[index];
            switch (item.Kind)
            {
                case "CURSE": break; // Doubt was added once at actual RevealItem.
                case "RELIC": plans.Add(new(PredictorRewardKind.Relic, UseEventRng: true)); break;
                case "GOLD_SMALL": plans.Add(new(PredictorRewardKind.Gold, 10, 10, UseEventRng: true)); break;
                case "GOLD_BIG": plans.Add(new(PredictorRewardKind.Gold, 30, 30, UseEventRng: true)); break;
                case "POTION_COMMON":
                case "POTION_RARE":
                    var rarity = item.Kind == "POTION_RARE" ? PredictorPotionRarity.Rare : PredictorPotionRarity.Common;
                    var available = Context.PotionPool.Where(p => p.Rarity == rarity).ToArray();
                    if (available.Length == 0) throw new InvalidOperationException("PredictorCrystalPotionPoolEmpty:" + rarity);
                    var selected = available[_eventRng!.NextInt(available.Length)];
                    plans.Add(new(PredictorRewardKind.Potion, PrefilledPotionKey: selected.Key, UseEventRng: true));
                    break;
                case "CARD_COMMON":
                case "CARD_UNCOMMON":
                case "CARD_RARE":
                    var cardRarity = item.Kind switch
                    {
                        "CARD_COMMON" => RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Common,
                        "CARD_UNCOMMON" => RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Uncommon,
                        _ => RolltheSpire2.Core.Effects.Snapshots.EffectCardRarity.Rare
                    };
                    plans.Add(new(PredictorRewardKind.Card, CardCount: 3, Odds: PredictorCardOdds.Uniform,
                        Rarity: cardRarity, UseEventRng: true));
                    break;
                default: throw new InvalidOperationException("PredictorCrystalRevealedKindInvalid:" + item.Kind);
            }
        }
        if (plans.Count == 0)
        {
            FinishEvent();
            return;
        }
        var generated = PredictorRewardGeneration.Generate(Context, Working, plans,
            cancellationToken: token, eventRng: _eventRng);
        _eventRewardContinuation = true;
        var parentInputs = _inputs;
        Phase = PredictorPhase.Complete;
        PrepareRewardNode(Working.Position,
            generated.Rewards.Select(r => r with { Origin = PredictorRewardOrigin.Event }).ToImmutableArray(),
            generated.State, token);
        _inputs = parentInputs;
        EventOptions = [];
        GenerationStages = generated.Stages;
    }

    private void ResetCrystalSphere()
    {
        _crystalActive = _crystalPlacedAllItems = false;
        _crystalRemaining = 0;
        _crystalTool = PredictorCrystalTool.Big;
        Array.Fill(_crystalHidden, false);
        Array.Fill(_crystalOccupancy, -1);
        _crystalItems.Clear();
        _crystalRevealed.Clear();
    }
}
