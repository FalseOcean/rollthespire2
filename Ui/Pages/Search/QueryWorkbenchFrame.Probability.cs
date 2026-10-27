using RolltheSpire2.Ui.Shell;
using RolltheSpire2.Ui.Workspace;
using RolltheSpire2.Core.Relics;
using RolltheSpire2.Ui.Tooltips;
using System.Globalization;
using RolltheSpire2.Bootstrap;
using RolltheSpire2.Core.Identity;
using RolltheSpire2.Presentation.ContentNames;
using RolltheSpire2.Presentation.Ui1;
using RolltheSpire2.Search.Compilation;
using RolltheSpire2.Search.Contracts;
using RolltheSpire2.Search.FamilyExecution;
using RolltheSpire2.Search.Selectivity;
using RolltheSpire2.Search.Semantics;
using RolltheSpire2.Ui.Persistence;

namespace RolltheSpire2.Ui.Pages.Search;

internal sealed partial class QueryWorkbenchFrame
{
    private string _analysisKey="", _expectationText="", _conditionText="";
    private FamilySearchEtaProjectionV1? _previewEta;
    private readonly WorkbenchProbabilityPreviewQueue _probabilityPreview = new();
    private bool _probabilityPending;
    private void InvalidateProbabilityPreview()
    {
        _probabilityPreview.Invalidate();
        _probabilityPending = false;
        _analysisKey = "";
    }

    private void InvalidateEnvironmentProbabilityPreview()
    {
        InvalidateProbabilityPreview();
        // The result/history surface belongs to its captured search, not this
        // draft's new environment. Only withdraw the draft estimate here.
        if (_persistence.Runtime.Receiving || _showResults) return;
        _draftCompileIssue = "";
        PresentPendingProbabilityPreview(awaitingRefresh: true);
    }

    private void PresentPendingProbabilityPreview(bool awaitingRefresh = false)
    {
        _previewEta = null;
        // Pending includes waiting for the normal refresh tick to submit work.
        _probabilityPending = true;
        _expectationText = _conditionText = "";
        _familyEntries.Clear(); RenderFamilyDashboard();
        _expectationContext = awaitingRefresh
            ? (_language == "zh" ? "环境已变化，等待更新条件估算…" : "Environment changed; awaiting updated estimates…")
            : (_language == "zh" ? "正在计算当前条件…" : "Calculating current conditions…");
        PresentExpectations(null, null, null);
        if (_expectationValues[0] is { } rarity)
        {
            rarity.Text = awaitingRefresh
                ? (_language == "zh" ? "等待更新…" : "Awaiting update…")
                : (_language == "zh" ? "计算中…" : "Calculating…");
            SearchTooltip.Set(rarity, rarity.Text);
        }
    }

    // Hiding must cancel in-flight work and reject late publication. Completed
    // presentation remains valid until query/context/language/estimate inputs change.
    private void SuspendProbabilityPreview()
    {
        _probabilityPreview.Invalidate();
        if (_probabilityPending) _analysisKey = "";
        _probabilityPending = false;
    }

