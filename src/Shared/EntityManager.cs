using System;
using System.Collections.Generic;
using IdToEntity = System.Collections.Generic.Dictionary<GraphQLinq.ID, GraphQLinq.Entity>;

namespace GraphQLinq
{
    public abstract partial class EntityManager
    {
        protected static Dictionary<Type, IdToEntity> s_ExistingEntities { get; set; } = new Dictionary<Type, IdToEntity>();
        protected static Dictionary<Type, EntityManager> s_Managers = new Dictionary<Type, EntityManager>();

        public static bool TryGetValue<T>(ID id, out T entity) where T : Entity
        {
            var found = TryGetValue(id, typeof(T), out var e);
            entity = e as T;
            return found;
        }

        public static bool TryGetValue(ID id, Type type, out Entity entity)
        {
            entity = null;

            // find IdToEntity from type
            if (!s_ExistingEntities.TryGetValue(type, out var idToEntity))
                return false;

            // find entity from id
            return idToEntity.TryGetValue(id, out entity);
        }

        /// <summary>
        /// Retrieve all base types from given type
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private static List<Type> GetBaseTypes(Type type)
        {
            var result = new List<Type>();

            Type entityType = typeof(Entity);
            if (!type.IsSubclassOf(entityType))
                return result;

            var t = type;
            while (t != null && t != entityType)
            {
                result.Add(t);
                t = t.BaseType;
            }

            return result;
        }

        public static void Add(Entity entity)
        {
            var types = GetBaseTypes(entity.GetType());

            foreach (var type in types)
            {
                if (!s_ExistingEntities.TryGetValue(type, out var idToEntity))
                {
                    idToEntity = new IdToEntity();
                    s_ExistingEntities.Add(type, idToEntity);
                }

                idToEntity.Add(entity.Id, entity);

                // notify managers
                if (s_Managers.TryGetValue(type, out var res))
                    res?.OnEntityAddedInternal(entity);
            }
        }

        public static bool Remove(Entity entity)
        {
            var types = GetBaseTypes(entity.GetType());

            foreach (var type in types)
            {
                // find IdToEntity from type
                if (!s_ExistingEntities.TryGetValue(type, out var idToEntity))
                    return false;

                // remove entity from idToEntity
                if (!idToEntity.Remove(entity.Id))
                    return false;

                // notify managers
                if (s_Managers.TryGetValue(type, out var res))
                    res?.OnEntityRemovedInternal(entity);
            }

            return true;
        }

        public static void RegisterManager<T>(EntityManager<T> manager) where T : Entity
        {
            Type type = typeof(T);
            s_Managers.Add(type, manager);

            // Register in manager the existing entities that correspond to the type
            if (s_ExistingEntities.TryGetValue(type, out var idToEntity))
            {
                foreach (var entity in idToEntity.Values)
                {
                    manager.OnEntityAddedInternal(entity);
                }
            }
        }

        public static void UnregisterManager<T>(EntityManager<T> manager) where T : Entity
        {
            Type type = typeof(T);

            // Unregister from manager the existing entities that correspond to the type
            if (s_ExistingEntities.TryGetValue(type, out var idToEntity))
            {
                foreach (var entity in idToEntity.Values)
                {
                    manager.OnEntityRemovedInternal(entity);
                }
            }

            s_Managers.Remove(typeof(T));
        }

        protected abstract void OnEntityAddedInternal(Entity entity);
        protected abstract void OnEntityRemovedInternal(Entity entity);
    }
}
