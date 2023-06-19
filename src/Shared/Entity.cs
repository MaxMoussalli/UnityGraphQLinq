using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.Runtime.Serialization;

namespace GraphQLinq
{
    namespace CustomEntity
    {
        public partial interface IEntity
        {
            ID Id { get; set; }
        }
    }

    [Serializable]
    [JsonConverter(typeof(EntitySerializer))]
    public partial class Entity : CustomEntity.IEntity, IDisposable
    {
        public delegate void OnUpdatedHandler(Entity entity);
        public event OnUpdatedHandler OnUpdated;

        /// <summary>
        /// 
        /// </summary>
        public ID Id { get; set; }

        /// <summary>
        /// The date this Entity has been updated
        /// </summary>
        [GraphQLinqIgnore]
        [JsonIgnore]
        public DateTime? UpdatedDate { get; private set; }

        public Entity()
        {
            UpdatedDate = DateTime.Now;
        }

        public Entity(ID id)
            :this()
        {
            Id = id;
        }

        public virtual void Dispose()
        {
            RemoveFromManager();
            OnUpdated = null;
        }

        public virtual void RegisterToManager()
        {
            EntityManager.Add(this);
        }

        public virtual void RemoveFromManager()
        {
            EntityManager.Remove(this);
        }

        protected virtual void FireOnUpdated()
        {
            UpdatedDate = DateTime.Now;
            OnUpdated?.Invoke(this);
        }

        [OnDeserialized]
        internal void OnDeserializedMethod(StreamingContext context)
        {
            FireOnUpdated();
        }
    }

    /// <summary>
    /// Serialize/Deserialize <see cref="ID"/> as a simple string
    /// </summary>
    internal class EntitySerializer : JsonConverter
    {
        public override bool CanWrite => false;

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            // Read Id value
            JObject jObject = JObject.Load(reader);

            if (!jObject.TryGetValue("id", StringComparison.OrdinalIgnoreCase, out var token))
                throw new SerializationException("Entity Id not found");

            if (token.Type != JTokenType.String)
                throw new SerializationException("Entity Id is not a string");

            var id = token.Value<string>();

            if (id == null)
                return null;

            // Recreate the reader (to avoid recursive call to this JsonConverter)
            using (reader = jObject.CreateReader())
            {
                Entity entity = existingValue as Entity;
                if (entity != null || EntityManager.TryGetValue(id, objectType, out entity))
                {
                    // Populate existing data
                    serializer.Populate(reader, entity);
                }
                else
                {
                    // Create new instance and add it to ExistingEntities
                    entity = (Entity)Activator.CreateInstance(objectType);
                    serializer.Populate(reader, entity);
                    entity.RegisterToManager();
                }

                return entity;
            }
        }

        public override bool CanConvert(Type objectType)
        {
            return typeof(Entity).IsAssignableFrom(objectType);
        }
    }
}
