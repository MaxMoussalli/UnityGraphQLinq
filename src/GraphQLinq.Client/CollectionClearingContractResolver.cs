using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Newtonsoft.Json.Serialization;

namespace GraphQLinq
{
    /// <summary>
    /// Populate existing collection will add new element by default instead of replace them
    /// This contract resolver will fix this
    /// https://stackoverflow.com/questions/42165648/populate-object-where-objects-are-reused-and-arrays-are-replaced
    /// </summary>
    public class CollectionClearingContractResolver : DefaultContractResolver
    {
        static void ClearGenericCollectionCallback<T>(object o, StreamingContext c)
        {
            var collection = o as ICollection<T>;
            if (collection == null || collection is Array || collection.IsReadOnly)
                return;
            collection.Clear();
        }

        static SerializationCallback ClearListCallback = (o, c) =>
        {
            var collection = o as IList;
            if (collection == null || collection is Array || collection.IsReadOnly)
                return;
            collection.Clear();
        };

        protected override JsonArrayContract CreateArrayContract(Type objectType)
        {
            var contract = base.CreateArrayContract(objectType);
            if (!objectType.IsArray)
            {
                if (objectType.GetInterface(nameof(IEnumerable)) != null)
                {
                    contract.OnDeserializingCallbacks.Add(ClearListCallback);
                }
            }

            return contract;
        }
    }
}