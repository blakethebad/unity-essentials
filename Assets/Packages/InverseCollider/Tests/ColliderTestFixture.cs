using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UnityEssentials.Colliders.Tests
{
    /// <summary>
    /// The base every Unity-object collider fixture derives from: it tracks what a test creates, tears
    /// each built component down before anything is destroyed, and carries the reflection helpers that
    /// reach the component's private serialized fields.
    /// </summary>
    public abstract class ColliderTestFixture
    {
        private readonly List<UnityEngine.Object> _trackedObjects = new List<UnityEngine.Object>();
        private readonly List<InverseCollider> _trackedComponents = new List<InverseCollider>();

        // Not named SetUp/TearDown: a derived fixture declaring its own would hide these and NUnit
        // would run only one of the two.
        [SetUp]
        public void ColliderTestFixtureSetUp()
        {
            // Defensive: a teardown that threw part way through would leave its remaining entries
            // behind, and the next test would then tear down objects it never created.
            _trackedComponents.Clear();
            _trackedObjects.Clear();
        }

        [TearDown]
        public void ColliderTestFixtureTearDown()
        {
            // Built components are torn down first: OnDestroy never runs in EditMode, so their
            // HideAndDontSave meshes would otherwise leak for the rest of the session. Objects then
            // go newest-first, so a child is always destroyed before the parent it was added to.
            try
            {
                for (var i = _trackedComponents.Count - 1; i >= 0; i--)
                {
                    var component = _trackedComponents[i];
                    if (component != null)
                    {
                        component.TearDownBuild();
                    }
                }
            }
            finally
            {
                _trackedComponents.Clear();

                for (var i = _trackedObjects.Count - 1; i >= 0; i--)
                {
                    var tracked = _trackedObjects[i];
                    if (tracked != null)
                    {
                        UnityEngine.Object.DestroyImmediate(tracked);
                    }
                }

                _trackedObjects.Clear();
            }
        }

        /// <summary>Registers a Unity object for destruction at the end of the test and hands it back.</summary>
        protected T Track<T>(T obj) where T : UnityEngine.Object
        {
            // Compared through a non-generic local so Unity's overloaded == applies.
            UnityEngine.Object candidate = obj;
            if (candidate != null)
            {
                _trackedObjects.Add(obj);
            }

            return obj;
        }

        /// <summary>Creates a tracked, still unbuilt <see cref="InverseCollider"/> on a GameObject of its own.</summary>
        protected InverseCollider CreateCollider(string name = "InverseCollider")
        {
            var host = Track(new GameObject(name));
            var component = host.AddComponent<InverseCollider>();
            _trackedComponents.Add(component);
            return component;
        }

        /// <summary>Writes a private instance field — the inspector state a test needs to author.</summary>
        protected static void SetField(object target, string fieldName, object value)
        {
            // Reflection rather than the JsonUtility overwrite the UI fixtures use: this component's
            // serialized set includes an enum and two UnityEngine.Object references, and a JSON round
            // trip covers neither uniformly. The name asked for here is the same string Unity
            // serializes, so a rename that would break authored scenes fails these tests too.
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new ArgumentException(
                    $"{target.GetType().Name} has no private instance field named '{fieldName}'.",
                    nameof(fieldName));
            }

            field.SetValue(target, value);
        }

        /// <summary>Reads a private instance field back, typed.</summary>
        protected static T GetField<T>(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new ArgumentException(
                    $"{target.GetType().Name} has no private instance field named '{fieldName}'.",
                    nameof(fieldName));
            }

            return (T)field.GetValue(target);
        }
    }
}
