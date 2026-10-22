using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEssentials.Collections
{
    /// <summary>
    /// A dictionary Unity can serialize. The serialized row list is the inspector's source of truth;
    /// at runtime rows with a null key are ignored and the first row of a duplicated key wins.
    /// Runtime mutations are written back to the rows on the next serialize.
    /// </summary>
    [Serializable]
    public class SerializedDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>, ISerializationCallbackReceiver
    {
        [SerializeField] private List<SerializedEntry<TKey, TValue>> _entries = new List<SerializedEntry<TKey, TValue>>();

        private readonly Dictionary<TKey, TValue> _dictionary = new Dictionary<TKey, TValue>();
        private bool _dirty;

        public SerializedDictionary()
        {
        }

        /// <summary>Creates a dictionary holding a copy of every pair in <paramref name="source"/>.</summary>
        public SerializedDictionary(IDictionary<TKey, TValue> source)
        {
            foreach (var pair in source)
                _dictionary.Add(pair.Key, pair.Value);

            _dirty = true;
        }

        public int Count => _dictionary.Count;

        public Dictionary<TKey, TValue>.KeyCollection Keys => _dictionary.Keys;
        public Dictionary<TKey, TValue>.ValueCollection Values => _dictionary.Values;

        ICollection<TKey> IDictionary<TKey, TValue>.Keys => _dictionary.Keys;
        ICollection<TValue> IDictionary<TKey, TValue>.Values => _dictionary.Values;
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => _dictionary.Keys;
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => _dictionary.Values;

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

        public TValue this[TKey key]
        {
            get => _dictionary[key];
            set
            {
                _dictionary[key] = value;
                _dirty = true;
            }
        }

        public void Add(TKey key, TValue value)
        {
            _dictionary.Add(key, value);
            _dirty = true;
        }

        public bool TryAdd(TKey key, TValue value)
        {
            if (!_dictionary.TryAdd(key, value))
                return false;

            _dirty = true;
            return true;
        }

        public bool Remove(TKey key)
        {
            if (!_dictionary.Remove(key))
                return false;

            _dirty = true;
            return true;
        }

        public bool Remove(TKey key, out TValue value)
        {
            if (!_dictionary.Remove(key, out value))
                return false;

            _dirty = true;
            return true;
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            return _dictionary.TryGetValue(key, out value);
        }

        public bool ContainsKey(TKey key)
        {
            return _dictionary.ContainsKey(key);
        }

        public bool ContainsValue(TValue value)
        {
            return _dictionary.ContainsValue(value);
        }

        /// <summary>Empties the dictionary and its serialized rows, including null and duplicate-key rows.</summary>
        public void Clear()
        {
            _dictionary.Clear();
            _entries.Clear();
            _dirty = false;
        }

        /// <summary>Returns the underlying dictionary's struct enumerator, so <c>foreach</c> allocates nothing.</summary>
        public Dictionary<TKey, TValue>.Enumerator GetEnumerator()
        {
            return _dictionary.GetEnumerator();
        }

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
        {
            return _dictionary.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _dictionary.GetEnumerator();
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item)
        {
            Add(item.Key, item.Value);
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
        {
            return ((ICollection<KeyValuePair<TKey, TValue>>)_dictionary).Contains(item);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)_dictionary).CopyTo(array, arrayIndex);
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item)
        {
            if (!((ICollection<KeyValuePair<TKey, TValue>>)_dictionary).Remove(item))
                return false;

            _dirty = true;
            return true;
        }

        /// <summary>Writes runtime mutations back to the serialized rows, keeping the rows' order.</summary>
        public void OnBeforeSerialize()
        {
            if (!_dirty)
                return;

            var emitted = new HashSet<TKey>(_dictionary.Comparer);
            var write = 0;
            for (var read = 0; read < _entries.Count; read++)
            {
                var entry = _entries[read];
                var key = entry.Key;

                // Null-key rows and later duplicates of a live key are inspector conflicts: kept as they are.
                if (IsNullKey(key))
                {
                    _entries[write++] = entry;
                    continue;
                }

                if (!_dictionary.TryGetValue(key, out var value))
                    continue;

                _entries[write++] = emitted.Add(key) ? new SerializedEntry<TKey, TValue>(key, value) : entry;
            }

            _entries.RemoveRange(write, _entries.Count - write);

            foreach (var pair in _dictionary)
            {
                if (!emitted.Contains(pair.Key))
                    _entries.Add(new SerializedEntry<TKey, TValue>(pair.Key, pair.Value));
            }

            _dirty = false;
        }

        /// <summary>Rebuilds the dictionary from the serialized rows, skipping null keys; the first row of a key wins.</summary>
        public void OnAfterDeserialize()
        {
            _dictionary.Clear();

            for (var i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                if (IsNullKey(entry.Key))
                    continue;

                _dictionary.TryAdd(entry.Key, entry.Value);
            }

            _dirty = false;
        }

        // Unassigned or missing object keys deserialize as Unity "fake null" instances, not real nulls.
        private static bool IsNullKey(TKey key)
        {
            return key == null || (key is UnityEngine.Object unityObject && unityObject == null);
        }
    }
}
