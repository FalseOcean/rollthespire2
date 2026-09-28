using System.Collections;
using System.Reflection;
using RolltheSpire2.Core.Identity;

namespace RolltheSpire2.Infrastructure.Snapshots;

/// <summary>
/// Main-thread presentation/identity discovery for the live runtime character catalog.
/// This deliberately accepts ModelDb entries implemented by third-party assemblies:
/// character identity discovery is not the same thing as prediction-behavior authority.
/// No runtime model object escapes this capture.
/// </summary>
internal sealed record RuntimeCharacterCatalogSnapshot(
    IReadOnlyList<ModelKey> CharactersInSourceOrder,
    bool IdentityCaptureComplete,
    string EvidenceCode)
{
    public IReadOnlyList<ModelKey> EffectiveCharacters => CharactersInSourceOrder.Count > 0
        ? CharactersInSourceOrder
        : BaseGameModelKeys.Characters.All;
}

internal static class RuntimeCharacterCatalogCapture
{
    private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    public static RuntimeCharacterCatalogSnapshot Capture()
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        try
        {
            Assembly? gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => string.Equals(
                    assembly.GetName().Name,
                    "sts2",
                    StringComparison.OrdinalIgnoreCase));
            Type? modelDb = gameAssembly?.GetType(
                "MegaCrit.Sts2.Core.Models.ModelDb",
                throwOnError: false,
                ignoreCase: false);
            object? raw = modelDb?.GetProperty("AllCharacters", PublicStatic)?.GetValue(null) ??
                          modelDb?.GetProperty("Characters", PublicStatic)?.GetValue(null);
            if (raw is not IEnumerable enumerable)
            {
                return Fallback("RuntimeCharacterCatalogMissing");
            }

            var output = new List<ModelKey>();
            var seen = new HashSet<ModelKey>(ModelKeyComparer.Instance);
            bool complete = true;
            int rawCount = 0;
            foreach (object? item in enumerable)
            {
                if (item is null)
                {
                    complete = false;
                    continue;
                }

                rawCount++;
                if (!TryModelKey(item, out ModelKey key) ||
                    !string.Equals(key.Category, BaseGameModelKeys.Categories.Character, StringComparison.Ordinal))
                {
                    complete = false;
                    continue;
                }

                if (seen.Add(key))
                {
                    output.Add(key);
                }
            }

            if (output.Count == 0)
            {
                return Fallback("RuntimeCharacterCatalogEmpty");
            }

            return new RuntimeCharacterCatalogSnapshot(
                output,
                complete && output.Count == rawCount,
                complete && output.Count == rawCount
                    ? "ModelDb.AllCharacters:runtime-modelkey-source-order"
                    : "ModelDb.AllCharacters:partial-runtime-modelkey-source-order");
        }
        catch (Exception ex)
        {
            return Fallback("RuntimeCharacterCatalogCaptureFailed:" + ex.GetType().Name);
        }
    }

    private static RuntimeCharacterCatalogSnapshot Fallback(string evidence) => new(
        BaseGameModelKeys.Characters.All,
        false,
        evidence + ":vanilla-fallback");

    private static bool TryModelKey(object model, out ModelKey key)
    {
        key = default;
        object? id;
        try
        {
            id = model.GetType().GetProperty("Id", PublicInstance)?.GetValue(model);
        }
        catch
        {
            return false;
        }

        if (id is null)
        {
            return false;
        }

        string category;
        string entry;
        try
        {
            category = id.GetType().GetProperty("Category", PublicInstance)?.GetValue(id)?.ToString() ?? string.Empty;
            entry = id.GetType().GetProperty("Entry", PublicInstance)?.GetValue(id)?.ToString() ?? string.Empty;
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(entry))
        {
            return false;
        }

        key = new ModelKey(category, entry);
        return key.IsValid;
    }
}
