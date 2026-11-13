using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace SewerPilot.OpenFlows.Adapter
{
    // StormFlow-specific persistence format. GravityFlow isolation is enforced on load: a GravityFlow or
    // other-module payload is rejected rather than reinterpreted.
    public static class StormFlowDesignBasisSerializer
    {
        private static JavaScriptSerializer Serializer() => new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };

        public static string Serialize(StormFlowDesignBasis basis)
        {
            if (basis == null) throw new ArgumentNullException(nameof(basis));
            return Serializer().Serialize(basis);
        }

        public static StormFlowDesignBasis Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new DesignBasisValidationException("StormFlowDesignBasis JSON cannot be empty.");

            StormFlowDesignBasis basis;
            try { basis = Serializer().Deserialize<StormFlowDesignBasis>(json); }
            catch (Exception ex) { throw new DesignBasisValidationException("Malformed StormFlowDesignBasis JSON: " + ex.Message); }

            if (basis == null) throw new DesignBasisValidationException("StormFlowDesignBasis JSON deserialized to null.");
            if (!string.Equals(basis.Module, StormFlowDesignBasis.ModuleName, StringComparison.Ordinal))
                throw new DesignBasisValidationException("Unsupported module: " + basis.Module, "UNSUPPORTED_MODULE");

            return basis;
        }

        public sealed class StormFlowSnapshotDto
        {
            public DesignBasisSerializer.DesignBasisSnapshotDto CommonSnapshot { get; set; }
            public string StormContentFingerprint { get; set; }
            public string CreatedUtc { get; set; }

            public List<StormDesignCriterion> Project { get; set; } = new List<StormDesignCriterion>();
            public List<StormDesignCriterion> Rainfall { get; set; } = new List<StormDesignCriterion>();
            public List<StormDesignCriterion> Hydrology { get; set; } = new List<StormDesignCriterion>();
            public List<StormDesignCriterion> NetworkCriteria { get; set; } = new List<StormDesignCriterion>();
            public List<StormDesignCriterion> HydraulicCriteria { get; set; } = new List<StormDesignCriterion>();
        }

        public static string SerializeSnapshot(StormFlowDesignBasisSnapshot snapshot)
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

            var dto = new StormFlowSnapshotDto
            {
                CommonSnapshot = commonDto,
                StormContentFingerprint = snapshot.StormContentFingerprint,
                CreatedUtc = snapshot.CreatedUtc.ToString("o", CultureInfo.InvariantCulture),
                Project = new List<StormDesignCriterion>(snapshot.Project),
                Rainfall = new List<StormDesignCriterion>(snapshot.Rainfall),
                Hydrology = new List<StormDesignCriterion>(snapshot.Hydrology),
                NetworkCriteria = new List<StormDesignCriterion>(snapshot.NetworkCriteria),
                HydraulicCriteria = new List<StormDesignCriterion>(snapshot.HydraulicCriteria)
            };

            return Serializer().Serialize(dto);
        }

        public static StormFlowDesignBasisSnapshot DeserializeSnapshot(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new DesignBasisValidationException("StormFlowDesignBasisSnapshot JSON cannot be empty.");

            StormFlowSnapshotDto dto;
            try { dto = Serializer().Deserialize<StormFlowSnapshotDto>(json); }
            catch (Exception ex) { throw new DesignBasisValidationException("Malformed StormFlowDesignBasisSnapshot JSON: " + ex.Message); }

            if (dto == null) throw new DesignBasisValidationException("StormFlowDesignBasisSnapshot JSON deserialized to null.");
            if (dto.CommonSnapshot == null) throw new DesignBasisValidationException("CommonSnapshot is missing in StormFlow snapshot JSON.");

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

            return new StormFlowDesignBasisSnapshot(commonSnapshot, dto.StormContentFingerprint, createdUtc,
                dto.Project, dto.Rainfall, dto.Hydrology, dto.NetworkCriteria, dto.HydraulicCriteria);
        }
    }
}
