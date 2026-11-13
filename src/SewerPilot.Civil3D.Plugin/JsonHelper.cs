using System;
using System.Web.Script.Serialization;

namespace SewerPilot.Civil3D.Plugin
{
    public static class JsonHelper
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static string Serialize(object obj)
        {
            return Serializer.Serialize(obj);
        }

        public static T Deserialize<T>(string json)
        {
            return Serializer.Deserialize<T>(json);
        }
    }
}
