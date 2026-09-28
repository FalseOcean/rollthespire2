using System.Collections;
using System.Reflection;
using Godot;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Infrastructure.Snapshots;

namespace RolltheSpire2.Ui.Icons;

internal enum EventThumbnailKind
{
    DefaultPortrait,
    AncientMapIcon,
    Fallback
}

internal sealed record EventThumbnailDescriptor(
    ModelKey EventKey,
    EventThumbnailKind Kind,
    Texture2D? Texture,
    string EvidenceCode)
{
    public bool HasTexture => Texture is not null && GodotObject.IsInstanceValid(Texture);
}

/// <summary>
/// Main-thread-only Event visual authority. Default-layout events use the audited
/// Vanilla portrait contract (res://images/events/{EventModel.Id.Entry}.png).
/// Ancient events may use their direct MapIcon. Combat/custom/unknown layouts and
/// missing resources intentionally return a generic presentation fallback.
/// No Event scene is instantiated here.
/// </summary>
internal sealed class EventThumbnailProvider
{
    private readonly Dictionary<ModelKey, EventThumbnailDescriptor> _cache = new(ModelKeyComparer.Instance);
    private readonly Type? _modelDb;

    public EventThumbnailProvider()
    {
        Assembly? gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(
                assembly.GetName().Name,
                "sts2",
                StringComparison.OrdinalIgnoreCase));
        _modelDb = gameAssembly?.GetType(
            "MegaCrit.Sts2.Core.Models.ModelDb",
            throwOnError: false,
            ignoreCase: false);
    }

    public EventThumbnailDescriptor Resolve(ModelKey eventKey)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (_cache.TryGetValue(eventKey, out EventThumbnailDescriptor? cached))
        {
            return cached;
        }

        EventThumbnailDescriptor resolved = ResolveUncached(eventKey);
        _cache[eventKey] = resolved;
        RuntimeLog.Detail(
            $"ui1EventThumbnailResolved={eventKey.Serialized};kind={resolved.Kind};hasTexture={resolved.HasTexture.ToString().ToLowerInvariant()};evidence={resolved.EvidenceCode}");
        return resolved;
    }

    private EventThumbnailDescriptor ResolveUncached(ModelKey eventKey)
    {
        if (!eventKey.IsValid || _modelDb is null)
        {
            return Fallback(eventKey, "event-thumbnail-modeldb-unavailable");
        }

        object? model = ResolveCatalogEvent(eventKey.Entry);
        if (model is null)
        {
            return Fallback(eventKey, "event-thumbnail-catalog-model-not-found");
        }

        string layout = ReadLayoutType(model);
        if (string.Equals(layout, "Default", StringComparison.OrdinalIgnoreCase))
        {
            string path = $"res://images/events/{eventKey.Entry.ToLowerInvariant()}.png";
            try
            {
                if (!ResourceLoader.Exists(path))
                {
                    return Fallback(eventKey, "event-thumbnail-default-portrait-missing:" + path);
                }

                Texture2D? texture = ResourceLoader.Load<Texture2D>(
                    path,
                    null,
                    ResourceLoader.CacheMode.Reuse);
                return texture is not null && GodotObject.IsInstanceValid(texture)
                    ? new EventThumbnailDescriptor(
                        eventKey,
                        EventThumbnailKind.DefaultPortrait,
                        texture,
                        "vanilla-default-event-portrait:" + path)
                    : Fallback(eventKey, "event-thumbnail-default-portrait-load-failed:" + path);
            }
            catch (Exception ex)
            {
                return Fallback(eventKey, "event-thumbnail-default-portrait-exception:" + ex.GetType().Name);
            }
        }

        if (string.Equals(layout, "Ancient", StringComparison.OrdinalIgnoreCase) &&
            TryReadTexture(model, "MapIcon", out Texture2D? ancientIcon) &&
            ancientIcon is not null && GodotObject.IsInstanceValid(ancientIcon))
        {
            return new EventThumbnailDescriptor(
                eventKey,
                EventThumbnailKind.AncientMapIcon,
                ancientIcon,
                "vanilla-ancient-map-icon");
        }

        return Fallback(
            eventKey,
            string.IsNullOrWhiteSpace(layout)
                ? "event-thumbnail-layout-unknown"
                : "event-thumbnail-no-direct-texture-for-layout:" + layout);
    }

    private object? ResolveCatalogEvent(string entry)
    {
        PropertyInfo? allEvents = _modelDb?.GetProperty(
            "AllEvents",
            BindingFlags.Public | BindingFlags.Static);
        if (allEvents is null)
        {
            return null;
        }

        try
        {
            if (allEvents.GetValue(null) is not IEnumerable models)
            {
                return null;
            }

            foreach (object? model in models)
            {
                if (model is null) continue;
                PropertyInfo? idProperty = model.GetType().GetProperty(
                    "Id",
                    BindingFlags.Public | BindingFlags.Instance);
                object? id = idProperty?.GetValue(model);
                PropertyInfo? entryProperty = id?.GetType().GetProperty(
                    "Entry",
                    BindingFlags.Public | BindingFlags.Instance);
                string? modelEntry = entryProperty?.GetValue(id)?.ToString();
                if (string.Equals(modelEntry, entry, StringComparison.OrdinalIgnoreCase))
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

    private static string ReadLayoutType(object model)
    {
        try
        {
            PropertyInfo? property = model.GetType().GetProperty(
                "LayoutType",
                BindingFlags.Public | BindingFlags.Instance);
            return property?.GetValue(model)?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool TryReadTexture(object model, string memberName, out Texture2D? texture)
    {
        texture = null;
        try
        {
            PropertyInfo? property = model.GetType().GetProperty(
                memberName,
                BindingFlags.Public | BindingFlags.Instance);
            if (property is not null &&
                property.GetIndexParameters().Length == 0 &&
                typeof(Texture2D).IsAssignableFrom(property.PropertyType))
            {
                texture = property.GetValue(model) as Texture2D;
                return texture is not null;
            }

            FieldInfo? field = model.GetType().GetField(
                memberName,
                BindingFlags.Public | BindingFlags.Instance);
            if (field is not null && typeof(Texture2D).IsAssignableFrom(field.FieldType))
            {
                texture = field.GetValue(model) as Texture2D;
                return texture is not null;
            }
        }
        catch
        {
            texture = null;
        }
        return false;
    }

    private static EventThumbnailDescriptor Fallback(ModelKey key, string evidence) =>
        new(key, EventThumbnailKind.Fallback, null, evidence);
}
