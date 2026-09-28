using System.Reflection;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Search.Runtime;

namespace RolltheSpire2.Infrastructure.ContentNames;

/// <summary>
/// Main-thread presentation adapter for game-localized content names. The live
/// game's localized model text is always preferred. The embedded catalog is only
/// an offline/unavailable-runtime fallback and must never pre-empt current game
/// localization. No game model or localization object is retained.
/// </summary>
internal sealed class RuntimeGameContentNameResolver : IGameContentNameResolver
{
    private readonly IGameContentNameResolver _fallback;
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly Assembly? _gameAssembly;
    private readonly Type[] _gameTypes;
    private readonly Type? _modelDb;
    private readonly Type? _modelIdType;
    private readonly ConstructorInfo? _modelIdConstructor;
    private readonly IReadOnlyDictionary<GameContentKind, MethodInfo> _runtimeModelLookups;

    private RuntimeGameContentNameResolver(string languageCode)
    {
        _fallback = JsonGameContentNameResolver.Create(languageCode);
        _gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(
                assembly.GetName().Name,
                "sts2",
                StringComparison.OrdinalIgnoreCase));
        _gameTypes = _gameAssembly is null ? Array.Empty<Type>() : GetLoadableTypes(_gameAssembly);
        _modelDb = _gameAssembly?.GetType(
            "MegaCrit.Sts2.Core.Models.ModelDb",
            throwOnError: false,
            ignoreCase: false);
        _modelIdType = _gameAssembly?.GetType(
            "MegaCrit.Sts2.Core.Models.ModelId",
            throwOnError: false,
            ignoreCase: false);
        _modelIdConstructor = _modelIdType?.GetConstructor(new[] { typeof(string), typeof(string) });
        _runtimeModelLookups = BuildRuntimeModelLookups(_gameAssembly, _modelDb, _modelIdType);
    }

    public string LanguageCode => _fallback.LanguageCode;

    public static IGameContentNameResolver Create(string languageCode) =>
        new RuntimeGameContentNameResolver(languageCode);

    public string Resolve(ModelKey key, GameContentKind kind)
    {
        string cacheKey = LanguageCode + "|" + key.Serialized + "|" + kind;
        if (_cache.TryGetValue(cacheKey, out string? cached))
        {
            return cached;
        }

        string fallback = _fallback.Resolve(key, kind);
        string? runtimeName = null;
        string evidence = key.IsValid
            ? "content-name-runtime-not-resolved"
            : "content-name-invalid-model-key";
        bool runtimeResolved = key.IsValid &&
            TryResolveRuntimeName(key, kind, out runtimeName, out evidence);
        string resolved = GameContentNameResolutionPolicy.PreferRuntimeLocalizedName(
            key,
            runtimeResolved ? runtimeName : null,
            string.Equals(fallback, key.Entry, StringComparison.Ordinal)
                ? HumanizeEntry(key.Entry)
                : fallback);

        _cache[cacheKey] = resolved;
        if (!runtimeResolved || SearchOperationalLogPolicy.VerboseTraceEnabled)
        {
            RuntimeLog.Info(
                $"ui1ContentNameResolved={key.Serialized};kind={kind};source={(runtimeResolved ? "game-runtime" : "embedded-fallback")};evidence={evidence}");
        }
        return resolved;
    }

    private bool TryResolveRuntimeName(
        ModelKey key,
        GameContentKind kind,
        out string? displayName,
        out string evidence)
    {
        displayName = null;
        evidence = "content-name-runtime-unavailable";
        try
        {
            RuntimeSnapshotThreadGuard.RequireMainThread();
            if (_gameAssembly is null || _modelDb is null)
            {
                evidence = "content-name-modeldb-unavailable";
                return false;
            }

            object? runtimeModel = ResolveRuntimeModelById(key, kind);
            if (runtimeModel is not null &&
                TryReadLocalizedName(runtimeModel, key, out string? byIdName, out string byIdSource))
            {
                displayName = byIdName;
                evidence = "runtime-model-id:" + runtimeModel.GetType().FullName + "." + byIdSource;
                return true;
            }

            if (kind == GameContentKind.Event &&
                TryResolveCatalogModel(_modelDb, "AllEvents", key.Entry, out object? catalogEvent) &&
                catalogEvent is not null &&
                TryReadLocalizedName(catalogEvent, key, out string? catalogName, out string catalogSource))
            {
                displayName = catalogName;
                evidence = "catalog:ModelDb.AllEvents:" + catalogEvent.GetType().FullName + "." + catalogSource;
                return true;
            }

            string normalizedEntry = NormalizeIdentityToken(key.Entry);
            Type[] modelTypes = _gameTypes
                .Where(type =>
                    !type.IsAbstract &&
                    type.FullName?.Contains(".Models.", StringComparison.Ordinal) == true &&
                    ModelTypeMatches(type.Name, normalizedEntry, kind))
                .ToArray();
            if (modelTypes.Length == 0)
            {
                evidence = "content-name-model-type-not-found";
                return false;
            }

            var names = new List<(string Name, string Source)>();
            foreach (Type modelType in modelTypes)
            {
                object? model = ResolveModel(_modelDb, modelType, kind);
                if (model is null)
                {
                    continue;
                }

                if (TryReadLocalizedName(model, key, out string? name, out string source))
                {
                    names.Add((name!, model.GetType().FullName + "." + source));
                }
            }

            string[] distinct = names
                .Select(candidate => candidate.Name)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (distinct.Length != 1)
            {
                evidence = distinct.Length == 0
                    ? "content-name-property-not-found"
                    : "content-name-model-ambiguous";
                return false;
            }

            string resolvedDisplayName = distinct[0];
            displayName = resolvedDisplayName;
            evidence = "reflection:" + names.First(candidate =>
                string.Equals(candidate.Name, resolvedDisplayName, StringComparison.Ordinal)).Source;
            return true;
        }
        catch (Exception ex)
        {
            evidence = "content-name-reflection-failed:" + ex.GetType().Name;
            return false;
        }
    }

    private static bool TryReadLocalizedName(
        object model,
        ModelKey key,
        out string? displayName,
        out string source)
    {
        string[] names =
        {
            "Title", "DisplayName", "Name", "CardName", "RelicName", "PotionName", "CharacterName"
        };
        Type type = model.GetType();
        foreach (string name in names)
        {
            PropertyInfo? property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property is not null && property.GetIndexParameters().Length == 0)
            {
                object? value = SafeGet(() => property.GetValue(model));
                if (TryConvertLocalizedValue(value, key, out displayName))
                {
                    source = "property:" + name;
                    return true;
                }
            }

            FieldInfo? field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field is not null)
            {
                object? value = SafeGet(() => field.GetValue(model));
                if (TryConvertLocalizedValue(value, key, out displayName))
                {
                    source = "field:" + name;
                    return true;
                }
            }
        }

        displayName = null;
        source = string.Empty;
        return false;
    }

    private static bool TryConvertLocalizedValue(object? value, ModelKey key, out string? displayName)
    {
        if (value is string direct && GameContentNameResolutionPolicy.IsUsefulRuntimeName(direct, key))
        {
            displayName = direct.Trim();
            return true;
        }
        if (value is null)
        {
            displayName = null;
            return false;
        }

        string[] methods = { "GetFormattedText", "GetText", "GetLocalizedText", "Localize" };
        foreach (string methodName in methods)
        {
            MethodInfo? method = value.GetType().GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            if (method is null || method.ReturnType != typeof(string))
            {
                continue;
            }

            object? result = SafeGet(() => method.Invoke(value, null));
            if (result is string text && GameContentNameResolutionPolicy.IsUsefulRuntimeName(text, key))
            {
                displayName = text.Trim();
                return true;
            }
        }

        displayName = null;
        return false;
    }

    private object? ResolveRuntimeModelById(ModelKey key, GameContentKind kind)
    {
        if (_modelIdConstructor is null || !_runtimeModelLookups.TryGetValue(kind, out MethodInfo? lookup))
        {
            return null;
        }
        try
        {
            object id = _modelIdConstructor.Invoke(new object[] { key.Category, key.Entry });
            return lookup.Invoke(null, new[] { id });
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<GameContentKind, MethodInfo> BuildRuntimeModelLookups(
        Assembly? gameAssembly,
        Type? modelDb,
        Type? modelIdType)
    {
        var output = new Dictionary<GameContentKind, MethodInfo>();
        if (gameAssembly is null || modelDb is null || modelIdType is null) return output;

        var modelTypes = new Dictionary<GameContentKind, string>
        {
            [GameContentKind.Character] = "MegaCrit.Sts2.Core.Models.CharacterModel",
            [GameContentKind.Card] = "MegaCrit.Sts2.Core.Models.CardModel",
            [GameContentKind.Relic] = "MegaCrit.Sts2.Core.Models.RelicModel",
            [GameContentKind.Potion] = "MegaCrit.Sts2.Core.Models.PotionModel",
            [GameContentKind.Event] = "MegaCrit.Sts2.Core.Models.EventModel",
            [GameContentKind.Encounter] = "MegaCrit.Sts2.Core.Models.EncounterModel",
            [GameContentKind.Act] = "MegaCrit.Sts2.Core.Models.ActModel"
        };
        MethodInfo[] candidates = modelDb.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method =>
            {
                if (!string.Equals(method.Name, "GetByIdOrNull", StringComparison.Ordinal) ||
                    !method.IsGenericMethodDefinition || method.GetGenericArguments().Length != 1)
                    return false;
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType == modelIdType;
            })
            .ToArray();
        if (candidates.Length != 1) return output;

        foreach ((GameContentKind kind, string typeName) in modelTypes)
        {
            Type? type = gameAssembly.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (type is null) continue;
            try { output[kind] = candidates[0].MakeGenericMethod(type); }
            catch { }
        }
        return output;
    }

    private static bool TryResolveCatalogModel(
        Type modelDb,
        string catalogPropertyName,
        string entry,
        out object? model)
    {
        model = null;
        PropertyInfo? property = modelDb.GetProperty(
            catalogPropertyName,
            BindingFlags.Public | BindingFlags.Static);
        if (property is null)
        {
            return false;
        }

        try
        {
            if (property.GetValue(null) is not System.Collections.IEnumerable models)
            {
                return false;
            }

            string normalizedEntry = NormalizeIdentityToken(entry);
            foreach (object? candidate in models)
            {
                if (candidate is null)
                {
                    continue;
                }

                PropertyInfo? idProperty = candidate.GetType().GetProperty(
                    "Id",
                    BindingFlags.Public | BindingFlags.Instance);
                object? id = idProperty?.GetValue(candidate);
                PropertyInfo? entryProperty = id?.GetType().GetProperty(
                    "Entry",
                    BindingFlags.Public | BindingFlags.Instance);
                string? candidateEntry = entryProperty?.GetValue(id)?.ToString();
                if (candidateEntry is not null &&
                    string.Equals(NormalizeIdentityToken(candidateEntry), normalizedEntry, StringComparison.Ordinal))
                {
                    model = candidate;
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static object? ResolveModel(Type modelDb, Type modelType, GameContentKind kind)
    {
        string preferredMethod = kind switch
        {
            GameContentKind.Relic => "Relic",
            GameContentKind.Character => "Character",
            GameContentKind.Potion => "Potion",
            GameContentKind.Card => "Card",
            GameContentKind.Event => "Event",
            GameContentKind.Encounter => "Encounter",
            GameContentKind.Act => "Act",
            _ => string.Empty
        };
        MethodInfo[] candidates = modelDb.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
            .OrderByDescending(method => string.Equals(method.Name, preferredMethod, StringComparison.Ordinal))
            .ThenBy(method => method.Name, StringComparer.Ordinal)
            .ToArray();
        foreach (MethodInfo method in candidates)
        {
            object? model = SafeGet(() => method.MakeGenericMethod(modelType).Invoke(null, null));
            if (model is not null && modelType.IsInstanceOfType(model))
            {
                return model;
            }
        }
        return null;
    }

    private static bool ModelTypeMatches(string typeName, string normalizedEntry, GameContentKind kind)
    {
        string normalizedType = NormalizeIdentityToken(typeName);
        if (string.Equals(normalizedType, normalizedEntry, StringComparison.Ordinal))
        {
            return true;
        }
        string[] suffixes = kind switch
        {
            GameContentKind.Character => new[] { "CHARACTER", "CHARACTERMODEL", "MODEL" },
            GameContentKind.Relic => new[] { "RELIC", "RELICMODEL", "MODEL" },
            GameContentKind.Card => new[] { "CARD", "CARDMODEL", "MODEL" },
            GameContentKind.Potion => new[] { "POTION", "POTIONMODEL", "MODEL" },
            GameContentKind.Event => new[] { "EVENT", "EVENTMODEL", "MODEL" },
            GameContentKind.Act => new[] { "ACT", "ACTMODEL", "MODEL" },
            _ => new[] { "MODEL" }
        };
        return suffixes.Any(suffix =>
            string.Equals(normalizedType, normalizedEntry + suffix, StringComparison.Ordinal));
    }

    private static string NormalizeIdentityToken(string value)
    {
        Span<char> buffer = stackalloc char[value.Length];
        int length = 0;
        foreach (char character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                continue;
            }
            buffer[length++] = char.ToUpperInvariant(character);
        }
        return new string(buffer[..length]);
    }

    private static string HumanizeEntry(string entry)
    {
        string[] parts = entry.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0
            ? entry
            : string.Join(" ", parts.Select(part =>
                part.Length == 1
                    ? part
                    : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
    }

    private static T? SafeGet<T>(Func<T?> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return default;
        }
    }

    private static Type[] GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }
    }
}
