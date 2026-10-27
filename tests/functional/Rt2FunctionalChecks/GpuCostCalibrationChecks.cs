using RolltheSpire2.Search.FamilyExecution;

internal static class GpuCostCalibrationChecks
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool pass,string name){checks++;if(!pass)throw new InvalidOperationException(name);}
        GpuLocalPeak Peak(string key,double throughput) => new("fixture-device","GPU",key,throughput,1000,(long)throughput,DateTimeOffset.UtcNow);
        double? Reference(string key) => key is "A" or "B" or "C" or "D" ? 5e9 : null;
        var atlas=GpuReferenceCostAtlas.Measurements;
        Check(atlas.Select(x=>x.Key).Distinct().Count()==atlas.Length,"Reference keys unique");
        Check(atlas.All(x=>x.Inputs>0 && x.ActiveMs>=1000 && double.IsFinite(x.ActiveMs) &&
            x.PeakThroughput==x.Inputs*1000d/x.ActiveMs && !string.IsNullOrWhiteSpace(x.Evidence)),
            "Reference peaks retain comparable full-window raw evidence");
        Check(atlas.Single(x=>x.Key.Contains("N.FixedPair28.Identity")).PeakThroughput >=
            7700742144d*1000/1000.5885999999998,"Historical replacement cannot lower eligible peak");
        var empty=new GpuCostSnapshot([],Reference);
        Check(empty.GlobalRatio==1 && empty.Ratio("A")==1,"No evidence uses reference cost");
        var one=new GpuCostSnapshot([Peak("A",4e9)],Reference);
        Check(one.Ratio("A")==1.25 && one.Ratio("unknown")==1.25,"5B/4B condition and global ratio");
        var many=new GpuCostSnapshot([Peak("A",5e9/.8),Peak("B",5e9),Peak("C",5e9/1.2),Peak("D",5e9/.9)],Reference);
        var three=new GpuCostSnapshot([Peak("A",5e9/.8),Peak("B",5e9),Peak("C",5e9/1.2)],Reference);
        Check(Math.Abs(three.GlobalRatio-1)<1e-12,"Median across conditions, not fastest condition");
        Check(Math.Abs(many.Ratio("D")-.9)<1e-12,"Condition overrides global");
        var incompatible=new GpuCostSnapshot([Peak("old-revision",1e15)],Reference);
        Check(incompatible.GlobalRatio==1,"Incompatible revision cannot enter global ratio");
        var samples=new GpuCostSamples();
        samples.Observe("A",1,1,"fixture-device"); // excluded cold observation
        for(int i=0;i<100;i++)samples.Observe("A",50_000_000,10,"fixture-device");
        Check(samples.Peaks.Count==1 && samples.Peaks.Single().PeakThroughput==5e9,"One second physical work creates peak");
        var frozen=new GpuCostSnapshot(samples.Peaks,Reference);
        for(int i=0;i<100;i++)samples.Observe("A",30_000_000,10,"fixture-device");
        Check(samples.Peaks.Single().PeakThroughput==5e9,"Slow samples do not lower peak");
        for(int i=0;i<100;i++)samples.Observe("A",52_000_000,10,"fixture-device");
        Check(samples.Peaks.Single().PeakThroughput==5.2e9,"Faster eligible window raises peak");
        Check(frozen.Ratio("A")==1 && new GpuCostSnapshot(samples.Peaks,Reference).Ratio("A")<1,"Snapshot is immutable; later search sees new peak");
        var pending=new GpuCostSamples();pending.Observe("A",1,1,"d");pending.Observe("A",10,999,"d");
        Check(pending.Peaks.Count==0,"Subsecond tail is not promoted");
        Check(new GpuCostSnapshot([Peak("A",4e9)], _=>6e9).Ratio("A")==1.5,"Updated reference reinterprets stored throughput");
        var encoded=System.Text.Json.JsonSerializer.Serialize(samples.Peaks.ToArray());
        var decoded=System.Text.Json.JsonSerializer.Deserialize<GpuLocalPeak[]>(encoded)!;
        Check(decoded.Single().PeakThroughput==5.2e9 && new GpuCostSnapshot(decoded,Reference).Ratio("A")==new GpuCostSnapshot(samples.Peaks,Reference).Ratio("A"),"Persist raw measurement, derive ratio after reload");
        var device=RolltheSpire2.Search.Runtime.SearchPerformanceProfileFoundation.CaptureKnownDeviceIdentity();
        RolltheSpire2.Search.Runtime.SearchPerformanceProfileFoundation.ObserveGpuIdentity("Unknown test GPU", "d3d12");
        Check(FamilyPhysicalQuote.HasReferenceBackend(),"Reference GPU model is provenance, not pricing admission");
        RolltheSpire2.Search.Runtime.SearchPerformanceProfileFoundation.ObserveGpuIdentity("Unknown test GPU", "unsupported-test-backend");
        Check(!FamilyPhysicalQuote.HasReferenceBackend(),"Measured backend boundary remains explicit");
        RolltheSpire2.Search.Runtime.SearchPerformanceProfileFoundation.ObserveGpuIdentity(device.GpuIdentity.Length==0 ? "Unknown" : device.GpuIdentity, device.RenderingBackend.Length==0 ? "unknown" : device.RenderingBackend);
        Console.WriteLine($"GpuCostCalibration checks={checks};PASS");return 0;
    }
}
