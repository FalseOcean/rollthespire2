using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using RolltheSpire2.Bootstrap;

namespace RolltheSpire2.Ui.InRunOperations;

// RF's workshop loader and implementation can have the same assembly name.
// Bind the running settings getter, including when RF initialized after RT2.
internal sealed class RandomForeseerSettings
{
    private readonly PropertyInfo _settings;
    private readonly PropertyInfo?[] _properties;
    private readonly MethodInfo? _otherReward;
    private static RandomForeseerSettings? _reader;
    private static string? _lastError;

    private RandomForeseerSettings(PropertyInfo settings)
    {
        _settings = settings;
        _properties = Enum.GetNames<RfSwitch>().Select(name => settings.PropertyType.GetProperty(name)).ToArray();
        _otherReward = settings.DeclaringType!.Assembly.GetType("RandomForeseer.OutOfCombat.RewardPagePredictionContext")
            ?.GetMethod("HasOtherPendingReward", [typeof(RelicModel)]);
    }

    internal static RfFeatures Read()
    {
        try
        {
            if (_reader == null)
            {
                var settings = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => a.GetName().Name == "RandomForeseer")
                    .Select(a => a.GetType("RandomForeseer.Data.ModData")?.GetProperty("Settings"))
                    .FirstOrDefault(p => p != null);
                if (settings == null) return default;
                _reader = new(settings);
            }
            var value = _reader._settings.GetValue(null);
            if (value == null) return default;
            int known = 0, enabled = 0;
            foreach (var key in Enum.GetValues<RfSwitch>())
            {
                var current = _reader._properties[(int)key]?.GetValue(value);
                if (current is not (bool or int)) continue;
                int bit = 1 << (int)key;
                known |= bit;
                if (current is true || current is int count && count > 1) enabled |= bit;
            }
            var net = RunManager.Instance is { IsInProgress: true } run ? run.NetService?.Type : null;
            return new(known, enabled, net);
        }
        catch (Exception error) { Report(error); return default; }
    }

    internal static bool HasOtherReward(RelicModel? relic)
    {
        if (relic == null) return false;
        try { return _reader?._otherReward?.Invoke(null, [relic]) is true; }
        catch (Exception error) { Report(error); return false; }
    }

    private static void Report(Exception error)
    {
        if (_lastError == error.ToString()) return;
        _lastError = error.ToString();
        RuntimeLog.WarnException("RandomForeseerSettingsUnread;keepRt2=true", error);
    }
}

internal enum RfSwitch
{
    SingleplayerPredictionEnabled, MultiplayerPredictionEnabled, FairModeEnabled, EventOptionPredictionEnabled,
    AncientRelicPickupPredictionEnabled, RelicPickupPredictionEnabled, DeckTransformPredictionEnabled,
    MerchantRestockPredictionEnabled, DriftwoodRerollPredictionEnabled, DriftwoodRerollPreviewCount,
    PaelsWingSacrificePredictionEnabled, RestSitePredictionEnabled, PotionPredictionEnabled,
    PotionCardGenerationPredictionEnabled, PotionGenerationPredictionEnabled
}

internal readonly partial record struct RfFeatures(int Known, int Enabled, NetGameType? Net)
{
    internal bool On(RfSwitch key) => (Enabled & (1 << (int)key)) != 0;
    private bool Off(RfSwitch key) => (Known & (1 << (int)key)) != 0 && !On(key);
    internal bool Active => Net switch
    {
        NetGameType.Singleplayer => On(RfSwitch.SingleplayerPredictionEnabled),
        NetGameType.Host or NetGameType.Client => On(RfSwitch.MultiplayerPredictionEnabled),
        _ => false
    };
    private bool Unrestricted => Off(RfSwitch.FairModeEnabled);
    internal bool SoloHiddenAllowed => Unrestricted || Net is NetGameType.Host or NetGameType.Client;

    internal bool Covers(string feature, bool inCombat = false) => Active && feature switch
    {
        "shop:restock" => On(RfSwitch.MerchantRestockPredictionEnabled),
        "reward:reroll" => On(RfSwitch.DriftwoodRerollPredictionEnabled),
        "extra:reroll-sequence" => On(RfSwitch.DriftwoodRerollPredictionEnabled) && On(RfSwitch.DriftwoodRerollPreviewCount),
        "reward:sacrifice" => On(RfSwitch.PaelsWingSacrificePredictionEnabled),
        "relic:SHOVEL" or "relic:DREAM_CATCHER" or "relic:TINY_MAILBOX" => On(RfSwitch.RestSitePredictionEnabled),
        "potion:ENTROPIC_BREW" => On(RfSwitch.PotionPredictionEnabled) && On(RfSwitch.PotionGenerationPredictionEnabled),
        "potion:ATTACK_POTION" or "potion:SKILL_POTION" or "potion:POWER_POTION" or "potion:COLORLESS_POTION" or
            "potion:COSMIC_CONCOCTION" or "potion:OROBIC_ACID" or "potion:BOTTLED_SONG" =>
            (inCombat || Unrestricted) && On(RfSwitch.PotionPredictionEnabled) && On(RfSwitch.PotionCardGenerationPredictionEnabled),
        _ => false
    };

    internal bool Pickup(RelicModel? relic)
    {
        if (!Active || relic == null) return false;
        var setting = relic.Rarity switch
        {
            MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Ancient => RfSwitch.AncientRelicPickupPredictionEnabled,
            MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Event => RfSwitch.EventOptionPredictionEnabled,
            _ => RfSwitch.RelicPickupPredictionEnabled
        };
        return On(setting) && relic.Id.Entry switch
        {
            "SMALL_CAPSULE" or "KALEIDOSCOPE" or "HEFTY_TABLET" or "LEAD_PAPERWEIGHT" or "LOST_COFFER" or
            "MASSIVE_SCROLL" or "SCROLL_BOXES" or "CALLING_BELL" or "GLASS_EYE" or "SEA_GLASS" or
            "TOY_BOX" or "CAULDRON" or "FRAGRANT_MUSHROOM" or "ORRERY" or "WAR_PAINT" or "WHETSTONE" => true,
            "ARCANE_SCROLL" or "LARGE_CAPSULE" or "LEAFY_POULTICE" or "NEW_LEAF" or "PHIAL_HOLSTER" =>
                SoloHiddenAllowed || RandomForeseerSettings.HasOtherReward(relic),
            "ASTROLABE" or "PANDORAS_BOX" or "BEAUTIFUL_BRACELET" or "ALCHEMICAL_COFFER" or "SAND_CASTLE" or
            "DISTINGUISHED_CAPE" or "NEOWS_BONES" => SoloHiddenAllowed,
            "SILKEN_TRESS" => Unrestricted,
            _ => false
        };
    }


}
