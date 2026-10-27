using System.Reflection;
using System.Collections.Immutable;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorCrystalAssistantChecks
{
    private static void CheckCrystalModCapture()
    {
        Type[] models = [typeof(CrystalProbeCharacter),typeof(CrystalProbePool),typeof(CrystalProbeCard),typeof(CrystalProbeRelic),typeof(CrystalProbeModifier)];
        foreach(var type in models) ModelDb.Inject(type);
        var loc=MegaCrit.Sts2.Core.Localization.LocManager.Instance.GetTable("characters");
        loc.MergeWith(loc.Keys.Where(k=>k.StartsWith("IRONCLAD.",StringComparison.Ordinal)).ToDictionary(k=>k.Replace("IRONCLAD.","CRYSTAL_PROBE_CHARACTER."),loc.GetRawText));
        try
        {
            var player=Player.CreateForNewRun(ModelDb.Character<CrystalProbeCharacter>(),UnlockState.all,77);
            var run=RunState.CreateForTest([player],modifiers:[ModelDb.Modifier<CrystalProbeModifier>().ToMutable(),
                ModelDb.Modifier<MegaCrit.Sts2.Core.Models.Modifiers.DeprecatedModifier>().ToMutable()],seed:"000000000000",ascensionLevel:10);
            run.CurrentActIndex=1;
            typeof(RunManager).GetProperty("State",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.SetValue(RunManager.Instance,run);
            LocalContext.NetId=player.NetId;
            player.AddRelicInternal(ModelDb.Relic<CrystalProbeRelic>().ToMutable(),silent:true);
            player.AddRelicInternal(ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.MoltenEgg>().ToMutable(),silent:true);
            player.PopulateRelicGrabBagIfNecessary(run.Rng.UpFront);
            var buckets=(Dictionary<RelicRarity,List<RelicModel>>)typeof(RelicGrabBag).GetField("_deques",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(player.RelicGrabBag)!;
            buckets[RelicRarity.Common].Insert(0,ModelDb.Relic<CrystalProbeRelic>());
            var ev=(CrystalSphere)ModelDb.Event<CrystalSphere>().ToMutable();ev.BeginEvent(player,null,false).GetAwaiter().GetResult();
            var game=new CrystalSphereMinigame(player,ev.Rng,6);
            var before=CrystalSphereLiveCapture.SceneFingerprint(run,player,ev,game);
            var source=CrystalSphereLiveCapture.CaptureScene(run,player,ev,game,"0.111.0").Branches.Single().Snapshot;
            Check(source.Context.Character.Entry=="CRYSTAL_PROBE_CHARACTER","Registered mod character passes the production Crystal capture");
            Check(source.Context.Crystal!.EligibleRelics.Contains(new("RELIC","CRYSTAL_PROBE_RELIC")),
                "A mod relic present only in the actual bag is not rejected by the global catalog");
            Check(source.Context.Catalog!.CharacterCards.Any(c=>c.CardKey.Entry=="CRYSTAL_PROBE_CARD"),"Actual mod pool contributes its own card");
            Check(source.Context.Crystal!.UnmodeledSources.Any(k=>k.Entry=="CRYSTAL_PROBE_RELIC") &&
                source.Context.Crystal.UnmodeledSources.Any(k=>k.Entry=="CRYSTAL_PROBE_MODIFIER") &&
                source.Context.Crystal.UnmodeledSources.Any(k=>k.Entry=="DEPRECATED_MODIFIER"),"Unknown sources and legacy modifier placeholders are retained and explicitly recorded");
            var path=CardPath(source);var predicted=PredictorCrystalExplorer.Replay(source,path);
            Check(predicted.Phase==PredictorPhase.Rewards,"Unknown relic and modifier do not block actual reveal settlement");
            Check(predicted.Rewards.Where(r=>r.Kind==PredictorRewardKind.Card).SelectMany(r=>r.Cards)
                .Where(c=>c.Type==RolltheSpire2.Core.Effects.Snapshots.EffectCardType.Attack).All(c=>c.UpgradeLevel>0),"Known Egg upgrade effect is retained alongside unknown content");
            var proto=source.Context.Catalog.CardDefinitions.Single(d=>d.Prototype.Key.Entry=="CRYSTAL_PROBE_CARD").Prototype;
            var upgraded=PredictorObtainSources.AddCard(source.Context,source.State,proto);
            Check(upgraded.Card(upgraded.Deck[^1]).UpgradeLevel==1,"Egg upgrades an unknown card on detached deck insertion");
            var choice=predicted.Rewards.First(r=>r.Kind==PredictorRewardKind.Card).Cards[0];
            Check(PredictorCrystalExplorer.VerifySelection(source,new(path,predicted.Rewards),[new(PredictorRewardKind.Card,choice.Key,choice.UpgradeLevel)])!=null,
                "Mod context yields an executable selected-target witness");
            Check(before==CrystalSphereLiveCapture.SceneFingerprint(run,player,ev,game),"Mod capture and search never mutate native state");
            bool strictRejected=false;
            try { PredictorSeedState.ValidateContext(source.Context with { Crystal=null }); }
            catch(InvalidDataException) { strictRejected=true; }
            Check(strictRejected,"Crystal approximation does not relax the donor's non-Crystal context contract");
            Console.WriteLine("CRYSTAL_MOD_CAPTURE_PASS registered-custom-character/card/relic/modifier;known-egg-preserved;target-witness=true;native-unmodified=true;scope=controlled-probe-not-third-party-mod-parity");
        }
        finally { foreach(var type in models) ModelDb.Remove(type); }
    }

    public sealed class CrystalProbeCharacter : CharacterModel
    {
        private static Ironclad Basis=>ModelDb.Character<Ironclad>();
        public override Godot.Color NameColor=>Basis.NameColor;
        public override MegaCrit.Sts2.Core.Entities.Characters.CharacterGender Gender=>Basis.Gender;
        protected override CharacterModel? UnlocksAfterRunAs=>null;
        public override int StartingHp=>80;
        public override int StartingGold=>200;
        public override CardPoolModel CardPool=>ModelDb.CardPool<CrystalProbePool>();
        public override RelicPoolModel RelicPool=>Basis.RelicPool;
        public override PotionPoolModel PotionPool=>Basis.PotionPool;
        public override IEnumerable<CardModel> StartingDeck=>Basis.StartingDeck;
        public override IReadOnlyList<RelicModel> StartingRelics=>Basis.StartingRelics;
        public override float AttackAnimDelay=>0;
        public override float CastAnimDelay=>0;
        public override List<string> GetArchitectAttackVfx()=>[];
    }
    public sealed class CrystalProbePool : CardPoolModel
    {
        public override string Title=>"Crystal probe";
        public override string EnergyColorName=>"red";
        public override string CardFrameMaterialPath=>"red";
        public override Godot.Color DeckEntryCardColor=>Godot.Colors.Red;
        public override bool IsColorless=>false;
        protected override CardModel[] GenerateAllCards()=>ModelDb.CardPool<IroncladCardPool>().AllCards.Append(ModelDb.Card<CrystalProbeCard>()).ToArray();
    }
    public sealed class CrystalProbeCard : CardModel
    {
        public CrystalProbeCard() : base(1,CardType.Attack,CardRarity.Common,TargetType.AnyEnemy) { }
        public override CardPoolModel Pool=>ModelDb.CardPool<CrystalProbePool>();
    }
    public sealed class CrystalProbeRelic : RelicModel { public override RelicRarity Rarity=>RelicRarity.Common; }
    public sealed class CrystalProbeModifier : ModifierModel { }
}
