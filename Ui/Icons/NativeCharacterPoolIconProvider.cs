using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Search.Runtime;

namespace RolltheSpire2.Ui.Icons;

/// <summary>
/// Main-thread-only provider for the native Card Library character-pool visuals.
///
/// Vanilla Card Library character icons have source-audited stable Texture2D
/// resource paths. Load those resources directly instead of instantiating the
/// native Card Library scene and borrowing Texture wrappers from temporary
/// child nodes. This gives the provider clear Resource ownership and avoids
/// coupling native UI texture lifetime to an unattached scene instance.
/// </summary>
internal sealed class NativeCharacterPoolIconProvider : ICharacterPoolIconProvider
{
    private static readonly IReadOnlyDictionary<ModelKey, string> NativeTexturePaths =
        new Dictionary<ModelKey, string>(ModelKeyComparer.Instance)
        {
            [BaseGameModelKeys.Characters.Ironclad] = "res://images/ui/top_panel/character_icon_ironclad.png",
            [BaseGameModelKeys.Characters.Silent] = "res://images/ui/top_panel/character_icon_silent.png",
            [BaseGameModelKeys.Characters.Defect] = "res://images/ui/top_panel/character_icon_defect.png",
            [BaseGameModelKeys.Characters.Necrobinder] = "res://images/ui/top_panel/character_icon_necrobinder.png",
            [BaseGameModelKeys.Characters.Regent] = "res://images/ui/top_panel/character_icon_regent.png"
        };

    private const string ColorlessPoolTexturePath =
        "res://images/atlases/ui_atlas.sprites/card/energy_colorless.tres";

    private readonly Dictionary<ModelKey, IconDescriptor> _cache = new(ModelKeyComparer.Instance);
    private readonly IGameIconResolver _runtimeFallback;
    private Texture2D? _colorlessPoolVisual;
    private string _colorlessPoolEvidence = "character-card-library-colorless-pool-not-read";
    private bool _loaded;
    private bool _disposed;


    public NativeCharacterPoolIconProvider(IGameIconResolver runtimeFallback)
    {
        _runtimeFallback = runtimeFallback ?? throw new ArgumentNullException(nameof(runtimeFallback));
    }

    public IconDescriptor Resolve(ModelKey key)
    {
        EnsureLoaded();
        if (_disposed)
        {
            return Missing(key, "character-card-library-provider-disposed");
        }

        if (_cache.TryGetValue(key, out IconDescriptor? descriptor) &&
            !descriptor.IsMissing && descriptor.Texture is not null &&
            IsTextureUsable(descriptor.Texture))
        {
            return descriptor;
        }

        if (!NativeTexturePaths.TryGetValue(key, out string? path))
        {
            IconDescriptor runtime = _runtimeFallback.Resolve(
                key,
                GameContentKind.Character,
                IconVariant.CharacterPortrait);
            IconDescriptor runtimeDescriptor = runtime.Texture is not null && IsTextureUsable(runtime.Texture)
                ? new IconDescriptor(
                    key,
                    GameContentKind.Character,
                    IconVariant.CharacterCompendiumPoolSmall,
                    runtime.Texture,
                    false,
                    "runtime-character-metadata:" + runtime.EvidenceCode)
                : Missing(key, "runtime-character-icon-unavailable:" + runtime.EvidenceCode);
            _cache[key] = runtimeDescriptor;
            return runtimeDescriptor;
        }

        // A Godot managed wrapper can become invalid independently of this
        // dictionary. Re-resolve from the stable resource path rather than
        // permanently poisoning the cache with a Missing descriptor.
        IconDescriptor refreshed = LoadDescriptor(key, path);
        _cache[key] = refreshed;
        if (!refreshed.IsMissing)
        {
            RuntimeLog.Detail(
                $"characterPoolIconReloaded=true;modelKey={key.Serialized};source=direct-static-resource;");
            return refreshed;
        }

        RuntimeLog.Detail(
            $"characterPoolIconInvalidFallback=true;modelKey={key.Serialized};evidence={refreshed.EvidenceCode};consumerSafe=true;");
        return refreshed;
    }

    public Texture2D? ResolveColorlessPoolVisual()
    {
        EnsureLoaded();
        if (_disposed)
        {
            return null;
        }

        if (_colorlessPoolVisual is not null && IsTextureUsable(_colorlessPoolVisual))
        {
            return _colorlessPoolVisual;
        }

        _colorlessPoolVisual = LoadTexture(ColorlessPoolTexturePath, out _colorlessPoolEvidence);
        if (_colorlessPoolVisual is not null)
        {
            RuntimeLog.Detail("characterPoolColorlessIconReloaded=true;source=direct-static-resource;");
        }
        return _colorlessPoolVisual;
    }

