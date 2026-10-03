using System.Collections;
using System.Reflection;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.Neow;

namespace RolltheSpire2.Infrastructure.Snapshots;

internal static partial class ReflectionNeowEffectSnapshotAdapter
{
    /// <summary>
    /// Proves whether the live Neow positive/curse option arrays still match the
    /// source-audited Modern catalog. This authority is independent of which
    /// CharacterModel is selected: a sixth character does not itself modify Neow.
    /// A changed/extended Neow option list returns false; reflection uncertainty returns null.
    /// </summary>
    internal static bool? CaptureVanillaNeowCatalogExact(out string evidence)
    {
        evidence = "runtime-neow-catalog-unavailable";
        try
        {
            Assembly? gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, "sts2", StringComparison.OrdinalIgnoreCase));
            if (gameAssembly is null) return null;

            Type? neowType = FindType(gameAssembly, "MegaCrit.Sts2.Core.Models.Events.Neow", "Neow");
            Type? modelDb = FindType(gameAssembly, "MegaCrit.Sts2.Core.Models.ModelDb", "ModelDb");
            if (neowType is null || modelDb is null) { evidence="runtime-neow-model-type-unavailable"; return null; }

            MethodInfo? eventMethod = modelDb.GetMethods(PublicStatic)
                .FirstOrDefault(method =>
                    string.Equals(method.Name, "Event", StringComparison.Ordinal) &&
                    method.IsGenericMethodDefinition &&
                    method.GetGenericArguments().Length == 1 &&
                    method.GetParameters().Length == 0);
            if (eventMethod is null) { evidence="runtime-neow-event-factory-unavailable"; return null; }

            object? neow = eventMethod.MakeGenericMethod(neowType).Invoke(null, null);
            if (neow is null) { evidence="runtime-neow-model-unavailable"; return null; }

            if (!TryReadNeowOptionRelics(neow, "PositiveOptions", out ModelKey[] positives) ||
                !TryReadNeowOptionRelics(neow, "CurseOptions", out ModelKey[] curses))
            {
                evidence="runtime-neow-option-members-unavailable";
                return null;
            }

            ModelKey[] expectedPositives = ModernNeowIdentityPredictor.BasePositivePoolForAuthority.ToArray();
            ModelKey[] expectedCurses = ModernNeowIdentityPredictor.BaseCursePoolForFiltering.ToArray();
            bool exact = positives.SequenceEqual(expectedPositives) && curses.SequenceEqual(expectedCurses);
            evidence = $"runtime-neow-catalog:{(exact ? "vanilla-exact" : "changed")};positive={positives.Length};curse={curses.Length}";
            return exact;
        }
        catch(Exception ex)
        {
            evidence="runtime-neow-catalog-capture-failed:"+ex.GetBaseException().GetType().Name+":"+ex.GetBaseException().Message;
            return null;
        }
    }

    private static bool TryReadNeowOptionRelics(object neow, string memberName, out ModelKey[] keys)
    {
        keys = Array.Empty<ModelKey>();
        object? value = ReadAnyMember(neow, memberName);
        if (value is not IEnumerable enumerable || value is string) return false;

        var output = new List<ModelKey>();
        foreach (object? item in enumerable)
        {
            if (item is null) return false;
            object? relic = ReadAnyMember(item, "Relic") ?? ReadAnyMember(item, "RelicModel") ?? item;
            object? canonical = ReadAnyMember(relic, "CanonicalInstance") ?? relic;
            if (!TryModelKey(canonical, out ModelKey key) || key.Category != BaseGameModelKeys.Categories.Relic)
            {
                return false;
            }
            output.Add(key);
        }
        keys = output.ToArray();
        return keys.Length > 0;
    }

    private static object? ReadAnyMember(object instance, string name)
    {
        Type type = instance.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        try
        {
            PropertyInfo? property = type.GetProperty(name, flags);
            if (property is not null && property.GetIndexParameters().Length == 0)
                return property.GetValue(property.GetMethod?.IsStatic == true ? null : instance);
            FieldInfo? field = type.GetField(name, flags);
            return field?.GetValue(field.IsStatic ? null : instance);
        }
        catch { return null; }
    }
}
