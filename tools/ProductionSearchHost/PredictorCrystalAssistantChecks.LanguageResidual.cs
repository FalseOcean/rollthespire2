using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using RolltheSpire2.Core.PredictorRuntime;
using RolltheSpire2.Infrastructure.Snapshots;

internal static partial class PredictorCrystalAssistantChecks
{
    private static int CheckLanguageResidual(string output,Action<PredictorCrystalSnapshot,PredictorCrystalSolution> native)
    {
        if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_PROFILE_SNAPSHOT") is {Length:>0} profileSnapshot)
        {
            var profileSource=ProductionSearchReplay.Read<PredictorCrystalSnapshot>(Path.GetFullPath(profileSnapshot));
            string frozenSource=JsonSerializer.Serialize(profileSource,ProductionSearchReplay.JsonOptions);
            ImmutableArray<CrystalRewardOption> profileGoals=Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_PROFILE_GOALS") is {Length:>0} goalsFile
                ?ProductionSearchReplay.Read<ImmutableArray<CrystalRewardOption>>(Path.GetFullPath(goalsFile))
                :[new(PredictorRewardKind.Relic,new("RELIC","GAME_PIECE")),new(PredictorRewardKind.Card,new("CARD","STOKE"),0)];
            int profileMs=int.Parse(Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_PROFILE_MS")??"2000");
            int profileLimit=int.Parse(Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_PROFILE_LIMIT")??"100000");
            int geometryMemoLimit=int.Parse(Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_PROFILE_GEOMETRY_MEMO")??"1000000");
            Check(profileMs>=0 && profileLimit>=0 && geometryMemoLimit>=0,"Single-query profile accepts nonnegative time/state/memo budgets");
            var timer=Stopwatch.StartNew();
            bool profileEnchantments=Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_PROFILE_ENCHANTMENTS")=="1";
            bool profileRerolls=Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_PROFILE_REROLLS")!="0";
            bool profileAvoidCurse=Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_PROFILE_AVOID_CURSE")!="0";
            var profileTable=new CrystalReachabilityTable(profileSource,includeRerolls:profileRerolls,includeEnchantments:profileEnchantments);
            double profileTableMs=timer.Elapsed.TotalMilliseconds;timer.Restart();
            var profileBound=new PredictorCrystalOffsetBound(profileSource,profileTable,profileAvoidCurse);
            double profileBoundMs=timer.Elapsed.TotalMilliseconds;
            var answer=PredictorCrystalGoalLanguage.Solve(profileSource,profileTable,profileGoals,profileAvoidCurse,
                TimeSpan.FromMilliseconds(profileMs),profileLimit,sharedBound:profileBound,rootCardSlots:true,profileCompilation:true,geometryMemoLimit:geometryMemoLimit);
            Check(frozenSource==JsonSerializer.Serialize(profileSource,ProductionSearchReplay.JsonOptions),"Single-query profile preserves frozen source");
            ProductionSearchReplay.Write(Path.Combine(output,"language-single-profile.json"),new {
                scope="Frozen generated capture diagnosis; not exact replay of Owner live scene or runtime acceptance",
                snapshot=Path.GetFullPath(profileSnapshot),seed=profileSource.Context.Seed,remaining=profileSource.Remaining,
                productMvid=typeof(PredictorCrystalSnapshot).Module.ModuleVersionId,profileGoals,profileMs,profileLimit,geometryMemoLimit,
                includeRerolls=profileTable.IncludesRerolls,includeEnchantments=profileTable.IncludesEnchantments,avoidCurse=profileAvoidCurse,
                tableMs=profileTableMs,boundMs=profileBoundMs,answer});
            Console.WriteLine($"CRYSTAL_LANGUAGE_SINGLE_PROFILE seed={profileSource.Context.Seed};remaining={profileSource.Remaining};verdict={answer.Verdict};seconds={answer.Seconds:F6};compileSeconds={answer.CompileSeconds:F6};rewardStates={answer.RewardStates};geometry={answer.GeometryNodes};languageNodes={answer.LanguageNodes};typedReplays={answer.VerifiedWords};nativeExactReplay=false");
            return 0;
        }
        string survey=Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_RESIDUAL")!;
        string owner=Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_OWNER_SNAPSHOT")!;
        var cases=new[]{("R93J1VRL814T","OwnerSingle"),("TZSUCA3EM6T6","RelicFirst"),("KAJE4M0MECQ6","RelicSecond")};
        ProductionSearchReplay.Write(Path.Combine(output,"residual-receipt.json"),new {sourceHead="ad0603b86c33371be81950349ffe96cd62e3c772",cases=cases.Select(c=>new {seed=c.Item1,stage=c.Item2}),
            budgetSeconds=10,order="Kind,serialized key,upgrade; no reordering",source="saved frozen snapshots",
            scope="R93 known production negative; two largest residual development groups after old 2.5s discovery/selection; not fresh candidate E2E"});
        foreach(var (seed,label) in cases)
        {
            var source=ProductionSearchReplay.Read<PredictorCrystalSnapshot>(label=="OwnerSingle"?owner:Path.Combine(survey,"source-"+seed+".json"));
            string frozen=JsonSerializer.Serialize(source,ProductionSearchReplay.JsonOptions);
            ImmutableArray<CrystalRewardOption> selected=[];double? priorMs=null,priorDiscoveryMs=null;int expectedGray=1;
            HashSet<CrystalRewardOption> available=[],excluded=[];
            if(label!="OwnerSingle")
            {
                using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(survey,"survey-"+seed+".json")));
                priorDiscoveryMs=doc.RootElement.GetProperty("stages").EnumerateArray().Single(s=>s.GetProperty("stage").GetString()=="Discovery").GetProperty("wallMs").GetDouble();
                var stage=doc.RootElement.GetProperty("stages").EnumerateArray().Single(s=>s.GetProperty("stage").GetString()==label);
                T Read<T>(string key)=>JsonSerializer.Deserialize<T>(stage.GetProperty(key).GetRawText(),ProductionSearchReplay.JsonOptions)!;
                selected=Read<ImmutableArray<CrystalRewardOption>>("selected");available=Read<HashSet<CrystalRewardOption>>("available");excluded=Read<HashSet<CrystalRewardOption>>("excluded");
                priorMs=stage.GetProperty("wallMs").GetDouble();expectedGray=stage.GetProperty("counts").GetProperty("pending").GetInt32();
            }
            if(Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_DISPATCH")=="1")
            {
                var reach=new PredictorCrystalReachability(source,true,potionScenarioBound:true,workers:4,targetDirected:true,includeRerolls:true,goalLanguageProof:true);
                if(label!="OwnerSingle")
                {
                    using var proofDoc=JsonDocument.Parse(File.ReadAllText(Path.Combine(survey,"discovery-proofs-"+seed+".json")));
                    foreach(var entry in proofDoc.RootElement.EnumerateArray())
                    {
                        var plan=JsonSerializer.Deserialize<PredictorCrystalSolution>(entry.GetProperty("plan").GetRawText(),ProductionSearchReplay.JsonOptions)!;
                        reach.AddWitnesses([new(plan.Steps,PredictorCrystalExplorer.Replay(source,plan.Steps).Rewards)]);
                    }
                }
                else selected=[new(PredictorRewardKind.Relic,new("RELIC","CHANDELIER")),new(PredictorRewardKind.Card,new("CARD","STOKE"),0)];
                using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(10));var wall=Stopwatch.StartNew();
                CrystalReachabilityUpdate? update=null;CrystalFocusUpdate? focus=null;bool cutoff=false;
                try
                {
                    reach.ProjectKnown(selected,stop.Token);
                    do
                    {
                        if(label=="OwnerSingle") focus=reach.AdvanceFocused(selected,new(PredictorRewardKind.Card,new("CARD","FEEL_NO_PAIN"),0),TimeSpan.FromMilliseconds(150),stop.Token);
                        else update=reach.Advance(selected,TimeSpan.FromMilliseconds(150),stop.Token,rows=>excluded.UnionWith(rows));
                    } while(label=="OwnerSingle"?focus!.Verdict==CrystalFocusVerdict.Checking:!update!.Complete);
                }
                catch(OperationCanceledException) {cutoff=true;}
                wall.Stop();var projection=reach.PeekKnown(selected);if(update!=null) excluded.UnionWith(update.ExcludedCandidates);
                var counts=Counts(reach,selected,projection,excluded);
                ProductionSearchReplay.Write(Path.Combine(output,"language-dispatch-"+seed+".json"),new {seed,label,selected,ms=wall.Elapsed.TotalMilliseconds,cutoff,counts,complete=update?.Complete,progress=update?.Progress,focusVerdict=focus?.Verdict,focusProgress=focus?.Progress,available=projection.Available.Keys.ToArray(),excluded=excluded.ToArray()});
                Console.WriteLine($"CRYSTAL_LANGUAGE_DISPATCH seed={seed};ms={wall.Elapsed.TotalMilliseconds:F2};cutoff={cutoff};complete={update?.Complete};focus={focus?.Verdict};counts={JsonSerializer.Serialize(counts)}");
                // Owner snapshot has a saved deck, while this native helper reconstructs the generated fixture deck. Its focal result is negative.
                if(label!="OwnerSingle") foreach(var plan in projection.Available.Values.DistinctBy(p=>string.Join(';',p.Steps))) native(source,plan);
                Check(frozen==JsonSerializer.Serialize(source,ProductionSearchReplay.JsonOptions),"Dispatch preserves source");
                continue;
            }
            var clock=Stopwatch.StartNew();var prep=Stopwatch.StartNew();
            var table=new CrystalReachabilityTable(source,includeRerolls:true);double tableMs=prep.Elapsed.TotalMilliseconds;prep.Restart();
            var bound=new PredictorCrystalOffsetBound(source,table,true);double boundMs=prep.Elapsed.TotalMilliseconds;
            var questions=label=="OwnerSingle"?new[]{new CrystalRewardOption(PredictorRewardKind.Card,new("CARD","FEEL_NO_PAIN"),0)}:
                table.Candidates.Where(c=>!selected.Contains(c) && !available.Contains(c) && !excluded.Contains(c))
                    .OrderBy(c=>c.Kind).ThenBy(c=>c.Key.Serialized,StringComparer.Ordinal).ThenBy(c=>c.UpgradeLevel).ToArray();
            if(label=="OwnerSingle") selected=[new(PredictorRewardKind.Relic,new("RELIC","CHANDELIER")),new(PredictorRewardKind.Card,new("CARD","STOKE"),0)];
            Check(questions.Length==expectedGray,"Saved residual candidate membership is unchanged: "+seed);
            var rows=new List<object>();int positive=0,negative=0,unknown=0;double compileMs=0,nativeMs=0;long geometry=0;CrystalRewardOption? exhaustedAt=null;
            foreach(var candidate in questions)
            {
                var goals=selected.Add(candidate);var left=TimeSpan.FromSeconds(10)-clock.Elapsed;
                if(left<=TimeSpan.Zero)
                {rows.Add(new {candidate,verdict="Unvisited",reason="group-budget"});unknown++;continue;}
                var answer=PredictorCrystalGoalLanguage.Solve(source,table,goals,true,left,sharedBound:bound);
                compileMs+=answer.CompileSeconds*1000;geometry+=answer.GeometryNodes;
                if(answer.Plan is {} plan)
                {
                    positive++;clock.Stop();var verify=Stopwatch.StartNew();native(source,plan);nativeMs+=verify.Elapsed.TotalMilliseconds;clock.Start();
                    Check(label!="OwnerSingle","R93 previously proved negative must not become a native positive");
                }
                else if(answer.Verdict==CrystalOffsetVerdict.Impossible) negative++;
                else {unknown++;if(clock.Elapsed>=TimeSpan.FromSeconds(10)) exhaustedAt=candidate;}
                rows.Add(new {candidate,answer});
                Console.WriteLine($"CRYSTAL_LANGUAGE_RESIDUAL_QUERY seed={seed};stage={label};candidate={candidate};verdict={answer.Verdict};negative={answer.NegativeKind};ms={answer.Seconds*1000:F2};compileMs={answer.CompileSeconds*1000:F2};geometry={answer.GeometryNodes}");
            }
            clock.Stop();Check(frozen==JsonSerializer.Serialize(source,ProductionSearchReplay.JsonOptions),"Residual experiment preserves frozen source");
            ProductionSearchReplay.Write(Path.Combine(output,"language-residual-"+seed+".json"),new {seed,label,selected,priorMs,priorDiscoveryMs,tableMs,boundMs,queryMs=clock.Elapsed.TotalMilliseconds,nativeMs,
                expectedGray,positive,negative,unknown,compileMs,geometry,exhaustedAt,rows,complete=unknown==0});
            Console.WriteLine($"CRYSTAL_LANGUAGE_RESIDUAL_GROUP seed={seed};stage={label};priorMs={priorMs};discoveryMs={priorDiscoveryMs};tableMs={tableMs:F2};boundMs={boundMs:F2};queryMs={clock.Elapsed.TotalMilliseconds:F2};nativeMs={nativeMs:F2};positive={positive};negative={negative};unknown={unknown};compileMs={compileMs:F2};geometry={geometry};exhaustedAt={exhaustedAt}");
        }
        Console.WriteLine(Environment.GetEnvironmentVariable("RT2_CRYSTAL_LANGUAGE_DISPATCH")=="1"?"CRYSTAL_LANGUAGE_DISPATCH_PASS frozen=true;nativeFixturePositives=true;savedOwnerNegative=true":"CRYSTAL_LANGUAGE_RESIDUAL_PASS frozen=true;unvisitedUnknown=true;nativePositives=true;noProductionDispatch=true");return 0;
    }
}
