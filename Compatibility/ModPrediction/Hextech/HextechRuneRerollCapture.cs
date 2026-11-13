using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

namespace RolltheSpire2.Compatibility.ModPrediction.Hextech;

internal static class HextechRuneRerollCapture
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    internal static bool IsScreen(Control owner) => owner.GetType().FullName == "HextechRunes.HextechRuneSelectionScreen" &&
        owner.GetType().Assembly.GetName().Name == "HextechRunes";
    internal static bool IsReroll(Control owner) => Find(owner) is not null;
    private static (Control Screen, int Slot, bool Enemy)? Find(Control owner)
    {
        for (Node? node = owner.GetParent(); node != null; node = node.GetParent())
        {
            if (node is not Control screen || !IsScreen(screen)) continue;
            foreach (bool enemy in new[] { false, true })
            {
                var buttons = (IList)Read(screen, enemy ? "_enemyHexRerollButtons" : "_rerollButtons")!;
                for (int slot = 0; slot < buttons.Count; slot++)
                    if (ReferenceEquals(buttons[slot], owner)) return (screen, slot, enemy);
            }
            return null;
        }
        return null;
    }

    internal static bool TryMatch(Control owner, out RunState? run, out ModelKey source, out long identity)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        run = null; source = default; identity = 0;
        if (Find(owner) is not { } match || owner is not BaseButton { Disabled: false } || !owner.IsVisibleInTree() || !match.Screen.IsVisibleInTree() ||
            !ReferenceEquals(NOverlayStack.Instance?.Peek(), match.Screen) || NMapScreen.Instance?.IsOpen == true ||
            NCapstoneContainer.Instance?.InUse == true || NModalContainer.Instance?.OpenModal != null) return false;
        run = OperationSurfaceHoverCapture.CurrentRun();
        if (run == null || LocalContext.GetMe(run) == null) return false;
        var relics = (IEnumerable)Read(match.Screen, "_relics")!;
        var options = relics.Cast<RelicModel>().ToArray();
        if (match.Enemy)
        {
            var kind = ((IList)Read(match.Screen, "_monsterHexKinds")!)[match.Slot];
            if (kind == null) return false;
            var coordinator = match.Screen.GetType().Assembly.GetType("HextechRunes.HextechRuneSelectionCoordinator", true)!;
            source = Key(ModelDb.GetById<RelicModel>((ModelId)Invoke(coordinator, "GetMonsterHexIconRelicId", kind)!));
        }
        else source = match.Slot < options.Length ? Key(options[match.Slot]) : default;
        identity = checked((long)owner.GetInstanceId());
        return match.Enemy || match.Slot < options.Length;
    }

    internal static HextechRuneRerollSnapshot Capture(Control owner, RunState run)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        try { return CaptureCore(owner, run); }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    private static HextechRuneRerollSnapshot CaptureCore(Control owner, RunState run)
    {
        if (!TryMatch(owner, out var current, out var source, out _) || !ReferenceEquals(current, run))
            throw new InvalidOperationException("HextechRuneRerollScreenChanged");
        var (screen, slot, enemy) = Find(owner)!.Value;
        var assembly = screen.GetType().Assembly;
        Type Mod(string name) => assembly.GetType("HextechRunes." + name, true)!;
        var coordinator = Mod("HextechRuneSelectionCoordinator");
        var builder = Mod("HextechRunePoolBuilder");
        var player = LocalContext.GetMe(run)!;
        var callback = Read(screen, enemy ? "_enemyHexRerollFunc" : "_rerollFunc") as Delegate;
        bool locked = (bool)Read(screen, "_choiceLocked")!;
        int limit = (int)Read(screen, enemy ? "_enemyHexRerollLimit" : "_playerRuneRerollLimit")!;
        var capturedCounts = ((IEnumerable)Read(screen, enemy ? "_enemyHexRerollCounts" : "_playerRuneRerollCounts")!).Cast<int>().ToArray();
        bool multiplayer = enemy ? (bool)Invoke(Mod("HextechPlayerContextHelper"), "IsNetworkMultiplayerRun")! :
            callback != null && Calls(callback.Method, coordinator, "RerollSingleOptionAndTrackMultiplayer");
        int stage = run.CurrentActIndex, rarity = 0;
        int ordinal = enemy ? 0 : ((ICollection)Read(screen, "_rerollHistory")!).Count;
        string playerKey = player.NetId != 0 ? "net:" + player.NetId : "slot:" + run.GetPlayerSlotIndex(player);
        var pools = ImmutableArray.CreateBuilder<HextechRuneRerollPool>();
        var seen = ImmutableHashSet<ModelKey>.Empty;
        int weight = 150; bool hasCharacter = false, chaos = false;
        int? stageRarity = null, noiseSlot = null;
        var goldenSession = Read(screen, "_goldenRerollSession");
        bool golden = !enemy && goldenSession != null && (bool)Read(goldenSession, "IsActive")!;
        int goldenRarity = goldenSession == null ? 0 : Convert.ToInt32(Read(goldenSession, "UpgradedRarity"));
        if (!enemy && Read(screen, "_signalNoise") is { } noiseState) noiseSlot = (int)Read(noiseState, "Slot")!;
        HextechRuneRerollOption Option(RelicModel model) => new(Key(model),
            Convert.ToInt32(Invoke(builder, "GetRarityForOptions", new object[] { new RelicModel[] { model } })),
            (bool)Invoke(builder, "IsUpgradeRune", model)!, (bool)Invoke(builder, "IsGuaranteedFirstPickRune", model)!);
        HextechRuneRerollOption EnemyOption(object? kind)
        {
            if (kind == null) return new(default, rarity, false, false);
            var model = ModelDb.GetById<RelicModel>((ModelId)Invoke(coordinator, "GetMonsterHexIconRelicId", kind)!);
            return new(Key(model), rarity, false, false, Convert.ToInt32(kind));
        }
        var panel = enemy ? ((IEnumerable)Read(screen, "_monsterHexKinds")!).Cast<object?>().Select(EnemyOption).ToImmutableArray() :
            ((IEnumerable)Read(screen, "_relics")!).Cast<RelicModel>().Select(Option).ToImmutableArray();
        // Native clicks index these arrays directly; missing entries cannot be
        // interpreted as unused charges in a detached continuation.
        if (capturedCounts.Length != panel.Length || slot < 0 || slot >= panel.Length)
            throw new InvalidOperationException("HextechRuneRerollPanelCountMismatch");
        var counts = capturedCounts.ToImmutableArray();
        if (enemy) ordinal = counts[slot];
        if (callback != null)
        {
            ValidateCallback(callback, coordinator, enemy);
            var closure = Closure(callback.Target!, assembly);
            if (enemy ? !ReferenceEquals(FindField(closure, "runState"), run) : !ReferenceEquals(FindField(closure, "player"), player))
                throw new InvalidOperationException("HextechRuneRerollPlayerMismatch");
            stage = (int)FindField(closure, "actIndex")!;
            var modifier = FindField(closure, "modifier") ?? throw new InvalidOperationException("HextechRuneRerollModifierMissing");
            if (enemy)
            {
                object nativeRarity = FindField(closure, "rarity")!;
                rarity = Convert.ToInt32(nativeRarity);
                panel = panel.Select(option => option with { Rarity = rarity }).ToImmutableArray();
                seen = ((IEnumerable)FindField(closure, "seenEnemyHexes")!).Cast<object>().Select(EnemyOption).Select(option => option.Key).ToImmutableHashSet();
                var pool = ((IEnumerable)Invoke(Mod("HextechMonsterHexRoller"), "GetConfiguredRarityPool", nativeRarity,
                    Read(modifier, "DisabledMonsterHexIdsForPool"))!).Cast<object>().Select(EnemyOption)
                    .Select(option => new HextechRuneRerollPoolCandidate(option, 1, false)).ToImmutableArray();
                pools.Add(new(rarity, pool));
            }
            else
            {
                if (!multiplayer && !Calls(callback.Method, coordinator, "RerollSingleOptionAndTrack"))
                    throw new NotSupportedException("HextechRuneRerollCallbackNotAudited");
                seen = ((IEnumerable)FindField(closure, "seenOptionIds")!).Cast<ModelId>().Select(id => new ModelKey(id.Category, id.Entry)).ToImmutableHashSet();
                if (FindField(closure, "stageRarity") is { } stageValue) stageRarity = Convert.ToInt32(stageValue);
                weight = (int)Invoke(Mod("HextechWeightedRuneOptions"), "GetWeight", Read(screen, "_relics"))!;
                bool endless = (bool)Read(modifier, "IsEndlessLoopActive")!;
                object? character = Invoke(builder, "GetRuneCharacterId", player);
                hasCharacter = character != null;
                var tags = Invoke(builder, "BuildOwnedRuneTagCounts", player, endless)!;
                bool availableChaos = (bool)Read(Mod("HextechRuneGeneration"), "ChaosAvailable")!;
                var config = InvokeInstance(modifier.GetType(), "GetEffectiveRunConfigurationSnapshot", modifier)!;
                chaos = availableChaos && (bool)Read(modifier, "IsModActiveForRun")! && (int)Read(config, "ChaosRuneChancePercent")! > 0;
                var needed = panel.Select(option => option.Rarity).ToHashSet();
                if (golden) needed.Add(goldenRarity);
                if (stageRarity is { } initialStage && panel.Any(option => option.Guaranteed)) needed.Add(initialStage);
                var captured = new HashSet<int>();
                while (needed.Except(captured).Order().FirstOrDefault(-1) is int nextRarity && nextRarity >= 0)
                {
                    captured.Add(nextRarity);
                    var nativeRarity = Enum.ToObject(Mod("HextechRarityTier"), nextRarity);
                    try
                    {
                        AuditAvailability(assembly, builder, nativeRarity);
                        // These audited pools exclude owned/forbidden identities,
                        // but deliberately retain current and seen identities:
                        // each detached step must apply its own changing context.
                        var models = (List<RelicModel>)Invoke(builder, "BuildSelectableRunePool", player, nativeRarity, run, null)!;
                        object?[] args = [models, tags, endless, 0];
                        var tagWeights = (List<int>)Method(builder, "BuildRuneTagWeights", 4).Invoke(null, args)!;
                        var definitions = models.Select((model, i) => new HextechRuneRerollPoolCandidate(Option(model), tagWeights[i],
                            (bool)Invoke(builder, "IsRuneForCharacter", model, character)!)).ToImmutableArray();
                        pools.Add(new(nextRarity, definitions));
                        if (stageRarity is { } requiredStage && definitions.Any(c => c.Option.Guaranteed)) needed.Add(requiredStage);
                    }
                    catch (Exception error)
                    {
                        // An unobserved future rarity limits only routes that
                        // actually draw from it. Preserve the original cause.
                        if (error is TargetInvocationException { InnerException: { } cause }) error = cause;
                        RolltheSpire2.Bootstrap.RuntimeLog.Fault("operationPredictionFailed=true;stage=hextech-reroll-pool-capture;rarity=" + nextRarity +
                            ";player=" + playerKey + ";act=" + stage + ";source=" + source, error);
                        pools.Add(new(nextRarity, [], "HextechRuneRerollPoolUnavailable"));
                    }
                }
            }
        }
        var niche = !enemy && !multiplayer ? CrystalSphereLiveCapture.RngState(PredictorStream.Niche, run.Rng.Niche) : null;
        var fullState = new HextechRuneRerollState(panel, counts, limit, seen, weight, hasCharacter,
            pools.ToImmutable(), niche, ordinal, golden, goldenRarity, noiseSlot, !locked && callback != null, stageRarity, chaos);
        var keys = panel.Select(option => option.Key).ToImmutableArray();
        bool noise = !enemy && noiseSlot == slot && !locked && callback != null;
        var snapshot = new HextechRuneRerollSnapshot("", source, slot, enemy, multiplayer, keys, golden, noise,
            false, run.Rng.StringSeed, run.CurrentActIndex, run.TotalFloor, stage, playerKey, ordinal,
            rarity, enemy ? panel[slot].NativeValue : -1, [], niche, null) { FullState = fullState };
        bool can = HextechRuneRerollPrediction.IsLegal(snapshot, fullState, slot);
        if (can && !noise)
        {
            var prepared = HextechRuneRerollPrediction.Prepare(snapshot, fullState, slot);
            snapshot = snapshot with { Rarity = prepared.Rarity, Candidates = prepared.Candidates,
                Issue = prepared.Issue ?? (!enemy && chaos && !prepared.Candidates.IsEmpty ? "HextechRuneRerollChaosUnknown" : null) };
        }
        snapshot = snapshot with { CanReroll = can };
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        { snapshot, fullState, locked, assembly.ManifestModule.ModuleVersionId }))));
        return snapshot with { Fingerprint = fingerprint };
    }

    private static void AuditAvailability(Assembly assembly, Type builder, object rarity)
    {
        Type Mod(string name) => assembly.GetType("HextechRunes." + name, true)!;
        var types = ((IEnumerable)Invoke(Mod("HextechCatalog"), "GetConfigurablePlayerRuneTypesForRarity", rarity)!).Cast<Type>();
        HextechRuneAvailabilityAudit.Validate(assembly, types);
    }

    private static void ValidateCallback(Delegate callback, Type coordinator, bool enemy)
    {
        string method = enemy ? "<CreateEnemyHexAdjustmentOptions>" : "<CreateLocalRuneSelectionScreenAsync>";
        if (callback.GetInvocationList().Length != 1 || callback.Method.Module != coordinator.Module ||
            !callback.Method.Name.StartsWith(method, StringComparison.Ordinal))
            throw new NotSupportedException("HextechRuneRerollCallbackNotAudited:" + callback.Method);
    }
    private static List<object> Closure(object root, Assembly assembly)
    {
        var objects = new List<object> { root };
        for (int i = 0; i < objects.Count; i++)
            foreach (var field in objects[i].GetType().GetFields(Members).Where(f => !f.IsStatic))
                if (field.GetValue(objects[i]) is { } value && value.GetType().Assembly == assembly &&
                    value.GetType().IsDefined(typeof(CompilerGeneratedAttribute), false) && !objects.Any(x => ReferenceEquals(x, value))) objects.Add(value);
        return objects;
    }
    private static object? FindField(IEnumerable<object> closure, string name)
    {
        foreach (var item in closure)
            if (item.GetType().GetField(name, Members) is { } field) return field.GetValue(item);
        throw new MissingFieldException("HextechRuneRerollClosure", name);
    }
    private static readonly IReadOnlyDictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
        .GroupBy(op => unchecked((ushort)op.Value)).ToDictionary(group => group.Key, group => group.First());
    private static bool Calls(MethodInfo method, Type target, string name)
    {
        var bytes = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        for (int at = 0; at < bytes.Length;)
        {
            ushort value = bytes[at++]; if (value == 0xfe) value = (ushort)(0xfe00 | bytes[at++]);
            var op = OpCodesByValue[value];
            if (op.OperandType == OperandType.InlineMethod && method.Module.ResolveMethod(BitConverter.ToInt32(bytes, at)) is { } called &&
                called.DeclaringType == target && called.Name == name) return true;
            at += op.OperandType switch
            {
                OperandType.InlineNone => 0, OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2, OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, at), _ => 4
            };
        }
        return false;
    }
    private static ModelKey Key(RelicModel model) => new(model.Id.Category, model.Id.Entry);
    private static object? Read(object target, string name)
    {
        Type type = target as Type ?? target.GetType(); object? instance = target is Type ? null : target;
        return type.GetProperty(name, Members) is { } property ? property.GetValue(instance) :
            (type.GetField(name, Members) ?? throw new MissingMemberException(type.FullName, name)).GetValue(instance);
    }
    private static MethodInfo Method(Type type, string name, int count) => type.GetMethods(Members)
        .Single(m => m.Name == name && m.GetParameters().Length == count);
    private static object? Invoke(Type type, string name, params object?[] args) => Method(type, name, args.Length).Invoke(null, args);
    private static object? InvokeInstance(Type type, string name, object instance) => Method(type, name, 0).Invoke(instance, null);
}
