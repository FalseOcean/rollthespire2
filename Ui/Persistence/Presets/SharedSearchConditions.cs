using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using RolltheSpire2.Core.Authority;
using RolltheSpire2.Core.Effects;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Workspace;

namespace RolltheSpire2.Ui.Persistence;

// V2 carries the semantic query. Slots follow roster order, and omitted fields use
// the empty query and broad eligibility defaults of this format.
internal sealed record SharedSearchConditions(
    [property: JsonPropertyName("r")] string[] Roster,
    [property: JsonPropertyName("a")] int Ascension,
    [property: JsonPropertyName("q")] JsonObject Conditions,
    [property: JsonPropertyName("s"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonObject[]? Seats = null,
    [property: JsonPropertyName("e"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonObject? Eligibility = null)
{
    private static readonly JsonSerializerOptions Fields = CreateOptions();
    private static readonly JsonObject EmptyQuery = Object(SearchQuery.Empty);
    private static readonly JsonObject DefaultEligibility = Object(AncientOptionConditionProfile.BroadDefault);
    private static readonly JsonObject EmptySeat = Object(new PlayerOfferQuery(0, ModelKeySetFilter.Empty));

    internal static SharedSearchConditions From(WorkbenchSearchDraft draft)
    {
        var query = draft.WithoutCapturedAuthority().Query;
        var roster = draft.Players.Count == 0 ? new[] { draft.Character } : draft.Players.Select(p => p.Character).ToArray();
        var shared = Difference(Object(query with { Players = [] }), EmptyQuery);
        var seats = query.Players.Select(p => Difference(Object(p with { Slot = 0 }), EmptySeat)).ToArray();
        var eligibility = Difference(Object(draft.AncientPremises), DefaultEligibility);
        if (roster.Any(key => key.Category != "CHARACTER")) throw new InvalidDataException("PresetShareRoster");
        return new(roster.Select(key => key.Entry).ToArray(), draft.Ascension, shared, seats.Any(s => s.Count > 0) ? seats : null,
            eligibility.Count > 0 ? eligibility : null);
    }

    internal WorkbenchSearchDraft ToDraft()
    {
        if (Roster.Length == 0 || Roster.Length > SeedPredictionInputLimits.MaximumPlayers ||
            Roster.Any(string.IsNullOrEmpty) ||
            Seats is not null && (Roster.Length == 1 || Seats.Length != Roster.Length))
            throw new InvalidDataException("PresetShareRoster");
        var party = Roster.Length > 1;
        var query = Restore<SearchQuery>(EmptyQuery, Conditions);
        if (query.Players.Count > 0) throw new InvalidDataException("PresetShareNestedPlayers");
        if (party)
        {
            var players = new PlayerOfferQuery[Roster.Length];
            for (int i = 0; i < players.Length; i++)
                players[i] = Restore<PlayerOfferQuery>(EmptySeat, Seats?[i]) with { Slot = i };
            query = query with { Players = players };
        }
        var characters = Roster.Select(entry => new ModelKey("CHARACTER", entry)).ToArray();
        return new(characters[0], Ascension, query, Restore<AncientOptionConditionProfile>(DefaultEligibility, Eligibility))
        {
            Version = 5, UseFullUnlocks = true,
            Mode = party ? WorldGameMode.Multiplayer : WorldGameMode.Singleplayer,
            Players = party ? characters.Select((key, i) => new WorkbenchPlayerDraft(i, key, null, "AssumedFullyUnlocked")).ToArray() : [],
            ObservationVersion = PartyNeowQuery.HasTransactions(query) ? PartyNeowAdmission.ObservationVersion : OrderedPartyAuthority.ObservationVersion
        };
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            // Computed properties describe the authored inputs and must not go on the wire.
            for (int i = info.Properties.Count - 1; i >= 0; i--)
            {
                var property = info.Properties[i];
                if (property.Set is null && property.AssociatedParameter is null)
                    info.Properties.RemoveAt(i);
                else if (property.AssociatedParameter is { HasDefaultValue: true } parameter)
                    property.ShouldSerialize = (_, value) => !Equals(value, parameter.DefaultValue);
            }
        });
        return new(SearchPresetStore.CreateJsonOptions())
        {
            TypeInfoResolver = resolver, PropertyNamingPolicy = new ConditionNames(),
            RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true
        };
    }

    // Append only. The index is a V2 wire field name. Other fields keep their full name.
    private sealed class ConditionNames : JsonNamingPolicy
    {
        private static readonly Dictionary<string, string> Names = (
            "OpeningRoute OpeningRouteRelicRequirement StructuredOpeningEffects VariantBossBranches AncientBranches RelicSequenceConstraints " +
            "EventSequenceConstraints LegacyNeow LegacyWorld CombatCardRewards CombatPotionRewards StandardMaps StandardMapRoutes " +
            "TransformationAggregate EventResultConditions MerchantColorlessSequenceConditions MerchantRelicConditions AncientIdentityFilters " +
            "AncientPremises TezcataraHasBasicStrike DarvAllowPandorasBoxRelicSet Conditions Offers SelectedOption Results " +
            "Act Kind Count Keys Any All Ban Slot Slots OrderMode TargetKey AncientKey OptionAny SeaGlassTargetAny " +
            "RangeMode RangeValue ActKey VariantKey FirstBoss SecondBoss RequireSecondBoss Metric Comparison Value " +
            "Required StartingRelicKey RequiredRelicKey TargetCardKey TargetMultiset Sources Predicate MinimumRareCount " +
            "MorphicGroveSecondCard MorphicGroveScenario TrialCase TinkerCardType TinkerRider " +
            "RouteRelicKey Lane Scope RouteObjective FirstBosses SecondBosses Requirement PotionKey"
        ).Split(' ').Select((name, index) => (name, code: index.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ToDictionary(pair => pair.name, pair => pair.code, StringComparer.Ordinal);

        public override string ConvertName(string name) => Names.GetValueOrDefault(name, name);
    }

    private static JsonObject Object<T>(T value) => JsonSerializer.SerializeToNode(value, Fields)!.AsObject();

    private static JsonObject Difference(JsonObject value, JsonObject defaults)
    {
        var result = new JsonObject();
        foreach (var (name, node) in value)
        {
            if (JsonNode.DeepEquals(node, defaults[name])) continue;
            result[name] = node is JsonObject obj && defaults[name] is JsonObject baseline
                ? Difference(obj, baseline) : node?.DeepClone();
        }
        return result;
    }

    private static T Restore<T>(JsonObject baseline, JsonObject? changes)
    {
        var result = baseline.DeepClone().AsObject();
        if (changes is not null) Overlay(result, changes);
        return result.Deserialize<T>(Fields) ?? throw new InvalidDataException("PresetShareConditions");
    }

    private static void Overlay(JsonObject result, JsonObject changes)
    {
        foreach (var (name, node) in changes)
        {
            if (node is JsonObject obj && result[name] is JsonObject target) Overlay(target, obj);
            else result[name] = node?.DeepClone();
        }
    }
}
