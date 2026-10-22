using System;
using UnityEngine;

namespace UnityEssentials.Collections
{
    /// <summary>
    /// One serialized row of a <see cref="SerializedDictionary{TKey, TValue}"/>. Rows are what the
    /// inspector edits, so a row may hold a null or duplicate key while it is mid-edit.
    /// </summary>
    [Serializable]
    internal struct SerializedEntry<TKey, TValue>
    {
        [SerializeField] private TKey _key;
        [SerializeField] private TValue _value;

        internal SerializedEntry(TKey key, TValue value)
        {
            _key = key;
            _value = value;
        }

        internal TKey Key => _key;
        internal TValue Value => _value;
    }
}
