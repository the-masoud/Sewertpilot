using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace SewerPilot.OpenFlows.Adapter
{
    public static class GravityFlowDesignBasisSerializer
    {
        private static JavaScriptSerializer Serializer() => new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };

        public static string Serialize(GravityFlowDesignBasis basis)
        {
            if (basis == null) throw new ArgumentNullException(nameof(basis));
            return Serializer().Serialize(basis);
        }

        public static GravityFlowDesignBasis Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new DesignBasisValidationException("GravityFlowDesignBasis JSON cannot be empty.");

            GravityFlowDesignBasis basis;
            try
            {
                basis = Serializer().Deserialize<GravityFlowDesignBasis>(json);
            }
            catch (Exception ex)
            {
                throw new DesignBasisValidationException("Malformed GravityFlowDesignBasis JSON: " + ex.Message);
            }

            if (basis == null)
                throw new DesignBasisValidationException("GravityFlowDesignBasis JSON deserialized to null.");

            if (!string.Equals(basis.Module, "GravityFlow", StringComparison.Ordinal))
                throw new DesignBasisValidationException("Unsupported module: " + basis.Module, "UNSUPPORTED_MODULE");

            return basis;
        }

        public sealed class GravityFlowSnapshotDto
        {
            public DesignBasisSerializer.DesignBasisSnapshotDto CommonSnapshot { get; set; }
            public string GravityContentFingerprint { get; set; }
            public string CreatedUtc { get; set; }

            public List<GravityDesignCriterion> Project { get; set; } = new List<GravityDesignCriterion>();
            public List<GravityDesignCriterion> Loading { get; set; } = new List<GravityDesignCriterion>();
            public List<GravityDesignCriterion> PipeCriteria { get; set; } = new List<GravityDesignCriterion>();
            public List<GravityDesignCriterion> StructureCriteria { get; set; } = new List<GravityDesignCriterion>();
            public List<GravityDesignCriterion> VerticalDesign { get; set; } = new List<GravityDesignCriterion>();
            public List<GravityDesignCriterion> HydraulicCriteria { get; set; } = new List<GravityDesignCriterion>();
            public List<GravityDesignCriterion> SolverSettings { get; set; } = new List<GravityDesignCriterion>();
        }

        public static string SerializeSnapshot(GravityFlowDesignBasisSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            var commonDto = new DesignBasisSerializer.DesignBasisSnapshotDto
            {
                SchemaVersion = snapshot.CommonSnapshot.SchemaVersion,
                DesignBasisId = snapshot.CommonSnapshot.DesignBasisId,
                DesignBasisVersion = snapshot.CommonSnapshot.DesignBasisVersion,
                ProjectId = snapshot.CommonSnapshot.ProjectId,
                ContentFingerprint = snapshot.CommonSnapshot.ContentFingerprint,
                CreatedUtc = snapshot.CommonSnapshot.CreatedUtc.ToString("o", CultureInfo.InvariantCulture),
                StandardReferences = new List<DesignBasisStandardReference>(snapshot.CommonSnapshot.StandardReferences),
                Assumptions = new List<DesignBasisAssumption>(snapshot.CommonSnapshot.Assumptions),
                Overrides = new List<DesignBasisOverride>(snapshot.CommonSnapshot.Overrides)
            };

            var dto = new GravityFlowSnapshotDto
            {
                CommonSnapshot = commonDto,
                GravityContentFingerprint = snapshot.GravityContentFingerprint,
                CreatedUtc = snapshot.CreatedUtc.ToString("o", CultureInfo.InvariantCulture),
                Project = new List<GravityDesignCriterion>(snapshot.Project),
                Loading = new List<GravityDesignCriterion>(snapshot.Loading),
                PipeCriteria = new List<GravityDesignCriterion>(snapshot.PipeCriteria),
                StructureCriteria = new List<GravityDesignCriterion>(snapshot.StructureCriteria),
                VerticalDesign = new List<GravityDesignCriterion>(snapshot.VerticalDesign),
                HydraulicCriteria = new List<GravityDesignCriterion>(snapshot.HydraulicCriteria),
                SolverSettings = new List<GravityDesignCriterion>(snapshot.SolverSettings)
            };

            return Serializer().Serialize(dto);
        }

        public static GravityFlowDesignBasisSnapshot DeserializeSnapshot(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new DesignBasisValidationException("GravityFlowDesignBasisSnapshot JSON cannot be empty.");

            GravityFlowSnapshotDto dto;
            try
            {
                dto = Serializer().Deserialize<GravityFlowSnapshotDto>(json);
            }
            catch (Exception ex)
            {
                throw new DesignBasisValidationException("Malformed GravityFlowDesignBasisSnapshot JSON: " + ex.Message);
            }

            if (dto == null)
                throw new DesignBasisValidationException("GravityFlowDesignBasisSnapshot JSON deserialized to null.");

            if (dto.CommonSnapshot == null)
                throw new DesignBasisValidationException("CommonSnapshot is missing in GravityFlow snapshot JSON.");

            DateTime commonCreatedUtc = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(dto.CommonSnapshot.CreatedUtc))
                DateTime.TryParse(dto.CommonSnapshot.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out commonCreatedUtc);

            var commonSnapshot = new DesignBasisSnapshot(
                dto.CommonSnapshot.SchemaVersion,
                dto.CommonSnapshot.DesignBasisId,
                dto.CommonSnapshot.DesignBasisVersion,
                dto.CommonSnapshot.ProjectId,
                dto.CommonSnapshot.ContentFingerprint,
                commonCreatedUtc,
                dto.CommonSnapshot.StandardReferences,
                dto.CommonSnapshot.Assumptions,
                dto.CommonSnapshot.Overrides);

            DateTime createdUtc = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(dto.CreatedUtc))
                DateTime.TryParse(dto.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out createdUtc);

            return new GravityFlowDesignBasisSnapshot(
                commonSnapshot,
                dto.GravityContentFingerprint,
                createdUtc,
                dto.Project,
                dto.Loading,
                dto.PipeCriteria,
                dto.StructureCriteria,
                dto.VerticalDesign,
                dto.HydraulicCriteria,
                dto.SolverSettings);
        }
    }
}
