using System;
using System.Web.Script.Serialization;

namespace SewerPilot.OpenFlows.Adapter
{
    public static class StandardsSerializer
    {
        private static JavaScriptSerializer Serializer() => new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };

        public static string Serialize(StandardDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            definition.Validate();
            return Serializer().Serialize(definition);
        }

        public static StandardDefinition Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new StandardsValidationException("Standards JSON cannot be empty.");

            StandardDefinition def;
            try
            {
                def = Serializer().Deserialize<StandardDefinition>(json);
            }
            catch (Exception ex)
            {
                throw new StandardsValidationException("Malformed standards JSON: " + ex.Message);
            }

            if (def == null)
                throw new StandardsValidationException("Standards JSON deserialized to null.");

            if (def.SchemaVersion != 1)
                throw new StandardsValidationException("Unsupported standards schema: " + def.SchemaVersion, "UNSUPPORTED_STANDARD_SCHEMA");

            def.Validate();
            return def;
        }
    }
}
