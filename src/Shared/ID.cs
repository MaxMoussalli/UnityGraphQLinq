using Newtonsoft.Json;
using System;
using System.Diagnostics;

namespace GraphQLinq
{
    [Serializable]
    [JsonConverter(typeof(IDSerializer))]
    [DebuggerDisplay("{m_ID}")]
    public struct ID
    {
        private string m_ID { get; set; }

        public static implicit operator ID(string id) => new ID() { m_ID = id };
        public static implicit operator string(ID id) => id.m_ID;

        public override string ToString()
        {
            return m_ID;
        }
    }

    /// <summary>
    /// Serialize/Deserialize <see cref="ID"/> as a simple string
    /// </summary>
    internal class IDSerializer : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var name = (ID)value;
            serializer.Serialize(writer, (string)name);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            return (ID)(string)reader.Value;
        }

        public override bool CanConvert(Type objectType)
        {
            return typeof(ID).IsAssignableFrom(objectType);
        }
    }
}
