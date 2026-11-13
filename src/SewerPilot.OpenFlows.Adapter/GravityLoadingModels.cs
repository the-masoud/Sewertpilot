using System;
using System.Collections.Generic;
using System.Globalization;

namespace SewerPilot.OpenFlows.Adapter
{
    public enum GravityPopulationBasisKind
    {
        Population = 1,
        PopulationEquivalent = 2
    }

    public enum GravityLoadingContributionKind
    {
        Infiltration = 1,
        Inflow = 2,
        IndustrialProcess = 3,
        KnownPointLoad = 4
    }

    public enum GravityLoadingCalculationStatus
    {
        Incomplete = 1,
        Complete = 2,
        Failed = 3
    }

    public sealed class GravityPopulationBasis
    {
        public GravityPopulationBasisKind Kind { get; set; }
        public double Value { get; set; }
        public StandardProvenance Provenance { get; set; }

        public GravityPopulationBasis() { }

        public GravityPopulationBasis(GravityPopulationBasisKind kind, double value, StandardProvenance provenance = null)
        {
            Kind = kind;
            Value = value;
            Provenance = provenance;
        }

        public GravityPopulationBasis Clone()
        {
            return new GravityPopulationBasis
            {
                Kind = Kind,
                Value = Value,
                Provenance = Provenance != null ? new StandardProvenance
                {
                    SourceLayer = Provenance.SourceLayer,
                    StandardId = Provenance.StandardId,
                    StandardName = Provenance.StandardName,
                    StandardVersion = Provenance.StandardVersion,
                    ParameterKey = Provenance.ParameterKey,
                    DocumentReference = Provenance.DocumentReference,
                    SectionReference = Provenance.SectionReference,
                    Notes = Provenance.Notes
                } : null
            };
        }
    }

    public sealed class WastewaterGenerationRate
    {
        public StandardQuantity Quantity { get; set; }
        public GravityPopulationBasisKind BasisKind { get; set; }
        public StandardProvenance Provenance { get; set; }

        public WastewaterGenerationRate() { }

        public WastewaterGenerationRate(StandardQuantity quantity, GravityPopulationBasisKind basisKind, StandardProvenance provenance = null)
        {
            Quantity = quantity;
            BasisKind = basisKind;
            Provenance = provenance;
        }

        public WastewaterGenerationRate Clone()
        {
            return new WastewaterGenerationRate
            {
                Quantity = Quantity != null ? new StandardQuantity(Quantity.Value, Quantity.Unit) : null,
                BasisKind = BasisKind,
                Provenance = Provenance != null ? new StandardProvenance
                {
                    SourceLayer = Provenance.SourceLayer,
                    StandardId = Provenance.StandardId,
                    StandardName = Provenance.StandardName,
                    StandardVersion = Provenance.StandardVersion,
                    ParameterKey = Provenance.ParameterKey,
                    DocumentReference = Provenance.DocumentReference,
                    SectionReference = Provenance.SectionReference,
                    Notes = Provenance.Notes
                } : null
            };
        }
    }

    public sealed class PeakFactor
    {
        public double Value { get; set; }
        public StandardProvenance Provenance { get; set; }

        public PeakFactor() { }

        public PeakFactor(double value, StandardProvenance provenance = null)
        {
            Value = value;
            Provenance = provenance;
        }

        public PeakFactor Clone()
        {
            return new PeakFactor
            {
                Value = Value,
                Provenance = Provenance != null ? new StandardProvenance
                {
                    SourceLayer = Provenance.SourceLayer,
                    StandardId = Provenance.StandardId,
                    StandardName = Provenance.StandardName,
                    StandardVersion = Provenance.StandardVersion,
                    ParameterKey = Provenance.ParameterKey,
                    DocumentReference = Provenance.DocumentReference,
                    SectionReference = Provenance.SectionReference,
                    Notes = Provenance.Notes
                } : null
            };
        }
    }

    public sealed class GravityLoadingContribution
    {
        public string ContributionId { get; set; }
        public GravityLoadingContributionKind Kind { get; set; }
        public StandardQuantity Flow { get; set; }
        public StandardProvenance Provenance { get; set; }
        public string ReferenceIdentifier { get; set; }

