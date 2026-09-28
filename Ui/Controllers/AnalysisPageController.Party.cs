using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Ui.Pages.Analysis;
using RolltheSpire2.Ui.Shell;

namespace RolltheSpire2.Ui.Controllers;

internal sealed partial class AnalysisPageController
{
    private WorkbenchSearchDraft? _partyDraft;
    private WorkbenchSearchDraft? _lastPartyDraft;
    private string _partySelectionSeed = "";
    private AnalysisRequestDraft? _soloDraft;
    private (int? Choice, string Route) _soloSelection;
    private void SelectPartyMode(bool multiplayer)
    {
        if (multiplayer == (_partyDraft is not null)) return;
        if (!multiplayer)
        {
            _lastPartyDraft = _partyDraft;
            _partyDraft = null;
            _page.ConfigureParty(1);
            var solo = _soloDraft ?? _page.CurrentDraft;
            _page.SetPredictorContext(solo.CharacterKey, solo.Ascension, 1, 0, solo.AncientOptionConditions,
                _soloSelection.Route ?? "", _soloSelection.Choice, "", false);
            if (_runtime.Profile.TryCanonicalizeSeed(_page.CurrentDraft.RawSeed, out _, out _)) AnalyzeCurrentDraft();
            else PersistPartyConfiguration();
            return;
        }
        var current = _page.CurrentDraft;
        _soloDraft = current;
        _soloSelection = (_page.PreferredOpeningChoiceSlotIndex, _page.PreferredOpeningRouteId);
        var draft = _lastPartyDraft;
        if (draft is null)
        {
            var slots = Enumerable.Range(0, 2).ToArray();
            draft = new WorkbenchSearchDraft(current.CharacterKey, current.Ascension,
                Search.Semantics.SearchQuery.Empty with { Players = slots.Select(slot =>
                    new Search.Semantics.PlayerOfferQuery(slot, Search.Contracts.ModelKeySetFilter.Empty)).ToArray() },
                current.AncientOptionConditions)
            {
                Mode = Core.World.Snapshots.WorldGameMode.Multiplayer,
                Players = slots.Select(slot => new WorkbenchPlayerDraft(slot, current.CharacterKey,
                    MegaCrit.Sts2.Core.Unlocks.UnlockState.all.ToSerializable(), "AssumedFullyUnlocked")).ToArray()
            };
        }
        // Predictor owns its roster; only explicit result/favorite navigation imports Search intent.
        _partyDraft = draft;
        _page.ConfigureParty(draft.Players.Count);
        SelectPartyPlayer(0);
        PersistPartyConfiguration();
    }
    private string _sourcePartyDraftKey = "";
    private bool _bindingPartyDocument;
    private readonly Dictionary<int, (int? Choice, string Route)> _partySelections = [];
    private readonly HashSet<int> _partyExplicitSelections = [];

    internal void SetPartyDraft(WorkbenchSearchDraft? draft, string? seed = null, PartySeedInformation? witness = null)
    {
        string sourceKey = System.Text.Json.JsonSerializer.Serialize(draft);
        bool changed = _sourcePartyDraftKey != sourceKey || witness is not null || (draft is null) != (_partyDraft is null);
        _sourcePartyDraftKey = sourceKey;
        if (changed) _partyDraft = draft;
        if (changed) { _partySelections.Clear(); _partyExplicitSelections.Clear(); _partySelectionSeed = seed ?? _page.CurrentDraft.RawSeed; }
        if (witness is not null)
            foreach (var transaction in witness.Transactions)
                _partySelections[transaction.Slot] = (witness.Players[transaction.Slot].Offers.ToList().IndexOf(transaction.Option) + 1,
                    transaction.OpeningRouteId);
        _page.ConfigureParty(draft?.Players.Count ?? 1);
        if (draft is null)
        {
            if (_page.CurrentDraft.PlayersCount > 1)
                _page.SetPredictorContext(_page.CurrentDraft.CharacterKey, _page.CurrentDraft.Ascension, 1, 0,
                    _page.CurrentDraft.AncientOptionConditions, "", null, "", false);
            return;
        }
        _lastPartyDraft = draft;
        if (seed is not null) _page.SetSeedText(seed, commit: true);
        SelectPartyPlayer(changed ? 0 : Math.Clamp(_page.CurrentDraft.PlayerSlotIndex, 0, draft.Players.Count - 1));
    }

    private void SelectPartyPlayer(int slot)
    {
        if (_partyDraft is not { } draft || slot < 0 || slot >= draft.Players.Count) return;
        var selected = _partySelections.GetValueOrDefault(slot);
        _page.SetPredictorContext(draft.Players[slot].Character, draft.Ascension, draft.Players.Count, slot,
            draft.Query.Players[slot].AncientPremises, selected.Route ?? "", selected.Choice, "", false);
        if (_runtime.Profile.TryCanonicalizeSeed(_page.CurrentDraft.RawSeed, out _, out _)) AnalyzeCurrentDraft();
    }

