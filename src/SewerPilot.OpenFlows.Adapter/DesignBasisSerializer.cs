using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace SewerPilot.OpenFlows.Adapter
{
    public static class DesignBasisSerializer
    {
        private static JavaScriptSerializer Serializer() => new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };

        public static string Serialize(DesignBasis designBasis)
        {
            if (designBasis == null) throw new ArgumentNullException(nameof(designBasis));
            return Serializer().Serialize(designBasis);
        }

        public static DesignBasis Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new DesignBasisValidationException("DesignBasis JSON cannot be empty.");

            DesignBasis db;
            try
            {
                db = Serializer().Deserialize<DesignBasis>(json);
            }
            catch (Exception ex)
            {
                throw new DesignBasisValidationException("Malformed DesignBasis JSON: " + ex.Message);
            }

            if (db == null)
                throw new DesignBasisValidationException("DesignBasis JSON deserialized to null.");

            if (db.SchemaVersion != 1)
                throw new DesignBasisValidationException("Unsupported DesignBasis schema: " + db.SchemaVersion, "UNSUPPORTED_DESIGN_BASIS_SCHEMA");

            return db;
        }

        public sealed class DesignBasisSnapshotDto
        {
            public int SchemaVersion { get; set; } = 1;
            public string DesignBasisId { get; set; }
            public string DesignBasisVersion { get; set; }
            public string ProjectId { get; set; }
            public string ContentFingerprint { get; set; }
            public string CreatedUtc { get; set; }
            public List<DesignBasisStandardReference> StandardReferences { get; set; } = new List<DesignBasisStandardReference>();
            public List<DesignBasisAssumption> Assumptions { get; set; } = new List<DesignBasisAssumption>();
            public List<DesignBasisOverride> Overrides { get; set; } = new List<DesignBasisOverride>();
        }

        public static string SerializeSnapshot(DesignBasisSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            var dto = new DesignBasisSnapshotDto
            {
                SchemaVersion = snapshot.SchemaVersion,
                DesignBasisId = snapshot.DesignBasisId,
                DesignBasisVersion = snapshot.DesignBasisVersion,
                ProjectId = snapshot.ProjectId,
                ContentFingerprint = snapshot.ContentFingerprint,
                CreatedUtc = snapshot.CreatedUtc.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                StandardReferences = new List<DesignBasisStandardReference>(snapshot.StandardReferences),
                Assumptions = new List<DesignBasisAssumption>(snapshot.Assumptions),
                Overrides = new List<DesignBasisOverride>(snapshot.Overrides)
            };
            return Serializer().Serialize(dto);
        }

        public static DesignBasisSnapshot DeserializeSnapshot(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new DesignBasisValidationException("DesignBasisSnapshot JSON cannot be empty.");

            DesignBasisSnapshotDto dto;
            try
            {
                dto = Serializer().Deserialize<DesignBasisSnapshotDto>(json);
            }
            catch (Exception ex)
            {
                throw new DesignBasisValidationException("Malformed DesignBasisSnapshot JSON: " + ex.Message);
            }

            if (dto == null)
                throw new DesignBasisValidationException("DesignBasisSnapshot JSON deserialized to null.");

            if (dto.SchemaVersion != 1)
                throw new DesignBasisValidationException("Unsupported DesignBasisSnapshot schema: " + dto.SchemaVersion, "UNSUPPORTED_DESIGN_BASIS_SCHEMA");

            DateTime createdUtc = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(dto.CreatedUtc))
                DateTime.TryParse(dto.CreatedUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out createdUtc);

            return new DesignBasisSnapshot(
                dto.SchemaVersion,
                dto.DesignBasisId,
                dto.DesignBasisVersion,
                dto.ProjectId,
                dto.ContentFingerprint,
                createdUtc,
                dto.StandardReferences,
                dto.Assumptions,
                dto.Overrides);
        }
    }
}
