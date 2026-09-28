using System.Reflection;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Ui.Pages.Search;
using RolltheSpire2.Search.Runtime;

namespace RolltheSpire2.Ui.Icons;

internal sealed record SearchCategoryTabIconAsset(
    Texture2D? Texture,
    string EvidenceCode)
{
    public bool IsMissing => Texture is null || !GodotObject.IsInstanceValid(Texture);

    public static SearchCategoryTabIconAsset Missing(string evidenceCode) =>
        new(null, evidenceCode);
}

internal interface ISearchCategoryTabIconProvider
{
    SearchCategoryTabIconAsset Resolve(SearchCategoryKey category);
}

/// <summary>
/// Main-thread-only visual provider for the Search category tabs. Every connected
/// source is an audited exact runtime entry or exact native scene node. Missing
/// visual authority degrades to the existing text-only tab without affecting Search.
/// </summary>
internal sealed class SearchCategoryTabIconProvider : ISearchCategoryTabIconProvider
{
    private const string CombatRewardCardIconPath =
        "res://images/ui/reward_screen/reward_icon_card.png";

    private const string RunHistoryTreasureIconPath =
        "res://images/ui/run_history/treasure.png";

    // NGameOverScreen uses this silhouette for the Bosses Slain score line.
    private const string BossScoreIconPath =
        "res://images/ui/game_over_screen/score_boss.png";

    private const string AncientBlueFlameFrame0Path =
        "res://images/atlases/compressed.sprites/card_template/ancient_flame/ancient_card_flame_0.tres";

    private const string NetScreenTypeExtensionsTypeName =
        "MegaCrit.Sts2.Core.Entities.Multiplayer.NetScreenTypeExtensions";

    private const string NetScreenTypeName =
        "MegaCrit.Sts2.Core.Entities.Multiplayer.NetScreenType";

    private const string ImageHelperTypeName =
        "MegaCrit.Sts2.Core.Helpers.ImageHelper";

    private const string PreloadManagerPreferredTypeName =
        "MegaCrit.Sts2.Core.Helpers.PreloadManager";

    private static readonly ModelKey NeowAncientKey =
        new(BaseGameModelKeys.Categories.Event, "NEOW");

    private static readonly ModelKey MembershipCardKey =
        new(BaseGameModelKeys.Categories.Relic, "MEMBERSHIP_CARD");

    private readonly Dictionary<SearchCategoryKey, SearchCategoryTabIconAsset> _cache = new();
    private readonly IGameIconResolver _icons;
    private readonly Assembly? _gameAssembly;
    private readonly Type[] _gameTypes;