    private void AnalyzeParty(AnalysisRequestDraft draft)
    {
        var configured = _partyDraft!;
        var players = configured.Players.ToArray();
        players[draft.PlayerSlotIndex] = players[draft.PlayerSlotIndex] with { Character = draft.CharacterKey };
        configured = configured with { Players = players, Character = players[0].Character, Ascension = draft.Ascension,
            Query = configured.Query with { Players = configured.Query.Players.Select(p => p.Slot == draft.PlayerSlotIndex
                ? p with { AncientPremises = draft.AncientOptionConditions } : p).ToArray() } };
        _partyDraft = configured;
        // Search predicates do not filter this document; authored Capsule-effect premises do carry over.
        var party = Infrastructure.Snapshots.PartyRuntimeAuthorityCapture.Capture(_runtime.Profile, draft.RawSeed,
            players.Select(p => p.Character).ToArray(), players.Select(p => p.Unlocks).ToArray(), draft.Ascension,
            _runtime.Detection.NormalizedVersion, players.Select(p => p.UnlockSource).ToArray());
        if (!_runtime.Profile.TryCanonicalizeSeed(draft.RawSeed, out var canonical, out var issue))
            throw new InvalidOperationException(issue);
        var root = _runtime.Profile.ComputeRootSeed(canonical);
        var rng = NeowEffectRngContext.CreateFromRootHash(_runtime.Profile, root, 0);
        bool sharedRngKnown = true;
        var selectedRelics = new Dictionary<int, Core.Identity.ModelKey>();
        for (int slot = 0; slot <= draft.PlayerSlotIndex; slot++)
        {
            var owner = party.Players[slot];
            if (!SeedPredictionRequest.TryCreate(draft.RawSeed, owner.Character, owner.Ascension, owner.PlayersCount, slot,
                owner, slot == draft.PlayerSlotIndex ? draft.AncientOptionConditions : configured.Query.Players[slot].AncientPremises,
                SeedPredictionDomainSelection.All, SeedPredictionInputLimits.MaximumRelicSequencePreviewCount, false,
                out var request, out var error)) throw new InvalidOperationException("Party.InformationRequest:" + error);
            var choices = PartyNeowProjection.ProjectChoices(request!, root, rng.Niche, rng.CombatPotionGeneration, sharedRngKnown,
                Search.Semantics.PartyInitialQuery.CapsuleEffectPremise(configured.Query.Players[slot].Conditions));
            var selection = _partySelections.GetValueOrDefault(slot);
            if (_strictLibrarySeed == canonical && (selection.Choice.HasValue || !string.IsNullOrWhiteSpace(selection.Route)))
                ValidateLibrarySelection(choices, selection.Choice, selection.Route);
            var desired = configured.Query.Players[slot].Conditions.OpeningRoute?.RouteRelicKey;
            var choice = choices.FirstOrDefault(c => c.SlotIndex == selection.Choice)
                ?? choices.FirstOrDefault(c => !string.IsNullOrWhiteSpace(selection.Route) &&
                    PartyNeowProjection.ConcreteRoutes(c).Any(r => r.OpeningRewardContinuations?.Routes.Any(x => x.Route.RouteId == selection.Route) == true))
                ?? choices.FirstOrDefault(c => c.RelicKey == desired) ?? choices.First();
            var routes = PartyNeowProjection.ConcreteRoutes(choice).ToArray();
            var concrete = routes.FirstOrDefault(c => c.OpeningRewardContinuations?.Routes.Any(r => r.Route.RouteId == selection.Route) == true)
                ?? routes.FirstOrDefault() ?? choice;
            var continuation = concrete.OpeningRewardContinuations?.Routes.FirstOrDefault();
            _partySelections[slot] = (choice.SlotIndex, continuation?.Route.RouteId ?? "");
            selectedRelics[slot] = choice.RelicKey;
            if (slot == draft.PlayerSlotIndex)
            {
                request!.PartyOpeningChoices = choices;
                var document = RuntimeProfileRegistry.Predict(_runtime.Detection, request);
                _bindingPartyDocument = true;
                try
                {
                    _page.SetPredictorContext(owner.Character.CharacterKey, owner.Ascension, owner.PlayersCount, slot,
                        draft.AncientOptionConditions, continuation?.Route.RouteId ?? "", choice.SlotIndex, "", false);
                    _page.ShowDocument(request, document);
                }
                finally { _bindingPartyDocument = false; }
                _page.SetPartyOpeningChoices(selectedRelics);
                _setGlobalStatus(GlobalStatusKind.Idle, _page.Text(Presentation.Localization.Ui1TextKey.AnalysisComplete), draft.RawSeed);
                return;
            }
            // Every displayed information-page choice is a pickup, including defaults.
            // Search retains its separate unauthored-opening policy.
            sharedRngKnown &= continuation?.NicheState is not null && continuation.CombatPotionGenerationState is not null;
            if (sharedRngKnown)
                rng = NeowEffectRngContext.CreateFromRootHash(_runtime.Profile, root, slot + 1)
                    .WithShared(continuation!.NicheState!.Restore(), continuation.CombatPotionGenerationState!.Restore());
        }
    }
}
