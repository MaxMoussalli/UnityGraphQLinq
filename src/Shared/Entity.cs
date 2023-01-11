using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
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

        internal static Dictionary<ID, Entity> s_ExistingEntities { get; set; } = new Dictionary<ID, Entity>();
        public static Entity GetCachedEntity(ID id)
        {
            s_ExistingEntities.TryGetValue(id, out Entity entity);
            return entity;
        }

        public static IEnumerable<Entity> ExistingEntities => s_ExistingEntities.Values;

        /// <summary>
        /// 
        /// </summary>
        public ID Id { get; set; }

        /// <summary>
        /// The date this Entity has been updated
        /// </summary>
        [GraphQLinqIgnore]
        public DateTime? UpdatedDate { get; private set; }

        public Entity()
        {
        }

        public Entity(ID id)
        {
            Id = id;
            s_ExistingEntities.Add(Id, this);
        }

        public void Dispose()
        {
            s_ExistingEntities.Remove(Id);
        }

        [OnDeserialized]
        internal void OnDeserializedMethod(StreamingContext context)
        {
            UpdatedDate = DateTime.Now;

            OnUpdated?.Invoke(this);
        }
    }

    /// <summary>
    /// Serialize/Deserialize <see cref="ID"/> as a simple string
    /// </summary>
    internal class EntitySerializer : JsonConverter
    {
        public override bool CanWrite { get { return false; } }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            // Read Id value
            JObject jObject = JObject.Load(reader);
            var id = jObject["Id"].Value<string>();

            if (id == null)
                return null;

            // Recreate the reader
            using (reader = jObject.CreateReader())
            {
                Entity entity = existingValue as Entity;
                if (entity != null || Entity.s_ExistingEntities.TryGetValue(id, out entity))
                {
                    // Populate existing data
                    serializer.Populate(reader, entity);
                }
                else
                {
                    // Create new instance and add it to ExistingEntities
                    entity = (Entity)Activator.CreateInstance(objectType);
                    serializer.Populate(reader, entity);
                    Entity.s_ExistingEntities.Add(entity.Id, entity);
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