    /// <summary>
    /// Drops managed Resource wrappers so the next resolve re-reads the same
    /// stable native resource paths. No native Card Library node tree exists
    /// for this provider and no game-owned resource is explicitly freed.
    /// </summary>
    public void Invalidate()
    {
        if (_disposed)
        {
            return;
        }

        _loaded = false;
        _cache.Clear();
        _colorlessPoolVisual = null;
        _colorlessPoolEvidence = "character-card-library-colorless-pool-not-read";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loaded = false;
        _cache.Clear();
        _colorlessPoolVisual = null;
        _colorlessPoolEvidence = "character-card-library-provider-disposed";

        // Texture2D is a RefCounted Resource. Releasing our managed references
        // is sufficient; never Free() native/game-owned Resource objects.
        RuntimeLog.Detail(
            "characterPoolIconProviderDisposed=true;disposeCount=1;sourceRootFreed=false;resourceMode=direct-static;");
        GC.SuppressFinalize(this);
    }

    private void EnsureLoaded()
    {
        if (_loaded || _disposed)
        {
            return;
        }

        _loaded = true;
        _cache.Clear();
        foreach ((ModelKey key, string path) in NativeTexturePaths)
        {
            _cache[key] = LoadDescriptor(key, path);
        }

        _colorlessPoolVisual = LoadTexture(ColorlessPoolTexturePath, out _colorlessPoolEvidence);

        int textureCount = _colorlessPoolVisual is null ? 0 : 1;
        foreach ((ModelKey key, IconDescriptor descriptor) in _cache)
        {
            if (!descriptor.IsMissing && descriptor.Texture is not null)
            {
                textureCount++;
            }
            if (descriptor.IsMissing || SearchOperationalLogPolicy.VerboseTraceEnabled)
            {
                RuntimeLog.Info(
                    $"characterCompendiumIconResolved={key.Serialized};missing={descriptor.IsMissing.ToString().ToLowerInvariant()};evidence={descriptor.EvidenceCode}");
            }
        }

        if (_colorlessPoolVisual is null || SearchOperationalLogPolicy.VerboseTraceEnabled)
        {
            RuntimeLog.Info(
                $"cardPickerAllCharactersVisualResolved={(_colorlessPoolVisual is not null).ToString().ToLowerInvariant()};" +
                $"resource={ColorlessPoolTexturePath};evidence={_colorlessPoolEvidence}");
        }

        RuntimeLog.Detail(
            $"characterPoolIconProviderLoaded=true;resourceMode=direct-static;textureCount={textureCount};");
    }

    private static IconDescriptor LoadDescriptor(ModelKey key, string path)
    {
        Texture2D? texture = LoadTexture(path, out string evidence);
        return texture is null
            ? Missing(key, evidence)
            : new IconDescriptor(
                key,
                GameContentKind.Character,
                IconVariant.CharacterCompendiumPoolSmall,
                texture,
                false,
                evidence);
    }

    private static Texture2D? LoadTexture(string path, out string evidence)
    {
        evidence = $"direct-static-resource:{path}";
        try
        {
            if (!ResourceLoader.Exists(path))
            {
                evidence = $"direct-static-resource-missing:{path}";
                return null;
            }

            Texture2D? texture = ResourceLoader.Load<Texture2D>(
                path,
                null,
                ResourceLoader.CacheMode.Reuse);
            if (texture is null || !IsTextureUsable(texture))
            {
                evidence = $"direct-static-resource-invalid:{path}";
                return null;
            }

            evidence = $"direct-static-resource:{texture.GetType().Name}:{path}";
            return texture;
        }
        catch (ObjectDisposedException)
        {
            evidence = $"direct-static-resource-disposed:{path}";
            return null;
        }
        catch (Exception ex)
        {
            evidence = $"direct-static-resource-load-failed:{path}:{ex.GetType().Name}";
            return null;
        }
    }

    private static bool IsTextureUsable(Texture2D texture)
    {
        try
        {
            return GodotObject.IsInstanceValid(texture);
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static IconDescriptor Missing(ModelKey key, string evidence) =>
        IconDescriptor.Missing(
            key,
            GameContentKind.Character,
            IconVariant.CharacterCompendiumPoolSmall,
            evidence);
}
