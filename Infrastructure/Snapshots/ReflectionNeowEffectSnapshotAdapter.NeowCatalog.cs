using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
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

    internal static bool? CaptureVanillaNeowCatalogExact(bool modern113, out NeowOfferCatalog? captured, out string evidence)
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

            var patches = Harmony.GetAllPatchedMethods().ToHashSet();
            if (modern113)
            {
                // Initialization runs before the event. A lifecycle subscriber can
                // change the actual player's modifiers or the rewards stream even
                // when all the Neow option factories are unchanged.
                var initialize = patches.FirstOrDefault(m => m.DeclaringType?.FullName ==
                    "MegaCrit.Sts2.Core.Runs.RunManager" && m.Name == "InitializeNewRun");
                if (initialize != null && !CanReplayPatchedRunInitialization(initialize, patches))
                {
                    evidence = "runtime-neow-run-initialization-unresolved:" +
                        string.Join(",", Harmony.GetPatchInfo(initialize)?.Owners.AsEnumerable() ?? []);
                    return false;
                }
                var wrapper = ObserveAncientOptionWrapper(patches);
                if (!wrapper.CanReplay(neowType))
                {
                    evidence = "runtime-neow-generation-method-patched:" + (wrapper.UnknownReason.Length > 0
                        ? wrapper.UnknownReason : "RitsuRegistryHasApplicableNeowRule");
                    return false;
                }
            }
            if (patches.FirstOrDefault(m => m.DeclaringType?.FullName switch
            {
                "MegaCrit.Sts2.Core.Models.Events.Neow" => m.Name == "GenerateInitialOptions",
                _ when !modern113 => false,
                "MegaCrit.Sts2.Core.Models.AncientEventModel" => m.Name is "ReplaceNullOptions" or "RelicOption" or "get_DebugOption",
                "MegaCrit.Sts2.Core.Models.RelicModel" => m.Name is "IsAllowed" or "IsLateInAct3",
                "MegaCrit.Sts2.Core.Hooks.Hook" => m.Name == "ShouldAllowAncient",
                "MegaCrit.Sts2.Core.Random.Rng" => m.Name is "NextBool" or "NextItem" or "NextInt" or "NextUnsignedLong",
                "MegaCrit.Sts2.Core.Extensions.ListExtensions" => m.Name == "UnstableShuffle",
                _ => false
            }) is { } blocked)
            {
                evidence = $"runtime-neow-generation-method-patched:{blocked.DeclaringType?.FullName}.{blocked.Name}:" +
                    string.Join(",", Harmony.GetPatchInfo(blocked)?.Owners.AsEnumerable() ?? []);
                return false;
            }
            if (modern113 && (!HasStaticNeowList(neowType, "PositiveOptions", patches) ||
                !HasStaticNeowList(neowType, "CurseOptions", patches)))
            { evidence = "runtime-neow-option-getter-behavior-unproven"; return false; }
            if (!TryReadNeowOptionRelics(neow, "PositiveOptions", modern113, patches, out ModelKey[] positives) ||
                !TryReadNeowOptionRelics(neow, "CurseOptions", modern113, patches, out ModelKey[] curses))
            {
                evidence="runtime-neow-option-members-unavailable";
                return null;
            }

            ModelKey[] expectedPositives = ModernNeowIdentityRules.PositiveCandidates.ToArray();
            ModelKey[] expectedCurses = ModernNeowIdentityRules.CurseCandidates.ToArray();
            bool exact = positives.SequenceEqual(expectedPositives) && curses.SequenceEqual(expectedCurses);
            if (modern113)
            {
                foreach (var (member, key) in new[]
                {
                    ("LavaRockOption", BaseGameModelKeys.Relics.LavaRock), ("SmallCapsuleOption", BaseGameModelKeys.Relics.SmallCapsule),
                    ("NutritiousOysterOption", BaseGameModelKeys.Relics.NutritiousOyster), ("StoneHumidifierOption", BaseGameModelKeys.Relics.StoneHumidifier),
                    ("NeowsTalismanOption", BaseGameModelKeys.Relics.NeowsTalisman), ("PomanderOption", BaseGameModelKeys.Relics.Pomander)
                })
                {
                    if (AccessTools.PropertyGetter(neowType, member) is not { } getter || patches.Contains(getter) ||
                        !TryReadNeowRelic(ReadAnyMember(neow, member), true, patches, out var actual) || actual != key)
                    { evidence = "runtime-neow-binary-option-unresolved:" + member; return false; }
                }
                if (!exact || patches.Any(m => m.DeclaringType == neowType && m.Name == "get_AllPossibleOptions"))
                {
                    ModelKey[] all = [];
                    bool allKnown = HasStaticNeowList(neowType, "AllPossibleOptions", patches) &&
                        TryReadNeowOptionRelics(neow, "AllPossibleOptions", true, patches, out all);
                    captured = new(positives.ToImmutableArray(), curses.ToImmutableArray(),
                        allKnown ? all.ToImmutableArray() : null);
                }
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

    // A canonical Neow has no player or event RNG. Matching its list proves live
    // order only for an unmodified getter or a context-independent transformation.
    internal static bool HasStaticNeowList(Type neowType, string member, HashSet<MethodBase> patches)
    {
        var getter = AccessTools.PropertyGetter(neowType, member);
        if (getter == null) return false;
        if (!patches.Contains(getter)) return true;
        var info = Harmony.GetPatchInfo(getter);
        return info != null && info.Prefixes.Count == 0 && info.Transpilers.Count == 0 && info.Finalizers.Count == 0 &&
            info.InnerPrefixes.Count == 0 && info.InnerPostfixes.Count == 0 &&
            info.Postfixes.All(patch => IsStaticNeowListPostfix(patch.PatchMethod, patches));
    }

    private static bool IsStaticNeowListPostfix(MethodInfo patch, HashSet<MethodBase> patches)
    {
        if (!patch.IsStatic || patch.ReturnType != typeof(void) || patches.Contains(patch) ||
            patch.GetMethodBody() is not { ExceptionHandlingClauses.Count: 0 }) return false;
        var parameters = patch.GetParameters();
        if (!parameters.Any(p => p.Name == "__result") || parameters.Any(p => p.Name switch
        {
            "__result" => p.ParameterType != typeof(IEnumerable<EventOption>).MakeByRefType(),
            "__instance" => p.ParameterType != typeof(Neow),
            _ => true
        })) return false;

        // Recognize straight-line sequence composition and the native relic option
        // factory. Branches, fields, delegate bodies, arbitrary helpers and RNG
        // never qualify, even if the canonical result happens to equal vanilla.
        return PatchProcessor.GetOriginalInstructions(patch).All(instruction =>
        {
            var op = instruction.opcode;
            if (op == OpCodes.Nop || op == OpCodes.Ret || op == OpCodes.Ldarg_0 || op == OpCodes.Ldarg_1 ||
                op == OpCodes.Ldind_Ref || op == OpCodes.Stind_Ref || op == OpCodes.Ldstr) return true;
            if (!(op == OpCodes.Call || op == OpCodes.Callvirt) || instruction.operand is not MethodInfo method ||
                patches.Contains(method) || method.IsGenericMethod && patches.Contains(method.GetGenericMethodDefinition())) return false;
            if (method.DeclaringType == typeof(Enumerable) && method.IsGenericMethod &&
                method.GetGenericArguments() is [var item] && item == typeof(EventOption))
                return method.Name is "Reverse" or "Append" or "Prepend" or "Concat" or "ToArray" or "ToList";
            if (method.DeclaringType == typeof(ModelDb) && method.Name == "Relic" && method.IsGenericMethod &&
                method.GetGenericArguments() is [var relic] && relic.Assembly == typeof(RelicModel).Assembly &&
                method.GetParameters().Length == 0) return true;
            if (method.DeclaringType == typeof(RelicModel) && method.Name == "ToMutable" &&
                method.GetParameters().Length == 0) return true;
            if (method.DeclaringType == typeof(AncientEventModel) && method.Name == "RelicOption" &&
                method.IsGenericMethod && method.GetGenericArguments() is [var native] &&
                native.Assembly == typeof(RelicModel).Assembly && method.GetParameters() is [var page] &&
                page.ParameterType == typeof(string)) return true;
            // An external C# mod can bind the protected native factory by signature.
            return method.IsStatic && method.GetMethodBody() == null && method.ReturnType == typeof(EventOption) &&
                method.GetCustomAttribute<UnsafeAccessorAttribute>() is { Kind: UnsafeAccessorKind.Method, Name: "RelicOption" } &&
                method.GetParameters().Select(p => p.ParameterType).SequenceEqual(
                    new[] { typeof(AncientEventModel), typeof(RelicModel), typeof(string) });
        });
    }

    private static bool TryReadNeowOptionRelics(object neow, string memberName, bool modern113,
        HashSet<MethodBase> patches, out ModelKey[] keys)
    {
        keys = Array.Empty<ModelKey>();
        object? value = ReadAnyMember(neow, memberName);
        if (value is not IEnumerable enumerable || value is string) return false;

        var output = new List<ModelKey>();
        foreach (object? item in enumerable)
        {
            if (!TryReadNeowRelic(item, modern113, patches, out ModelKey key)) return false;
            output.Add(key);
        }
        keys = output.ToArray();
        return true;
    }

    private static bool TryReadNeowRelic(object? option, bool modern113, HashSet<MethodBase> patches, out ModelKey key)
    {
        key = default;
        if (option == null || (ReadAnyMember(option, "Relic") ?? ReadAnyMember(option, "RelicModel")) is not { } relic ||
            !TryModelKey(relic, out key) || key.Category != BaseGameModelKeys.Categories.Relic) return false;
        if (!modern113) return true;
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
        foreach (string name in new[] { "IsAllowedAtNeow", "IsAllowedInternal" })
        {
            var rule = type.GetMethod(name, flags);
            if (rule == null || patches.Contains(rule)) return false;
            if (rule.DeclaringType == typeof(MegaCrit.Sts2.Core.Models.RelicModel)) continue;
            if (rule.DeclaringType?.Assembly != typeof(MegaCrit.Sts2.Core.Models.RelicModel).Assembly ||
                rule.DeclaringType.Name is not ("Kaleidoscope" or "ScrollBoxes" or "MassiveScroll" or "WingedBoots" or
                    "SilverCrucible" or "PotionBelt")) return false;
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