    public SearchCategoryTabIconProvider(IGameIconResolver icons)
    {
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly =>
                string.Equals(assembly.GetName().Name, "sts2", StringComparison.OrdinalIgnoreCase));
        _gameTypes = _gameAssembly is null ? Array.Empty<Type>() : GetLoadableTypes(_gameAssembly);
    }

    public SearchCategoryTabIconAsset Resolve(SearchCategoryKey category)
    {
        if (_cache.TryGetValue(category, out SearchCategoryTabIconAsset? cached))
        {
            return cached;
        }

        SearchCategoryTabIconAsset resolved;
        if (!RuntimeSnapshotThreadGuard.IsMainThread)
        {
            resolved = SearchCategoryTabIconAsset.Missing(
                $"search-category-{category.ToString().ToLowerInvariant()}:main-thread-required");
        }
        else
        {
            resolved = category switch
            {
                SearchCategoryKey.Neow => ResolveModelIcon(
                    NeowAncientKey,
                    GameContentKind.Ancient,
                    IconVariant.WorldCompendiumAncientIcon,
                    "search-category-neow-runtime-ancient-icon"),
                SearchCategoryKey.Ancient => ResolveOfficialAncientGlyph(),
                SearchCategoryKey.BossIdentity => LoadExactTexture(
                    BossScoreIconPath,
                    "search-category-boss-score-silhouette"),
                SearchCategoryKey.BossAndMap => ResolveOfficialMapLocationIcon(),
                SearchCategoryKey.Relic => LoadExactTexture(
                    RunHistoryTreasureIconPath,
                    "search-category-relic-run-history-treasure"),
                SearchCategoryKey.CombatReward => LoadExactTexture(
                    CombatRewardCardIconPath,
                    "search-category-combat-reward-card-icon"),
                SearchCategoryKey.Event => ResolveOfficialRoomIcon("Event"),
                SearchCategoryKey.Shop => ResolveModelIcon(
                    MembershipCardKey,
                    GameContentKind.Relic,
                    IconVariant.Small,
                    "search-category-shop-membership-card"),
                SearchCategoryKey.Transformation => ResolveModelIcon(
                    BaseGameModelKeys.Relics.NewLeaf,
                    GameContentKind.Relic,
                    IconVariant.Small,
                    "search-category-transformation-new-leaf"),
                _ => SearchCategoryTabIconAsset.Missing("search-category-unsupported")
            };
        }

        _cache[category] = resolved;
        if (resolved.IsMissing || SearchOperationalLogPolicy.VerboseTraceEnabled)
        {
            RuntimeLog.Info(
                $"searchCategoryTabIconResolved={category};missing={resolved.IsMissing.ToString().ToLowerInvariant()};evidence={resolved.EvidenceCode}");
        }
        return resolved;
    }

    private static SearchCategoryTabIconAsset ResolveOfficialAncientGlyph()
    {
        const string evidencePrefix =
            "search-category-ancient:card.tscn/AncientBanner/Fire/default/frame0";

        try
        {
            if (!ResourceLoader.Exists(AncientBlueFlameFrame0Path))
            {
                return SearchCategoryTabIconAsset.Missing(
                    $"{evidencePrefix}:ancient-blue-flame-resource-missing:{AncientBlueFlameFrame0Path}");
            }

            Texture2D? texture = ResourceLoader.Load<Texture2D>(
                AncientBlueFlameFrame0Path,
                null,
                ResourceLoader.CacheMode.Reuse);
            if (texture is null || !GodotObject.IsInstanceValid(texture))
            {
                return SearchCategoryTabIconAsset.Missing(
                    $"{evidencePrefix}:ancient-blue-flame-resource-type-mismatch:{AncientBlueFlameFrame0Path}");
            }

            return new SearchCategoryTabIconAsset(
                texture,
                $"{evidencePrefix}:ExactPackedSceneExternalResource:{TextureEvidence(texture)}");
        }
        catch (Exception ex)
        {
            return SearchCategoryTabIconAsset.Missing(
                $"{evidencePrefix}:ancient-blue-flame-load-failed:{ex.GetType().Name}");
        }
    }

    private SearchCategoryTabIconAsset ResolveOfficialMapLocationIcon()
    {
        const string evidencePrefix =
            "search-category-map:NetScreenTypeExtensions.GetLocationIcon(Map)";

        try
        {
            if (_gameAssembly is null)
            {
                return SearchCategoryTabIconAsset.Missing($"{evidencePrefix}:sts2-assembly-missing");
            }

            Type? extensionsType = _gameAssembly.GetType(
                NetScreenTypeExtensionsTypeName,
                throwOnError: false,
                ignoreCase: false);
            Type? enumType = _gameAssembly.GetType(
                NetScreenTypeName,
                throwOnError: false,
                ignoreCase: false);
            if (extensionsType is null || enumType is null || !enumType.IsEnum)
            {
                return SearchCategoryTabIconAsset.Missing($"{evidencePrefix}:type-or-enum-missing");
            }

            MethodInfo? method = extensionsType.GetMethod(
                "GetLocationIcon",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { enumType },
                modifiers: null);
            if (method is null || !typeof(Texture2D).IsAssignableFrom(method.ReturnType))
            {
                return SearchCategoryTabIconAsset.Missing($"{evidencePrefix}:exact-method-missing");
            }

            object map = Enum.Parse(enumType, "Map", ignoreCase: false);
            object? value = method.Invoke(null, new[] { map });
            if (value is not Texture2D texture || !GodotObject.IsInstanceValid(texture))
            {
                return SearchCategoryTabIconAsset.Missing($"{evidencePrefix}:texture-unavailable");
            }

            return new SearchCategoryTabIconAsset(
                texture,
                $"{evidencePrefix}:runtime-public-static:{TextureEvidence(texture)}");
        }
        catch (Exception ex)
        {
            return SearchCategoryTabIconAsset.Missing(
                $"{evidencePrefix}:invoke-failed:{UnwrapExceptionType(ex)}");
        }
    }

    private SearchCategoryTabIconAsset ResolveOfficialRoomIcon(string roomName)
    {
        string evidencePrefix =
            $"search-category-{roomName.ToLowerInvariant()}:ImageHelper.GetRoomIconPath(Unknown,{roomName},null)";

        try
        {
            if (_gameAssembly is null)
            {
                return SearchCategoryTabIconAsset.Missing($"{evidencePrefix}:sts2-assembly-missing");
            }

            Type? imageHelper = _gameAssembly.GetType(
                ImageHelperTypeName,
                throwOnError: false,
                ignoreCase: false);
            if (imageHelper is null)
            {
                return SearchCategoryTabIconAsset.Missing($"{evidencePrefix}:image-helper-missing");
            }

            MethodInfo[] matches = imageHelper.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(IsExactRoomIconPathMethod)
                .ToArray();
            if (matches.Length != 1)
            {
                return SearchCategoryTabIconAsset.Missing(
                    $"{evidencePrefix}:exact-method-count-{matches.Length}");
            }

            MethodInfo method = matches[0];
            ParameterInfo[] parameters = method.GetParameters();
            object mapPointUnknown = Enum.Parse(parameters[0].ParameterType, "Unknown", ignoreCase: false);
            object roomType = Enum.Parse(parameters[1].ParameterType, roomName, ignoreCase: false);
            string? path = method.Invoke(null, new object?[] { mapPointUnknown, roomType, null }) as string;
            if (string.IsNullOrWhiteSpace(path))
            {
                return SearchCategoryTabIconAsset.Missing($"{evidencePrefix}:official-path-empty");
            }

            if (path.Contains("map_unknown", StringComparison.OrdinalIgnoreCase))
            {
                return SearchCategoryTabIconAsset.Missing(
                    $"{evidencePrefix}:rejected-hidden-identity-icon:{path}");
            }

            if (!TryLoadOfficialCompressedTexture(path, out Texture2D? texture, out string cacheEvidence) ||
                texture is null)
            {
                return SearchCategoryTabIconAsset.Missing(
                    $"{evidencePrefix}:official-cache-load-failed:{cacheEvidence}");
            }

            return new SearchCategoryTabIconAsset(
                texture,
                $"{evidencePrefix}:official-path={path}:{cacheEvidence}:{TextureEvidence(texture)}");
        }
        catch (Exception ex)
        {
            return SearchCategoryTabIconAsset.Missing(
                $"{evidencePrefix}:invoke-failed:{UnwrapExceptionType(ex)}");
        }
    }

    private static bool IsExactRoomIconPathMethod(MethodInfo method)
    {
        if (!string.Equals(method.Name, "GetRoomIconPath", StringComparison.Ordinal) ||
            method.ReturnType != typeof(string))
        {
            return false;
        }

        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length != 3 ||
            !parameters[0].ParameterType.IsEnum ||
            !parameters[1].ParameterType.IsEnum ||
            !string.Equals(parameters[0].ParameterType.Name, "MapPointType", StringComparison.Ordinal) ||
            !string.Equals(parameters[1].ParameterType.Name, "RoomType", StringComparison.Ordinal))
        {
            return false;
        }

        Type thirdType = Nullable.GetUnderlyingType(parameters[2].ParameterType) ??
                         parameters[2].ParameterType;
        return string.Equals(thirdType.Name, "ModelId", StringComparison.Ordinal);
    }

    private bool TryLoadOfficialCompressedTexture(
        string path,
        out Texture2D? texture,
        out string evidence)
    {
        texture = null;
        evidence = "preload-manager-unavailable";

        if (_gameAssembly is null)
        {
            evidence = "sts2-assembly-missing";
            return false;
        }

        Type? preloadManager = ResolvePreloadManagerType(out string typeEvidence);
        if (preloadManager is null)
        {
            evidence = typeEvidence;
            return false;
        }

        object? cache = ReadExactStaticMember(preloadManager, "Cache");
        if (cache is null)
        {
            evidence = $"{typeEvidence}:Cache-unavailable";
            return false;
        }

        MethodInfo? method = cache.GetType().GetMethod(
            "GetCompressedTexture2D",
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: new[] { typeof(string) },
            modifiers: null);
        if (method is null || !typeof(Texture2D).IsAssignableFrom(method.ReturnType))
        {
            evidence = $"{typeEvidence}:Cache.GetCompressedTexture2D(string)-missing";
            return false;
        }

        try
        {
            texture = method.Invoke(cache, new object[] { path }) as Texture2D;
            if (texture is null || !GodotObject.IsInstanceValid(texture))
            {
                texture = null;
                evidence = $"{typeEvidence}:Cache.GetCompressedTexture2D-texture-unavailable";
                return false;
            }

            evidence = $"{typeEvidence}:Cache.GetCompressedTexture2D(string)";
            return true;
        }
        catch (Exception ex)
        {
            texture = null;
            evidence = $"{typeEvidence}:Cache.GetCompressedTexture2D-invoke-failed:{UnwrapExceptionType(ex)}";
            return false;
        }
    }

    private Type? ResolvePreloadManagerType(out string evidence)
    {
        if (_gameAssembly is null)
        {
            evidence = "sts2-assembly-missing";
            return null;
        }

        Type? preloadManager = _gameAssembly.GetType(
            PreloadManagerPreferredTypeName,
            throwOnError: false,
            ignoreCase: false);
        if (preloadManager is not null)
        {
            evidence = PreloadManagerPreferredTypeName;
            return preloadManager;
        }

        Type[] exactNameMatches = _gameTypes
            .Where(type =>
                string.Equals(type.Name, "PreloadManager", StringComparison.Ordinal) &&
                type.Namespace?.StartsWith("MegaCrit.Sts2.Core", StringComparison.Ordinal) == true)
            .ToArray();
        if (exactNameMatches.Length != 1)
        {
            evidence = $"preload-manager-exact-type-count-{exactNameMatches.Length}";
            return null;
        }

        evidence = $"unique-runtime-type:{exactNameMatches[0].FullName}";
        return exactNameMatches[0];
    }

    private static object? ReadExactStaticMember(Type type, string name)
    {
        PropertyInfo? property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
        if (property is not null && property.GetIndexParameters().Length == 0)
        {
            return property.GetValue(null);
        }

        FieldInfo? field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
        return field?.GetValue(null);
    }

    private SearchCategoryTabIconAsset ResolveModelIcon(
        ModelKey key,
        GameContentKind kind,
        IconVariant variant,
        string evidencePrefix)
    {
        IconDescriptor descriptor = _icons.Resolve(key, kind, variant);
        return descriptor.Texture is not null &&
               GodotObject.IsInstanceValid(descriptor.Texture) &&
               !descriptor.IsMissing
            ? new SearchCategoryTabIconAsset(
                descriptor.Texture,
                $"{evidencePrefix}:{descriptor.EvidenceCode}")
            : SearchCategoryTabIconAsset.Missing(
                $"{evidencePrefix}-missing:{descriptor.EvidenceCode}");
    }

    private static SearchCategoryTabIconAsset LoadExactTexture(
        string path,
        string evidencePrefix)
    {
        try
        {
            if (!ResourceLoader.Exists(path))
            {
                return SearchCategoryTabIconAsset.Missing(
                    $"{evidencePrefix}:resource-missing:{path}");
            }

            Texture2D? texture = ResourceLoader.Load<Texture2D>(
                path,
                null,
                ResourceLoader.CacheMode.Reuse);
            return texture is not null && GodotObject.IsInstanceValid(texture)
                ? new SearchCategoryTabIconAsset(texture, $"{evidencePrefix}:{path}")
                : SearchCategoryTabIconAsset.Missing(
                    $"{evidencePrefix}:texture-unavailable:{path}");
        }
        catch (Exception ex)
        {
            return SearchCategoryTabIconAsset.Missing(
                $"{evidencePrefix}:load-failed:{ex.GetType().Name}:{path}");
        }
    }

    private static string TextureEvidence(Texture2D texture)
    {
        string path = string.IsNullOrWhiteSpace(texture.ResourcePath)
            ? "embedded-resource"
            : texture.ResourcePath;
        return $"{texture.GetType().FullName}:{path}";
    }

    private static string UnwrapExceptionType(Exception exception)
    {
        Exception actual = exception is TargetInvocationException { InnerException: Exception inner }
            ? inner
            : exception;
        return actual.GetType().Name;
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
