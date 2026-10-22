using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace UnityEssentials.Collections.Editor
{
    internal enum SerializedKeyStatus : byte
    {
        Valid,
        NullKey,
        Duplicate,
    }

    /// <summary>
    /// Key validation, unique key suggestions and default values for serialized dictionary rows.
    /// Validation mirrors the runtime: null keys are skipped and the first occurrence of a key wins.
    /// </summary>
    internal static class SerializedDictionaryKeyUtility
    {
        private const string NewKeyText = "New Key";

        private static readonly HashSet<object> _seenKeys = new HashSet<object>();
        private static readonly List<SerializedProperty> _seenKeyProperties = new List<SerializedProperty>();

        internal static void ValidateKeys(SerializedProperty entries, int count, SerializedKeyStatus[] statuses)
        {
            try
            {
                ValidateByBoxedValue(entries, count, statuses);
            }
            catch (Exception)
            {
                // boxedValue rejects some key shapes (arrays, unsupported property types); compare raw data instead.
                ValidateBySerializedData(entries, count, statuses);
            }
        }

        internal static void RemoveInvalidEntries(SerializedProperty entries)
        {
            int count = entries.arraySize;
            SerializedKeyStatus[] statuses = new SerializedKeyStatus[count];
            ValidateKeys(entries, count, statuses);

            for (int i = count - 1; i >= 0; i--)
            {
                if (statuses[i] != SerializedKeyStatus.Valid)
                {
                    entries.DeleteArrayElementAtIndex(i);
                }
            }
        }

        internal static bool TrySuggestUniqueKey(Type keyType, SerializedProperty entries, int count, out object key)
        {
            if (typeof(UnityEngine.Object).IsAssignableFrom(keyType))
            {
                key = null;
                return true;
            }

            if (keyType == typeof(string))
            {
                key = SuggestString(entries, count);
                return true;
            }

            if (keyType == typeof(bool))
            {
                key = SuggestBool(entries, count);
                return true;
            }

            if (keyType == typeof(char))
            {
                key = SuggestChar(entries, count);
                return true;
            }

            if (keyType.IsEnum)
            {
                key = SuggestEnum(keyType, entries, count);
                return true;
            }

            if (TryGetIntegralRange(keyType, out decimal min, out decimal max))
            {
                key = Convert.ChangeType(SuggestIntegral(entries, count, min, max), keyType);
                return true;
            }

            if (keyType == typeof(float) || keyType == typeof(double))
            {
                key = Convert.ChangeType(SuggestFloatingPoint(entries, count), keyType);
                return true;
            }

            return TryCreateDefaultValue(keyType, out key);
        }

        internal static bool TryCreateDefaultValue(Type type, out object value)
        {
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                value = null;
                return true;
            }

            if (type == typeof(string))
            {
                value = string.Empty;
                return true;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (type.IsValueType || (!type.IsAbstract && type.GetConstructor(flags, null, Type.EmptyTypes, null) != null))
            {
                value = Activator.CreateInstance(type, true);
                return true;
            }

            value = null;
            return false;
        }

        private static void ValidateByBoxedValue(SerializedProperty entries, int count, SerializedKeyStatus[] statuses)
        {
            try
            {
                for (int i = 0; i < count; i++)
                {
                    SerializedProperty key = GetKeyProperty(entries, i);
                    if (IsNullKey(key))
                    {
                        statuses[i] = SerializedKeyStatus.NullKey;
                        continue;
                    }

                    // boxedValue yields fresh instances, so the default comparer matches the runtime's
                    // EqualityComparer<TKey>.Default: struct value equality, Equals overrides, otherwise identity.
                    statuses[i] = _seenKeys.Add(key.boxedValue) ? SerializedKeyStatus.Valid : SerializedKeyStatus.Duplicate;
                }
            }
            finally
            {
                _seenKeys.Clear();
            }
        }

        private static void ValidateBySerializedData(SerializedProperty entries, int count, SerializedKeyStatus[] statuses)
        {
            try
            {
                for (int i = 0; i < count; i++)
                {
                    SerializedProperty key = GetKeyProperty(entries, i);
                    if (IsNullKey(key))
                    {
                        statuses[i] = SerializedKeyStatus.NullKey;
                        continue;
                    }

                    statuses[i] = SerializedKeyStatus.Valid;
                    for (int j = 0; j < _seenKeyProperties.Count; j++)
                    {
                        if (SerializedProperty.DataEquals(key, _seenKeyProperties[j]))
                        {
                            statuses[i] = SerializedKeyStatus.Duplicate;
                            break;
                        }
                    }

                    if (statuses[i] == SerializedKeyStatus.Valid)
                    {
                        _seenKeyProperties.Add(key);
                    }
                }
            }
            finally
            {
                _seenKeyProperties.Clear();
            }
        }

        private static bool IsNullKey(SerializedProperty key)
        {
            switch (key.propertyType)
            {
                case SerializedPropertyType.ObjectReference:
                    return key.objectReferenceValue == null;
                case SerializedPropertyType.ManagedReference:
                    return key.managedReferenceValue == null;
                default:
                    return false;
            }
        }

        private static SerializedProperty GetKeyProperty(SerializedProperty entries, int index)
        {
            return entries.GetArrayElementAtIndex(index).FindPropertyRelative(SerializedDictionaryDrawer.KeyField);
        }

        private static object GetKeyValue(SerializedProperty entries, int index)
        {
            return GetKeyProperty(entries, index).boxedValue;
        }

        private static string SuggestString(SerializedProperty entries, int count)
        {
            HashSet<string> used = new HashSet<string>();
            for (int i = 0; i < count; i++)
            {
                used.Add((string)GetKeyValue(entries, i));
            }

            if (!used.Contains(string.Empty))
            {
                return string.Empty;
            }

            if (!used.Contains(NewKeyText))
            {
                return NewKeyText;
            }

            for (int suffix = 1; ; suffix++)
            {
                string candidate = NewKeyText + " " + suffix;
                if (!used.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        private static bool SuggestBool(SerializedProperty entries, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!(bool)GetKeyValue(entries, i))
                {
                    return true;
                }
            }

            return false;
        }

        private static char SuggestChar(SerializedProperty entries, int count)
        {
            // Character properties box as ushort, not char.
            HashSet<int> used = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                used.Add(Convert.ToInt32(GetKeyValue(entries, i)));
            }

            for (int candidate = 'a'; candidate <= char.MaxValue; candidate++)
            {
                if (!used.Contains(candidate))
                {
                    return (char)candidate;
                }
            }

            return 'a';
        }

        private static object SuggestEnum(Type enumType, SerializedProperty entries, int count)
        {
            // Enum properties box as their underlying integer, so compare numerically.
            HashSet<decimal> used = new HashSet<decimal>();
            for (int i = 0; i < count; i++)
            {
                used.Add(Convert.ToDecimal(GetKeyValue(entries, i)));
            }

            Array values = Enum.GetValues(enumType);
            for (int i = 0; i < values.Length; i++)
            {
                object value = values.GetValue(i);
                if (!used.Contains(Convert.ToDecimal(value)))
                {
                    return value;
                }
            }

            return values.Length > 0 ? values.GetValue(0) : Activator.CreateInstance(enumType);
        }

        private static decimal SuggestIntegral(SerializedProperty entries, int count, decimal min, decimal max)
        {
            if (count == 0)
            {
                return 0m;
            }

            HashSet<decimal> used = new HashSet<decimal>();
            decimal highest = min;
            for (int i = 0; i < count; i++)
            {
                decimal value = Convert.ToDecimal(GetKeyValue(entries, i));
                used.Add(value);
                if (value > highest)
                {
                    highest = value;
                }
            }

            if (highest < max)
            {
                return highest + 1m;
            }

            for (decimal candidate = min; candidate <= max; candidate++)
            {
                if (!used.Contains(candidate))
                {
                    return candidate;
                }
            }

            return min;
        }

        private static double SuggestFloatingPoint(SerializedProperty entries, int count)
        {
            if (count == 0)
            {
                return 0d;
            }

            double highest = double.MinValue;
            for (int i = 0; i < count; i++)
            {
                double value = Convert.ToDouble(GetKeyValue(entries, i));
                if (value > highest)
                {
                    highest = value;
                }
            }

            return highest == double.MinValue ? 0d : highest + 1d;
        }

        private static bool TryGetIntegralRange(Type type, out decimal min, out decimal max)
        {
            if (type == typeof(int))
            {
                min = int.MinValue;
                max = int.MaxValue;
                return true;
            }

            if (type == typeof(long))
            {
                min = long.MinValue;
                max = long.MaxValue;
                return true;
            }

            if (type == typeof(short))
            {
                min = short.MinValue;
                max = short.MaxValue;
                return true;
            }

            if (type == typeof(sbyte))
            {
                min = sbyte.MinValue;
                max = sbyte.MaxValue;
                return true;
            }

            if (type == typeof(byte))
            {
                min = byte.MinValue;
                max = byte.MaxValue;
                return true;
            }

            if (type == typeof(uint))
            {
                min = uint.MinValue;
                max = uint.MaxValue;
                return true;
            }

            if (type == typeof(ulong))
            {
                min = ulong.MinValue;
                max = ulong.MaxValue;
                return true;
            }

            if (type == typeof(ushort))
            {
                min = ushort.MinValue;
                max = ushort.MaxValue;
                return true;
            }

            min = 0m;
            max = 0m;
            return false;
        }
    }
}
