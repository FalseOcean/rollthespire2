using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation;
using RolltheSpire2.Ui.Settings;
using System.Diagnostics;

namespace RolltheSpire2.Ui.InRunOperations;

// A native hover owns a detached query; only main-thread verified results publish.
internal sealed partial class NeowOperationHoverController : Control
{
    internal const string NodeName = "RolltheSpire2_OperationHover";
    private enum Kind { Ancient, Event, Shop, Rest, RewardAlternative, Reward, Potion, CardTarget }
    private sealed record Request(Kind Kind, ModelKey Source, long Id = 0, int Slot = 0,
        string Option = "", long? Wing = null, PredictorRestAction Rest = default,
        PredictorOperationPotionSource? PotionSource = null);
    private static NeowOperationHoverController? _active;
    private static WeakReference<Control>? _focusedOwner;
    private static string _focusedVersion = "";
    private sealed class RoyaltiesHypothesis { internal bool Played; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MegaCrit.Sts2.Core.Events.EventOption, RoyaltiesHypothesis>
        RoyaltiesChoices = new();
    private bool _swallowRoyaltiesRelease;

    static NeowOperationHoverController()
    {
        InRunFeaturePreferences.OperationChanged += ApplyFeaturePreference;
        InRunFeaturePreferences.OperationPolicyChanged += ApplyOperationPolicy;
    }

    private static void ApplyFeaturePreference(bool enabled)
    {
        try
        {
            RuntimeSnapshotThreadGuard.RequireMainThread();
            if (!enabled)
            {
                if (_active != null && IsInstanceValid(_active)) _active.Clear();
            }
            else if (_focusedOwner?.TryGetTarget(out var owner) == true && IsInstanceValid(owner))
                Focus(owner, _focusedVersion);
        }
        catch (Exception error) { RuntimeLog.Fault("operationUiFailed=true;stage=featurePreference", error); }
    }
    private Control? _owner;
    private Control? _nativeTextTip;
    private Vector2 _nativeTextTipPosition;
    private RunState? _run;
    private Request? _request;
    private string _version = "";
    private PredictorOperationSnapshot? _snapshot;
    private string? _stamp;
    private PredictorOperationPotionObservation? _potionObservation;
    private CancellationTokenSource? _cancellation;
    private Task<OperationPreview>? _work;
    private NeowOperationOverlay? _view;
    private ulong? _viewLayoutFrame;
    private readonly OperationCardDisplayReuse _cardDisplays = new();
    private double _poll;
    private bool _captureQueued;
    private NDeckTransformSelectScreen? _directTransformSelector;
    internal sealed class HoverTiming
    {
        public double CaptureMs, InitialObservationMs, WorkerMs, PublishCaptureMs, RenderMs, PlacementMs;
        public double PollObservationMs;
        public int PollCount;
        public PredictorOperationCapture.CaptureTiming Capture = new(), Publication = new();
    }
    private HoverTiming _timing = new();
    private static double Elapsed(long started) => Stopwatch.GetElapsedTime(started).TotalMilliseconds;

    internal static void ObserveGridFocus(NGridCardHolder owner)
    {
        if (!InRunFeaturePreferences.OperationPredictionEnabled) return;
        bool admitted = OperationSurfaceHoverCapture.TryCardTarget(owner, out var run, out long cardId, out string reason);
        bool active = _active != null && IsInstanceValid(_active) && ReferenceEquals(_active._owner, owner);
        RuntimeLog.Info("operationCardHover=true;stage=native-grid-focus;admitted=" + admitted + ";reason=" + reason +
            ";card=" + owner.CardModel?.Id + ";cardId=" + cardId + ";deckCount=" + (run == null ? null : LocalContext.GetMe(run)?.Deck.Cards.Count) +
            ";baseFocusActive=" + active + ";topOverlay=" + NOverlayStack.Instance?.Peek()?.GetType().Name +
            ";mapOpen=" + (NMapScreen.Instance?.IsOpen == true) + ";capstone=" + (NCapstoneContainer.Instance?.InUse == true) +
            ";modal=" + NModalContainer.Instance?.OpenModal?.GetType().Name);
    }

