using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UnityEssentials.UI.Tests
{
    /// <summary>
    /// The two-tier validation of a <see cref="WindowData"/>: the dispatch-time
    /// <c>ThrowIfInvalid</c> pass that rejects every malformed list shape naming the list and index,
    /// and the inspector-time <c>OnValidate</c> pass that must never throw on the same shapes.
    /// </summary>
    [TestFixture]
    public class WindowDataValidationTests : UITestFixture
    {
        // ---- UI prefab list ------------------------------------------------------

        [Test]
        public void ThrowIfInvalid_NullEntryInUIPrefabs_NamesTheListAndIndex()
        {
            var data = BuildWindowData(new[] { BuildUIPrefab<TestScreenA>(), null });

            var error = Assert.Throws<WindowConfigurationException>(() => data.ThrowIfInvalid());

            StringAssert.Contains("UI Prefabs", error.Message);
            StringAssert.Contains("index 1", error.Message);
        }

        [Test]
        public void ThrowIfInvalid_PrefabWithNoUIComponent_NamesTheListIndexAndPrefab()
        {
            var data = BuildWindowData(BuildEmptyPrefab("NotAUIElement"));

            var error = Assert.Throws<WindowConfigurationException>(() => data.ThrowIfInvalid());

            StringAssert.Contains("UI Prefabs", error.Message);
            StringAssert.Contains("index 0", error.Message);
            StringAssert.Contains("NotAUIElement", error.Message);
        }

        [Test]
        public void ThrowIfInvalid_DuplicateTypeInUIPrefabs_NamesBothIndices()
        {
            var data = BuildWindowData(
                BuildUIPrefab<TestScreenA>(),
                BuildUIPrefab<TestPopupA>(),
                BuildUIPrefab<TestScreenA>());

            var error = Assert.Throws<WindowConfigurationException>(() => data.ThrowIfInvalid());

            StringAssert.Contains("TestScreenA", error.Message);
            StringAssert.Contains("indices 0 and 2", error.Message);
        }

        [Test]
        public void ThrowIfInvalid_WidgetPrefabInUIPrefabs_PointsAtTheWidgetsList()
        {
            var data = BuildWindowData(BuildWidgetPrefab<TestWidget>());

            var error = Assert.Throws<WindowConfigurationException>(() => data.ThrowIfInvalid());

            StringAssert.Contains("UI Prefabs", error.Message);
            StringAssert.Contains("index 0", error.Message);
            StringAssert.Contains("Widgets", error.Message);
        }

        // ---- Widget list -----------------------------------------------------------

        [Test]
        public void ThrowIfInvalid_NullEntryInWidgets_NamesTheListAndIndex()
        {
            var data = BuildWindowData(new GameObject[0], new WidgetData[] { null });

            var error = Assert.Throws<WindowConfigurationException>(() => data.ThrowIfInvalid());

            StringAssert.Contains("Widgets", error.Message);
            StringAssert.Contains("index 0", error.Message);
        }

        [Test]
        public void ThrowIfInvalid_WidgetEntryWithNoPrefab_NamesTheListAndIndex()
        {
            var data = BuildWindowData(new GameObject[0], BuildWidgets(new GameObject[] { null }));

            var error = Assert.Throws<WindowConfigurationException>(() => data.ThrowIfInvalid());

            StringAssert.Contains("Widgets", error.Message);
            StringAssert.Contains("index 0", error.Message);
        }

        [Test]
        public void ThrowIfInvalid_WidgetPrefabWithNoUIComponent_NamesTheListIndexAndPrefab()
        {
            var data = BuildWindowData(new GameObject[0], BuildWidgets(BuildEmptyPrefab("BareWidgetPrefab")));

            var error = Assert.Throws<WindowConfigurationException>(() => data.ThrowIfInvalid());

            StringAssert.Contains("Widgets", error.Message);
            StringAssert.Contains("index 0", error.Message);
            StringAssert.Contains("BareWidgetPrefab", error.Message);
        }

        [Test]
        public void ThrowIfInvalid_ScreenPrefabInWidgets_PointsAtTheUIPrefabsList()
        {
            var data = BuildWindowData(new GameObject[0], BuildWidgets(BuildUIPrefab<TestScreenA>()));

            var error = Assert.Throws<WindowConfigurationException>(() => data.ThrowIfInvalid());

            StringAssert.Contains("Widgets", error.Message);
            StringAssert.Contains("index 0", error.Message);
            StringAssert.Contains("UI Prefabs", error.Message);
        }

        [Test]
        public void ThrowIfInvalid_DuplicateTypeInWidgets_NamesBothIndices()
        {
            var data = BuildWindowData(
                new GameObject[0],
                BuildWidgets(
                    BuildWidgetPrefab<TestWidget>(),
                    BuildWidgetPrefab<TestWidgetB>(),
                    BuildWidgetPrefab<TestWidget>()));

            var error = Assert.Throws<WindowConfigurationException>(() => data.ThrowIfInvalid());

            StringAssert.Contains("TestWidget", error.Message);
            StringAssert.Contains("indices 0 and 2", error.Message);
        }

        // ---- Ordering and the well-formed case -----------------------------------------

        [Test]
        public void ThrowIfInvalid_WithFaultsInBothLists_ReportsTheUIPrefabsListFirst()
        {
            var data = BuildWindowData(new GameObject[] { null }, new WidgetData[] { null });

            var error = Assert.Throws<WindowConfigurationException>(() => data.ThrowIfInvalid());

            StringAssert.Contains("UI Prefabs", error.Message);
        }

        [Test]
        public void ThrowIfInvalid_WithWellFormedLists_DoesNotThrow()
        {
            var data = BuildWindowData(
                new[]
                {
                    BuildUIPrefab<TestScreenA>(),
                    BuildUIPrefab<TestPopupA>(),
                    BuildUIPrefab<TestPanelA>()
                },
                BuildWidgets(BuildWidgetPrefab<TestWidget>()));

            Assert.DoesNotThrow(() => data.ThrowIfInvalid());
        }

        // ---- OnValidate never throws -----------------------------------------------------

        [Test]
        public void OnValidate_WithEveryMalformedListShape_DoesNotThrow()
        {
            var data = BuildWindowData(
                new[]
                {
                    null,
                    BuildEmptyPrefab("NoComponent"),
                    BuildWidgetPrefab<TestWidget>(),
                    BuildUIPrefab<TestScreenA>(),
                    BuildUIPrefab<TestScreenA>()
                },
                new[]
                {
                    null,
                    new WidgetData(null),
                    new WidgetData(BuildUIPrefab<TestScreenB>())
                });

            Assert.DoesNotThrow(() => InvokeOnValidate(data));
        }

        [Test]
        public void OnValidate_OnAnEmptyDefinition_DoesNotThrow()
        {
            var data = BuildWindowData(new GameObject[0]);

            Assert.DoesNotThrow(() => InvokeOnValidate(data));
        }

        // Staying silent must not mean quietly repairing the lists.
        [Test]
        public void OnValidate_DoesNotRepairMalformedLists()
        {
            var data = BuildWindowData(new GameObject[] { null });

            InvokeOnValidate(data);

            Assert.Throws<WindowConfigurationException>(() => data.ThrowIfInvalid());
        }

        // ---- Helpers ----------------------------------------------------------------------

        // Reflection because Unity calls OnValidate by name and it is private; the wrapper
        // exception is unwrapped so a failure reports the actual fault.
        private static void InvokeOnValidate(WindowData data)
        {
            var method = typeof(WindowData).GetMethod(
                "OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(
                method,
                "WindowData has no private OnValidate method. Unity calls that hook by name, so renaming " +
                "or removing it silently disables inspector-time clamping instead of failing to compile.");

            try
            {
                method.Invoke(data, null);
            }
            catch (TargetInvocationException invocation) when (invocation.InnerException != null)
            {
                throw invocation.InnerException;
            }
        }
    }
}
