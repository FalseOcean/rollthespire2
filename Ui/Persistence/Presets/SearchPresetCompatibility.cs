using RolltheSpire2.Ui.Workspace;
using System.Collections;
using System.Reflection;
using RolltheSpire2.Core.Authority.Runtime;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Prediction;
using RolltheSpire2.Core.World.Snapshots;
using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Pages.Search.CombatReward;
using RolltheSpire2.Search.Semantics;

namespace RolltheSpire2.Ui.Persistence;

internal sealed record SearchPresetUnresolvedReference(
    string Path,
    string StableIdentity,
    string Reason);

internal sealed record SearchPresetWorkbenchLoadResolution(
    WorkbenchSearchDraft? Draft,
    IReadOnlyList<SearchPresetUnresolvedReference> Unresolved,
    string Issue)
{
    public bool CanLoad => Draft is not null;
}

/// <summary>
/// B-Compatibility only resolves whether persisted stable content identities still
/// exist in the current RT2-relevant SemanticUniverse. It deliberately does not
/// inspect CurrentUnlockState and therefore never becomes Search admission authority.
/// Current intent is indivisible: compatibility never removes conditions. Card
/// exclusion snapshots retain missing identities for Exact's explicit Unknown path;
/// other unresolved identities still prevent use of the template.
/// </summary>
internal static class SearchPresetCompatibilityResolver
{
    /// <summary>
    /// Roster, per-seat conditions and dormant editor selections are resolved
    /// together. Exclusion members alone may remain explicitly unverified; their
    /// original identities are passed intact to Exact. The current compiler and
    /// editor catalog still own admission and representability.
    /// </summary>
    internal static SearchPresetWorkbenchLoadResolution ResolveWorkbench(
        SearchPresetDefinition preset, RuntimeAuthoritySnapshot authority)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(authority);
        if (preset.Workbench is not { } draft)
            return new(null, preset.UnresolvedAsset?.References ?? [], preset.UnresolvedAsset?.Issue ?? "PresetCurrentIntentRequired");
        var unresolved = new List<SearchPresetUnresolvedReference>();
        try
        {
            ValidateWorkbenchShape(draft);
            unresolved.AddRange(FindUnresolvedIntentReferences(draft.WithoutCapturedAuthority(), authority, "Workbench", deferActVariants: true));
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException)
        {
            return new(null, unresolved, "PresetWorkbenchShapeUnresolved:" + ex.Message);
        }
        if (unresolved.Count != 0)
            return new(null, unresolved, HasAuthorityIncomplete(unresolved)
                ? "CurrentSemanticAuthorityIncomplete" : "PresetWorkbenchReferencesUnavailable");
        return new(draft.WithoutCapturedAuthority(), [], string.Empty);
    }

    // Runtime authority DTOs are immutable captures. A new capture owns a new
    // index; weak ownership cannot keep an obsolete game environment alive.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RuntimeAuthoritySnapshot, SemanticUniverseIndex> UniverseIndexes = new();

    internal static IReadOnlyList<SearchPresetUnresolvedReference> FindUnresolvedIntentReferences(
        object intent, RuntimeAuthoritySnapshot authority, string rootPath, bool deferActVariants = false)
    {
        var universe = UniverseIndexes.GetValue(authority, value => new(value));
        var unresolved = new List<SearchPresetUnresolvedReference>();
        VisitWorkbenchIntent(intent, rootPath, (key, path) =>
        {
            // RuntimeAuthority has no Act domain. Workbench variant membership is
            // checked by the live editor transaction; seed opening contexts have no Acts.
            if (deferActVariants && key.Category == "ACT") return;
            // This negative predicate can still prove a mismatch from known cards.
            // Exact receives the whole original set and decides whether absence is
            // proven; compatibility must not drop its unresolved obligations.
            if (path.Contains(".CombatCardExclusion.CardKeys[", StringComparison.Ordinal) ||
                path.Contains(".CombatRewardEditor.ExcludedCollection.Cards[", StringComparison.Ordinal)) return;
            universe.TryResolveAny(key, path, unresolved);
        });
        return unresolved;
    }

    internal static void ValidateWorkbenchShape(WorkbenchSearchDraft draft)
    {
        if (draft.Version is not (1 or 2 or 3 or 4 or 5))
            throw new InvalidDataException("PresetWorkbenchVersionUnsupported");
        // Inspect only authored/init properties, never computed getters (which may
        // assume complete DTOs), runtime authority, or serializable unlock internals.
        VisitWorkbenchIntent(draft, "Workbench", (_, _) => { });
        if (!draft.Character.IsValid || draft.Ascension is < SeedPredictionInputLimits.MinimumAscension or > SeedPredictionInputLimits.MaximumAscension)
            throw new InvalidDataException("PresetWorkbenchContextInvalid");
        bool party = draft.Mode == WorldGameMode.Multiplayer;
        if (draft.Mode is not (WorldGameMode.Singleplayer or WorldGameMode.Multiplayer) ||
            party != (draft.Players.Count > 0) ||
            draft.Query.Players.Count != draft.Players.Count)
            throw new InvalidDataException("PresetWorkbenchModeMismatch");
        ValidateExclusion(draft.Query, draft.CombatRewardEditor);
        foreach (var player in draft.Players)
        {
            var query = draft.Query.Players.FirstOrDefault(p => p.Slot == player.Slot)?.Conditions;
            if (query is not null) ValidateExclusion(query, player.CombatRewardEditor);
        }
        if (!party) return;
        if (draft.Version < 2 || draft.Players.Count is < 2 or > SeedPredictionInputLimits.MaximumPlayers || draft.Character != draft.Players[0].Character ||
            draft.Players.Where((p, i) => p.Slot != i || draft.Query.Players[i].Slot != i ||
                !p.Character.IsValid || (draft.Version < 5 && p.Unlocks is null) ||
                (draft.Version == 5 && (p.Unlocks is not null || p.UnlockSource is not ("CurrentContext" or "AssumedFullyUnlocked"))) ||
                string.IsNullOrWhiteSpace(p.UnlockSource)).Any())
            throw new InvalidDataException("PresetWorkbenchRosterInvalid");
        if (draft.Query.TransformationAggregate is not null || draft.Query.Players.Any(p =>
            p.Conditions.Players.Count != 0 || p.Conditions.TransformationAggregate is not null))
            throw new InvalidDataException("PresetWorkbenchPartyConditionsUnsupported");
    }

    private static void ValidateExclusion(SearchQuery query, CombatRewardSearchDraft? state)
    {
        var exclusion = query.CombatCardExclusion;
        if (exclusion is not null && (exclusion.BattleCount is < 1 or > 6 || exclusion.CardKeys is not { Count: > 0 } ||
            exclusion.CardKeys.Any(k => !k.IsValid || k.Category != BaseGameModelKeys.Categories.Card)))
            throw new InvalidDataException("PresetCardExclusionInvalid");
        if (state is null) return;
        if (state.ExclusionBattleCount is < 1 or > 6 || exclusion is not null && exclusion.BattleCount != state.ExclusionBattleCount)
            throw new InvalidDataException("PresetCardExclusionRangeInvalid");
        if (state.ExcludedCollection is not { } snapshot) return;
        if (string.IsNullOrWhiteSpace(snapshot.Id) || string.IsNullOrWhiteSpace(snapshot.Name) || snapshot.Revision < 1 ||
            snapshot.Cards is not { Count: > 0 } || snapshot.Cards.Any(k => !k.IsValid || k.Category != BaseGameModelKeys.Categories.Card) ||
            snapshot.Cards.Distinct(ModelKeyComparer.Instance).Count() != snapshot.Cards.Count ||
            exclusion is null || exclusion.BattleCount != state.ExclusionBattleCount ||
            !new HashSet<ModelKey>(exclusion.CardKeys, ModelKeyComparer.Instance).SetEquals(snapshot.Cards))
            throw new InvalidDataException("PresetCardExclusionSnapshotMismatch");
    }

    private static void VisitWorkbenchIntent(object value, string path, Action<ModelKey, string> visitKey)
    {
        if (value is ModelKey key)
        {
            if (!key.IsValid) throw new InvalidDataException("PresetStableIdentityInvalid:" + path);
            visitKey(key, path);
            return;
        }
        Type type = value.GetType();
        if (type.IsEnum && !Enum.IsDefined(type, value))
            throw new InvalidDataException("PresetEnumValueUnsupported:" + path);
        if (value is string || type.IsPrimitive || type.IsEnum || type.IsValueType) return;
        // Unlock state is a game-owned serializable DTO and carries no ModelKey
        // identity. Strict JSON materialization preserves it without interpretation.
        if (value is MegaCrit.Sts2.Core.Unlocks.SerializableUnlockState) return;
        if (value is IEnumerable sequence)
        {
            int index = 0;
            foreach (object? item in sequence)
            {
                if (item is null)
                {
                    if (value is not IEnumerable<ModelKey?>)
                        throw new InvalidDataException("PresetNullListEntry:" + path);
                }
                else VisitWorkbenchIntent(item, $"{path}[{index}]", visitKey);
                index++;
            }
            return;
        }
        var nullability = new NullabilityInfoContext();
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetMethod is null || property.SetMethod is null || property.GetIndexParameters().Length != 0) continue;
            // Captured pools are deliberately excluded; they are removed before
            // writing and rebound by the existing Workbench compiler.
            if (property.Name is "MorphicGroveScenario" or "EventScenario") continue;
            object? child = property.GetValue(value);
            if (child is not null) VisitWorkbenchIntent(child, path + "." + property.Name, visitKey);
            else if (nullability.Create(property).WriteState == NullabilityState.NotNull)
                throw new InvalidDataException("PresetRequiredValueMissing:" + path + "." + property.Name);
        }
    }

    private static bool HasAuthorityIncomplete(IEnumerable<SearchPresetUnresolvedReference> unresolved) =>
        unresolved.Any(item => item.Reason.StartsWith("CurrentSemanticDomainAuthorityIncomplete:", StringComparison.Ordinal));

    private sealed class SemanticUniverseIndex
    {
        private readonly Dictionary<string, HashSet<string>> _domains = new(StringComparer.Ordinal);
        private readonly HashSet<string> _all = new(StringComparer.Ordinal);
        private readonly HashSet<string> _complete = new(StringComparer.Ordinal);

        public SemanticUniverseIndex(RuntimeAuthoritySnapshot authority)
        {
            foreach (RuntimeSemanticDomainSnapshot domain in authority.SemanticUniverse)
            {
                var set = new HashSet<string>(
                    domain.Items.Select(item => item.StableIdentity).Where(value => !string.IsNullOrWhiteSpace(value)),
                    StringComparer.Ordinal);
                _domains[domain.Domain] = set;
                _all.UnionWith(set);
                if (domain.AuthorityComplete) _complete.Add(domain.Domain);
            }
        }

        public bool TryResolve(
            string domain,
            ModelKey key,
            string path,
            List<SearchPresetUnresolvedReference> unresolved)
        {
            if (!key.IsValid)
            {
                unresolved.Add(new SearchPresetUnresolvedReference(path, key.Serialized, "StableIdentityInvalid"));
                return false;
            }
            if (!_complete.Contains(domain) || !_domains.TryGetValue(domain, out HashSet<string>? values))
            {
                unresolved.Add(new SearchPresetUnresolvedReference(path, key.Serialized, "CurrentSemanticDomainAuthorityIncomplete:" + domain));
                return false;
            }
            if (values.Contains(key.Serialized)) return true;
            unresolved.Add(new SearchPresetUnresolvedReference(path, key.Serialized, "StableIdentityMissingFromCurrentSemanticUniverse:" + domain));
            return false;
        }

        public bool TryResolveAny(
            ModelKey key,
            string path,
            List<SearchPresetUnresolvedReference> unresolved)
        {
            string[] candidateDomains = key.Category switch
            {
                BaseGameModelKeys.Categories.Character => new[] { RuntimeAuthorityDomains.Characters },
                BaseGameModelKeys.Categories.Card => new[] { RuntimeAuthorityDomains.Cards },
                BaseGameModelKeys.Categories.Relic => new[] { RuntimeAuthorityDomains.Relics },
                BaseGameModelKeys.Categories.Potion => new[] { RuntimeAuthorityDomains.Potions },
                BaseGameModelKeys.Categories.Encounter => new[] { RuntimeAuthorityDomains.Bosses },
                // Ancient identities are EVENT:* in the current Beta111 runtime authority,
                // so EVENT references may legitimately live in either domain.
                BaseGameModelKeys.Categories.Event => new[] { RuntimeAuthorityDomains.Events, RuntimeAuthorityDomains.Ancients },
                _ => Array.Empty<string>()
            };

            if (candidateDomains.Length > 0)
            {
                foreach (string domain in candidateDomains)
                {
                    if (_domains.TryGetValue(domain, out HashSet<string>? values) && values.Contains(key.Serialized))
                        return true;
                }
                foreach (string domain in candidateDomains)
                {
                    if (!_complete.Contains(domain))
                    {
                        unresolved.Add(new SearchPresetUnresolvedReference(
                            path,
                            key.Serialized,
                            "CurrentSemanticDomainAuthorityIncomplete:" + domain));
                        return false;
                    }
                }
                unresolved.Add(new SearchPresetUnresolvedReference(path, key.Serialized, "StableIdentityMissingFromCurrentSemanticUniverse"));
                return false;
            }

            if (_all.Contains(key.Serialized)) return true;
            if (_complete.Count != _domains.Count)
            {
                unresolved.Add(new SearchPresetUnresolvedReference(path, key.Serialized, "CurrentSemanticDomainAuthorityIncomplete:UnknownCategory"));
                return false;
            }
            unresolved.Add(new SearchPresetUnresolvedReference(path, key.Serialized, "StableIdentityMissingFromCurrentSemanticUniverse"));
            return false;
        }
    }
}