    internal static string CompactNumber(double value)
    {
        if(!double.IsFinite(value) || value<0) return "—";
        if (value >= 1e15) return value.ToString("0.##E+0", CultureInfo.InvariantCulture);
        string[] units=["","K","M","B","T"];
        int unit=0;while(value>=999.5 && unit<units.Length-1){value/=1000;unit++;}
        return value.ToString(value>=100?"0":value>=10?"0.#":"0.##",CultureInfo.InvariantCulture)+units[unit];
    }
    internal static string Rarity(double? probability) => probability is not { } p || !double.IsFinite(p) || p<0 || p>1
        ? "—" : p==0 ? "1 / ∞" : double.IsPositiveInfinity(1/p)
            ? "1 / 10^" + (-Math.Log10(p)).ToString("0.##", CultureInfo.InvariantCulture)
            : "1 / "+CompactNumber(1/p);
    private string Rate(double? rate) => rate is >0 && double.IsFinite(rate.Value) ? _text.Format("workflow.rate",CompactNumber(rate.Value)) : "—";
    private string Duration(double? milliseconds)
    {
        if(milliseconds is not { } ms || !double.IsFinite(ms) || ms<0) return "—";
        double seconds=ms/1000;
        return seconds<60 ? _text.Format("workflow.seconds",seconds.ToString("0.#",CultureInfo.InvariantCulture)) :
            seconds<3600 ? _text.Format("workflow.minutes",(seconds/60).ToString("0.#",CultureInfo.InvariantCulture)) :
            seconds<86400 ? _text.Format("workflow.hours",(seconds/3600).ToString("0.#",CultureInfo.InvariantCulture)) :
            _text.Format("workflow.days",CompactNumber(seconds/86400));
    }
    private int SearchWorkers => SearchWorkspace.ResolveSearchWorkers(
        _persistence.Preferences.SearchWorkerBudget, System.Environment.ProcessorCount);
    private string AnalysisKey(string query) => query+_editorInputs.Freshness+_language+":"+_target+":"+_persistence.Preferences.SearchMode+":"+SearchWorkers+
        ":gpu="+FamilyDeviceProfileFoundation.Initialization.Capture().Revision;
    private string _draftCompileIssue="";
    private bool UpdateProbabilityPanel(WorkbenchSearchDraft draft,string serialized)
    {
        // The first restore can publish editor changes before the estimate surface exists.
        if (!IsVisibleInTree() || _familyRows is null) return true;
        if(_analysisKey==AnalysisKey(serialized)) return _draftCompileIssue.Length==0;
        _analysisKey=AnalysisKey(serialized);_draftCompileIssue="";_expectationContext="";
        _probabilityPreview.Invalidate();
        _probabilityPending = false;
        CompiledSearch compiled;
        try { compiled=_editorInputs.Compile(draft, _runtime); }
        catch(Exception ex) {
            _draftCompileIssue=Explain(ex);
            _previewEta=null;_familyEntries.Clear();
            var q=draft.Query;
            void Pending(string id,bool present) {if(present)_familyEntries.Add(new(id,FamilyTitle(id),null,"—",""));}
            Pending("N",q.OpeningRoute is not null || q.StructuredOpeningEffects.Count>0);
            Pending("T",q.TransformationAggregate is not null);Pending("C",q.HasCombatRewardConstraints);
            Pending("A",q.AncientBranches.Any(b=>b.OptionAny.Count>0||b.SeaGlassTargetAny.Count>0)||q.LegacyWorld.AncientOptionFilters.Count>0);
            Pending("W",q.AncientBranches.Count>0||q.EventSequenceConstraints.Count>0||q.VariantBossBranches.Count>0);
            Pending("R",q.TreasureRelics is { IsEmpty: false }||q.RelicSequenceConstraints.Count>0||q.RelicShopSequenceConditions.Count>0||q.StructuredOpeningEffects.Any(NeowReplayPlan.IsCapsule));
            Pending("E",q.EventResultConditions.Count>0||q.FirstActUnknownEventPrefixCount>0);
            Pending("M",q.StandardMaps.Count>0);Pending("S",q.MerchantColorlessSequenceConditions.Count>0);
            ApplyFamilySources(q); RenderFamilyDashboard();PresentExpectations(null,null,null);
            return false;
        }
        PresentPendingProbabilityPreview();
        _probabilityPreview.Submit(new(_analysisKey, compiled, _target, SearchWorkers, _persistence.Preferences.SearchMode == "CPU"));
        // Estimate failures do not change admission; canonical compile failures do.
        return true;
    }

    private void PollProbabilityPreview()
    {
        if (!IsVisibleInTree() || !_probabilityPreview.TryTake(out var completed) || completed is null ||
            completed.Request.Key != _analysisKey || _persistence.Runtime.Receiving || _showResults) return;
        _probabilityPending = false;
        if (completed.Error is not null)
        {
            // The worker records the complete failure even when this page is gone.
            _familyEntries.Clear(); RenderFamilyDashboard();
            _expectationContext = _language == "zh" ? "当前条件的估算不可用。" : "Estimate unavailable for these conditions.";
            PresentExpectations(null, null, null);
            return;
        }
        try
        {
            DescribeProbability(completed.Request.Compiled, completed.Preview!);
            RenderFamilyDashboard();
        }
        catch (Exception ex)
        {
            RolltheSpire2.Infrastructure.Diagnostics.RuntimeIssueLedger.Current.Record(ex,
                "ProbabilityPresentationFailed", nameof(QueryWorkbenchFrame), "PresentEstimate", "issues.estimate_failed");
            _familyEntries.Clear(); RenderFamilyDashboard(); _expectationContext = "";
            PresentExpectations(null, null, null);
        }
        if (completed.Milliseconds >= 100)
            RuntimeLog.Info($"workbenchPreviewCompleted=true;elapsedMs={completed.Milliseconds:F1};background=true");
    }

