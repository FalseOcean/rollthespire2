using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using MegaCrit.Sts2.Core.Models;
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
    internal static bool? CaptureVanillaNeowCatalogExact(out string evidence) =>
        CaptureVanillaNeowCatalogExact(false, out _, out evidence);

    internal static bool? CaptureVanillaNeowCatalogExact(bool readOfferCatalog, out NeowOfferCatalog? captured, out string evidence)
    {
        captured = null;
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

            // Read the ordered inputs on a detached event of the same kind the
            // game uses to produce its offers. There is no run, owner or event
            // RNG attached. Generation itself belongs to the prediction model.
            object canonical = neow;
            if (readOfferCatalog && neow is EventModel model) neow = model.ToMutable();
            var unknownEligibility = new HashSet<ModelKey>();
            if (!TryReadNeowOptionRelics(neow, "PositiveOptions", readOfferCatalog, unknownEligibility, out ModelKey[] positives) ||
                !TryReadNeowOptionRelics(neow, "CurseOptions", readOfferCatalog, unknownEligibility, out ModelKey[] curses))
            {
                evidence="runtime-neow-option-members-unavailable";
                return null;
            }

            ModelKey[] expectedPositives = ModernNeowIdentityRules.PositiveCandidates.ToArray();
            ModelKey[] expectedCurses = ModernNeowIdentityRules.CurseCandidates.ToArray();
            bool exact = positives.SequenceEqual(expectedPositives) && curses.SequenceEqual(expectedCurses);
            if (readOfferCatalog)
            {
                var binary = ImmutableArray.CreateBuilder<ModernNeowBinaryChoice>(3);
                foreach (var (whenTrue, whenFalse, suppressed) in new[]
                {
                    ("LavaRockOption", "SmallCapsuleOption", (ModelKey?)BaseGameModelKeys.Relics.LargeCapsule),
                    ("NutritiousOysterOption", "StoneHumidifierOption", null),
                    ("NeowsTalismanOption", "PomanderOption", null)
                })
                {
                    if (!TryReadNeowRelic(ReadAnyMember(neow, whenTrue), true, unknownEligibility, out var a) ||
                        !TryReadNeowRelic(ReadAnyMember(neow, whenFalse), true, unknownEligibility, out var b))
                    { evidence = "runtime-neow-binary-option-unresolved:" + whenTrue + "/" + whenFalse; return null; }
                    binary.Add(new(a, b, suppressed));
                }
                var binaryChoices = binary.MoveToImmutable();
                exact &= binaryChoices.SequenceEqual(ModernNeowIdentityRules.BinaryChoices);
                // Bones has its own ordered source, including when the opening
                // positive and curse lists happen to match the native lists.
                bool allKnown = TryReadNeowOptionRelics(canonical, "AllPossibleOptions", true, unknownEligibility, out var all);
                if (!exact || !allKnown || !all.SequenceEqual(BaseGameModelKeys.Relics.AllNeow) || unknownEligibility.Count != 0)
                    captured = new(positives.ToImmutableArray(), curses.ToImmutableArray(), allKnown ? all.ToImmutableArray() : null)
                    {
                        BinaryChoices = binaryChoices,
                        UnresolvedEligibility = unknownEligibility.OrderBy(k => k.Serialized, StringComparer.Ordinal).ToImmutableArray()
                    };
            }
            evidence = $"runtime-neow-catalog:{(exact ? "vanilla-exact" : "changed")};positive={positives.Length};curse={curses.Length}";
            return exact;
        }
        catch(Exception ex)
        {
            RecordCaptureFailure(ex, "Neow.OptionCatalog");
            evidence="runtime-neow-catalog-capture-failed:"+ex.GetBaseException().GetType().Name+":"+ex.GetBaseException().Message;
            return null;
        }
    }

    internal static bool TryReadNeowOptionRelics(object neow, string memberName, bool readOfferCatalog,
        HashSet<ModelKey> unknownEligibility, out ModelKey[] keys)
    {
        keys = Array.Empty<ModelKey>();
        object? value = ReadAnyMember(neow, memberName);
        if (value is not IEnumerable enumerable || value is string) return false;

        try
        {
            var output = new List<ModelKey>();
            foreach (object? item in enumerable)
            {
                if (!TryReadNeowRelic(item, readOfferCatalog, unknownEligibility, out ModelKey key)) return false;
                output.Add(key);
            }
            keys = output.ToArray();
            return true;
        }
        catch (Exception ex)
        {
            // Some getters defer their owner-dependent work until enumeration.
            // An unreadable Bones source does not erase the opening offer pools.
            RecordCaptureFailure(ex, "Neow." + memberName);
            return false;
        }
    }

    private static bool TryReadNeowRelic(object? option, bool readOfferCatalog, HashSet<ModelKey> unknownEligibility, out ModelKey key)
    {
        key = default;
        if (option == null || (ReadAnyMember(option, "Relic") ?? ReadAnyMember(option, "RelicModel")) is not { } relic ||
            !TryModelKey(relic, out key) || key.Category != BaseGameModelKeys.Categories.Relic) return false;
        if (!readOfferCatalog) return true;
        // Native conflicts use C# type checks. A replacement that reuses a
        // special key cannot inherit those exclusions by spelling alone.
        var type = relic.GetType();
        var candidateKey = key;
        if (ModernNeowIdentityRules.PositiveCandidates.Contains(key) || ModernNeowIdentityRules.CurseCandidates.Contains(key) ||
            ModernNeowIdentityRules.BinaryChoices.Any(c => c.TrueCandidate == candidateKey || c.FalseCandidate == candidateKey))
        {
            if (type.Assembly != typeof(MegaCrit.Sts2.Core.Models.RelicModel).Assembly ||
                type.Namespace != "MegaCrit.Sts2.Core.Models.Relics") return false;
            var nativeId = MegaCrit.Sts2.Core.Models.ModelDb.GetId(type);
            if (new ModelKey(nativeId.Category, nativeId.Entry) != key) return false;
        }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        // 0.111 delegates to virtual IsAllowed. 0.113 delegates to
        // IsAllowedInternal after the standard run-mode checks.
        string eligibilityMethod = typeof(RelicModel).GetMethod("IsAllowedInternal", flags) != null
            ? "IsAllowedInternal" : "IsAllowed";
        foreach (string name in new[] { "IsAllowedAtNeow", eligibilityMethod })
        {
            var rule = type.GetMethod(name, flags);
            if (rule?.DeclaringType == typeof(MegaCrit.Sts2.Core.Models.RelicModel)) continue;
            if (rule?.DeclaringType?.Assembly != typeof(MegaCrit.Sts2.Core.Models.RelicModel).Assembly ||
                rule.DeclaringType.Name is not ("Kaleidoscope" or "ScrollBoxes" or "MassiveScroll" or "WingedBoots" or
                    "SilverCrucible" or "PotionBelt")) unknownEligibility.Add(key);
        }
        return true;
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
        catch (Exception ex)
        {
            RecordCaptureFailure(ex, instance.GetType().FullName + "." + name);
            return null;
        }
    }
}
