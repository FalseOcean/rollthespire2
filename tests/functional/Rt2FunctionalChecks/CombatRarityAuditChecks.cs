using System.Reflection;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Rewards;
using RolltheSpire2.Compatibility;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace RolltheSpire2.Tests.FastSearch;

internal static class CombatRarityAuditChecks
{
    public static int Run()
    {
        var roll = typeof(Beta110CombatRewardFastStage).GetMethod("RollCardRarity", BindingFlags.NonPublic | BindingFlags.Static)!;
        var branches = typeof(CombatRewardProbabilityEstimator).GetMethod("RarityBranches", BindingFlags.NonPublic | BindingFlags.Static)!;
        int checks = 0;
        void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
        SharedScalarRules(Check, roll);
        SharedRelicTriggers(Check);
        foreach (int asc in new[] { 0, 6, 7, 10 })
        foreach (int groups in new[] { 1, 2 })
        {
            float[] offsets = new float[49]; offsets[0] = -.05f;
            for (int k = 1; k < offsets.Length; k++) offsets[k] = Math.Min(.4f, offsets[k - 1] + (asc >= 7 ? .005f : .01f));
            var states = new Dictionary<(int K, int Mask, bool Ever), double> { [(0, 0, false)] = 1 };
            for (int battle = 1; battle <= 8; battle++)
            {
                var reset = new Dictionary<(int K, int Mask, bool Ever), double>();
                foreach (var (s, m) in states) Add(reset, (s.K, 0, s.Ever), m);
                states = reset;
                double[] expected = new double[3];
                for (int draw = 0; draw < groups * 3; draw++)
                {
                    var next = new Dictionary<(int K, int Mask, bool Ever), double>();
                    foreach (var (s, mass) in states)
                    {
                        float threshold = (asc >= 7 ? .0149f : .03f) + offsets[s.K];
                        double r = Beta110GpuCombatRewardHotLoopCompiler.FindNextFloatCutoff(threshold, false) / (double)(1UL << 53);
                        double ru = Beta110GpuCombatRewardHotLoopCompiler.FindNextFloatCutoff(threshold + .37f, false) / (double)(1UL << 53);
                        double[] probs = [1 - ru, ru - r, r];
                        var analytic = (IEnumerable<(EffectCardRarity Rarity, int NextPity, double Probability)>)branches.Invoke(null, new object[] { asc, s.K })!;
                        double analyticRare = analytic.Where(x => x.Rarity == EffectCardRarity.Rare).Sum(x => x.Probability);
                        Check((r > 0) == (analyticRare > 0), "Probability support differs from numerical support");
                        object[] minimum = { 0f, asc, offsets[s.K] };
                        Check(((byte)roll.Invoke(null, minimum)! == 3) == (r > 0), "Production roll minimum/support");
                        for (int type = 0; type < 3; type++)
                        {
                            if (probs[type] <= 0) continue;
                            float sample = type == 2 ? 0 : type == 1 ? (Math.Max(0, threshold) + threshold + .37f) / 2 : .999f;
                            object[] args = { sample, asc, offsets[s.K] };
                            Check((byte)roll.Invoke(null, args)! == type + 1, "Production branch witness");
                            Check((float)args[2] == offsets[type == 2 ? 0 : s.K + 1], "Production float state transition");
                            expected[type] += mass * probs[type];
                            Add(next, (type == 2 ? 0 : s.K + 1, s.Mask | 1 << type, s.Ever || type == 2), mass * probs[type]);
                        }
                    }
                    states = next;
                }
                Check(Math.Abs(states.Values.Sum() - 1) < 1e-12, "Mass conservation");
                double rare = states.Where(x => (x.Key.Mask & 4) != 0).Sum(x => x.Value);
                double window = states.Where(x => x.Key.Ever).Sum(x => x.Value);
                int first = groups == 1 ? (asc >= 7 ? 3 : 2) : (asc >= 7 ? 2 : 1);
                Check((rare > 0) == (battle >= first), "Battle support");
                Check((window > 0) == (battle >= first), "Window support");
                Console.WriteLine(FormattableString.Invariant($"RARITY A{asc} groups={groups} B{battle}: C={expected[0]/(groups*3)*100:F6} U={expected[1]/(groups*3)*100:F6} R={expected[2]/(groups*3)*100:F6} rewardRare={rare*100:F6} windowRare={window*100:F6}"));
            }
        }
        var candyBranches = typeof(CombatRewardProbabilityEstimator).GetMethod("CandyRarityBranches", BindingFlags.NonPublic | BindingFlags.Static)!;
        var baseRoll = typeof(Beta110CombatRewardFastStage).GetMethod("RollBaseCardRarity", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (int asc in new[] { 0, 7, 10 })
        foreach (int pity in new[] { 0, 6, 12, 30 })
        {
            var outcomes = ((IEnumerable<(EffectCardRarity Rarity, int NextPity, double Probability)>)candyBranches.Invoke(null, new object[] { asc, pity })!).ToArray();
            Check(outcomes.All(x => x.NextPity == pity), "Candy never advances or resets pity, including Rare");
            Check(outcomes.Single(x => x.Rarity == EffectCardRarity.Rare).Probability == (asc >= 7 ? .0149d : .03d), "Candy base Rare mass");
            Check((byte)baseRoll.Invoke(null, new object[] { 0f, asc })! == 3, "Candy rare witness independent of pity");
        }
        var pool = new List<(RolltheSpire2.Core.Identity.ModelKey Key, EffectCardRarity Rarity, EffectCardType Type)>();
        foreach (var rarity in new[] { EffectCardRarity.Common, EffectCardRarity.Uncommon, EffectCardRarity.Rare })
        foreach (var type in new[] { EffectCardType.Attack, EffectCardType.Skill, EffectCardType.Power })
        for (int i = 0; i < 3; i++) pool.Add((new("CARD", $"{rarity}_{type}_{i}"), rarity, type));
        var ra = pool.First(c => c.Rarity == EffectCardRarity.Rare && c.Type == EffectCardType.Attack).Key;
        var rp = pool.First(c => c.Rarity == EffectCardRarity.Rare && c.Type == EffectCardType.Power).Key;
        bool Support(int asc, bool unordered, bool wheel, bool candy, params RolltheSpire2.Core.Identity.ModelKey?[] targets)
            => CombatRewardProbabilityEstimator.CanAuthorCards(asc, pool, targets, unordered, wheel, candy);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Check(!Support(7, false, false, false, ra), "A7 B1 no Rare");
        Check(!Support(7, false, false, false, null, ra), "A7 B2 no Rare");
        Check(Support(6, false, false, false, null, ra), "A6 B2 Rare");
        Check(Support(7, false, false, false, null, null, ra), "A7 B3 Rare");
        Check(Support(7, false, false, false, null, null, null, ra), "A7 B4 Rare without preceding target");
        Check(!Support(7, false, false, false, null, null, ra, ra), "B3 Rare resets pity and excludes B4 Rare");
        Check(!Support(7, true, false, false, ra, null), "Two battle window excludes Rare");
        Check(Support(7, true, false, false, ra, null, null), "Three battle window allows Rare");
        Check(!Support(7, true, false, false, ra, rp, null, null), "Unordered joint Rare capacity");
        Check(Support(7, false, false, true, null, rp), "Candy B2 Rare Power");
        Check(!Support(7, false, false, true, null, ra), "Candy B2 excludes Rare Attack");
        Check(Support(7, true, false, true, ra, rp, rp, null), "Candy mixed multiset on distinct battles");
        Check(!Support(7, true, false, true, rp, rp, rp, rp), "Four repeated Rare requirements cannot share battles");
        Check(Support(7, false, true, false, null, ra), "Wheel B2 Rare Attack");
        Check(!Support(7, false, true, false, ra), "Wheel A7 B1 still no Rare");
        Check(Support(6, false, true, false, ra), "Wheel A6 B1 Rare");
        Check(Support(7, true, true, true, ra, rp, rp, null), "Wheel plus Candy");
        Check(!CombatRewardProbabilityEstimator.CanAuthorCards(7, pool.Where(c => c.Key != rp).ToArray(), new RolltheSpire2.Core.Identity.ModelKey?[] { null, rp }, false, true, true), "Modifiers never restore excluded/unlocked target");
        Check(Support(7, false, false, false, null, null, null, null, ra), "B5 ordered Rare reachable");
        Check(Support(7, false, false, false, null, null, ra, null, null, ra), "B3 then B6 Rare reachable");
        Check(!Support(7, false, false, false, null, null, ra, null, ra), "B3 Rare excludes B5 Rare");
        Check(!Support(7, true, false, false, ra, ra, null, null, null), "Five battle repeated Rare capacity");
        Check(Support(7, true, false, false, ra, ra, null, null, null, null), "Six battle repeated Rare assignment");
        Check(!Support(7, true, false, false, ra, ra, ra, null, null, null), "Six battle third Rare rejected");
        Check(Support(7, false, false, true, null, rp, null, rp, null, rp), "Candy B2 B4 B6 base-odds powers");
        Check(Support(7, false, true, true, null, rp, null, ra, null, rp), "Wheel and Candy six battle ordered");
        var common = pool.First(c => c.Rarity == EffectCardRarity.Common).Key;
        foreach (bool unordered in new[] { false, true })
        foreach (int window in new[] { 5, 6 })
            Check(Support(7, unordered, false, false, Enumerable.Repeat<RolltheSpire2.Core.Identity.ModelKey?>(common, window).ToArray()),
                "Five/six target assignment mask retains all occurrences");
        // UI shrink policy: remove out-of-window slots, then replay retained slots in stable order.
        var shrunk = new RolltheSpire2.Core.Identity.ModelKey?[] { ra, ra, common, null, null };
        var retained = new RolltheSpire2.Core.Identity.ModelKey?[5];
        for (int i = 0; i < shrunk.Length; i++)
        {
            retained[i] = shrunk[i];
            if (!Support(7, true, false, false, retained)) retained[i] = null;
        }
        Check(retained[0] == ra && retained[1] is null && retained[2] == common,
            "Shrinking six to five keeps first Rare and later feasible Common");
        foreach (int invalid in new[] { 0, 7, 8 })
        {
            bool rejected = false;
            try { Support(7, false, false, false, new RolltheSpire2.Core.Identity.ModelKey?[invalid]); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Authoring boundary remains one through six");
        }
        Console.WriteLine($"C_RESIDUAL_SUPPORT_PASS elapsedMs={watch.ElapsedMilliseconds}");
        if (Environment.GetEnvironmentVariable("RT2_AUTHORED_GODOT_RUNNER") == "1") checks += VanillaCandy();
        Console.WriteLine($"COMBAT_RARITY_AUDIT_PASS checks={checks}");
        return 0;
    }

    private static void SharedScalarRules(Action<bool, string> check, MethodInfo fastRoll)
    {
        // Official CardRarityOdds: Scarcity reduces both Elite and Shop Rare odds.
        // These examples protect their source mapping independently of consumers.
        foreach (var (ascension, elite, shop) in new[] { (0, .1f, .09f), (6, .1f, .09f), (7, .05f, .045f), (10, .05f, .045f) })
        {
            check(CardBaseOddsPolicyEvaluator.GetBaseOdds(CardBaseOddsType.Elite, ascension) == new CardBaseOddsThresholds(elite, .4f), "Official Elite odds at Scarcity boundary");
            check(CardBaseOddsPolicyEvaluator.GetBaseOdds(CardBaseOddsType.Shop, ascension) == new CardBaseOddsThresholds(shop, .37f), "Official Shop odds at Scarcity boundary");
            foreach (var type in new[] { CardBaseOddsType.Regular, CardBaseOddsType.Elite, CardBaseOddsType.Shop })
            {
                var odds = CardBaseOddsPolicyEvaluator.GetBaseOdds(type, ascension);
                check(CardBaseOddsPolicyEvaluator.Evaluate(MathF.BitDecrement(odds.Rare), type, ascension, CardBaseOddsPolicy.Beta110Cumulative) == EffectCardRarity.Rare &&
                    CardBaseOddsPolicyEvaluator.Evaluate(odds.Rare, type, ascension, CardBaseOddsPolicy.Beta110Cumulative) == EffectCardRarity.Uncommon,
                    "Base Rare uses strict lower comparison");
                check(CardBaseOddsPolicyEvaluator.Evaluate(odds.Rare + odds.Uncommon, type, ascension, CardBaseOddsPolicy.Beta110Cumulative) == EffectCardRarity.Common &&
                    CardBaseOddsPolicyEvaluator.Evaluate(odds.Uncommon, type, ascension, CardBaseOddsPolicy.LegacyAndBeta109NonCumulative) == EffectCardRarity.Common,
                    "Versioned base Uncommon upper boundary");
            }
        }
        foreach (var (roll, expected) in new[] { (0f, EffectPotionRarity.Rare), (.1f, EffectPotionRarity.Rare),
            (MathF.BitIncrement(.1f), EffectPotionRarity.Uncommon), (.35f, EffectPotionRarity.Uncommon),
            (MathF.BitIncrement(.35f), EffectPotionRarity.Common), (1f, EffectPotionRarity.Common) })
            check(PotionRarityRules.Evaluate(roll) == expected, "PotionFactory inclusive scalar boundaries");

        var coreRoll = typeof(NormalCombatRewardSequencePredictor).GetMethod("RollCardRarity", BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (int ascension in new[] { 0, 7, 10 })
        foreach (float offset in new[] { -.05f, 0f, .399f, .4f })
        {
            float rare = (ascension >= 7 ? .0149f : .03f) + offset;
            float uncommon = rare + .37f;
            foreach (float value in new[] { 0f, rare, MathF.BitDecrement(rare), uncommon, MathF.BitDecrement(uncommon), 1f }.Where(v => v >= 0f && v <= 1f))
            {
                var expected = value < rare ? EffectCardRarity.Rare : value < uncommon ? EffectCardRarity.Uncommon : EffectCardRarity.Common;
                float next = expected == EffectCardRarity.Rare ? -.05f : Math.Min(.4f, offset + (ascension >= 7 ? .005f : .01f));
                var shared = CardRarityRules.EvaluateWithOffset(value, CardBaseOddsType.Regular, ascension, offset);
                check(shared == new CardRarityEvaluation(expected, rare, uncommon) && CardRarityRules.AdvanceOffset(offset, shared.Rarity, ascension) == next,
                    "Shared stateful rule retains float thresholds and offset");
                object[] fastArgs = [value, ascension, offset];
                object[] coreArgs = [value, ascension, offset];
                var core = coreRoll.Invoke(null, coreArgs)!;
                check((byte)fastRoll.Invoke(null, fastArgs)! == (byte)(expected == EffectCardRarity.Rare ? 3 : expected == EffectCardRarity.Uncommon ? 2 : 1) &&
                    (float)fastArgs[2] == next && (float)coreArgs[2] == next &&
                    (EffectCardRarity)core.GetType().GetProperty("Rarity")!.GetValue(core)! == expected,
                    "Shared Core and retained Fast numerical rarity/offset equivalence");
            }
        }
    }

    private static void SharedRelicTriggers(Action<bool, string> check)
    {
        foreach (var room in Enum.GetValues<CombatRewardRoomKind>())
        {
            var wheel = CombatRewardRelicRules.PrayerWheel(room);
            var star = CombatRewardRelicRules.WhiteStar(room);
            check(wheel.RewardCount == (room == CombatRewardRoomKind.Monster ? 1 : 0) &&
                star.RewardCount == (room == CombatRewardRoomKind.Elite ? 1 : 0) &&
                CombatRewardRelicRules.BlackStarRelics(room) == (room == CombatRewardRoomKind.Elite ? 1 : 0),
                "Relic additions retain their exact room triggers");
            if (wheel.RewardCount > 0) check(wheel.CardCount == 3 && wheel.Odds == CardBaseOddsType.Regular && wheel.EncounterSource && !wheel.IsFromCombat,
                "Prayer Wheel uses encounter rarity without combat-card flags");
            if (star.RewardCount > 0) check(star.CardCount == 3 && star.Odds == CardBaseOddsType.Boss && star.EncounterSource && !star.IsFromCombat,
                "White Star uses Boss encounter rarity without combat-card flags");
            foreach (int act in new[] { 1, 2 })
            foreach (bool triggered in new[] { false, true })
            {
                var lava = CombatRewardRelicRules.LavaRock(room, act, triggered);
                bool applies = room == CombatRewardRoomKind.Boss && act == 1 && !triggered;
                check(lava.RelicCount == (applies ? 2 : 0) && lava.HasTriggeredAfterModification == (triggered || applies),
                    "Lava Rock adds twice and commits its one-shot state in modification");
            }
        }
        foreach (var policy in Enum.GetValues<LastingCandyCounterPolicy>())
        {
            int counter = 0;
            for (int battle = 1; battle <= 6; battle++)
            {
                counter = CardRewardRelicRules.CandyCounterBeforeReward(policy, counter);
                check(CardRewardRelicRules.CandyExtraCardCount(policy, counter, true, true, true) == (battle % 2 == 0 ? 1 : 0),
                    "Candy profile-specific counter stages share the same every-other-combat meaning");
                check(CardRewardRelicRules.CandyExtraCardCount(policy, counter, false, true, true) == 0 &&
                    CardRewardRelicRules.CandyExtraCardCount(policy, counter, true, false, true) == 0,
                    "Candy requires a card reward with encounter source");
                if (battle % 2 == 0)
                    check(CardRewardRelicRules.CandyExtraCardCount(policy, counter, true, true, false) ==
                        (policy == LastingCandyCounterPolicy.RewardOfferingOdd ? 0 : 1), "Candy preserves historical IsFromCombat distinction");
                check(CardRewardRelicRules.CandyCounterBeforeOffering(policy, counter, false) == counter,
                    "No-card reward sets do not advance offered-reward counters");
                counter = CardRewardRelicRules.CandyCounterBeforeOffering(policy, counter, true);
            }
        }
        foreach (int uses in new[] { 0, 2, 3, 4 })
            check(CardRewardRelicRules.SilverCrucibleApplies(true, uses) == (uses < 3) &&
                !CardRewardRelicRules.SilverCrucibleApplies(false, uses) &&
                CardRewardRelicRules.SilverCrucibleUsesAfterModification(uses) == (uses < 3 ? uses + 1 : uses),
                "Crucible eligibility and after-hook charges are per reward, not per upgraded card");
        check(CardRewardRelicRules.SilkenTressApplies(true, false) && !CardRewardRelicRules.SilkenTressApplies(false, false) &&
            !CardRewardRelicRules.SilkenTressApplies(true, true), "Tress has one card-reward application");
        check(CombatRewardRelicRules.AmethystGold(CombatRewardRoomKind.Monster, true) == 15 &&
            CombatRewardRelicRules.AmethystGold(CombatRewardRoomKind.Boss, false) == 15 &&
            CombatRewardRelicRules.AmethystGold(CombatRewardRoomKind.Boss, true) == 0 &&
            CombatRewardRelicRules.AmethystGold(CombatRewardRoomKind.Other, false) == 0, "Amethyst excludes only noncombat and final-act Boss rewards");
        check(CombatRewardRelicRules.Wongo(CombatRewardRoomKind.Monster, 4, false).RelicCount == 0 &&
            CombatRewardRelicRules.Wongo(CombatRewardRoomKind.Monster, 5, false) == new WongoReward(3, true) &&
            CombatRewardRelicRules.Wongo(CombatRewardRoomKind.Elite, 6, true) == new WongoReward(0, true) &&
            CombatRewardRelicRules.Wongo(CombatRewardRoomKind.Other, 5, false).RelicCount == 0, "Wongo schedules its count and after-modifying one-shot state only at the fifth completed combat");
        foreach (var profile in new[] { RuntimeProfileId.Stable107, RuntimeProfileId.Beta109, RuntimeProfileId.Beta110, RuntimeProfileId.Beta111 })
        {
            var wheel = CombatRewardRelicRules.PrayerWheel(CombatRewardRoomKind.Monster);
            check(OpeningCombatRewardImpactAdapterRegistry.TryResolve(profile, new RolltheSpire2.Core.Identity.ModelKey("RELIC", "PRAYER_WHEEL"), out var adapter) &&
                adapter.Operations.Single() is { Kind: OpeningCombatRewardImpactOperationKind.AddCardReward } operation &&
                operation.Amount == wheel.RewardCount && operation.AddedCardRewardIsFromCombat == wheel.IsFromCombat,
                "Opening adapter carries shared Prayer Wheel count and flag meaning into Exact/Fast preparation");
        }
        var influenceType = typeof(CombatRewardProbabilityEstimator).GetNestedType("RewardInfluenceProfile", BindingFlags.NonPublic)!;
        var influence = Activator.CreateInstance(influenceType, new object[] { true, false, false, false })!;
        check((int)influenceType.GetProperty("AdditionalCardRewardCount")!.GetValue(influence)! == CombatRewardRelicRules.PrayerWheel(CombatRewardRoomKind.Monster).RewardCount,
            "Search probability preparation consumes the same Prayer Wheel addition");
    }
    private static int VanillaCandy()
    {
        MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn = true;
        RolltheSpire2.Infrastructure.Snapshots.RuntimeSnapshotThreadGuard.BindCurrentThread();
        typeof(MegaCrit.Sts2.Core.Modding.ModManager).GetProperty("State")!.SetValue(null, MegaCrit.Sts2.Core.Modding.ModManagerState.Initialized);
        MegaCrit.Sts2.Core.Models.ModelDb.Init(); MegaCrit.Sts2.Core.Modding.AssemblyInfo.Init();
        MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache.Init(); MegaCrit.Sts2.Core.Models.ModelDb.InitIds();
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.InitSettingsDataForTest();
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.SettingsSave.Language = "eng";
        MegaCrit.Sts2.Core.Localization.LocManager.Initialize();
        int checks = 0;
        var stateProperty = typeof(MegaCrit.Sts2.Core.Runs.RunManager).GetProperty("State", BindingFlags.NonPublic | BindingFlags.Instance)!;
        checks += VanillaRelicTriggers(stateProperty);
        foreach (int asc in new[] { 0, 7 })
        {
            int rareSeen = 0;
            for (int seed = 0; seed < 512; seed++)
            {
                var player = MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun<MegaCrit.Sts2.Core.Models.Characters.Ironclad>(MegaCrit.Sts2.Core.Unlocks.UnlockState.all, 1);
                var run = MegaCrit.Sts2.Core.Runs.RunState.CreateForTest([player], seed: seed.ToString(), ascensionLevel: asc);
                stateProperty.SetValue(MegaCrit.Sts2.Core.Runs.RunManager.Instance, run);
                typeof(MegaCrit.Sts2.Core.Runs.RunManager).GetProperty("AscensionManager", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
                    .SetValue(MegaCrit.Sts2.Core.Runs.RunManager.Instance, new MegaCrit.Sts2.Core.Entities.Ascension.AscensionManager(asc));
                var options = MegaCrit.Sts2.Core.Runs.CardCreationOptions.ForRoom(player, MegaCrit.Sts2.Core.Rooms.RoomType.Monster)
                    .WithFlags(MegaCrit.Sts2.Core.Runs.CardCreationFlags.IsCardReward | MegaCrit.Sts2.Core.Runs.CardCreationFlags.IsFromCombat);
                _ = MegaCrit.Sts2.Core.Factories.CardFactory.CreateForReward(player, 3, options).ToArray();
                var cards = MegaCrit.Sts2.Core.Factories.CardFactory.CreateForReward(player, 3, options).ToList();
                if (asc == 7 && cards.Any(c => c.Card.Rarity == MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Rare)) throw new Exception("Vanilla baseline B2 Rare");
                // Exercise the vanilla allow-duplicate retry when every legal Power is already offered.
                if (seed % 64 == 0)
                {
                    var only = options.GetPossibleCards(player).First(c => c.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Power && c.Rarity == MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Uncommon);
                    cards[0] = new MegaCrit.Sts2.Core.Entities.Cards.CardCreationResult(run.CreateCard(only, player));
                    options = new MegaCrit.Sts2.Core.Runs.CardCreationOptions(options.CardPools, MegaCrit.Sts2.Core.Runs.CardCreationSource.Encounter,
                        options.RarityOdds, c => c.Id == only.Id).WithFlags(MegaCrit.Sts2.Core.Runs.CardCreationFlags.IsCardReward | MegaCrit.Sts2.Core.Runs.CardCreationFlags.IsFromCombat);
                }
                var candy = (MegaCrit.Sts2.Core.Models.Relics.LastingCandy)MegaCrit.Sts2.Core.Models.ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.LastingCandy>().ToMutable();
                player.AddRelicInternal(candy, silent: true); candy.CombatRewardsSeen = 1;
                float before = player.PlayerOdds.CardRarity.CurrentValue;
                var checkpoint = player.PlayerRng.Rewards.ToSerializable();
                var copiedRng = RolltheSpire2.Core.Rng.Xoshiro256StarStar.FromState(checkpoint.state0, checkpoint.state1, checkpoint.state2, checkpoint.state3, checkpoint.counter);
                var possible = (IEnumerable<MegaCrit.Sts2.Core.Models.CardModel>)typeof(MegaCrit.Sts2.Core.Factories.CardFactory)
                    .GetMethod("FilterForPlayerCount", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, new object[] { run, options.GetPossibleCards(player) })!;
                var snapshots = possible.Select((c, i) => new NeowEffectCardSnapshot(c.Id.Entry,
                    new RolltheSpire2.Core.Identity.ModelKey("CARD", c.Id.Entry), i,
                    Enum.Parse<EffectCardRarity>(c.Rarity.ToString()), Enum.Parse<EffectCardType>(c.Type.ToString()),
                    false, false, false, c.IsUpgradable, true)).ToArray();
                var selected = cards.Select(c => new RolltheSpire2.Core.Identity.ModelKey("CARD", c.Card.Id.Entry)).ToHashSet();
                object[] donorArgs = { RolltheSpire2.Compatibility.RuntimeProfileId.Beta111, copiedRng,
                    new List<RolltheSpire2.Core.Rewards.NormalCombatRewardRngTraceEntry>(), 2, 1, 4, true, 1, asc,
                    snapshots, selected, before, EffectCardType.Power };
                var predicted = (RolltheSpire2.Core.Rewards.NormalCombatRewardCardResult)typeof(RolltheSpire2.Core.Rewards.NormalCombatRewardSequencePredictor)
                    .GetMethod("GenerateCard", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, donorArgs)!;
                if (!candy.TryModifyCardRewardOptions(player, cards, options) || cards.Count != 4) throw new Exception("Vanilla Candy option count");
                if (cards[3].Card.Type != MegaCrit.Sts2.Core.Entities.Cards.CardType.Power || player.PlayerOdds.CardRarity.CurrentValue != before) throw new Exception("Vanilla Candy pool/pity");
                if (predicted.CardKey.Entry != cards[3].Card.Id.Entry || (float)donorArgs[11] != before) throw new Exception("Predictor vs vanilla Candy identity/pity");
                var after = player.PlayerRng.Rewards.ToSerializable();
                if (copiedRng.State != (after.state0, after.state1, after.state2, after.state3) || copiedRng.CallCount != after.counter) throw new Exception("Predictor vs vanilla Candy RNG continuation");
                if (cards[3].Card.Rarity == MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Rare) rareSeen++;
                checks += 5;
            }
            if (rareSeen == 0) throw new Exception("Vanilla real unlocked Ironclad pool Rare Power witness missing");
            Console.WriteLine($"VANILLA_CANDY A{asc} Ironclad UnlockState.all B2 rarePowerSeeds={rareSeen}/512 pityUnchanged=true");
        }
        stateProperty.SetValue(MegaCrit.Sts2.Core.Runs.RunManager.Instance, null);
        return checks;
    }

    private static int VanillaRelicTriggers(PropertyInfo stateProperty)
    {
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception("VANILLA_REWARD_TRIGGER " + message); }
        foreach (var roomKind in Enum.GetValues<CombatRewardRoomKind>())
        foreach (string entry in new[] { "PRAYER_WHEEL", "WHITE_STAR", "BLACK_STAR", "LAVA_ROCK", "AMETHYST_AUBERGINE", "WONGOS_MYSTERY_TICKET" })
        {
            var player = MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun<MegaCrit.Sts2.Core.Models.Characters.Ironclad>(MegaCrit.Sts2.Core.Unlocks.UnlockState.all, 1);
            var run = RunState.CreateForTest([player], seed: "reward-triggers", ascensionLevel: 0);
            stateProperty.SetValue(RunManager.Instance, run);
            AbstractRoom? room = roomKind == CombatRewardRoomKind.Other ? null : new CombatRoom(ModelDb.AllEncounters.First(e => e.RoomType ==
                (roomKind == CombatRewardRoomKind.Monster ? RoomType.Monster : roomKind == CombatRewardRoomKind.Elite ? RoomType.Elite : RoomType.Boss)).ToMutable(), run);
            RelicModel relic = entry switch
            {
                "PRAYER_WHEEL" => ModelDb.Relic<PrayerWheel>().ToMutable(), "WHITE_STAR" => ModelDb.Relic<WhiteStar>().ToMutable(),
                "BLACK_STAR" => ModelDb.Relic<BlackStar>().ToMutable(), "LAVA_ROCK" => ModelDb.Relic<LavaRock>().ToMutable(),
                "AMETHYST_AUBERGINE" => ModelDb.Relic<AmethystAubergine>().ToMutable(), _ => ModelDb.Relic<WongosMysteryTicket>().ToMutable()
            };
            player.AddRelicInternal(relic, silent: true);
            if (relic is WongosMysteryTicket ticket) ticket.CombatsFinished = 5;
            var rewards = new List<Reward>();
            int callsBefore = player.PlayerRng.Rewards.ToSerializable().counter;
            bool modified = relic.TryModifyRewards(player, rewards, room);
            var cards = entry == "PRAYER_WHEEL" ? CombatRewardRelicRules.PrayerWheel(roomKind) :
                entry == "WHITE_STAR" ? CombatRewardRelicRules.WhiteStar(roomKind) : default;
            int relics = entry switch { "BLACK_STAR" => CombatRewardRelicRules.BlackStarRelics(roomKind),
                "LAVA_ROCK" => CombatRewardRelicRules.LavaRock(roomKind, 1, false).RelicCount,
                "WONGOS_MYSTERY_TICKET" => CombatRewardRelicRules.Wongo(roomKind, 5, false).RelicCount, _ => 0 };
            int gold = entry == "AMETHYST_AUBERGINE" ? CombatRewardRelicRules.AmethystGold(roomKind, false) : 0;
            Check(rewards.OfType<CardReward>().Count() == cards.RewardCount && rewards.OfType<RelicReward>().Count() == relics &&
                rewards.OfType<GoldReward>().Sum(r => r.Amount) == gold && modified == (cards.RewardCount + relics + gold > 0), "Native condition and addition count: " + entry);
            foreach (var reward in rewards.OfType<CardReward>())
            {
                var options = (CardCreationOptions)typeof(CardReward).GetProperty("Options", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reward)!;
                Check((int)typeof(CardReward).GetProperty("OptionCount", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reward)! == cards.CardCount &&
                    options.Source == CardCreationSource.Encounter && options.Flags.HasFlag(CardCreationFlags.IsCardReward) &&
                    options.Flags.HasFlag(CardCreationFlags.IsFromCombat) == cards.IsFromCombat &&
                    options.RarityOdds == (cards.Odds == CardBaseOddsType.Boss ? CardRarityOddsType.BossEncounter : CardRarityOddsType.RegularEncounter),
                    "Native recipe preserves encounter source and separate combat flag");
            }
            if (relic is LavaRock lava) Check(lava.HasTriggered == (relics > 0), "Lava Rock commits during TryModifyRewards");
            if (relic is WongosMysteryTicket wongo)
            {
                Check(!wongo.GaveRelic, "Wongo is pending after TryModifyRewards");
                if (modified) { wongo.AfterModifyingRewards().GetAwaiter().GetResult(); Check(wongo.GaveRelic, "Wongo commits in AfterModifyingRewards"); }
            }
            Check(player.PlayerRng.Rewards.ToSerializable().counter == callsBefore, "Native reward construction cannot consume population RNG");
        }
        {
            var player = MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun<MegaCrit.Sts2.Core.Models.Characters.Ironclad>(MegaCrit.Sts2.Core.Unlocks.UnlockState.all, 1);
            var run = RunState.CreateForTest([player], seed: "empty-card-reward", ascensionLevel: 0);
            stateProperty.SetValue(RunManager.Instance, run);
            var crucible = (SilverCrucible)ModelDb.Relic<SilverCrucible>().ToMutable();
            var tress = (SilkenTress)ModelDb.Relic<SilkenTress>().ToMutable();
            player.AddRelicInternal(crucible, silent: true); player.AddRelicInternal(tress, silent: true);
            crucible.TimesUsed = 2;
            var options = CardCreationOptions.ForRoom(player, RoomType.Monster).WithFlags(CardCreationFlags.IsCardReward);
            Check(crucible.TryModifyCardRewardOptionsLate(player, [], options) == CardRewardRelicRules.SilverCrucibleApplies(true, 2) &&
                tress.TryModifyCardRewardOptionsLate(player, [], options) == CardRewardRelicRules.SilkenTressApplies(true, false),
                "Empty options still have successful native modifier listeners");
            Check(crucible.TimesUsed == 2 && !tress.IsUsedUp, "Late modification does not prematurely commit use counters");
            crucible.AfterModifyingCardRewardOptions().GetAwaiter().GetResult(); tress.AfterModifyingCardRewardOptions().GetAwaiter().GetResult();
            Check(crucible.TimesUsed == CardRewardRelicRules.SilverCrucibleUsesAfterModification(2) && tress.IsUsedUp, "Native after hooks consume an empty eligible reward");
        }
        Console.WriteLine($"VANILLA_REWARD_TRIGGERS_PASS checks={checks} construction/flags/one-shot stages/empty-card modifiers");
        return checks;
    }

    private static void Add(Dictionary<(int K, int Mask, bool Ever), double> values, (int K, int Mask, bool Ever) key, double mass)
        => values[key] = values.GetValueOrDefault(key) + mass;
}
