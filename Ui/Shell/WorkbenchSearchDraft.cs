using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Compatibility;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Ui.Shell;

internal sealed record WorkbenchPlayerDraft(int Slot, ModelKey Character,
    MegaCrit.Sts2.Core.Unlocks.SerializableUnlockState Unlocks, string UnlockSource)
{
    public AncientEditorStateSnapshot? AncientEditor { get; init; }
}

internal sealed record AncientEditorRowSnapshot(int Act, ModelKey Ancient,
    IReadOnlyList<ModelKey> Options, ModelKey? SeaGlassTarget);

internal sealed record AncientEditorModeSnapshot(IReadOnlyList<AncientEditorRowSnapshot> Rows,
    bool NeowEnabled, IReadOnlyList<ModelKey> NeowOptions, int SelectedAct);

internal sealed record AncientEditorStateSnapshot(int ActiveMode, int LastAdvancedMode,
    IReadOnlyList<AncientEditorModeSnapshot> Modes);

internal sealed record PartyAncientIdentitySnapshot(int Act, IReadOnlyList<ModelKey> Ancients);
internal sealed record PartyAncientEditorStateSnapshot(bool Advanced,
    IReadOnlyList<PartyAncientIdentitySnapshot> DefaultIdentities,
    IReadOnlyList<PartyAncientIdentitySnapshot> AdvancedIdentities);

// Typed player intent only. Captured event pools are rebound at compile, never persisted.
internal sealed record WorkbenchSearchDraft(ModelKey Character, int Ascension, SearchQuery Query,
    AncientOptionConditionProfile AncientPremises)
{
    public int Version { get; init; } = 3;
    public AncientEditorStateSnapshot? AncientEditor { get; init; }
    public PartyAncientEditorStateSnapshot? PartyAncientEditor { get; init; }
    public IReadOnlyList<WorkbenchPlayerDraft> Players { get; init; } = Array.Empty<WorkbenchPlayerDraft>();
    public WorldGameMode Mode { get; init; } = WorldGameMode.Singleplayer;
    public string GameVersion { get; init; } = "";
    public RuntimeProfileId? Profile { get; init; }
    public string ObservationVersion { get; init; } = OrderedPartyAuthority.ObservationVersion;
    internal WorkbenchSearchDraft WithoutCapturedAuthority() => this with
    {
        Version = Math.Max(Version, Players.Count > 0 ? 4 : 3),
        Query = Query with
        {
            EventResultConditions = Query.EventResultConditions.Select(c => c with { MorphicGroveScenario = null }).ToArray(),
            Players = Query.Players.Select(p => p with { Conditions = p.Conditions with
            { EventResultConditions = p.Conditions.EventResultConditions.Select(c => c with { MorphicGroveScenario = null }).ToArray() } }).ToArray(),
            TransformationAggregate = Query.TransformationAggregate is { } t ? t with { EventScenario = null } : null
        }
    };

    internal CompiledSearch Compile(ModRuntimeSnapshot runtime, out RuntimeContextAuthoritySnapshot authority)
    {
        if (Version is not (1 or 2 or 3 or 4)) throw new InvalidOperationException("integration.load_version");
        if (Mode is not (WorldGameMode.Singleplayer or WorldGameMode.Multiplayer) || (Mode == WorldGameMode.Multiplayer) != (Players.Count > 0))
            throw new InvalidOperationException("Party.DraftModeMismatch");
        if (Players.Count == 0)
            return Controllers.SearchPageController.CompileAuthoredQuery(Query, AncientPremises, runtime, Character, Ascension, out authority);
        if (Version < 2 || (Version < 3 && PartyNeowQuery.HasTransactions(Query)) || ObservationVersion != (PartyNeowQuery.HasTransactions(Query) ? Core.Effects.PartyNeowAdmission.ObservationVersion : OrderedPartyAuthority.ObservationVersion) ||
            Character != Players[0].Character ||
            Players.Where((p, i) => p.Slot != i || string.IsNullOrWhiteSpace(p.UnlockSource)).Any())
            throw new InvalidOperationException("Party.InvalidDraft");
        if (GameVersion != runtime.Detection.NormalizedVersion || Profile != runtime.Profile.ProfileId)
            throw new InvalidOperationException("Party.StaleDraftVersion");
        var party = Infrastructure.Snapshots.PartyRuntimeAuthorityCapture.Capture(runtime.Profile, "000000000000",
            Players.Select(p => p.Character).ToArray(), Players.Select(p => p.Unlocks).ToArray(), Ascension, runtime.Detection.NormalizedVersion, Players.Select(p => p.UnlockSource).ToArray());
        authority = party.Players[0];
        var context = SearchContextFactory.From(runtime.Profile.ProfileId, authority.Character.CharacterKey, Ascension,
            authority, runtime.Detection, AncientPremises) with { Party = party };
        var query = Query with { Players = Query.Players.Select(p => p with
        {
            Conditions = p.Conditions with { EventResultConditions = p.Conditions.EventResultConditions.Select(c =>
                EventResultTransformSemantics.IsTransform(c.Kind)
                    ? c with { MorphicGroveScenario = Infrastructure.Snapshots.Beta111MorphicGroveAuthorityCapture.CaptureAuthoredEventResult(
                        party.Players[p.Slot], c.Kind, MegaCrit.Sts2.Core.Unlocks.UnlockState.FromSerializable(Players[p.Slot].Unlocks)) }
                    : c).ToArray() }
        }).ToArray() };
        return SearchCompiler.Compile(query, context);
    }
}
