using System.Text.Json;
using RolltheSpire2.Search.FamilyExecution;

internal static class FamilyPrivateCompleteCallCostChecks
{
    internal static int RunReference(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using var embedded = typeof(FamilyPrivateCompleteCallCost).Assembly.GetManifestResourceStream(FamilyPrivateCompleteCallCost.ResourceName)
            ?? throw new InvalidDataException("PrivateReference.EmbeddedResourceMissing");
        using var buffer = new MemoryStream(); embedded.CopyTo(buffer);
        if (!bytes.AsSpan().SequenceEqual(buffer.ToArray())) throw new InvalidDataException("PrivateReference.EmbeddedBytesDiffer");
        string json = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        var snapshot = FamilyPrivateCompleteCallCost.Parse(json);
        using var document = JsonDocument.Parse(json);
        var curves = document.RootElement.GetProperty("CompleteCallCurves").EnumerateArray()
            .Select(c => c.Deserialize<FamilyPrivateCompleteCallCurve>() ?? throw new InvalidDataException("PrivateReference.NullCurve")).ToArray();
        if (curves.Length == 0 || snapshot.IsEmpty) throw new InvalidDataException("PrivateReference.TrainingCurvesRequired");
        int points = 0;
        foreach (var curve in curves)
        foreach (var point in curve.Points)
        {
            var key = curve.Key.PrivateInput ? curve.Key : curve.Key with {
                Invocation = curve.Key.Invocation with { RootWindow = checked((int)point.N) } };
            var quote = snapshot.Quote(key, point.N, curve.SourceSurvival);
            if (quote is null || Math.Abs(quote.Milliseconds - point.CallMilliseconds) > 1e-12 * Math.Max(1, point.CallMilliseconds) ||
                quote.SetupMilliseconds.HasValue || !quote.Evidence.Contains("StandaloneSingleFamily", StringComparison.Ordinal))
                throw new InvalidDataException("PrivateReference.ActualAnchorNotReadable:" + curve.Key.Invocation.FamilyId + ":" + curve.Role + ":" + point.N);
            points++;
        }
        Console.WriteLine($"FamilyPrivateCompleteCallReference: embedded bytes and {curves.Length} curves/{points} actual knots passed.");
        return 0;
    }

    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string reason)
        { checks++; if (!value) throw new InvalidDataException("FamilyPrivateCompleteCallCost:" + reason); }
        void Reject(Action action, string reason)
        {
            bool rejected = false;
            try { action(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, reason);
        }
        FamilyPrivateCompleteCallSnapshot Parse(params object?[] curves) => FamilyPrivateCompleteCallCost.Parse(
            JsonSerializer.Serialize(new { SchemaVersion = 1, FamilyPrivateCompleteCallCost.Boundary, CompleteCallCurves = curves }));
        var invocation = new FamilyCanonicalCostKey(1, "R.Relic", "GPU", "FixtureRuntime", "FixtureCpu",
            "FixtureGpu", "d3d12", "Beta111", "FixtureBody", 4, 1, true, 128, "FixtureShape;NotMeasured", "");
        var key = new FamilyPrivateCompleteCallKey(invocation, true, true, "FixtureBinding", "FixtureBindingBody");
        var curve = new FamilyPrivateCompleteCallCurve(key, [new(16, 1, 10), new(64, 4, 20)],
            FamilyPrivateCompleteCallCost.CompactCountReadyRole, "training", FamilyPrivateCompleteCallCost.Boundary,
            FamilyCanonicalMeasuredCost.LiveMainLoopCadence, ["fixture-training"], new string('A', 64),
            ["fixture-source"], "SyntheticOnly;NotAProductPrice", JsonSerializer.SerializeToElement(new { Split = "training", Source = "synthetic" }));
        var snapshot = Parse(curve);
        var price = snapshot.Quote(key, 32);
        Check(price is not null && price.Milliseconds == 2 && price.SetupMilliseconds is null,
            "adjacent complete-wall interpolation charges the measured call once and leaves setup unknown");
        Check(price!.Evidence.Contains("StandaloneSingleFamily", StringComparison.Ordinal) &&
            price.Evidence.Contains("Completion=CountReady", StringComparison.Ordinal) &&
            price.Evidence.Contains("NoExtrapolation", StringComparison.Ordinal), "quote discloses boundary and finite scope");
        Check(snapshot.Quote(key, 16)?.Milliseconds == 1 && snapshot.Quote(key, 64)?.Milliseconds == 4,
            "actual anchor values are retained unchanged");
        Check(snapshot.Quote(key, 32.5)?.Milliseconds == 2.03125, "conditional mean populations may interpolate between integer actual anchors");
        foreach (double population in new[] { 0, 15, 65, double.NaN, double.PositiveInfinity })
            Check(snapshot.Quote(key, population) is null, "unmeasured populations never extrapolate:" + population);
        var one = Parse(curve with { Points = [new(32, 7, 5)] });
        Check(one.Quote(key, 32)?.Milliseconds == 7 && one.Quote(key, 31) is null && one.Quote(key, 33) is null,
            "a single knot prices only that measured population");
        Check(Parse(curve with { Points = [new(64, 4, 20), new(16, 1, 10)] }).Quote(key, 32)?.Milliseconds == 2,
            "anchor order in the asset does not change interpolation");

        var publicKey = key with { PrivateOutput = false };
        var publicCurve = curve with { Key = publicKey, Role = FamilyPrivateCompleteCallCost.CompactPublicRole,
            Points = [new(16, 5, 10), new(64, 8, 20)] };
        var denseKey = key with { Invocation = invocation with { CompactInput = false, RootWindow = 64 }, PrivateInput = false };
        var denseCurve = curve with { Key = denseKey, Role = FamilyPrivateCompleteCallCost.DenseCountReadyRole };
        var roles = Parse(curve, publicCurve, denseCurve);
        Check(roles.Quote(key, 32)?.Milliseconds == 2 && roles.Quote(publicKey, 32)?.Milliseconds == 6 &&
            roles.Quote(publicKey, 32)!.Evidence.Contains("IncludesPayloadSortAndAbi1Materialization", StringComparison.Ordinal),
            "Compact public ABI1 and CountReady keep separate prices and truthful completion evidence");
        var dense32 = denseKey with { Invocation = denseKey.Invocation with { RootWindow = 32 } };
        Check(roles.Quote(dense32, 32)?.Milliseconds == 2 && roles.Quote(dense32, 31) is null,
            "Dense normalizes only its actual N=root span across measured anchors");
        Check(roles.Quote(key with { Invocation = invocation with { RootWindow = 64 } }, 32) is null,
            "resident Compact retains its exact root span independently of its population");
        Check(roles.Quote(denseKey with { PrivateOutput = false }, 64) is null &&
            FamilyPrivateCompleteCallCost.Role(false, false) is null,
            "Dense public binding comparison is excluded from the three private forms");
        Check(Parse(curve).Quote(publicKey, 32) is null && Parse(curve).Quote(dense32, 32) is null,
            "a private role cannot borrow another role's complete call");
        Check(Parse(curve with { Key = publicKey }).IsEmpty && Parse(denseCurve with { Role = "PublicAbi1Canonical" }).IsEmpty,
            "key and explicit role must agree and public curves cannot be relabeled implicitly");

        foreach (var other in new[] {
            key with { BindingPath = "OtherPath" }, key with { BindingPhysicalRevision = "OtherBindingBody" },
            key with { Invocation = invocation with { SchemaVersion = 2 } },
            key with { Invocation = invocation with { FamilyId = "N.Neow" } },
            key with { Invocation = invocation with { ExecutionClass = "CPU" } },
            key with { Invocation = invocation with { RuntimeTarget = "OtherRuntime" } },
            key with { Invocation = invocation with { CpuIdentity = "OtherCpu" } },
            key with { Invocation = invocation with { GpuIdentity = "OtherGpu" } },
            key with { Invocation = invocation with { RenderingBackend = "vulkan" } },
            key with { Invocation = invocation with { Profile = "OtherProfile" } },
            key with { Invocation = invocation with { PhysicalRevision = "OtherBody" } },
            key with { Invocation = invocation with { ConfiguredWorkers = 1 } },
            key with { Invocation = invocation with { Players = 2 } },
            key with { Invocation = invocation with { CompactInput = false } },
            key with { Invocation = invocation with { WorkShape = "OtherShape" } },
            key with { Invocation = invocation with { OrderedChildBodies = "OtherChild" } } })
            Check(snapshot.Quote(other, 32) is null, "full invocation and binding identity remains exact");

        foreach (var invalid in new[] {
            curve with { Split = "validation" }, curve with { HeldoutUsedForFit = true },
            curve with { Boundary = FamilyCanonicalMeasuredCost.Boundary },
            curve with { MeasurementCadence = FamilyCanonicalMeasuredCost.HistoricalCadence },
            curve with { MeasurementCadence = FamilyCanonicalMeasuredCost.BlockedMainLoopCadence },
            curve with { MeasurementCadence = "GuessedLive" }, curve with { TrainingRowsSha256 = "missing" },
            curve with { TrainingCases = [] }, curve with { SourceGroups = [] }, curve with { Evidence = "" },
            curve with { Provenance = JsonSerializer.SerializeToElement(new { }) },
            curve with { Provenance = JsonSerializer.SerializeToElement(new { Children = new[] { new { split = "heldout" } } }) },
            curve with { Provenance = JsonSerializer.SerializeToElement(new { DatasetRole = "test" }) },
            curve with { Provenance = JsonSerializer.SerializeToElement(new { Role = "combination" }) },
            curve with { Provenance = JsonSerializer.SerializeToElement(new { Derived = true }) },
            curve with { Provenance = JsonSerializer.SerializeToElement(new { HeldoutUsedForFit = true }) },
            curve with { SourceSurvival = .5 }, curve with { Points = [] },
            curve with { Points = [new(16, 0)] }, curve with { Points = [new(16, -1)] },
            curve with { Points = [new(0, 1)] }, curve with { Points = [new(16.5, 1)] },
            curve with { Points = [new(129, 1)] }, curve with { Points = [new(16, 1, 0)] },
            curve with { Points = [new(16, 1), new(16, 2)] },
            curve with { Key = key with { Invocation = invocation with { FamilyId = "I.InitialRooms" } } } })
        {
            Check(Parse(invalid).IsEmpty, "invalid provenance, geometry or support identity cannot produce a price");
            Check(Parse(invalid, publicCurve).Quote(publicKey, 32)?.Milliseconds == 6,
                "invalid optional curve does not discard unrelated valid evidence");
        }
        Check(Parse("malformed optional DTO", publicCurve).Quote(publicKey, 32)?.Milliseconds == 6,
            "deserialization failure is isolated to the affected curve");
        Check(Parse(curve, curve, publicCurve).Quote(key, 32) is null &&
            Parse(curve, curve, publicCurve).Quote(publicKey, 32)?.Milliseconds == 6,
            "duplicate identity rejects all ambiguous candidates while retaining independent roles");
        Check(Parse(denseCurve, denseCurve with { Key = dense32 }).IsEmpty,
            "Dense duplicate identity is detected across normalized root spans");

        var neowKey = key with { Invocation = invocation with { FamilyId = "N.Neow" } };
        var neow = curve with { Key = neowKey, SourceSurvival = .25 };
        var neowSnapshot = Parse(neow);
        Check(neowSnapshot.RequiresSourceSurvival(neowKey) && neowSnapshot.Quote(neowKey, 32, .25)?.Milliseconds == 2 &&
            neowSnapshot.Quote(neowKey, 32, .25)!.Evidence.Contains("NoSurvivalScaling", StringComparison.Ordinal),
            "N source survival selects an actual curve without rescaling its price");
        foreach (double? survival in new double?[] { null, double.NaN, -1, 2, .5, 0 })
            Check(neowSnapshot.Quote(neowKey, 32, survival) is null, "unknown or unmeasured N source probability cannot alias a curve");
        Check(neowSnapshot.Quote(neowKey, 32, .25 * (1 + 5e-11))?.Milliseconds == 2,
            "equivalent positive source sums tolerate roundoff only within the declared relative tolerance");
        Check(neowSnapshot.Quote(neowKey, 32, .25 * (1 + 2e-10)) is null,
            "distinct source probability outside tolerance remains unmeasured");
        Check(Parse(neow, neow with { SourceSurvival = .25 * (1 + 5e-11) }).Quote(neowKey, 32, .25) is null,
            "nearby exact stored identities are retained but ambiguous tolerant selection returns no price");
        var zero = Parse(neow with { SourceSurvival = 0 });
        Check(zero.Quote(neowKey, 32, 0)?.Milliseconds == 2 && zero.Quote(neowKey, 32, 1e-30) is null,
            "zero source probability uses exact equality");
        Check(Parse(neow with { SourceSurvival = 0 }, neow with { SourceSurvival = -0d }).IsEmpty,
            "signed zero cannot create two distinct stored source identities");
        Check(Parse(neow with { SourceSurvival = null }).IsEmpty && Parse(neow with { SourceSurvival = 1.1 }).IsEmpty,
            "N training requires a finite source model probability");

        Reject(() => FamilyPrivateCompleteCallCost.Parse("{\"SchemaVersion\":2,\"Boundary\":\"" + FamilyPrivateCompleteCallCost.Boundary + "\"}"),
            "unknown document schema rejects explicitly");
        Reject(() => FamilyPrivateCompleteCallCost.Parse(JsonSerializer.Serialize(new {
            SchemaVersion = 1, Boundary = FamilyCanonicalMeasuredCost.Boundary, CompleteCallCurves = new[] { curve } })),
            "public Invoke wall cannot load as complete binding evidence");
        Check(FamilyPrivateCompleteCallCost.Parse(JsonSerializer.Serialize(new {
            SchemaVersion = 1, FamilyPrivateCompleteCallCost.Boundary })).IsEmpty,
            "absent optional private curves mean no price");
        Check(FamilyPrivateCompleteCallCost.Parse(JsonSerializer.Serialize(new {
            SchemaVersion = 1, FamilyPrivateCompleteCallCost.Boundary, CompleteCallCurves = 17 })).IsEmpty,
            "malformed optional curve collection means no price");
        Console.WriteLine($"FamilyPrivateCompleteCallCost: {checks} checks passed (synthetic evidence only).");
        return 0;
    }
}
