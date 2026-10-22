using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace UnityEssentials.Collections.Editor
{
    internal sealed class SerializedDictionaryTypeInfo
    {
        internal SerializedDictionaryTypeInfo(Type keyType, Type valueType, bool keyHasCustomDrawer, bool valueHasCustomDrawer)
        {
            KeyType = keyType;
            ValueType = valueType;
            KeyHasCustomDrawer = keyHasCustomDrawer;
            ValueHasCustomDrawer = valueHasCustomDrawer;
        }

        internal Type KeyType { get; }
        internal Type ValueType { get; }
        internal bool KeyHasCustomDrawer { get; }
        internal bool ValueHasCustomDrawer { get; }
    }

    /// <summary>
    /// Resolves the key and value types behind a drawn dictionary field and whether either type has its own
    /// property drawer, so the dictionary drawer knows when inline child drawing must defer to that drawer.
    /// </summary>
    internal static class SerializedDictionaryTypeUtility
    {
        private static readonly Dictionary<Type, SerializedDictionaryTypeInfo> _typeInfos = new Dictionary<Type, SerializedDictionaryTypeInfo>();
        private static readonly Dictionary<Type, bool> _customDrawerLookups = new Dictionary<Type, bool>();
        private static HashSet<Type> _drawerTargets;
        private static HashSet<Type> _inheritedDrawerTargets;

        internal static SerializedDictionaryTypeInfo GetTypeInfo(Type fieldType)
        {
            if (_typeInfos.TryGetValue(fieldType, out SerializedDictionaryTypeInfo info))
            {
                return info;
            }

            Type type = fieldType;
            if (type.IsArray)
            {
                type = type.GetElementType();
            }
            else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                type = type.GetGenericArguments()[0];
            }

            Type keyType = null;
            Type valueType = null;
            for (; type != null; type = type.BaseType)
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(SerializedDictionary<,>))
                {
                    Type[] arguments = type.GetGenericArguments();
                    keyType = arguments[0];
                    valueType = arguments[1];
                    break;
                }
            }

            info = new SerializedDictionaryTypeInfo(
                keyType,
                valueType,
                keyType != null && HasCustomDrawer(keyType),
                valueType != null && HasCustomDrawer(valueType));
            _typeInfos.Add(fieldType, info);
            return info;
        }

        private static bool HasCustomDrawer(Type type)
        {
            if (_customDrawerLookups.TryGetValue(type, out bool hasDrawer))
            {
                return hasDrawer;
            }

            if (_drawerTargets == null)
            {
                CollectDrawerTargets();
            }

            for (Type current = type; current != null; current = current.BaseType)
            {
                // Base types only count when their drawer opted into useForChildren, mirroring Unity's lookup.
                HashSet<Type> targets = current == type ? _drawerTargets : _inheritedDrawerTargets;
                if (targets.Contains(current) || (current.IsGenericType && targets.Contains(current.GetGenericTypeDefinition())))
                {
                    hasDrawer = true;
                    break;
                }
            }

            _customDrawerLookups.Add(type, hasDrawer);
            return hasDrawer;
        }

        // CustomPropertyDrawer exposes its target only through internal fields; if a future Unity renames
        // them the lookup degrades to "no custom drawer" and keys/values are drawn inline.
        private static void CollectDrawerTargets()
        {
            _drawerTargets = new HashSet<Type>();
            _inheritedDrawerTargets = new HashSet<Type>();

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo typeField = typeof(CustomPropertyDrawer).GetField("m_Type", flags);
            FieldInfo useForChildrenField = typeof(CustomPropertyDrawer).GetField("m_UseForChildren", flags);
            if (typeField == null || useForChildrenField == null)
            {
                return;
            }

            foreach (Type drawerType in TypeCache.GetTypesWithAttribute<CustomPropertyDrawer>())
            {
                if (!typeof(PropertyDrawer).IsAssignableFrom(drawerType))
                {
                    continue;
                }

                object[] attributes = drawerType.GetCustomAttributes(typeof(CustomPropertyDrawer), false);
                for (int i = 0; i < attributes.Length; i++)
                {
                    if (!(typeField.GetValue(attributes[i]) is Type target))
                    {
                        continue;
                    }

                    _drawerTargets.Add(target);
                    if ((bool)useForChildrenField.GetValue(attributes[i]))
                    {
                        _inheritedDrawerTargets.Add(target);
                    }
                }
            }
        }
    }
}