    internal string DescribeProbability(CompiledSearch compiled) => DescribeProbability(compiled,
        WorkbenchProbabilityPreview.Compute(compiled, _target, SearchWorkers, _persistence.Preferences.SearchMode == "CPU"));

    internal string DescribeProbability(CompiledSearch compiled, WorkbenchProbabilityPreview preview)
    {
        foreach (string issue in preview.Issues) RuntimeLog.Warn(issue);
        if (compiled.Context.Party is { } party)
        {
            var partyProbability = preview.Joint;
            _familyEntries.Clear(); _previewEta = preview.Eta;
            string PartyCost(string id)
            {
                var entry = preview.Costs.FirstOrDefault(pair => pair.Key.StartsWith(id + ".", StringComparison.Ordinal));
                return entry.Key is null ? "—" : BestQuotedSpeed(entry.Value.Cpu, entry.Value.Gpu);
            }
            if (PartyInitialQuery.HasWorld(compiled.NormalizedQuery) || compiled.NormalizedQuery.EventSequenceConstraints.Count > 0 ||
                compiled.NormalizedQuery.LegacyWorld.AncientIdentityFilters.Count > 0)
                _familyEntries.Add(new("W", FamilyTitle("W"), null, PartyCost("W"), _language == "zh" ? "整桌共用条件；概率只计一次。" : "Shared table conditions; probability counted once."));
            if (compiled.NormalizedQuery.Players.Any(p => !p.Offers.IsEmpty || p.SelectedOption is not null || p.Conditions.OpeningRoute is not null))
                _familyEntries.Add(new("N", FamilyTitle("N"), null, PartyCost("N"), string.Join("\n", compiled.NormalizedQuery.Players.Where(p => !p.Offers.IsEmpty || p.SelectedOption is not null || p.Conditions.OpeningRoute is not null)
                    .Select(p => $"P{p.Slot + 1} · " + (p.SelectedOption is null ? (_language == "zh" ? "初始选项" : "Initial offers") :
                        p.SelectedOption.Intent == Core.Effects.PartyNeowIntent.PremiseOnly ? (_language == "zh" ? "仅领取前提" : "Premise only") : (_language == "zh" ? "领取结果" : "Selected result")) + ": " +
                        string.Join(",", p.Offers.All.Concat(p.SelectedOption is null ? p.Conditions.OpeningRoute is { } n ? [n.RouteRelicKey] : [] : new[] { p.SelectedOption.Option }).Select(k =>
                            RolltheSpire2.Infrastructure.ContentNames.RuntimeGameContentNameResolver.Create(_language).Resolve(k, GameContentKind.Relic)))))));
            foreach (var family in new[] { "R", "S", "E", "A", "C" })
            {
                var owners = compiled.NormalizedQuery.Players.Where(p => family switch
                {
                    "R" => p.Conditions.RelicSequenceConstraints.Count > 0 || p.Conditions.RelicShopSequenceConditions.Count > 0 ||
                        p.Conditions.StructuredOpeningEffects.Any(NeowReplayPlan.IsCapsule) ||
                        !p.Conditions.LegacyNeow.CapsuleContainedRelics.IsEmpty || p.Conditions.LegacyNeow.RequireWhetstone || p.Conditions.LegacyNeow.RequireWarPaint,
                    "S" => p.Conditions.MerchantColorlessSequenceConditions.Count > 0,
                    "E" => p.Conditions.EventResultConditions.Count > 0,
                    "A" => p.Conditions.AncientBranches.Any(b => b.OptionAny.Count > 0 || b.SeaGlassTargetAny.Count > 0) ||
                        p.Conditions.LegacyWorld.AncientOptionFilters.Count > 0 || p.Conditions.LegacyWorld.AncientSeaGlassTargetFilters.Count > 0,
                    "C" => p.Conditions.HasCombatRewardConstraints,
                    _ => false
                }).ToArray();
                if (owners.Length > 0)
                {
                    double? probability = null;
                    string detail = string.Join(" · ", owners.Select(p => $"P{p.Slot + 1}"));
                    if (family == "C")
                    {
                        var terms = owners.Select(p => (Slot: p.Slot, P: partyProbability.KnownComponents
                            .FirstOrDefault(c => c.Id == $"party:player:{p.Slot}/C")?.Probability)).ToArray();
                        // The joint authority exposes each player's conditional C factor,
                        // not an independent family-wide probability. Never combine them here.
                        if (terms.Length == 1) probability = terms[0].P;
                        detail = (_language == "zh" ? "给定各自开局条件\n" : "Conditional on each player's opening\n") +
                            string.Join("\n", terms.Select(t => $"P{t.Slot + 1} · " + Rarity(t.P)));
                    }
                    _familyEntries.Add(new(family, FamilyTitle(family), probability, PartyCost(family), detail,
                        string.Join(" / ", owners.Select(p => $"P{p.Slot + 1}"))));
                }
            }
            if (compiled.NormalizedQuery.StandardMaps.Count > 0)
                _familyEntries.Add(new("M", FamilyTitle("M"), partyProbability.KnownComponents.FirstOrDefault(c => c.Id == "party:map")?.Probability,
                    PartyCost("M"), _language == "zh" ? "共用开局地图" : "Shared initial maps"));
            string premise = _language == "zh" ? "共享事实只计一次；个人条件采用独立近似。" : "Shared facts counted once; personal conditions use an independence approximation.";
            double? partyRate = EtaRate(_previewEta);
            _expectationText = ExpectationText(_previewEta?.FirstResultSearchMeanMs, partyProbability.Probability, partyRate) + "\n" + premise;
            if (partyProbability.Probability is null)
                _expectationText += "\n" + (_language == "zh" ? "概率未知：部分条件尚无多人模型。" : "Probability unknown: some conditions lack a multiplayer model.");
            _expectationContext = premise + (partyProbability.Probability is null
                ? "\n" + (_language == "zh" ? "概率未知：部分条件尚无多人模型。" : "Probability unknown: some conditions lack a multiplayer model.") : "");
            PresentExpectations(_previewEta?.FirstResultSearchMeanMs, partyProbability.Probability, partyRate);
            _conditionText = premise + "\n" + string.Join("\n", partyProbability.KnownComponents.Select(c => c.Condition + " · " + Rarity(c.Probability))) +
                "\n" + string.Join("\n", partyProbability.UnknownComponents);
            ApplyFamilySources(compiled.NormalizedQuery, partyProbability.JointlyPriced);
            return _expectationText + "\n" + _conditionText;
        }
        var view=preview.View!;
        var q=compiled.NormalizedQuery;
        var names=RolltheSpire2.Infrastructure.ContentNames.RuntimeGameContentNameResolver.Create(_language);
        string Name(ModelKey k) => names.Resolve(k,k.Category switch {
            "CARD"=>GameContentKind.Card,"RELIC"=>GameContentKind.Relic,"EVENT"=>GameContentKind.Event,
            "CHARACTER"=>GameContentKind.Character,"POTION"=>GameContentKind.Potion,"ENCOUNTER"=>GameContentKind.Encounter,
            "ACT"=>GameContentKind.Act,_=>GameContentKind.Relic });
        string Keys(IEnumerable<ModelKey> keys) => string.Join(" · ",keys.Select(Name));
        var relicKeys=q.RelicSequenceConstraints.SelectMany(c=>c.Keys.Any.Concat(c.Keys.All).Concat(c.Keys.Ban))
            .Concat(q.RelicShopSequenceConditions.SelectMany(c=>c.Slots).Where(k=>k.HasValue).Select(k=>k!.Value))
            .Concat(q.TreasureRelics is { } treasure ? treasure.Slots.Take(treasure.Count).Where(k=>k.HasValue).Select(k=>k!.Value) : []);
        string Probability(double? p) => Rarity(p);
        var output=new List<string>();
        _familyEntries.Clear();
        _previewEta=preview.Eta;
        var costs=preview.Costs.ToDictionary(p=>p.Key,p=>BestQuotedSpeed(p.Value.Cpu,p.Value.Gpu));
        string Cost(params string[] ids) {
            string FamilyName(string id)=>_text.Get(id switch {
                "N.Neow"=>"query.domain.neow","R.Relic"=>"query.domain.relics","W.World"=>"coverage.world",
                "A.AncientOption"=>"query.domain.ancients",_=>"query.domain.events"});
            var found=ids.Where(costs.ContainsKey).Select(id=>ids.Length>1?FamilyName(id)+" · "+costs[id]:costs[id]).ToArray();
            return found.Length==0?"—":string.Join("\n",found.Distinct());
        }
        foreach(var row in view.Rows)
        {
            if(row.Kind is SearchProbabilityRowKind.EventResult or SearchProbabilityRowKind.FirstActUnknownEventPrefix) continue;
            var (title,detail,cost)=row.Kind switch {
                SearchProbabilityRowKind.Neow => (_text.Get("query.domain.neow"),Keys((q.OpeningRoute is { } n?new[]{n.RouteRelicKey}:q.LegacyNeow.NeowRelics.Any.Concat(q.LegacyNeow.NeowRelics.All))
                    .Concat(q.OpeningRouteRelicRequirement?.RequiredRelicKeys??[]).Concat(q.StructuredOpeningEffects.SelectMany(c=>c.OutputKeys))),
                    q.StructuredOpeningEffects.Any(c=>c.OutputKind==NeowStructuredOutputKind.Relic)?Cost("N.Neow","R.Relic"):Cost("N.Neow")),
                SearchProbabilityRowKind.Relic => (_text.Get("query.domain.relics"),Keys(relicKeys),Cost("R.Relic")),
                SearchProbabilityRowKind.CapsuleRelicJoint => (_text.Get("coverage.capsule.joint"),Keys(q.StructuredOpeningEffects.SelectMany(c=>c.OutputKeys).Concat(relicKeys).Distinct()),Cost("R.Relic","N.Neow")),
                SearchProbabilityRowKind.Ancient => (_text.Get("query.domain.ancients"),Keys(q.AncientBranches.SelectMany(b=>b.OptionAny).Concat(q.LegacyWorld.AncientOptionFilters.SelectMany(f=>f.Keys.All))),Cost("A.AncientOption","W.World")),
                SearchProbabilityRowKind.WorldEvent => (_text.Get("coverage.world"),Keys(q.EventSequenceConstraints.SelectMany(c=>c.Keys.Any.Concat(c.Keys.All).Concat(c.Keys.Ban))
                    .Concat(q.AncientBranches.Select(b=>b.AncientKey)).Concat(q.LegacyWorld.AncientIdentityFilters.SelectMany(f=>f.Keys.Any.Concat(f.Keys.All).Concat(f.Keys.Ban)))),Cost("W.World")),
                SearchProbabilityRowKind.CombatReward => (_text.Get("query.domain.combat_rewards"),Keys((q.CombatCardRewards?.Slots.Where(k=>k.HasValue).Select(k=>k!.Value)??[])
                    .Concat(q.CombatPotionRewards?.Slots.Where(p=>p.PotionKey.HasValue).Select(p=>p.PotionKey!.Value)??[])
                    .Concat(q.CombatCardExclusion?.CardKeys??[])),Cost("C.CombatReward")),
                SearchProbabilityRowKind.TransformationAggregate => (_text.Get("query.summary.transform_aggregate"),Keys(q.TransformationAggregate!.TargetMultiset),Cost("T.TransformationAggregate")),
                _ => (_text.Get("coverage.shop"),Keys(q.MerchantColorlessSequenceConditions.SelectMany(c=>c.Slots).Where(k=>k.HasValue).Select(k=>k!.Value)),Cost("S.MerchantShopColorless"))
            };
            if(row.Kind==SearchProbabilityRowKind.TransformationAggregate && q.TransformationAggregate is { RequiresRareRemainder: true } mixed)
                detail += "\n" + _text.Format("query.transform.remaining_rare", mixed.OpportunityCount - mixed.TargetMultiset.Count);
            if(row.Kind==SearchProbabilityRowKind.TransformationAggregate && row.Probability is null)
                detail += (detail.Length>0 ? "\n" : "") + (_language=="zh"
                    ? "概率未知：这组来源或开局副作用的联合模型尚未闭合；不会用独立相乘代替。"
                    : "Probability unknown: the joint model for these sources or opening effects is incomplete; no independent-product substitute is used.");
            string id=row.Kind switch {
                SearchProbabilityRowKind.Neow=>"N",SearchProbabilityRowKind.Relic=>"R",SearchProbabilityRowKind.CapsuleRelicJoint=>"N/R",
                SearchProbabilityRowKind.Ancient=>"A",SearchProbabilityRowKind.WorldEvent=>"W",SearchProbabilityRowKind.CombatReward=>"C",
                SearchProbabilityRowKind.TransformationAggregate=>"T",_=>"S"};
            _familyEntries.Add(new(id,FamilyTitle(id),row.Probability,id == "N/R" ? Cost("N.Neow", "R.Relic") :
                costs.FirstOrDefault(k=>k.Key.StartsWith(id+".")).Value??"—",detail));
            output.Add(title+(detail.Length>0?"\n"+detail:"")+"\n"+_text.Get("workflow.rarity")+" "+Probability(row.Probability)+"\n"+cost);
        }
        if(q.StandardMaps.Count>0)
        {
            var mapEstimate = preview.Map!;
            string detail = MapProbabilityExplanation(mapEstimate.EvidenceCode, mapEstimate.Probability.HasValue, _language=="zh");
            string rarity = mapEstimate.Probability.HasValue ? Probability(mapEstimate.Probability) : (_language=="zh" ? "未知" : "Unknown");
            output.Add(_text.Get("coverage.map")+"\n"+_text.Get("workflow.rarity")+" "+rarity+"\n"+detail+"\n"+Cost("M.StandardMap"));
            _familyEntries.Add(new("M",_text.Get("coverage.map"),mapEstimate.Probability,Cost("M.StandardMap"),detail));
        }
        var eventDetails=new List<string>();
        foreach(var c in q.EventResultConditions) {
            string entry=c.Kind switch {
                EventResultConditionKind.FakeMerchantOfferedFakeRelic=>"FAKE_MERCHANT",
                EventResultConditionKind.TrashHeapDiveRelic or EventResultConditionKind.TrashHeapGrabCard=>"TRASH_HEAP",
                EventResultConditionKind.ColorfulPhilosophersOfferedColor=>"COLORFUL_PHILOSOPHERS",
                EventResultConditionKind.TrialCase=>"TRIAL",EventResultConditionKind.TinkerTimeTypeAndRider=>"TINKER_TIME",
                _=>EventResultTransformSemantics.Entry(c.Kind) };
            string detail=c.Kind switch {
                EventResultConditionKind.TrialCase=>_text.Get("query.event.results.case_"+((int)c.TrialCase!+1)),
                EventResultConditionKind.TinkerTimeTypeAndRider=>_text.Get("query.event.results.card_type_"+((int)c.TinkerCardType!+1))+
                    (c.TinkerRider is { } rider?" · "+_text.Get("query.event.results.rider."+rider.ToString().ToLowerInvariant()):""),
                _=>Name(c.TargetKey)+(c.MorphicGroveSecondCard is { } second?"\n"+Name(second):"") };
            var eventPreview=preview.Events[c];
            var estimate=eventPreview.Estimate;
            string eventSpeed=BestQuotedSpeed(eventPreview.Cpu,eventPreview.Gpu);
            eventDetails.Add(Name(new("EVENT",entry))+"\n"+detail+"\n"+Probability(estimate.Probability)+"\n"+eventSpeed);
            output.Add(Name(new("EVENT",entry))+"\n"+detail+"\n"+_text.Get("workflow.rarity")+" "+Probability(estimate.Probability)+"\n"+eventSpeed);
        }
        if (q.FirstActUnknownEventPrefixCount > 0)
        {
            string detail = _text.Get("query.event.unknown.title") + " · " +
                _text.Get("query.event.unknown.option_" + q.FirstActUnknownEventPrefixCount) + "\n" +
                Probability(view.Rows.FirstOrDefault(row => row.Kind == SearchProbabilityRowKind.FirstActUnknownEventPrefix)?.Probability) +
                "\n" + _text.Get("query.event.unknown.probability_assumption");
            eventDetails.Add(detail);
            output.Add(detail);
        }
        if(eventDetails.Count>0)
        {
            double? eventProbability = q.EventResultConditions.Count == 0 ? 1d :
                view.Rows.FirstOrDefault(row => row.Kind == SearchProbabilityRowKind.EventResult)?.Probability;
            if (q.FirstActUnknownEventPrefixCount > 0)
                eventProbability *= view.Rows.FirstOrDefault(row => row.Kind == SearchProbabilityRowKind.FirstActUnknownEventPrefix)?.Probability;
            _familyEntries.Add(new("E",_text.Get("query.domain.events"),eventProbability,
                Cost("E.EventResult"),string.Join("\n\n",eventDetails)));
        }
        // Execution Families remain visible even when their probability is priced in a shared block.
        foreach(var id in costs.Keys.Select(k=>k.Split('.')[0]))
            if(!_familyEntries.Any(e=>e.Id.Split('/').Contains(id))) _familyEntries.Add(new(id,FamilyTitle(id),null,costs.First(k=>k.Key.StartsWith(id+".")).Value,_text.Get("workflow.shared")));
        double? rate=EtaRate(_previewEta);
        _expectationText=ExpectationText(_previewEta?.FirstResultSearchMeanMs,view.Status==SearchProbabilityQuickViewStatus.Impossible?0:view.TotalProbability,rate);
        _expectationContext = compiled.Context.Authority.UsesBestEffortModel
            ? (view.TotalProbability == 0 || view.Status == SearchProbabilityQuickViewStatus.Impossible
                ? (_language == "zh" ? "按当前模型无匹配；模组规则仍未验证。" : "No match under the current model; Mod rules remain unverified.")
                : (_language == "zh" ? "按当前模型估算；模组规则可能影响结果。" : "Current-model estimate; Mod rules may affect the result.")) : "";
        PresentExpectations(_previewEta?.FirstResultSearchMeanMs,view.Status==SearchProbabilityQuickViewStatus.Impossible?0:view.TotalProbability,rate);
        _conditionText=string.Join("\n\n",output);
        ApplyFamilySources(q);
        return _expectationText+"\n\n"+_conditionText;
    }
    // Labels connect an existing probability projection to its authored sources.
    // They do not price, split or combine any terms, nor select execution Families.
    private void ApplyFamilySources(SearchQuery query, bool jointlyAttributed = false)
    {
        SearchQuery[] sources = [query, ..query.Players.Select(p => p.Conditions)];
        string Source(string key) => _text.Get("workflow.source." + key);
        string RelicSources()
        {
            var labels = new List<string>();
            if (sources.Any(q => q.StructuredOpeningEffects.Any(NeowReplayPlan.IsCapsule) ||
                q.LegacyNeow.RequireSmallCapsule || q.LegacyNeow.RequireLargeCapsule ||
                !q.LegacyNeow.CapsuleContainedRelics.IsEmpty || q.LegacyNeow.RequireWhetstone || q.LegacyNeow.RequireWarPaint))
                labels.Add(Source("neow_capsule"));
            if (sources.Any(q => q.RelicSequenceConstraints.Any(c => c.Lane != RelicSequenceKind.Shop)))
                labels.Add(Source("relics"));
            if (sources.Any(q => q.TreasureRelics is { IsEmpty: false })) labels.Add(Source("treasure"));
            if (sources.Any(q => q.RelicShopSequenceConditions.Count > 0 || q.RelicSequenceConstraints.Any(c => c.Lane == RelicSequenceKind.Shop)))
                labels.Add(Source("shop"));
            return labels.Count == 0 ? Source("relics") : string.Join(" / ", labels);
        }
        string WorldSources()
        {
            var labels = new List<string>();
            if (sources.Any(q => q.VariantBossBranches.Count > 0 || q.LegacyWorld.BossFilters.Count > 0 || q.LegacyWorld.BossOrdinalFilters.Count > 0))
                labels.Add(Source("acts"));
            if (sources.Any(q => q.AncientBranches.Count > 0 || q.LegacyWorld.AncientIdentityFilters.Count > 0))
                labels.Add(Source("ancients"));
            if (sources.Any(q => q.EventSequenceConstraints.Count > 0)) labels.Add(Source("events"));
            return labels.Count == 0 ? Source("acts") : string.Join(" / ", labels);
        }
        string TransformationSources()
        {
            var labels = new List<string>();
            if (sources.Any(q => q.TransformationAggregate?.UsesNeow == true)) labels.Add(Source("neow"));
            if (sources.Any(q => q.TransformationAggregate?.UsesEvents == true)) labels.Add(Source("events"));
            return string.Join(" / ", labels);
        }
        for (int i = 0; i < _familyEntries.Count; i++)
        {
            var entry = _familyEntries[i];
            string source = entry.Id switch
            {
                "N" => Source("neow"), "R" or "N/R" => RelicSources(),
                "W" => WorldSources(), "A" => Source("ancients"), "E" => query.FirstActUnknownEventPrefixCount > 0
                    ? (query.EventResultConditions.Count > 0 ? Source("events") + " / " : "") + Source("unknown") : Source("events"),
                "C" => Source("combat"), "S" => Source("shop"), "M" => Source("map"),
                "T" => TransformationSources(), _ => ""
            };
            _familyEntries[i] = entry with
            {
                Title = FamilyTitle(entry.Id),
                Source = source + (entry.Source.Length == 0 ? "" : " · " + entry.Source),
                JointlyAttributed = jointlyAttributed && entry.Probability is null
            };
        }
    }
    private static double? EtaRate(FamilySearchEtaProjectionV1? eta) =>
        eta?.TargetMeanRootHorizon is { } roots && eta.TargetSearchMeanMs is >0 ? roots*1000d/eta.TargetSearchMeanMs.Value :
        eta?.FixedScanRangeMs is >0 ? eta.ScanCount*1000d/eta.FixedScanRangeMs.Value : null;
    internal static bool PreferGpuQuote(double? cpuNs,double? gpuNs) =>
        gpuNs is >0 && double.IsFinite(gpuNs.Value) &&
        (!(cpuNs is >0 && double.IsFinite(cpuNs.Value)) || gpuNs.Value<cpuNs.Value);
    private string BestQuotedSpeed(double? cpuNs,double? gpuNs)
    {
        bool gpu=PreferGpuQuote(cpuNs,gpuNs);
        double? ns=gpu?gpuNs:cpuNs;
        return ns is >0 && double.IsFinite(ns.Value)
            ? _text.Format(gpu?"workflow.quote.gpu":"workflow.quote.cpu",CompactNumber(1e9/ns.Value)) : "—";
    }
    internal static string MapProbabilityExplanation(string evidence, bool known, bool zh) => known
        ? (zh ? "依据已有地图样本估计。" : "Estimated from the existing map sample corpus.")
        : evidence.Contains("NoObserved",StringComparison.Ordinal)
            ? (zh ? "概率未知：已有样本中没有命中，不能据此认定条件不可能。" : "Probability unknown: no hits in the existing samples; this does not prove impossibility.")
            : evidence.Contains("ContextMismatch",StringComparison.Ordinal)
                ? (zh ? "概率未知：现有样本不适用于当前游戏上下文。" : "Probability unknown: the existing samples do not cover this game context.")
                : (zh ? "概率未知：现有样本未覆盖这组地图条件的联合分布。" : "Probability unknown: the existing samples do not cover this combination of map conditions.");

    private string ExpectationText(double? time,double? probability,double? rate) =>
        _text.Get("workflow.total_rarity")+"   "+(probability is null && _familyEntries.Any(e=>e.Id is "T" or "M") ? (_language=="zh"?"未知":"Unknown") : DashboardRarity(probability))+"\n"+
        _text.Get("workflow.expected_speed")+"   "+Rate(rate)+"\n"+
        _text.Get("workflow.eta")+"   "+Duration(time);
}