    internal static void Focus(Control owner, string version)
    {
        RuntimeSnapshotThreadGuard.BindCurrentThread();
        _focusedOwner = new(owner); _focusedVersion = version;
        if (!InRunFeaturePreferences.OperationPredictionEnabled) return;
        if (!TryCurrent(owner, out var run, out var request)) return;
        if (request?.Kind == Kind.CardTarget) RuntimeLog.Info("operationCardHover=true;stage=base-focus;cardId=" + request.Id);
        if (_active != null && IsInstanceValid(_active) && ReferenceEquals(_active._owner, owner) && _active._request == request) return;
        if (NGame.Instance?.HoverTipsContainer is not { } parent) return;
        var controller = parent.GetNodeOrNull<NeowOperationHoverController>(NodeName);
        if (controller == null)
        {
            controller = new() { Name = NodeName, MouseFilter = MouseFilterEnum.Ignore, FocusMode = FocusModeEnum.None };
            parent.AddChild(controller);
        }
        if (_active != null && IsInstanceValid(_active) && !ReferenceEquals(_active, controller)) _active.Clear();
        _active = controller; controller.Clear();
        controller._owner = owner; controller._run = run; controller._request = request; controller._version = version;
        controller._captureQueued = true; controller._poll = .5; controller.SetProcess(true);
    }

    internal static void Leave(Control owner)
    {
        if (_focusedOwner?.TryGetTarget(out var focused) == true && ReferenceEquals(focused, owner)) _focusedOwner = null;
        if (_active != null && IsInstanceValid(_active) && ReferenceEquals(_active._owner, owner)) _active.Clear();
    }

    private static bool TryCurrent(Control owner, out RunState? run, out Request? request)
    {
        run = null; request = null;
        if (!IsInstanceValid(owner) || !owner.IsInsideTree() || !owner.IsVisibleInTree() || owner.IsQueuedForDeletion()) return false;
        switch (owner)
        {
            case NEventOptionButton button:
                if (!button.IsEnabled || button.Event is not { } ev || ev.IsFinished ||
                    button.Option is not { IsLocked: false, WasChosen: false, IsProceed: false } option ||
                    ev.Owner?.RunState is not RunState actual || !ReferenceEquals(LocalContext.GetMe(actual), ev.Owner) ||
                    actual.CurrentRoom is not EventRoom room || !ReferenceEquals(room.LocalMutableEvent, ev) ||
                    NOverlayStack.Instance?.Peek() != null ||
                    PredictorOperationCapture.ReadNullable(ev, "_currentOptions") is not IReadOnlyList<MegaCrit.Sts2.Core.Events.EventOption> options ||
                    !options.Any(o => ReferenceEquals(o, option))) return false;
                if (ev is AncientEventModel)
                {
                    if (option.Relic == null || option.Relic.Id.Entry is "ROYAL_STAMP" or "PAELS_LEGION" or "PRECARIOUS_SHEARS" ||
                        ev.Id.Entry == "NEOW" && option.Relic.Id.Entry == "SILKEN_TRESS") return false;
                    if (!OperationSurfaceHoverCapture.MayDisplayRelicObtain(option.Relic)) return false;
                    request = new(Kind.Ancient, PredictorLiveRunCapture.Key(option.Relic), PredictorLiveRunCapture.Id(option));
                }
                else
                {
                    if (!PredictorOperationEventCapture.Supports(ev)) return false;
                    var eventOption = PredictorOperationEventCapture.Option(ev, option);
                    if (!OperationSurfaceHoverCapture.EventMayDisplay(ev, eventOption)) return false;
                    request = new(Kind.Event, PredictorLiveRunCapture.Key(ev), Option: eventOption.Key);
                }
                run = actual; return true;
            case NMerchantSlot merchant when OperationSurfaceHoverCapture.TryShop(merchant, out run, out long entryId, out var shopSource):
                request = new(Kind.Shop, shopSource, entryId); return true;
            case NRestSiteButton rest when OperationSurfaceHoverCapture.TryRest(rest, out run, out var action):
                request = new(Kind.Rest, new("RELIC", action == PredictorRestAction.Dig ? "SHOVEL" : "DREAM_CATCHER"), Rest: action); return true;
            case NCardRewardAlternativeButton alternative when OperationSurfaceHoverCapture.TryRewardAlternative(alternative, out run,
                out long rewardId, out string optionId, out long? wingId):
                request = new(Kind.RewardAlternative, default, rewardId, Option: optionId, Wing: wingId); return true;
            case NRewardButton reward when OperationSurfaceHoverCapture.TryReward(reward, out run, out long actualReward, out var rewardSource):
                request = new(Kind.Reward, rewardSource, actualReward); return true;
            case NRewardButton reward when OperationSurfaceHoverCapture.TryPotionReward(reward, out run, out var offeredPotion):
                request = new(Kind.Potion, offeredPotion!.Potion.Key, PotionSource: offeredPotion); return true;
            case NPotionHolder potion when OperationSurfaceHoverCapture.TryPotion(potion, out run, out var potionSource):
                request = new(Kind.Potion, potionSource!.Potion.Key, PotionSource: potionSource); return true;
            case NCardHolder card when OperationSurfaceHoverCapture.TryCardTarget(card, out run, out long cardId):
                request = new(Kind.CardTarget, default, cardId); return true;
            default: return false;
        }
    }

