using RolltheSpire2.Ui.InRunOperations;
using RolltheSpire2.Infrastructure.Snapshots;
using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Compatibility.ModPrediction.Hextech;

// Optional integration: no Hextech assembly is loaded or required by RT2.
// Only audited read-only accessors/predicates run here, on the main thread.
internal static class HextechShopForgeCapture
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Static | BindingFlags.Instance;
    private const string ModAssembly = "HextechRunes";
    private static string? _lastContinuationFault;

    internal static bool IsForge(RelicModel relic) =>
        relic.GetType().FullName == "HextechRunes.RandomForgeShopRelic" &&
        relic.GetType().Assembly.GetName().Name == ModAssembly;

    internal static HextechShopForgeSnapshot Capture(RunState run, RelicModel shopRelic)
    {
        RuntimeSnapshotThreadGuard.RequireMainThread();
        if (!IsForge(shopRelic)) throw new ArgumentException("HextechShopForgeIdentityMismatch", nameof(shopRelic));
        try { return CaptureCore(run, shopRelic); }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            // Reflection is transport, not the failure's origin or disposition.
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static HextechShopForgeSnapshot CaptureCore(RunState run, RelicModel shopRelic)
    {
        var player = LocalContext.GetMe(run) ?? throw new InvalidOperationException("HextechShopForgeLocalPlayerMissing");
        var assembly = shopRelic.GetType().Assembly;
        Type ModType(string name) => assembly.GetType("HextechRunes." + name, throwOnError: true)!;
        var grant = ModType("HextechForgeGrantHelper");
        var registry = ModType("HextechContentRegistry");
        var catalog = ModType("HextechCatalog");
        var external = ModType("HextechExternalContentRegistry");
        var baseRelic = ModType("HextechRelicBase");
        var weightsObject = Invoke(grant, "GetBaseForgeRarityWeights", [typeof(Player)], null, player);
        var weights = new HextechShopForgeWeights(Read<int>(weightsObject, "Silver"),
            Read<int>(weightsObject, "Gold"), Read<int>(weightsObject, "Prismatic"));
        bool diceManiac = player.Relics.Any(r => r.GetType() == ModType("DiceManiacRune"));
        weights = HextechShopForgePrediction.ApplyDiceManiac(weights, diceManiac);
        var disabled = ((IEnumerable)Invoke(grant, "GetEffectiveDisabledForgeIds", [typeof(Player)], null, player))
            .Cast<string>().ToHashSet(StringComparer.Ordinal);
        bool directGrant = (bool)Invoke(ModType("HextechForgeSelectionCoordinator"),
            "ShouldDirectlyGrantRandomForge", [typeof(Player)], null, player);
        var rarity = ModType("HextechRarityTier");
        // Numeric rarity values are part of the selection salt, so validate them.
        foreach (var (name, value) in new[] { ("Silver", 0), ("Gold", 1), ("Prismatic", 2) })
            if (Convert.ToInt32(Enum.Parse(rarity, name)) != value)
                throw new NotSupportedException("HextechForgeRarityContractChanged: " + name);
        object byRarity = Read<object>(registry, "ForgeTypesByRarity");
        var available = new Dictionary<Type, ModelKey?>();
        ModelKey? Available(Type type)
        {
            if (available.TryGetValue(type, out var found)) return found;
            var id = ModelDb.GetId(type);
            if (disabled.Contains(id.Entry)) { available[type] = null; return null; }
            var model = ModelDb.GetById<RelicModel>(id);
            // Catalog calls this callback after the virtual predicate. Unknown
            // code must never receive the real Player merely to obtain a preview.
            var callback = Method(external, "GetPlayerRuneAvailability", [typeof(ModelId)])
                .Invoke(null, [id]);
            if (callback != null) throw new NotSupportedException("HextechForgeAvailabilityCallbackNotAudited: " + type.FullName);
            if (baseRelic.IsAssignableFrom(type))
            {
                var predicate = Method(type, "IsAvailableForPlayer", [typeof(Player)]);
                var declaring = predicate.DeclaringType!;
                string[] audited = ["HextechRelicBase", "HextechFocusForgeBase", "ForgingForge", "NecrobinderForge",
                    "OrbSlotForge", "SilverOrbForge", "SilverStarsForge", "StarsForge", "SummonForge"];
                if (declaring.Assembly != assembly || !audited.Contains(declaring.Name, StringComparer.Ordinal))
                    throw new NotSupportedException("HextechForgeAvailabilityOverrideNotAudited: " + declaring.FullName);
                if (!(bool)predicate.Invoke(model, [player])!) { available[type] = null; return null; }
            }
            // Non-HextechRelicBase forge types pass Catalog's rune-only check;
            // use the actual catalog for that metadata disposition, not a guess.
            else if (!(bool)Invoke(catalog, "IsAvailableForPlayer", [typeof(RelicModel), typeof(Player)], null, model, player))
            { available[type] = null; return null; }
            return available[type] = new ModelKey(id.Category, id.Entry);
        }
        ImmutableArray<ModelKey> Pool(IEnumerable types) => types.Cast<Type>().Select(Available)
            .Where(k => k.HasValue).Select(k => k!.Value).ToImmutableArray();
        var item = byRarity.GetType().GetProperty("Item") ?? throw new MissingMemberException(byRarity.GetType().FullName, "Item");
        ImmutableArray<ModelKey> Tier(int value) => Pool((IEnumerable)(item.GetValue(byRarity, [Enum.ToObject(rarity, value)])
            ?? throw new InvalidOperationException("HextechForgeRarityPoolMissing: " + value)));
        var silver = Tier(0);
        var gold = Tier(1);
        var prismatic = Tier(2);
        var all = Pool((IEnumerable)Read<object>(registry, "AllForgeTypes"));
        int ordinal = Read<int>(shopRelic, "PurchaseCount");
        if (ordinal < 0) throw new InvalidOperationException("HextechShopForgePurchaseOrdinalInvalid");
        string playerKey = player.NetId != 0 ? $"net:{player.NetId}" : $"slot:{run.GetPlayerSlotIndex(player)}";
        var snapshot = new HextechShopForgeSnapshot("", new(shopRelic.Id.Category, shopRelic.Id.Entry),
            run.Rng.StringSeed, run.CurrentActIndex, run.TotalFloor, playerKey, ordinal, player.Relics.Count,
            weights, directGrant, silver, gold, prismatic, all);
        try
        {
            var builtIn = ((IEnumerable)Read<object>(ModType("HextechForgeRegistry"), "Registrations"))
                .Cast<object>().Select(registration => Read<Type>(registration, "Type"));
            snapshot = snapshot with
            {
                OwnedForges = player.Relics.Where(relic => ModType("HextechForgeBase").IsInstanceOfType(relic))
                    .Select(relic => new ModelKey(relic.Id.Category, relic.Id.Entry)).Distinct().ToImmutableArray(),
                ContinuationForges = builtIn.Select(type => ModelDb.GetId(type))
                    .Select(id => new ModelKey(id.Category, id.Entry)).ToImmutableArray()
            };
        }
        catch (Exception error)
        {
            string fault = error.ToString();
            if (_lastContinuationFault != fault)
            {
                _lastContinuationFault = fault;
                RuntimeLog.Fault("operationPredictionFailed=true;stage=hextech-forge-continuation-capture;source=" + snapshot.Source, error);
            }
            snapshot = snapshot with { ContinuationIssue = "HextechForgeContinuationUnsupported" };
        }
        snapshot = snapshot with { UpgradePlans = HextechShopForgeUpgradeCapture.Capture(player, assembly, snapshot) };
        // Length-safe serialization includes all effective algorithm inputs and
        // actual availability results; inert config changes need no invalidation.
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            Assembly = assembly.ManifestModule.ModuleVersionId, Snapshot = snapshot,
            Character = new ModelKey(player.Character.Id.Category, player.Character.Id.Entry),
            Disabled = disabled.OrderBy(x => x, StringComparer.Ordinal).ToArray(), DiceManiac = diceManiac,
            RegistryVersion = Read<int>(registry, "Version")
        }))));
        return snapshot with { Fingerprint = fingerprint };
    }

    private static T Read<T>(object target, string name)
    {
        var type = target as Type ?? target.GetType();
        var property = type.GetProperty(name, Members) ?? throw new MissingMemberException(type.FullName, name);
        return property.GetValue(target is Type ? null : target) is T value ? value :
            throw new InvalidOperationException("HextechMemberValueInvalid: " + type.FullName + "." + name);
    }

    private static MethodInfo Method(Type type, string name, Type[] parameters) =>
        type.GetMethod(name, Members, null, parameters, null) ?? throw new MissingMethodException(type.FullName, name);

    private static object Invoke(Type type, string name, Type[] parameters, object? target, params object[] arguments) =>
        Method(type, name, parameters).Invoke(target, arguments) ??
            throw new InvalidOperationException("HextechMemberReturnedNull: " + type.FullName + "." + name);
}
