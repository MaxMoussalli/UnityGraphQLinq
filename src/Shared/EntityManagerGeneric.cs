using System.Collections.Generic;

namespace GraphQLinq
{
    public partial class EntityManager<T> : EntityManager where T : Entity
    {
        private Dictionary<ID, T> m_ExistingEntities = new Dictionary<ID, T>();

        public delegate void EntityEventHandler(T entity);
        public event EntityEventHandler OnEntityAdded;
        public event EntityEventHandler OnEntityRemoved;
        public event EntityEventHandler OnEntityUpdated;

        protected IReadOnlyDictionary<ID, T> ExistingEntities => m_ExistingEntities;
        public bool TryGetValue(ID id, out T entity)
        {
            return m_ExistingEntities.TryGetValue(id, out entity);
        }

        public EntityManager()
        {
            RegisterManager(this);
        }

        ~EntityManager()
        {
            UnregisterManager(this);
        }

        protected override void OnEntityAddedInternal(Entity entity)
        {
            var toAdd = entity as T;

            if (toAdd == null)
                return;
            
            m_ExistingEntities.Add(entity.Id, toAdd);
            entity.OnUpdated += e => OnEntityUpdated?.Invoke(e as T);
            OnEntityAdded?.Invoke(toAdd);
        }

        protected override void OnEntityRemovedInternal(Entity entity)
        {
            m_ExistingEntities.Remove(entity.Id);

            var removed = entity as T;
            if (removed == null)
                return;

            OnEntityRemoved?.Invoke(removed);
        }
    }
}
