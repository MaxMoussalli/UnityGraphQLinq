using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;

namespace GraphQLinq
{
    [Serializable]
    [JsonConverter(typeof(JSONObjectSerializer))]
    [DebuggerDisplay("{JObject}")]
    public struct JSONObject
    {
        [JsonIgnore]
        public JObject JObject { get; private set; }

        public static implicit operator JSONObject(JObject jObject)
        {
            return new JSONObject()
            {
                JObject = jObject,
            };
        }
        public static implicit operator string(JSONObject jsonObj) => jsonObj.ToString();

        public override string ToString()
        {
            return JObject.ToString();
        }
    }

    /// <summary>
    /// Serialize/Deserialize <see cref="JSONObject"/> as a JObject
    /// </summary>
    internal class JSONObjectSerializer : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var jsonObj = (JSONObject)value;
            serializer.Serialize(writer, jsonObj.JObject?.ToObject<object>());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var jObject = JObject.Load(reader);
            return (JSONObject)jObject;
        }

        public override bool CanConvert(Type objectType)
        {
            return typeof(JSONObject).IsAssignableFrom(objectType);
        }
    }
}
