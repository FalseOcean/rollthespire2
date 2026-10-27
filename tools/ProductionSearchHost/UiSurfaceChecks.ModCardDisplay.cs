using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using RolltheSpire2.Core.Effects.Snapshots;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;
using RolltheSpire2.Ui.InRunOperations;

internal static partial class UiSurfaceChecks
{
    // This host-assembly card exercises only native display and detached
    // upgrade construction, not third-party prediction/mechanics parity.
    private static async Task RunModCardDisplayChecks(Node root, string output, SubViewport viewport,
        Control hoverRoot, RunState run, Player player, Func<string> signature, Action<bool, string> check)
    {
        string before = signature();
        var type = typeof(OperationDisplayProbeCard);
        check(!ModelDb.Contains(type), "mod display fixture starts without a registered probe");
        ModelDb.Inject(type);
        var canonical = ModelDb.Card<OperationDisplayProbeCard>();
        var live = canonical.ToMutable();
        var table = LocManager.Instance.GetTable("cards");
        var translations = PredictorOperationCapture.Read<Dictionary<string, string>>(table, "_translations");
        string prefix = canonical.Id.Entry + ".";
        var keys = new[] { "title", "description" }.Select(suffix => prefix + suffix).ToArray();
        var prior = keys.ToDictionary(key => key, key => translations.TryGetValue(key, out var value) ? value : null);
        foreach (var key in keys) translations[key] = table.GetRawText(key.Replace(prefix, "ANGER.", StringComparison.Ordinal));
        NeowOperationOverlay? view = null;
        object? evidence = null;
        string CardState(CardModel card) => JsonSerializer.Serialize(new
        {
            card.Id, card.IsMutable, card.CurrentUpgradeLevel,
            damage = card.DynamicVars.Damage.BaseValue,
            keywords = card.Keywords.Order().ToArray(), card.EnergyCost.Canonical,
            attached = card.RunState != null, combat = card.CombatState != null
        });
        async Task Frames(int count = 4)
        { for (int i = 0; i < count; i++) await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame); }
        try
        {
            run.AddCard(live, player); player.Deck.AddInternal(live, silent: true);
            check(canonical.GetType().Assembly != typeof(CardModel).Assembly && ReferenceEquals(live.RunState, run),
                "mod display probe is an external registered model with a real live deck instance");
            string canonicalBefore = CardState(canonical), liveBefore = CardState(live), runBefore = signature();
            var key = new ModelKey(canonical.Id.Category, canonical.Id.Entry);
            var basic = new PredictorCard(200001, key, EffectCardType.Attack, 0, 1, true, 0);
            var upgraded = basic with { Id = 200002, UpgradeLevel = 1 };
            var source = new ModelKey("EVENT", "TRASH_HEAP");
            var preview = new OperationPreview(source, [], [], false,
                [new([], [new OperationPreviewSection(source,
                    [new(OperationPreviewAction.AddCard, [], [basic, upgraded], false)], [])], [], 1, true)], [])
                { OptionKey = "GRAB" };
            view = new NeowOperationOverlay(); hoverRoot.AddChild(view); view.SetPreview(preview);
            await Frames();
            view.SetLayout(new(new Vector2(80, 180), view.SummaryDesiredSize),
                new(new Vector2(560, 180), new Vector2(1250, 780)));
            await Frames();
            var cards = Descendants(view).OfType<NCard>().OrderBy(card => card.Model!.CurrentUpgradeLevel).ToArray();
            check(cards.Length == 2 && cards.All(card => card.IsVisibleInTree()),
                "external base and upgraded cards render visible native NCard faces");
            check(cards.Select(card => card.Model!.CurrentUpgradeLevel).SequenceEqual([0, 1]) &&
                cards.Select(card => card.Model!.DynamicVars.Damage.BaseValue).SequenceEqual([6m, 8m]) &&
                cards[1].Model!.Title == cards[0].Model!.Title + "+",
                "external display runs its normal detached upgrade and native upgraded title");
            check(cards.All(card => card.Model!.GetType() == type && card.Model.IsMutable &&
                !ReferenceEquals(card.Model, canonical) && !ReferenceEquals(card.Model, live) &&
                ReferenceEquals(card.Model.CanonicalInstance, canonical) &&
                card.Model.RunState == null && card.Model.CombatState == null && card.Model.Owner == null) &&
                !ReferenceEquals(cards[0].Model, cards[1].Model),
                "both external card faces own independent canonical clones with no live owner/run/combat");
            check(cards.All(card => Descendants(card).OfType<Control>().Append(card).All(control =>
                control.MouseFilter == Control.MouseFilterEnum.Ignore && control.FocusMode == Control.FocusModeEnum.None)),
                "external native card faces remain passive on the original overlay");
            check(cards.All(card => card.GetNode<TextureRect>("%Portrait").Texture is { } texture &&
                texture.ResourcePath == ModelDb.Card<Anger>().PortraitPath && texture.GetWidth() > 0) &&
                cards.All(card => !card.GetParent().GetChildren().OfType<Label>().Any()),
                "external cards use loaded official native portraits without a text fallback");
            check(view.VisibleRegions.Count > 0 && view.VisibleRegions.All(region => view.GetViewportRect().Grow(.5f).Encloses(region)),
                "external native card display stays inside the viewport");
            check(CardState(canonical) == canonicalBefore && CardState(live) == liveBefore && signature() == runBefore,
                "base/upgraded rendering leaves the canonical definition and live deck/run/RNG unchanged");
            await root.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            check(viewport.GetTexture().GetImage().SavePng(Path.Combine(output, "operation-mod-card-display-zh.png")) == Error.Ok,
                "external native card display screenshot is saved");
            evidence = new
            {
                passed = true, displayOnly = true, registeredExternalAssembly = canonical.GetType().Assembly.GetName().Name,
                nativeFaces = cards.Length, upgradeLevels = cards.Select(card => card.Model!.CurrentUpgradeLevel).ToArray(),
                damage = cards.Select(card => card.Model!.DynamicVars.Damage.BaseValue).ToArray(),
                canonicalAndLiveInputsUnchanged = true, thirdPartyMechanicsParity = false, ownerRuntimeAcceptance = false
            };
        }
        finally
        {
            view?.Free(); player.Deck.RemoveInternal(live, silent: true); run.RemoveCard(live);
            foreach (var key in keys)
                if (prior[key] is { } original) translations[key] = original;
                else translations.Remove(key);
            ModelDb.Remove(type);
        }
        check(signature() == before && !ModelDb.Contains(type), "mod display fixture restores its live run and registration");
        File.WriteAllText(Path.Combine(output, "operation-mod-card-display-result.json"),
            JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
    }

    public sealed class OperationDisplayProbeCard : CardModel
    {
        public OperationDisplayProbeCard() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }
        public override CardPoolModel Pool => ModelDb.CardPool<IroncladCardPool>();
        public override string PortraitPath => ModelDb.Card<Anger>().PortraitPath;
        protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(6m, ValueProp.Move)];
        protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(2m);
    }
}
