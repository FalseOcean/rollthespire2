using System.Collections.Immutable;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.ValueProps;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Core.Rng;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Tests.FastSearch;

internal static class EventOperationAuthorityChecks
{
    private sealed class ExternalCard : CardModel
    {
        internal static CardType TestType { get; set; }
        public ExternalCard() : base(1, TestType, CardRarity.Common, TargetType.AnyEnemy) { }
        protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(3m, ValueProp.Move)];
    }

    internal static int Run()
    {
        NativeWorkbenchCheckEnvironment.Initialize("EventOperationAuthority");
        var (context, state) = CrystalSeedGoalChecks.CreateReplayFixture();
        context = context with { Crystal = null, Operation = new(1, 0, 1, []) };
        state = state with { OpeningCompleted = true };
        var token = ModelDb.AllPotions.First(p => p.Rarity.ToString() == "Token");
        var common = ModelDb.AllPotions.First(p => p.Rarity.ToString() == "Common");
        var rare = ModelDb.AllPotions.First(p => p.Rarity.ToString() == "Rare");
        var nativePool = new[] { common, token, rare };
        context = context with { PotionPool = nativePool.Select(PredictorLiveRunCapture.PotionOption).ToImmutableArray() };
        var eventRng = state.Streams.Single(s => s.Stream == PredictorStream.Niche);
        var wuppy = new PredictorOperationEventScene(new("EVENT", "WUPPY_IN_TROUBLE"), eventRng, [new("IGNORE")], new(50, 80));
        var rolled = new HashSet<ModelKey>();
        foreach (ulong seed in Enumerable.Range(0, 16).Select(i => (ulong)i))
        {
            var native = new Rng(seed);
            var expected = PredictorLiveRunCapture.Key(native.NextItem(nativePool)!);
            var entry = state.WithRng(PredictorStream.Rewards, new Xoshiro256StarStar(seed));
            var result = PredictorOperation.Preview(new("Wuppy", context, entry, wuppy, 50, 80), new ChooseEventOption("IGNORE"));
            Check(result.Rewards.Single().Key == expected, "Wuppy ordered pool incl. Token");
            rolled.Add(expected);
        }
        Check(rolled.Count == nativePool.Length, "all Wuppy candidates observed");

        foreach (var (source, type) in new[] { ("WHETSTONE", CardType.Attack), ("WAR_PAINT", CardType.Skill) })
        {
            ExternalCard.TestType = type;
            ModelDb.Inject(typeof(ExternalCard));
            var canonical = ModelDb.Card<ExternalCard>();
            var originals = new[] { 6, 1, 3 }.Select(damage =>
            {
                var card = canonical.ToMutable();
                card.DynamicVars.Damage.BaseValue = damage;
                return card;
            }).ToList();
            var cards = originals.Select(c => PredictorLiveRunCapture.Card(c, context.Catalog!, true)).ToImmutableArray();
            var definition = new PredictorCardDefinition(cards[0], "Common", default, false, false, false)
            {
                Levels = [new(0, 1, false, false, false, false, false, false), new(1, 1, false, false, false, false, false, false)]
            };
            var cardContext = context with { Catalog = context.Catalog! with { CardDefinitions = [definition] } };
            var entry = state with { Cards = cards, Deck = cards.Select(c => c.Id).ToImmutableArray() };
            for (ulong seed = 0; seed < 8; seed++)
            {
                var native = new Rng(seed);
                var expected = originals.ToList().StableShuffle(native).Take(2).Select(PredictorLiveRunCapture.Id).ToHashSet();
                var result = PredictorSettlementEffects.AfterRelicAdded(entry.WithRng(PredictorStream.NonCombatGeneration,
                    new Xoshiro256StarStar(seed)), new("RELIC", source), cardContext);
                Check(expected.SetEquals(result.Cards.Where(c => c.UpgradeLevel == 1).Select(c => c.Id)), source + " concrete copies");
            }
            ModelDb.Remove(typeof(ExternalCard));
        }

        var colorEvent = ModelDb.Event<ColorfulPhilosophers>();
        CardPoolModel green = ModelDb.CardPool<SilentCardPool>();
        EventOption ColorButton(CardPoolModel pool) => new(colorEvent, () => { GC.KeepAlive(pool); return Task.CompletedTask; },
            new("events", "unused.title"), new("events", "unused.description"),
            "COLORFUL_PHILOSOPHERS.pages.INITIAL.options." + pool.EnergyColorName.ToUpperInvariant(), []);
        var red = ModelDb.CardPool<IroncladCardPool>();
        var harmony = new HarmonyLib.Harmony("rt2.check.colorful-shared-pool");
        var getter = typeof(MegaCrit.Sts2.Core.Models.Characters.Defect).GetProperty(nameof(CharacterModel.CardPool))!.GetMethod!;
        var postfix = typeof(EventOperationAuthorityChecks).GetMethod(nameof(UseRedPool),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        PredictorEventOption[] options;
        harmony.Patch(getter, postfix: new HarmonyLib.HarmonyMethod(postfix));
        try
        {
            Check(ModelDb.AllCharacters.Count(c => c.CardPool.Id == red.Id) >= 2, "two registered owners of red");
            options = new[] { ColorButton(red), ColorButton(green) }.Select(o => PredictorOperationEventCapture.Option(colorEvent, o)).ToArray();
        }
        finally { harmony.Unpatch(getter, postfix); }
        var captured = options[1];
        var greenKey = PredictorLiveRunCapture.Key(green);
        Check(captured.OfferedCardPool == greenKey && captured.Key == "SILENT", "button retains actual pool");
        var greenCards = context.Catalog!.CharacterCards.Select(c => c.CardKey).ToImmutableArray();
        var redKey = PredictorLiveRunCapture.Key(ModelDb.CardPool<IroncladCardPool>());
        var aliases = new PredictorCharacterPool[] { new(new("CHARACTER", "IRONCLAD"), [], redKey),
            new(new("CHARACTER", "EXTERNAL_RED"), [], redKey), new(new("CHARACTER", "SILENT"), greenCards, greenKey) };
        var colorContext = context with { Catalog = context.Catalog with { CharacterPools = aliases.ToImmutableArray() } };
        var scene = new PredictorOperationEventScene(PredictorLiveRunCapture.Key(colorEvent), eventRng,
            options.ToImmutableArray(), new(50, 80));
        var forecast = PredictorOperation.Preview(new("Colorful", colorContext, state, scene, 50, 80), new ChooseEventOption(captured.Key));
        Check(forecast.Rewards.Length == 3 && forecast.Rewards.All(r => r.Cards.Length == 3 &&
            r.Cards.All(c => greenCards.Contains(c.Key))), "selected green pool with two owners of red");

        Console.WriteLine("EVENT_OPERATION_AUTHORITY_PASS Wuppy=16 pooled Token; Whetstone+WarPaint=16 native shuffles external copies; Colorful=3 selected-pool rewards");
        return 0;
    }

    private static void UseRedPool(ref CardPoolModel __result) => __result = ModelDb.CardPool<IroncladCardPool>();

    private static void Check(bool value, string reason)
    {
        if (!value) throw new InvalidOperationException("EventOperationAuthority." + reason);
    }
}
