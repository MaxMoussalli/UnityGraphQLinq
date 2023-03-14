using System;
using System.Collections.Generic;

namespace GraphQLinq
{
    public abstract partial class EntityManager
    {
        internal static Dictionary<ID, Entity> s_ExistingEntities { get; set; } = new Dictionary<ID, Entity>();
        protected static Dictionary<Type, EntityManager> s_Managers = new Dictionary<Type, EntityManager>();

        public static Entity Get(ID id)
        {
            s_ExistingEntities.TryGetValue(id, out Entity entity);
            return entity;
        }

        public static bool TryGetValue(ID id, out Entity entity)
        {
            return s_ExistingEntities.TryGetValue(id, out entity);
        }

        public static void Add(Entity entity)
        {
            s_ExistingEntities.Add(entity.Id, entity);

            if (s_Managers.TryGetValue(entity.GetType(), out var res))
                res?.OnEntityAddedInternal(entity);
        }

        public static void Remove(Entity entity)
        {
            s_ExistingEntities.Remove(entity.Id);

            if (s_Managers.TryGetValue(entity.GetType(), out var res))
                res?.OnEntityRemovedInternal(entity);
        }

        public static void RegisterManager<T>(EntityManager<T> manager) where T : Entity
        {
            s_Managers.Add(typeof(T), manager);
        }

        public static void UnregisterManager<T>(EntityManager<T> manager) where T : Entity
        {
            s_Managers.Remove(typeof(T));
        }

        protected abstract void OnEntityAddedInternal(Entity entity);
        protected abstract void OnEntityRemovedInternal(Entity entity);
    }
}