        public GravityLoadingContribution() { }

        public GravityLoadingContribution(string contributionId, GravityLoadingContributionKind kind, StandardQuantity flow, StandardProvenance provenance = null, string referenceIdentifier = null)
        {
            ContributionId = contributionId;
            Kind = kind;
            Flow = flow;
            Provenance = provenance;
            ReferenceIdentifier = referenceIdentifier;
        }

        public GravityLoadingContribution Clone()
        {
            return new GravityLoadingContribution
            {
                ContributionId = ContributionId,
                Kind = Kind,
                Flow = Flow != null ? new StandardQuantity(Flow.Value, Flow.Unit) : null,
                Provenance = Provenance != null ? new StandardProvenance
                {
                    SourceLayer = Provenance.SourceLayer,
                    StandardId = Provenance.StandardId,
                    StandardName = Provenance.StandardName,
                    StandardVersion = Provenance.StandardVersion,
                    ParameterKey = Provenance.ParameterKey,
                    DocumentReference = Provenance.DocumentReference,
                    SectionReference = Provenance.SectionReference,
                    Notes = Provenance.Notes
                } : null,
                ReferenceIdentifier = ReferenceIdentifier
            };
        }
    }

    public sealed class KnownPointLoad
    {
        public string LoadId { get; set; }
        public StandardQuantity Flow { get; set; }
        public StandardProvenance Provenance { get; set; }
        public string ExternalReference { get; set; }

        public KnownPointLoad() { }

        public KnownPointLoad(string loadId, StandardQuantity flow, StandardProvenance provenance = null, string externalReference = null)
        {
            LoadId = loadId;
            Flow = flow;
            Provenance = provenance;
            ExternalReference = externalReference;
        }

        public GravityLoadingContribution ToContribution()
        {
            return new GravityLoadingContribution
            {
                ContributionId = LoadId,
                Kind = GravityLoadingContributionKind.KnownPointLoad,
                Flow = Flow != null ? new StandardQuantity(Flow.Value, Flow.Unit) : null,
                Provenance = Provenance,
                ReferenceIdentifier = ExternalReference
            };
        }
    }

    public sealed class LoadingCalculationTrace
    {
        public string Output { get; set; }
        public string Operation { get; set; }
        public string Formula { get; set; }
        public StandardQuantity CalculatedValue { get; set; }
        public List<string> InputReferences { get; set; } = new List<string>();
        public List<string> ProvenanceReferences { get; set; } = new List<string>();

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} = {1} [{2}] => {3}", Output, Formula, Operation, CalculatedValue);
        }
    }

    public sealed class GravityLoadingCalculationInput
    {
        public GravityPopulationBasis PopulationBasis { get; set; }
        public GravityPopulationBasis PopulationEquivalentBasis { get; set; }
        public WastewaterGenerationRate GenerationRate { get; set; }
        public PeakFactor PeakFactor { get; set; }
        public List<GravityLoadingContribution> Contributions { get; set; } = new List<GravityLoadingContribution>();
        public List<KnownPointLoad> KnownPointLoads { get; set; } = new List<KnownPointLoad>();
    }

    public sealed class GravityLoadingResult
    {
        public GravityLoadingCalculationStatus Status { get; set; }
        public GravityPopulationBasis Basis { get; set; }
        public WastewaterGenerationRate GenerationRate { get; set; }
        public StandardQuantity BaseSanitaryFlow { get; set; }
        public PeakFactor PeakFactor { get; set; }
        public StandardQuantity PeakedSanitaryFlow { get; set; }
        public StandardQuantity InfiltrationTotal { get; set; }
        public StandardQuantity InflowTotal { get; set; }
        public StandardQuantity IndustrialProcessTotal { get; set; }
        public StandardQuantity KnownPointLoadTotal { get; set; }
        public StandardQuantity TotalDesignLoading { get; set; }
        public List<LoadingCalculationTrace> Trace { get; set; } = new List<LoadingCalculationTrace>();
        public string CalculationFingerprint { get; set; }
        public DesignBasisValidationResult ValidationResult { get; set; } = new DesignBasisValidationResult();
    }
}