    public override void _Ready() => SetProcess(false);

    public override void _Input(InputEvent input)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Right } mouse) return;
        if (!mouse.Pressed)
        {
            if (_swallowRoyaltiesRelease) { _swallowRoyaltiesRelease = false; GetViewport().SetInputAsHandled(); }
            return;
        }
        if (_owner is not NEventOptionButton button || !button.GetGlobalRect().HasPoint(GetGlobalMousePosition()) ||
            !TryCurrent(button, out var run, out var request) || request?.Kind != Kind.Event ||
            !InRunFeaturePreferences.OperationPredictionEnabled) return;
        try
        {
            // Eligibility is already public on the actual option and deck.
            // The refreshed prediction captures once after changing this input.
            if (!OperationPreviewEligibility.RoyaltiesBattleOption(request.Source.Entry, request.Option) ||
                InRunFeaturePreferences.GetOperationMode("event:" + request.Source.Entry) == OperationPredictionMode.Off ||
                InRunFeaturePreferences.GetOperationMode("extra:event-combat") == OperationPredictionMode.Off ||
                LocalContext.GetMe(run!)?.Deck.Cards.Any(card => card.Id.Entry == "ROYALTIES") != true) return;
            var choice = RoyaltiesChoices.GetOrCreateValue(button.Option);
            choice.Played = !choice.Played;
            _swallowRoyaltiesRelease = true;
            GetViewport().SetInputAsHandled();
            QueueRefresh();
        }
        catch (Exception error) { RuntimeLog.Fault("operationUiFailed=true;stage=royaltiesHypothesis", error); }
    }
    public override void _Process(double delta)
    {
        try
        {
            if (!InRunFeaturePreferences.OperationPredictionEnabled) { Clear(); return; }
            if (_owner == null || !TryCurrent(_owner, out var run, out var request) || !ReferenceEquals(run, _run) || request != _request ||
                _request?.Kind != Kind.Potion && (NMapScreen.Instance?.IsOpen == true || NCapstoneContainer.Instance?.InUse == true) ||
                NModalContainer.Instance?.OpenModal != null)
            {
                if (_request?.Kind == Kind.CardTarget)
                {
                    string reason = "OwnerMissing";
                    if (_owner is NCardHolder card) OperationSurfaceHoverCapture.TryCardTarget(card, out _, out _, out reason);
                    RuntimeLog.Info("operationCardHover=true;stage=clear;admission=" + reason +
                        ";mapOpen=" + (NMapScreen.Instance?.IsOpen == true) + ";capstone=" + (NCapstoneContainer.Instance?.InUse == true) +
                        ";modal=" + NModalContainer.Instance?.OpenModal?.GetType().Name);
                }
                Clear(); return;
            }
            if ((_policyPoll -= delta) <= 0)
            {
                _policyPoll = .5;
                var currentDemand = CaptureDemand();
                if (_demand == null || !_demand.SamePolicy(currentDemand)) QueueRefresh();
            }
            if (_captureQueued) BeginPrediction();
            if (_work is { IsCompleted: true } work)
            {
                _work = null;
                if (work.IsCanceled || work.IsFaulted) { HideView(); return; }
                var preview = work.Result;
                if (!NeowOperationOverlay.HasVisibleContent(preview))
                {
                    // No result is being published, so another complete capture
                    // cannot make this empty view safer. Keep observing changing
                    // inputs: an empty upgrade pool can later gain a target.
                    HideView();
                    _poll = .5;
                    if (_request?.Kind == Kind.CardTarget) LogTiming("empty");
                    return;
                }
                long checking = Stopwatch.GetTimestamp();
                bool fresh = _directTransformSelector != null
                    ? PredictorOperationCapture.DirectTransformObservationStamp(_snapshot!, _run!, _directTransformSelector) == _stamp
                    : _potionObservation is { } potion
                    ? PredictorOperationPotionTargetCapture.ObservationStamp(_run!, potion.Context.Operation!.PlayerId, potion.Source) == _stamp
                    : _snapshot != null && PredictorOperationCapture.IsCurrent(_snapshot, _run!, _version, NGame.Instance, _timing.Publication);
                _timing.PublishCaptureMs = Elapsed(checking);
                if (!fresh) { QueueRefresh(); return; }
                // Every published result has just checked its dynamic inputs.
                // Do not observe them a second time in this same frame.
                _poll = .5;
                if (_request?.Kind == Kind.CardTarget) RuntimeLog.Info("operationCardHover=true;stage=publication;visibleContent=" +
                    NeowOperationOverlay.HasVisibleContent(preview) + ";paths=" + preview.Branches.Sum(branch => branch.PathsExamined) + ";faults=" + preview.Faults.Length);
                if (NeowOperationOverlay.HasVisibleContent(preview))
                {
                    long rendering = Stopwatch.GetTimestamp();
                    PresentPreview(preview);
                    _timing.RenderMs = Elapsed(rendering);
                    long placement = Stopwatch.GetTimestamp();
                    PositionView(); _timing.PlacementMs = Elapsed(placement);
                }
                if (_request?.Kind == Kind.CardTarget) LogTiming("published");
                return;
            }
            // A pending worker cannot publish without the check above. Reading
            // the same scene while it computes would only duplicate that work.
            if (!_captureQueued && _work == null && _stamp != null && (_poll -= delta) <= 0)
            {
                _poll = .5;
                long checking = Stopwatch.GetTimestamp();
                string? current = _directTransformSelector != null
                    ? PredictorOperationCapture.DirectTransformObservationStamp(_snapshot!, _run!, _directTransformSelector)
                    : _potionObservation is { } potion ? PredictorOperationPotionTargetCapture.ObservationStamp(
                        _run!, potion.Context.Operation!.PlayerId, potion.Source) :
                    PredictorOperationCapture.ObservationStamp(_run!, _snapshot!.Context.Catalog!, NGame.Instance);
                _timing.PollObservationMs += Elapsed(checking); _timing.PollCount++;
                if (_request?.Kind == Kind.CardTarget && _timing.PollCount == 1) LogTiming("first-poll");
                if (current != _stamp) { QueueRefresh(); return; }
            }
            if (_view != null) PositionView();
        }
        catch (Exception error)
        {
            RuntimeLog.Fault("operationUiFailed=true;stage=main-thread-capture-or-publication;source=" + _request?.Source, error);
            Clear();
        }
    }

    private void BeginPrediction()
    {
        if (!InRunFeaturePreferences.OperationPredictionEnabled) { Clear(); return; }
        _timing = new(); var timing = _timing;
        _captureQueued = false;
        _demand = CaptureDemand(); _policyPoll = .5;
        var demand = _demand;
        if (!demand.MayPredict) { HideView(); return; }
        if (demand.PublicSacrificeProgress is { } progress)
        {
            // Only a public Wing counter remains visible. Its value is part of
            // the lightweight demand observation; no RNG snapshot is needed.
            PresentPreview(new(new("RELIC", "PAELS_WING"), [], [], false, [], [])
            { OptionKey = "SACRIFICE", Notices = ["RewardSacrificeProgress:" + progress] });
            PositionView();
            return;
        }
        var request = _request!;
        long capture = Stopwatch.GetTimestamp();
        if (request.Kind == Kind.Potion)
        {
            _potionObservation = PredictorOperationPotionTargetCapture.CaptureLive(_run!, LocalContext.GetMe(_run!)!.NetId,
                _version, request.PotionSource!, _potionObservation);
            _snapshot = null;
            _stamp = _potionObservation.Stamp;
            timing.CaptureMs = Elapsed(capture);
        }
        else
        {
            _directTransformSelector = null;
            if (request.Kind == Kind.CardTarget && _owner is NCardHolder holder &&
                PredictorOperationCapture.TransformSelector(holder) is { } selector)
            {
                _snapshot = PredictorOperationCapture.CaptureSelection(_run!, _version, selector, NGame.Instance,
                    out var stamp, out bool direct, timing.Capture);
                _stamp = stamp;
                if (direct) _directTransformSelector = selector;
            }
            else _snapshot = PredictorOperationCapture.Capture(_run!, _version, NGame.Instance, out _stamp, timing.Capture);
            timing.CaptureMs = Elapsed(capture);
        }
        var snapshot = _snapshot;
        var potionObservation = _potionObservation;
        bool playRoyalties = _owner is NEventOptionButton eventButton &&
            RoyaltiesChoices.TryGetValue(eventButton.Option, out var hypothesis) && hypothesis.Played;
        if (request.Kind == Kind.CardTarget) RuntimeLog.Info("operationCardHover=true;stage=capture;scene=" + snapshot?.Scene.GetType().Name +
            ";selected=" + snapshot?.SelectedCardIds.Length + ";deckCount=" + snapshot?.State.Deck.Length + ";fingerprint=" + snapshot?.Fingerprint);
        _cancellation = new(); var token = _cancellation.Token;
        _work = Task.Run(() =>
        {
            long calculating = Stopwatch.GetTimestamp();
            try
            {
                var result = request.Kind switch
                {
                    Kind.Ancient => RelicOperationPreview.Build(snapshot!, request.Id, token, demand.IncludeChain),
                    Kind.Event => EventOperationPreview.Build(snapshot!, request.Option, token, playRoyalties,
                        demand.IncludeEventContinuation, demand.IncludeEventCombat),
                    Kind.Shop => SurfaceOperationPreview.BuildShop(snapshot!, request.Id, token, demand.Surface),
                    Kind.Rest => SurfaceOperationPreview.BuildRest(snapshot!, request.Rest, token, demand.Surface),
                    Kind.RewardAlternative => SurfaceOperationPreview.BuildRewardAlternative(snapshot!, request.Id, request.Option, request.Wing, token, demand.Surface),
                    Kind.Reward => RelicOperationPreview.Build(snapshot!, [new TakeReward(request.Id)], request.Source, token, demand.IncludeChain),
                    Kind.Potion => PotionOperationPreview.Build(potionObservation!, token, demand.Potion),
                    Kind.CardTarget => CardTargetOperationPreview.Build(snapshot!, request.Id, token,
                        demand.IncludeEventContinuation, demand.IncludeEventCombat),
                    _ => throw new InvalidOperationException("OperationHoverKindMissing")
                };
                result = OperationPredictionPolicy.Filter(result, demand);
                foreach (var failure in result.Faults)
                    RuntimeLog.Fault("operationPredictionFailed=true;stage=" + failure.Stage + ";source=" + request.Source +
                        ";fingerprint=" + snapshot?.Fingerprint + ";inputs=" + string.Join(" -> ", failure.InputPath), failure.Exception);
                return result;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                RuntimeLog.Fault("operationPredictionFailed=true;stage=detached-projection;source=" + request.Source +
                    ";fingerprint=" + snapshot?.Fingerprint, error);
                return new OperationPreview(request.Source, [], [], false, [], [])
                    { Faults = [new("DetachedProjection", [], error)] };
            }
            finally { timing.WorkerMs = Elapsed(calculating); }
        }, token);
        _ = _work.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void LogTiming(string phase) => RuntimeLog.Info("operationCardHover=true;stage=timing;phase=" + phase +
        ";captureMs=" + _timing.CaptureMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
        CapturePhases("capture", _timing.Capture) + CapturePhases("publish", _timing.Publication) +
        ";initialStampMs=" + _timing.InitialObservationMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
        ";workerMs=" + _timing.WorkerMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
        ";publishCaptureMs=" + _timing.PublishCaptureMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
        ";nativeViewMs=" + _timing.RenderMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
        ";placementMs=" + _timing.PlacementMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
        ";nativeCardCreateMs=" + (_view?.NativeCardCreateMilliseconds ?? 0).ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
        ";nativeCardReadyMs=" + (_view?.NativeCardReadyMilliseconds ?? 0).ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
        ";nativeCardCount=" + (_view?.NativeCardCount ?? 0) +
        ";nativeCardReused=" + (_view?.NativeCardReuseCount ?? 0) +
        ";pollStampMs=" + _timing.PollObservationMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
        ";publicationValidation=" + (_directTransformSelector != null ? "DirectAstrolabeDynamicStamp" : "FullCapture") +
        ";pollCount=" + _timing.PollCount);

    private static string CapturePhases(string prefix, PredictorOperationCapture.CaptureTiming timing) =>
        ";" + prefix + "NativeStampMs=" + timing.NativeStampMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
        ";" + prefix + "CatalogMs=" + timing.CatalogMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
        ";" + prefix + "CompleteCatalogMs=" + timing.CompleteCatalogMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
        ";" + prefix + "HashMs=" + timing.HashMs.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
        ";" + prefix + "AllocatedBytes=" + timing.AllocatedBytes +
        ";" + prefix + "PoolReads=" + timing.PoolReads + ";" + prefix + "PoolReuses=" + timing.PoolReuses +
        ";" + prefix + "PoolProjectionReuses=" + timing.PoolProjectionReuses +
        ";" + prefix + "CatalogLevelReads=" + timing.CatalogLevelReads +
        ";" + prefix + "CatalogLevelReuses=" + timing.CatalogLevelReuses +
        ";" + prefix + "SnapshotReuses=" + timing.SnapshotReuses +
        ";" + prefix + "DirectPoolReads=" + timing.DirectPoolReads +
        ";" + prefix + "ContentObservationReads=" + timing.ContentObservationReads;

    private void QueueRefresh()
    {
        _cancellation?.Cancel(); _cancellation?.Dispose(); _cancellation = null; _work = null;
        _snapshot = null; _stamp = null; _directTransformSelector = null; HideView(); _captureQueued = true;
    }

    private void PositionView()
    {
        if (_view == null || _owner == null) return;
        var viewport = GetViewportRect();
        // Card and merchant holders are centered on their visible hitbox;
        // their nominal root rectangle is not the region the player operates.
        var anchor = _owner switch
        {
            NMerchantSlot slot => slot.Hitbox.GetGlobalRect(),
            NCardHolder card => card.Hitbox.GetGlobalRect(),
            _ => _owner.GetGlobalRect()
        };
        var occupied = new List<Rect2> { anchor };
        RestoreNativeTip();
        var nativeTipSet = NGame.Instance?.HoverTipsContainer?.GetChildren().OfType<NHoverTipSet>()
            .FirstOrDefault(t => ReferenceEquals(PredictorOperationCapture.ReadNullable(t, "_owner"), _owner));
        var nativeTip = nativeTipSet?.GetNodeOrNull<Control>("textHoverTipContainer");
        if (nativeTipSet?.GetNodeOrNull<Control>("cardHoverTipContainer") is { } nativeCards &&
            nativeCards.IsVisibleInTree() && nativeCards.Size.X > 0 && nativeCards.Size.Y > 0)
            occupied.Add(nativeCards.GetGlobalRect());
        Rect2? nativeTipRect = null;
        if (nativeTip is { } tip && tip.IsVisibleInTree() && tip.Size.X > 0 && tip.Size.Y > 0)
        {
            _nativeTextTip = tip; _nativeTextTipPosition = tip.Position;
            nativeTipRect = tip.GetGlobalRect();
        }
        if (NRun.Instance?.GlobalUi is { } global)
        {
            if (global.TopBar is { } top && top.IsVisibleInTree())
                foreach (string name in new[] { "BgImage", "LeftAlignedStuff", "RightAlignedStuff" })
                    if (top.GetNodeOrNull<Control>(name) is { } part && part.IsVisibleInTree()) occupied.Add(part.GetGlobalRect());
            if (global.RelicInventory is { } relics)
                occupied.AddRange(relics.RelicNodes.Where(c => c.IsVisibleInTree()).Select(c => c.GetGlobalRect()));
        }
        if (_view.PlanPotionGroupRegions(viewport, anchor, occupied, nativeTipRect) is { } potionRegions)
        {
            _view.SetLayout(new(viewport.Position, Vector2.Zero), viewport, potionRegions);
            if (_viewLayoutFrame == Engine.GetProcessFrames()) return;
            _viewLayoutFrame = null;
            _view.Modulate = Colors.White; _view.Show();
            return;
        }
        var areas = NeowHoverPlacement.Plan(viewport, anchor, [anchor], occupied,
            _view.SummaryDesiredSize, _view.CardsDesiredSize, _view.IsBones, _view.CardsSizeForWidth, nativeTipRect);
        if (areas.Summary.Size.X <= 0 && areas.Cards.Size.X <= 0) { _view.Hide(); return; }
        _view.SetLayout(areas.Summary, areas.Cards);
        // Wrapped containers need a visible-tree layout pass; hiding the tree
        // postpones that pass until Show and merely delays the same jump.
        // Prepare transparently before exposing its height or moving native text.
        if (_viewLayoutFrame == Engine.GetProcessFrames()) return;
        _viewLayoutFrame = null;
        if (_nativeTextTip != null) _nativeTextTip.GlobalPosition += areas.NativeTipShift;
        _view.Modulate = Colors.White; _view.Show();
    }

    private void HideView()
    {
        RestoreNativeTip();
        _viewLayoutFrame = null;
        if (_view == null) return;
        if (IsInstanceValid(_view)) { _view.Hide(); _view.ReleaseCardDisplays(); RemoveChild(_view); _view.QueueFree(); }
        _view = null;
    }
    private void PresentPreview(OperationPreview preview)
    {
        HideView();
        _view = new() { Modulate = new Color(1, 1, 1, 0) }; _view.UseCardDisplays(_cardDisplays);
        AddChild(_view); _view.SetPreview(preview);
        _viewLayoutFrame = _view.SummaryDesiredSize.Y > 0 ? Engine.GetProcessFrames() : null;
    }
    private void RestoreNativeTip()
    {
        if (_nativeTextTip != null && IsInstanceValid(_nativeTextTip)) _nativeTextTip.Position = _nativeTextTipPosition;
        _nativeTextTip = null;
    }
    private void Clear()
    {
        _cancellation?.Cancel(); _cancellation?.Dispose(); _cancellation = null; _work = null;
        _snapshot = null; _stamp = null; _directTransformSelector = null; _captureQueued = false; _potionObservation = null;
        _demand = null; _policyPoll = 0;
        _owner = null; _run = null; _request = null; HideView(); SetProcess(false);
    }
    public override void _ExitTree() { Clear(); _cardDisplays.Clear(); if (ReferenceEquals(_active, this)) _active = null; }
}
