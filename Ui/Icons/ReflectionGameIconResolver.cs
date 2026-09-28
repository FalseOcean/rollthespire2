using System.Reflection;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;

namespace RolltheSpire2.Ui.Icons;

/// <summary>
/// Main-thread-only visual adapter. It discovers game models through reflection and
/// extracts Texture2D properties without retaining version-sensitive game models.
/// Failure produces a stable MissingIcon descriptor and never hides the business item.
/// </summary>
internal sealed class ReflectionGameIconResolver : IGameIconResolver
{
    private readonly Dictionary<string, IconDescriptor> _cache = new(StringComparer.Ordinal);
    private readonly Assembly? _gameAssembly;
    private readonly Type[] _gameTypes;
    private readonly Type? _modelDb;
    private readonly Type? _modelIdType;
    private readonly ConstructorInfo? _modelIdConstructor;
    private readonly IReadOnlyDictionary<GameContentKind, MethodInfo> _runtimeModelLookups;
    private string _catalogFingerprint;

    public ReflectionGameIconResolver(string catalogFingerprint)
    {
        _catalogFingerprint = catalogFingerprint ?? string.Empty;
        _gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "sts2", StringComparison.OrdinalIgnoreCase));
        _gameTypes = _gameAssembly is null ? Array.Empty<Type>() : GetLoadableTypes(_gameAssembly);
        _modelDb = _gameAssembly?.GetType("MegaCrit.Sts2.Core.Models.ModelDb", throwOnError: false, ignoreCase: false);
        _modelIdType = _gameAssembly?.GetType("MegaCrit.Sts2.Core.Models.ModelId", throwOnError: false, ignoreCase: false);
        _modelIdConstructor = _modelIdType?.GetConstructor(new[] { typeof(string), typeof(string) });
        _runtimeModelLookups = BuildRuntimeModelLookups(_gameAssembly, _modelDb, _modelIdType);
    }

    public IconDescriptor Resolve(ModelKey key, GameContentKind kind, IconVariant variant)
    {
        string cacheKey = $"{_catalogFingerprint}|{key.Serialized}|{kind}|{variant}";
        if (_cache.TryGetValue(cacheKey, out IconDescriptor? cached))
        {
            // Successful Resource references may be reused while their Godot
            // wrapper remains valid. Missing/invalid presentation results are
            // deliberately retried: a Mod may register/load its visual metadata
            // after this resolver was constructed, and one early miss must not
            // poison that identity for the lifetime of the shell.
            if (!cached.IsMissing && cached.Texture is not null && IsTextureUsable(cached.Texture))
            {
                return cached;
            }
            _cache.Remove(cacheKey);
        }

        IconDescriptor resolved = TryResolveTexture(key, kind, variant, out Texture2D? texture, out string evidence)
            ? new IconDescriptor(key, kind, variant, texture, false, evidence)
            : IconDescriptor.Missing(key, kind, variant, evidence);
        _cache[cacheKey] = resolved;
        RuntimeLog.Detail($"ui1IconResolved={key.Serialized};kind={kind};missing={resolved.IsMissing.ToString().ToLowerInvariant()};evidence={resolved.EvidenceCode}");
        return resolved;
    }

    public void InvalidateCatalog(string catalogFingerprint)
    {
        string next = catalogFingerprint ?? string.Empty;
        if (string.Equals(next, _catalogFingerprint, StringComparison.Ordinal))
        {
            return;
        }

        _catalogFingerprint = next;
        _cache.Clear();
    }

    private bool TryResolveTexture(
        ModelKey key,
        GameContentKind kind,
        IconVariant variant,
        out Texture2D? texture,
        out string evidence)
    {
        texture = null;
        evidence = "icon-model-unavailable";
        try
        {
            if (_gameAssembly is null)
            {
                evidence = "icon-sts2-assembly-missing";
                return false;
            }

            if (_modelDb is null)
            {
                evidence = "icon-modeldb-not-found";
                return false;
            }

            // World cards resolve by the exact runtime ModelKey catalog entry. They
            // must not depend on a CLR type-name heuristic: several encounter/event
            // entries intentionally differ from their implementation type names.
            if (IsWorldCompendiumRequest(kind, variant))
            {
                return TryResolveWorldTexture(key, kind, variant, out texture, out evidence);
            }

            if (kind == GameContentKind.Event)
            {
                object? catalogEvent = ResolveCatalogModel("AllEvents", key.Entry);
                if (catalogEvent is not null)
                {
                    Texture2D? eventTexture = FindTexture(
                        catalogEvent,
                        new[] { "Icon", "Portrait", "Image", "EventIcon", "MapIcon" },
                        out string eventSource);
                    if (eventTexture is not null)
                    {
                        texture = eventTexture;
                        evidence = $"catalog:ModelDb.AllEvents:{catalogEvent.GetType().FullName}:{eventSource}:{variant}";
                        return true;
                    }
                }
            }

            object? runtimeModel = ResolveRuntimeModelById(key, kind);
            if (runtimeModel is not null)
            {
                string[] runtimePropertyNames = kind switch
                {
                    GameContentKind.Relic => new[] { "Icon" },
                    GameContentKind.Character => new[]
                    {
                        "CharacterSelectIcon", "CharacterIcon", "Portrait", "Icon", "TopBarPortrait", "SmallPortrait"
                    },
                    GameContentKind.Potion => new[] { "Image", "Icon", "Portrait" },
                    GameContentKind.Card => new[] { "Portrait", "Icon", "Image" },
                    GameContentKind.Act => new[] { "MapIcon", "Icon", "RunHistoryIcon", "Portrait", "Image" },
                    _ => new[] { "Icon", "Portrait", "Image" }
                };
                string runtimeSource;
                Texture2D? runtimeTexture = kind == GameContentKind.Potion
                    ? FindPotionSmallTexture(runtimeModel, out runtimeSource)
                    : FindTexture(runtimeModel, runtimePropertyNames, out runtimeSource);
                if (runtimeTexture is not null && IsTextureUsable(runtimeTexture))
                {
                    texture = runtimeTexture;
                    evidence = $"runtime-model-id:{runtimeModel.GetType().FullName}:{runtimeSource}:{variant}";
                    return true;
                }
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
                evidence = "icon-model-type-not-found";
                return false;
            }
            string[] propertyNames = kind switch
            {
                GameContentKind.Relic => new[] { "Icon" },
                GameContentKind.Character => new[]
                {
                    "CharacterSelectIcon", "CharacterIcon", "Portrait", "Icon", "TopBarPortrait", "SmallPortrait"
                },
                GameContentKind.Potion => new[] { "Image" },
                GameContentKind.Card => new[] { "Portrait", "Icon" },
                GameContentKind.Act => new[] { "MapIcon", "Icon", "RunHistoryIcon", "Portrait", "Image" },
                _ => new[] { "Icon", "Portrait" }
            };

            var resolved = new List<(object Model, Texture2D Texture, string Source)>();
            var failures = new List<string>();
            foreach (Type modelType in modelTypes)
            {
                object? model = ResolveModel(_modelDb, modelType, kind);
                if (model is null)
                {
                    failures.Add($"{modelType.Name}:model-unavailable");
                    continue;
                }

                string source;
                Texture2D? candidate = kind == GameContentKind.Potion
                    ? FindPotionSmallTexture(model, out source)
                    : FindTexture(model, propertyNames, out source);
                if (candidate is not null)
                {
                    resolved.Add((model, candidate, source));
                }
                else
                {
                    failures.Add($"{modelType.Name}:{source}");
                }
            }
            if (resolved.Count != 1)
            {
                evidence = resolved.Count == 0
                    ? $"icon-texture-not-found:{CompactEvidence(failures)}"
                    : "icon-model-type-ambiguous";
                return false;
            }

            texture = resolved[0].Texture;
            evidence = $"reflection:{resolved[0].Model.GetType().FullName}:{resolved[0].Source}:{variant}";
            return true;
        }
        catch (Exception ex)
        {
            evidence = $"icon-reflection-failed:{ex.GetType().Name}";
            return false;
        }
    }

    private static bool IsWorldCompendiumRequest(GameContentKind kind, IconVariant variant) =>
        (kind == GameContentKind.Encounter && variant == IconVariant.WorldCompendiumBossIcon) ||
        (kind == GameContentKind.Ancient && variant == IconVariant.WorldCompendiumAncientIcon);

    private bool TryResolveWorldTexture(
        ModelKey key,
        GameContentKind kind,
        IconVariant variant,
        out Texture2D? texture,
        out string evidence)
    {
        texture = null;
        evidence = "world-compendium-icon-model-unavailable";
        object? model = ResolveWorldModel(key.Entry, kind);
        if (model is null)
        {
            evidence = $"world-compendium-catalog-model-not-found:{kind}:{key.Serialized}";
            return false;
        }

        Type modelType = model.GetType();
        if (kind == GameContentKind.Ancient)
        {
            PropertyInfo? runHistoryIcon = modelType.GetProperty(
                "RunHistoryIcon",
                BindingFlags.Public | BindingFlags.Instance);
            if (runHistoryIcon is not null && typeof(Texture2D).IsAssignableFrom(runHistoryIcon.PropertyType))
            {
                try
                {
                    if (runHistoryIcon.GetValue(model) is Texture2D candidate && GodotObject.IsInstanceValid(candidate))
                    {
                        texture = candidate;
                        evidence = $"world-compendium:{modelType.FullName}:RunHistoryIcon:{variant}";
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    // Continue to the audited Run-History path rather than turning a
                    // visual getter failure into a missing identity prediction.
                    evidence = $"world-compendium-ancient-runhistoryicon-getter-failed:{ex.GetType().Name}";
                }
            }
        }

        if (!TryResolveOfficialRunHistoryIconPath(model, out string? iconPath, out string pathEvidence) ||
            string.IsNullOrWhiteSpace(iconPath))
        {
            if (kind == GameContentKind.Ancient &&
                string.Equals(evidence, "world-compendium-icon-model-unavailable", StringComparison.Ordinal))
            {
                evidence = "world-compendium-ancient-runhistoryicon-member-not-found";
            }
            else if (!string.IsNullOrWhiteSpace(pathEvidence))
            {
                evidence = pathEvidence;
            }
            return false;
        }

        var loadedPaths = new HashSet<string>(StringComparer.Ordinal);
        if (!TryLoadTexture(iconPath, loadedPaths, out Texture2D? worldTexture) || worldTexture is null ||
            !GodotObject.IsInstanceValid(worldTexture))
        {
            evidence = $"world-compendium-icon-missing:{kind}:{iconPath}";
            return false;
        }

        texture = worldTexture;
        evidence = $"world-compendium:{modelType.FullName}:{pathEvidence}:{iconPath}:{variant}";
        return true;
    }

    private bool TryResolveOfficialRunHistoryIconPath(object model, out string? path, out string evidence)
    {
        path = null;
        evidence = "world-compendium-model-id-unavailable";
        PropertyInfo? idProperty = model.GetType().GetProperty(
            "Id",
            BindingFlags.Public | BindingFlags.Instance);
        if (idProperty is null)
        {
            return false;
        }

        object? modelId;
        try
        {
            modelId = idProperty.GetValue(model);
        }
        catch (Exception ex)
        {
            evidence = $"world-compendium-model-id-getter-failed:{ex.GetType().Name}";
            return false;
        }

        if (modelId is null)
        {
            return false;
        }

        Type? imageHelper = _gameAssembly?.GetType("MegaCrit.Sts2.Core.Helpers.ImageHelper", throwOnError: false);
        if (imageHelper is not null)
        {
            MethodInfo? getRoomIconPath = imageHelper.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => string.Equals(method.Name, "GetRoomIconPath", StringComparison.Ordinal))
                .FirstOrDefault(method => IsCompatibleRoomIconMethod(method, modelId));
            if (getRoomIconPath is not null)
            {
                try
                {
                    ParameterInfo[] parameters = getRoomIconPath.GetParameters();
                    object mapPointType = Enum.Parse(parameters[0].ParameterType, "Boss");
                    object roomType = Enum.Parse(parameters[1].ParameterType, "Boss");
                    path = getRoomIconPath.Invoke(null, new[] { mapPointType, roomType, modelId }) as string;
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        evidence = $"{imageHelper.FullName}.GetRoomIconPath";
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    evidence = $"world-compendium-imagehelper-invoke-failed:{ex.GetType().Name}";
                }
            }
        }

        // Direct-source audit confirms GetRoomIconPath's model-specific fallback in
        // both supported profiles. This uses the runtime model Id, never a localized
        // name or implementation type, and only runs if reflective invocation is not
        // ABI-compatible with the current assembly.
        PropertyInfo? entryProperty = modelId.GetType().GetProperty(
            "Entry",
            BindingFlags.Public | BindingFlags.Instance);
        string? entry = entryProperty?.GetValue(modelId)?.ToString();
        if (string.IsNullOrWhiteSpace(entry))
        {
            if (string.IsNullOrWhiteSpace(evidence))
            {
                evidence = "world-compendium-model-id-entry-missing";
            }
            return false;
        }

        path = $"res://images/ui/run_history/{entry.ToLowerInvariant()}.png";
        evidence = "audited-ImageHelper.GetRoomIconPath-model-id-fallback";
        return true;
    }

    private static bool IsCompatibleRoomIconMethod(MethodInfo method, object modelId)
    {
        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length != 3 ||
            !parameters[0].ParameterType.IsEnum ||
            !parameters[1].ParameterType.IsEnum)
        {
            return false;
        }

        Type modelIdParameter = parameters[2].ParameterType;
        if (modelIdParameter.IsInstanceOfType(modelId))
        {
            return true;
        }

        Type? nullableUnderlying = Nullable.GetUnderlyingType(modelIdParameter);
        return nullableUnderlying?.IsInstanceOfType(modelId) == true;
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
        if (gameAssembly is null || modelDb is null || modelIdType is null)
        {
            return output;
        }

        var modelTypes = new Dictionary<GameContentKind, string>
        {
            [GameContentKind.Character] = "MegaCrit.Sts2.Core.Models.CharacterModel",
            [GameContentKind.Card] = "MegaCrit.Sts2.Core.Models.CardModel",
            [GameContentKind.Relic] = "MegaCrit.Sts2.Core.Models.RelicModel",
            [GameContentKind.Potion] = "MegaCrit.Sts2.Core.Models.PotionModel",
            [GameContentKind.Event] = "MegaCrit.Sts2.Core.Models.EventModel",
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

    private static bool IsTextureUsable(Texture2D texture)
    {
        try { return GodotObject.IsInstanceValid(texture); }
        catch (ObjectDisposedException) { return false; }
    }

    private object? ResolveCatalogModel(string catalogPropertyName, string entry)
    {
        PropertyInfo? catalogProperty = _modelDb?.GetProperty(
            catalogPropertyName,
            BindingFlags.Public | BindingFlags.Static);
        if (catalogProperty is null)
        {
            return null;
        }

        try
        {
            if (catalogProperty.GetValue(null) is not System.Collections.IEnumerable models)
            {
                return null;
            }

            string normalizedEntry = NormalizeIdentityToken(entry);
            foreach (object? model in models)
            {
                if (model is null)
                {
                    continue;
                }

                PropertyInfo? idProperty = model.GetType().GetProperty(
                    "Id",
                    BindingFlags.Public | BindingFlags.Instance);
                object? id = idProperty?.GetValue(model);
                PropertyInfo? entryProperty = id?.GetType().GetProperty(
                    "Entry",
                    BindingFlags.Public | BindingFlags.Instance);
                string? modelEntry = entryProperty?.GetValue(id)?.ToString();
                if (modelEntry is not null &&
                    string.Equals(NormalizeIdentityToken(modelEntry), normalizedEntry, StringComparison.Ordinal))
                {
                    return model;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private object? ResolveWorldModel(string entry, GameContentKind kind)
    {
        string catalogPropertyName = kind == GameContentKind.Encounter
            ? "AllEncounters"
            : "AllAncients";
        return ResolveCatalogModel(catalogPropertyName, entry);
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
            try
            {
                object? model = method.MakeGenericMethod(modelType).Invoke(null, null);
                if (model is not null && modelType.IsInstanceOfType(model))
                {
                    return model;
                }
            }
            catch
            {
                // The method belongs to another model category. Continue safely.
            }
        }

        return null;
    }

    private static Texture2D? FindPotionSmallTexture(
        object model,
        out string source)
    {
        Type type = model.GetType();
        if (TryReadTextureMember(type, model, "Image", out Texture2D? texture, out string memberSource) &&
            texture is not null &&
            GodotObject.IsInstanceValid(texture))
        {
            source = $"potion-small-{memberSource}";
            return texture;
        }

        PropertyInfo? imagePathProperty = type.GetProperty(
            "ImagePath",
            BindingFlags.Public | BindingFlags.Instance);
        if (imagePathProperty is not null &&
            imagePathProperty.GetIndexParameters().Length == 0 &&
            IsSupportedPathType(imagePathProperty.PropertyType))
        {
            try
            {
                string? path = ToResourcePath(imagePathProperty.GetValue(model));
                if (!string.IsNullOrWhiteSpace(path))
                {
                    Texture2D? loaded = ResourceLoader.Load<Texture2D>(
                        path,
                        null,
                        ResourceLoader.CacheMode.Reuse);
                    if (loaded is not null && GodotObject.IsInstanceValid(loaded))
                    {
                        source = "potion-small-path-property:ImagePath";
                        return loaded;
                    }
                }
            }
            catch
            {
                // Preserve MissingIcon behavior when a version-specific getter or loader fails.
            }
        }

        source = "potion-small-image-and-imagepath-unavailable";
        return null;
    }

    private static Texture2D? FindTexture(
        object model,
        IReadOnlyList<string> preferredNames,
        out string source)
    {
        Type type = model.GetType();
        foreach (string name in preferredNames)
        {
            if (TryReadTextureMember(type, model, name, out Texture2D? texture, out string memberSource))
            {
                source = memberSource;
                return texture;
            }
        }

        IReadOnlyList<(Texture2D Texture, string Source)> structuralTextures = FindStructuralTextureMembers(type, model);
        if (structuralTextures.Count == 1)
        {
            source = structuralTextures[0].Source;
            return structuralTextures[0].Texture;
        }
        if (structuralTextures.Count > 1)
        {
            source = $"visual-texture-member-ambiguous:{string.Join(",", structuralTextures.Select(candidate => candidate.Source))}";
            return null;
        }

        IReadOnlyList<(Texture2D Texture, string Source)> pathTextures = FindIconPathTextures(type, model);
        if (pathTextures.Count == 1)
        {
            source = pathTextures[0].Source;
            return pathTextures[0].Texture;
        }
        if (pathTextures.Count > 1)
        {
            source = $"visual-path-member-ambiguous:{string.Join(",", pathTextures.Select(candidate => candidate.Source))}";
            return null;
        }

        source = "no-public-visual-texture-or-icon-path";
        return null;
    }

    private static bool TryReadTextureMember(
        Type type,
        object model,
        string name,
        out Texture2D? texture,
        out string source)
    {
        texture = null;
        source = string.Empty;
        PropertyInfo? property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property is not null &&
            property.GetIndexParameters().Length == 0 &&
            typeof(Texture2D).IsAssignableFrom(property.PropertyType))
        {
            try
            {
                if (property.GetValue(model) is Texture2D propertyTexture)
                {
                    texture = propertyTexture;
                    source = $"property:{property.Name}";
                    return true;
                }
            }
            catch
            {
                // Continue to a public field or a structural fallback.
            }
        }

        FieldInfo? field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        if (field is not null && typeof(Texture2D).IsAssignableFrom(field.FieldType))
        {
            try
            {
                if (field.GetValue(model) is Texture2D fieldTexture)
                {
                    texture = fieldTexture;
                    source = $"field:{field.Name}";
                    return true;
                }
            }
            catch
            {
                // Continue to a structural fallback.
            }
        }

        return false;
    }

    private static IReadOnlyList<(Texture2D Texture, string Source)> FindStructuralTextureMembers(
        Type type,
        object model)
    {
        var candidates = new List<(Texture2D Texture, string Source)>();
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(property =>
                         property.GetIndexParameters().Length == 0 &&
                         typeof(Texture2D).IsAssignableFrom(property.PropertyType) &&
                         IsVisualMemberName(property.Name))
                     .OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            try
            {
                if (property.GetValue(model) is Texture2D texture)
                {
                    candidates.Add((texture, $"structural-property:{property.Name}"));
                }
            }
            catch
            {
                // A version-specific getter failed. Preserve MissingIcon behavior.
            }
        }

        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                     .Where(field =>
                         typeof(Texture2D).IsAssignableFrom(field.FieldType) &&
                         IsVisualMemberName(field.Name))
                     .OrderBy(field => field.Name, StringComparer.Ordinal))
        {
            try
            {
                if (field.GetValue(model) is Texture2D texture)
                {
                    candidates.Add((texture, $"structural-field:{field.Name}"));
                }
            }
            catch
            {
                // A version-specific field access failed. Preserve MissingIcon behavior.
            }
        }

        return candidates;
    }

    private static IReadOnlyList<(Texture2D Texture, string Source)> FindIconPathTextures(
        Type type,
        object model)
    {
        var candidates = new List<(Texture2D Texture, string Source)>();
        var loadedPaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(property =>
                         property.GetIndexParameters().Length == 0 &&
                         IsSupportedPathType(property.PropertyType) &&
                         IsVisualPathMemberName(property.Name))
                     .OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            try
            {
                string? path = ToResourcePath(property.GetValue(model));
                if (TryLoadTexture(path, loadedPaths, out Texture2D? texture) && texture is not null)
                {
                    candidates.Add((texture, $"path-property:{property.Name}"));
                }
            }
            catch
            {
                // A version-specific getter failed. Preserve MissingIcon behavior.
            }
        }

        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                     .Where(field =>
                         IsSupportedPathType(field.FieldType) &&
                         IsVisualPathMemberName(field.Name))
                     .OrderBy(field => field.Name, StringComparer.Ordinal))
        {
            try
            {
                string? path = ToResourcePath(field.GetValue(model));
                if (TryLoadTexture(path, loadedPaths, out Texture2D? texture) && texture is not null)
                {
                    candidates.Add((texture, $"path-field:{field.Name}"));
                }
            }
            catch
            {
                // A version-specific field access failed. Preserve MissingIcon behavior.
            }
        }

        return candidates;
    }

    private static bool TryLoadTexture(
        string? path,
        ISet<string> loadedPaths,
        out Texture2D? texture)
    {
        texture = null;
        if (string.IsNullOrWhiteSpace(path) ||
            !loadedPaths.Add(path) ||
            !ResourceLoader.Exists(path))
        {
            return false;
        }

        try
        {
            texture = ResourceLoader.Load<Texture2D>(path, null, ResourceLoader.CacheMode.Reuse);
            return texture is not null;
        }
        catch
        {
            texture = null;
            return false;
        }
    }

    private static bool IsSupportedPathType(Type type) =>
        type == typeof(string) || type == typeof(StringName);

    private static string? ToResourcePath(object? value) => value switch
    {
        string path => path,
        StringName path => path.ToString(),
        _ => null
    };

    private static bool IsVisualMemberName(string name)
    {
        string normalized = NormalizeIdentityToken(name);
        if (normalized.Contains("OUTLINE", StringComparison.Ordinal) ||
            normalized.Contains("MASK", StringComparison.Ordinal) ||
            normalized.Contains("SHADOW", StringComparison.Ordinal))
        {
            return false;
        }

        return normalized.Contains("ICON", StringComparison.Ordinal) ||
               normalized.Contains("PORTRAIT", StringComparison.Ordinal) ||
               normalized.Contains("SPRITE", StringComparison.Ordinal) ||
               normalized.Contains("TEXTURE", StringComparison.Ordinal) ||
               normalized.Contains("IMAGE", StringComparison.Ordinal);
    }

    private static bool IsVisualPathMemberName(string name)
    {
        string normalized = NormalizeIdentityToken(name);
        return normalized.Contains("PATH", StringComparison.Ordinal) && IsVisualMemberName(normalized);
    }

    private static string CompactEvidence(IReadOnlyList<string> failures)
    {
        if (failures.Count == 0)
        {
            return "no-model-resolved";
        }

        string compact = string.Join(",", failures.Take(3));
        return compact.Length <= 240 ? compact : compact[..240];
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
        return suffixes.Any(suffix => string.Equals(normalizedType, normalizedEntry + suffix, StringComparison.Ordinal));
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
}
