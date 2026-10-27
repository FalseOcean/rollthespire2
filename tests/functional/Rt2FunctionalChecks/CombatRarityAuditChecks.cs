using System.Reflection;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Core.Effects.Snapshots;

namespace RolltheSpire2.Tests.FastSearch;

internal static class CombatRarityAuditChecks
{
    public static int Run()
    {
        var roll = typeof(Beta110CombatRewardFastStage).GetMethod("RollCardRarity", BindingFlags.NonPublic | BindingFlags.Static)!;
        var branches = typeof(CombatRewardProbabilityEstimator).GetMethod("RarityBranches", BindingFlags.NonPublic | BindingFlags.Static)!;
        int checks = 0;
        void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
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

    private static void Add(Dictionary<(int K, int Mask, bool Ever), double> values, (int K, int Mask, bool Ever) key, double mass)
        => values[key] = values.GetValueOrDefault(key) + mass;
}
