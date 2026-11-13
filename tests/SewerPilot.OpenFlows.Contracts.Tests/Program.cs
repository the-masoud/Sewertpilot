using System;
using System.Collections.Generic;
using System.Linq;
using SewerPilot.OpenFlows.Contracts;

namespace SewerPilot.OpenFlows.Contracts.Tests;

internal static class Program
{
    private static int assertions;

    private static int Main()
    {
        (string Name, Action Test)[] tests =
        {
            ("State defaults and report values", StateSemantics),
            ("Exact P1 capability matrix", P1Matrix),
            ("Version and build metadata have no authority", VersionTolerance),
            ("Missing and future capabilities", FutureCapabilities),
            ("Evidence rejects invalid and contradictory input", EvidenceValidation),
            ("Profile snapshots are immutable", ProfileSnapshot),
            ("Profile rejects ambiguous input", ProfileValidation),
            ("Typed opaque identities", IdentitySemantics),
            ("All P1 operations remain gated", P1OperationGates),
            ("Only Supported permits a fake operation", FourStateGate),
            ("Consumer can check evidence before calling", ConsumerGate),
            ("Portable assembly and separate write boundary", ContractBoundary)
        };
        int failed = 0;
        foreach (var test in tests)
        {
            try { test.Test(); Console.WriteLine("PASS: " + test.Name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL: " + test.Name + ": " + error.Message); }
        }
        Console.WriteLine("CONTRACT_TESTS: " + tests.Length);
        Console.WriteLine("CONTRACT_ASSERTIONS: " + assertions);
        Console.WriteLine("CONTRACT_FAILURES: " + failed);
        return failed == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        assertions++;
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static CapabilityEvidence Evidence(CapabilityIdentifier id, CapabilityState state = CapabilityState.Supported,
        CapabilityEvidenceKind kind = CapabilityEvidenceKind.RuntimeOperation) =>
        new(id, state, kind, "Deterministic test evidence.", "In-memory test fixture.");

    private static void StateSemantics()
    {
        Check(default(CapabilityState) == CapabilityState.NotEstablished, "Default must be unknown.");
        Check(CapabilityState.Supported.ToReportValue() == "SUPPORTED", "Supported report meaning.");
        Check(CapabilityState.NotSupported.ToReportValue() == "NOT_SUPPORTED", "Unsupported report meaning.");
        Check(CapabilityState.NotTested.ToReportValue() == "NOT_TESTED", "Untested report meaning.");
        Check(CapabilityState.NotEstablished.ToReportValue() == "NOT_ESTABLISHED", "Unknown report meaning.");
        Throws<ArgumentOutOfRangeException>(() => ((CapabilityState)99).ToReportValue());
    }

    private static void P1Matrix()
    {
        var profile = P1CapabilityFixture.Create();
        Check(profile.Capabilities.Count == 11, "P1 has exactly eleven capabilities.");
        Check(profile.GetCapability(CapabilityIdentifier.ProductDetected).State == CapabilityState.Supported &&
            profile.ProductDetected == CapabilityState.Supported, "Detected product is supported.");
        Check(profile.GetCapability(CapabilityIdentifier.WaterObjectsPresent).State == CapabilityState.Supported,
            "Core API presence is supported.");
        var loaded = profile.GetCapability(CapabilityIdentifier.ManagedAssembliesLoad);
        Check(loaded.State == CapabilityState.Supported && loaded.Kind == CapabilityEvidenceKind.AssemblyLoad,
            "Assembly load evidence remains bounded.");
        foreach (var id in P1CapabilityFixture.Unperformed)
        {
            var entry = profile.GetCapability(id);
            Check(entry.State == CapabilityState.NotTested && entry.Kind == CapabilityEvidenceKind.NotTested,
                id + " must remain untested, never supported or unsupported.");
        }
        Check(profile.Classification == CompatibilityClassification.Partial, "P1 operations are unproven.");
    }

    private static void VersionTolerance()
    {
        // Version strings are inert diagnostic metadata only.
        var versions = new[] { "24.0." + "2.21", "26.x", "99.99" };
        var baseline = P1CapabilityFixture.Create();
        foreach (var version in versions)
        {
            var profile = P1CapabilityFixture.Create(version, "arbitrary-build-" + version);
            Check(profile.ProductVersion == version && profile.ProductBuild == "arbitrary-build-" + version,
                "Diagnostic metadata round trips without parsing.");
            Check(profile.Classification == baseline.Classification && baseline.Capabilities.All(pair =>
                profile.GetCapability(pair.Key).State == pair.Value.State &&
                profile.GetCapability(pair.Key).Kind == pair.Value.Kind), "Metadata must not change capabilities.");
        }
    }

    private static void FutureCapabilities()
    {
        var future = new CapabilityIdentifier("FutureObservation");
        var missing = P1CapabilityFixture.Create().GetCapability(future);
        Check(missing.State == CapabilityState.NotEstablished && missing.Kind == CapabilityEvidenceKind.NotEstablished,
            "Missing evidence is unknown rather than unsupported.");
        var profile = new OpenFlowsCompatibilityProfile(new[] { Evidence(future) }, CompatibilityClassification.Partial);
        Check(profile.GetCapability(new CapabilityIdentifier("FutureObservation")).State == CapabilityState.Supported,
            "Future identifiers are queryable by value.");
        Check(profile.GetCapability(new CapabilityIdentifier("futureObservation")).State == CapabilityState.NotEstablished,
            "Identifiers have ordinal case-sensitive identity.");
        Check(profile.ProductDetected == CapabilityState.NotEstablished, "Absent product evidence is unknown.");
    }

    private static void EvidenceValidation()
    {
        var id = CapabilityIdentifier.ModelRead;
        Throws<ArgumentNullException>(() => Evidence(null!));
        Throws<ArgumentOutOfRangeException>(() => Evidence(id, (CapabilityState)99));
        Throws<ArgumentOutOfRangeException>(() => Evidence(id, kind: (CapabilityEvidenceKind)99));
        Throws<ArgumentException>(() => new CapabilityEvidence(id, CapabilityState.Supported,
            CapabilityEvidenceKind.ModelRead, " ", "test"));
        Throws<ArgumentException>(() => new CapabilityEvidence(id, CapabilityState.Supported,
            CapabilityEvidenceKind.ModelRead, "test", " "));
        Throws<ArgumentException>(() => Evidence(id, kind: CapabilityEvidenceKind.NotTested));
        Throws<ArgumentException>(() => Evidence(id, kind: CapabilityEvidenceKind.ExplicitUnsupported));
        Throws<ArgumentException>(() => Evidence(id, kind: CapabilityEvidenceKind.NotEstablished));
        var time = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var observed = new CapabilityEvidence(id, CapabilityState.Supported, CapabilityEvidenceKind.ModelRead,
            "Observation", "test", time);
        Check(observed.ObservedAt == time && observed.Capability.Equals(id), "Time is optional evidence metadata.");
    }

    private static void ProfileSnapshot()
    {
        var entries = new List<CapabilityEvidence> { Evidence(CapabilityIdentifier.ModelRead) };
        var warnings = new List<string> { "Test limitation" };
        var profile = new OpenFlowsCompatibilityProfile(entries, CompatibilityClassification.Partial, limitations: warnings);
        entries.Clear(); warnings.Clear();
        Check(profile.Capabilities.Count == 1 && profile.Limitations.Count == 1, "Input mutation cannot change a snapshot.");
        Throws<NotSupportedException>(() => ((IDictionary<CapabilityIdentifier, CapabilityEvidence>)profile.Capabilities).Clear());
        Throws<NotSupportedException>(() => ((IList<string>)profile.Limitations).Clear());
    }

    private static void ProfileValidation()
    {
        var entry = Evidence(CapabilityIdentifier.ModelRead);
        Throws<ArgumentNullException>(() => new OpenFlowsCompatibilityProfile(null!, CompatibilityClassification.Partial));
        Throws<ArgumentException>(() => new OpenFlowsCompatibilityProfile(new[] { entry, Evidence(new CapabilityIdentifier("ModelRead")) },
            CompatibilityClassification.Partial));
        Throws<ArgumentException>(() => new OpenFlowsCompatibilityProfile(new CapabilityEvidence[] { null! }, CompatibilityClassification.Partial));
        Throws<ArgumentOutOfRangeException>(() => new OpenFlowsCompatibilityProfile(Array.Empty<CapabilityEvidence>(), (CompatibilityClassification)99));
        Throws<ArgumentException>(() => new OpenFlowsCompatibilityProfile(Array.Empty<CapabilityEvidence>(), CompatibilityClassification.Partial,
            limitations: new[] { " " }));
        Throws<ArgumentNullException>(() => P1CapabilityFixture.Create().GetCapability(null!));
    }

    private static void IdentitySemantics()
    {
        var model = new ModelIdentity("same");
        var same = new ModelIdentity("same");
        Check(model.Equals(same) && model.GetHashCode() == same.GetHashCode(), "Equal typed values share hash semantics.");
        OpenFlowsIdentity[] others = { new ScenarioIdentity("same"), new AlternativeIdentity("same"),
            new ElementIdentity("same"), new CalculationRunIdentity("same") };
        Check(others.All(other => !model.Equals(other)) && new HashSet<OpenFlowsIdentity>(others.Append(model)).Count == 5,
            "Different identity kinds cannot alias despite identical text.");
        Check(!model.Equals(new ModelIdentity("Same")), "Opaque values are ordinal.");
        Throws<ArgumentException>(() => new ModelIdentity(" "));
        Throws<ArgumentException>(() => new ScenarioIdentity(" leading"));
        Throws<ArgumentException>(() => new AlternativeIdentity(null!));
        Throws<ArgumentException>(() => new ElementIdentity("trailing "));
        Throws<ArgumentException>(() => new CalculationRunIdentity(""));
        Throws<ArgumentException>(() => new CapabilityIdentifier(" "));
    }

    private static void P1OperationGates()
    {
        using var fake = new FakeOpenFlowsRuntime(P1CapabilityFixture.Create());
        using var model = new FakeModel(new ModelIdentity("test-model"));
        Throws<NotSupportedException>(() => ((IOpenFlowsModelReader)fake).Read("test-source"));
        Throws<NotSupportedException>(() => ((IOpenFlowsScenarioService)fake).Read(model));
        Throws<NotSupportedException>(() => ((IOpenFlowsScenarioService)fake).Create(model, "test-scenario"));
        Throws<NotSupportedException>(() => ((IOpenFlowsAlternativeService)fake).Read(model));
        Throws<NotSupportedException>(() => ((IOpenFlowsAlternativeService)fake).Create(model, "test-alternative"));
        Throws<NotSupportedException>(() => fake.Run(model, new ScenarioIdentity("test-scenario")));
        Throws<NotSupportedException>(() => fake.ReadElements(model, new CalculationRunIdentity("test-run")));
        Throws<NotSupportedException>(() => fake.WriteCopy(model, "test-destination"));
        Check(fake.OperationCount == 0, "No untested operation executed even when called directly.");
    }

    private static void FourStateGate()
    {
        foreach (var state in Enum.GetValues<CapabilityState>())
        {
            var kind = state switch
            {
                CapabilityState.NotSupported => CapabilityEvidenceKind.ExplicitUnsupported,
                CapabilityState.NotTested => CapabilityEvidenceKind.NotTested,
                CapabilityState.NotEstablished => CapabilityEvidenceKind.NotEstablished,
                _ => CapabilityEvidenceKind.ModelRead
            };
            using var fake = new FakeOpenFlowsRuntime(new OpenFlowsCompatibilityProfile(
                new[] { Evidence(CapabilityIdentifier.ModelRead, state, kind) }, CompatibilityClassification.Partial));
            if (state == CapabilityState.Supported)
            {
                using var model = fake.Read("fake-source");
                Check(model.Identity.Equals(new ModelIdentity("fake-source")) && fake.OperationCount == 1,
                    "Explicit support permits an in-memory fake operation.");
            }
            else
            {
                Throws<NotSupportedException>(() => fake.Read("fake-source"));
                Check(fake.OperationCount == 0, state + " cannot grant support.");
            }
        }
    }

    private static void ConsumerGate()
    {
        using IOpenFlowsRuntime runtime = new FakeOpenFlowsRuntime(P1CapabilityFixture.Create());
        var provider = runtime.CapabilityProvider;
        var evidence = provider.GetCapability(CapabilityIdentifier.ModelRead);
        var called = false;
        if (evidence.State == CapabilityState.Supported)
        {
            called = true;
            using var model = ((IOpenFlowsModelReader)runtime).Read("test-source");
        }
        Check(!called && evidence.State == CapabilityState.NotTested &&
            ((FakeOpenFlowsRuntime)runtime).OperationCount == 0, "A contract-only caller preserves the untested boundary.");
        Check(provider.Profile.Limitations.Count > 0 && !string.IsNullOrWhiteSpace(evidence.Provenance),
            "Consumer receives limitations and evidence provenance.");
    }

    private static void ContractBoundary()
    {
        var references = typeof(IOpenFlowsRuntime).Assembly.GetReferencedAssemblies();
        Check(references.Length == 1 && references[0].Name == "netstandard", "Production assembly depends only on the standard library.");
        Check(typeof(IOpenFlowsModelReader).GetMethods().All(method => method.Name == "Read") &&
            typeof(IOpenFlowsModel).GetMethods().All(method => method.Name == "get_Identity"),
            "Read and model interfaces expose no persistence method.");
        var write = typeof(IOpenFlowsModelWriter).GetMethods();
        Check(write.Length == 1 && write[0].Name == "WriteCopy" && write[0].GetParameters()[1].ParameterType == typeof(string),
            "Persistence requires an explicit write boundary and destination.");
    }
}
