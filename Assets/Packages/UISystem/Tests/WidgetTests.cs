using NUnit.Framework;
using UnityEngine;

namespace UnityEssentials.UI.Tests
{
    /// <summary>
    /// The spawned element kind: what GetWidget creates, parents, binds and shows; what
    /// ReturnWidget tears down; the ownership errors; and the guarantees that widgets enter no
    /// service list and are torn down with their window wherever they were parented.
    /// </summary>
    [TestFixture]
    public class WidgetTests : UITestFixture
    {
        private WindowService CreateWidgetService(params GameObject[] widgetPrefabs)
        {
            return CreateLoadedService(new GameObject[0], BuildWidgets(widgetPrefabs));
        }

        // ---- Spawning --------------------------------------------------------

        [Test]
        public void GetWidget_ClonesThePrototypeRatherThanReturningIt()
        {
            var prefab = BuildWidgetPrefab<TestWidget>();
            var prototype = prefab.GetComponent<TestWidget>();
            var service = CreateWidgetService(prefab);

            var widget = service.GetWidget<TestWidget>();

            Assert.AreNotSame(prototype, widget);
            Assert.AreEqual(0, prototype.ShowCount, "The prototype must never be shown.");
        }

        [Test]
        public void GetWidget_WithNoParent_ParentsUnderTheWindow()
        {
            var service = CreateWidgetService(BuildWidgetPrefab<TestWidget>());

            var widget = service.GetWidget<TestWidget>();

            Assert.AreEqual(
                service.ActiveWindow.gameObject,
                widget.transform.parent.gameObject);
        }

        [Test]
        public void GetWidget_WithAnExplicitParent_HonoursIt()
        {
            var service = CreateWidgetService(BuildWidgetPrefab<TestWidget>());
            var externalParent = BuildEmptyPrefab("ExternalWidgetParent");

            var widget = service.GetWidget<TestWidget>(externalParent.transform);

            Assert.AreEqual(externalParent, widget.transform.parent.gameObject);
        }

        [Test]
        public void GetWidget_BindsTheWidgetToTheWindow()
        {
            var service = CreateWidgetService(BuildWidgetPrefab<TestWidget>());

            var widget = service.GetWidget<TestWidget>();

            Assert.AreEqual(service.ActiveWindow, widget.Window);
        }

        [Test]
        public void GetWidget_ShowsTheWidget()
        {
            var service = CreateWidgetService(BuildWidgetPrefab<TestWidget>());

            var widget = service.GetWidget<TestWidget>();

            Assert.AreEqual(UIElementState.Shown, widget.State);
        }

        [Test]
        public void GetWidget_ForwardsTheDataToOnShow()
        {
            var service = CreateWidgetService(BuildWidgetPrefab<TestWidget>());
            var data = new TestUIData("spawn", 42);

            var widget = service.GetWidget<TestWidget>(null, data);

            Assert.AreSame(data, widget.LastData);
        }

        [Test]
        public void GetWidget_UnregisteredType_ThrowsUIElementNotFound()
        {
            var service = CreateWidgetService(BuildWidgetPrefab<TestWidget>());

            var exception = Assert.Throws<UIElementNotFoundException>(
                () => service.GetWidget<UnregisteredWidget>());
            StringAssert.Contains("UnregisteredWidget", exception.Message);
        }

        // ---- Returning -------------------------------------------------------

        // Asserted through the hide log: by assertion time the widget is destroyed, and the log
        // records the call at the moment it happened.
        [Test]
        public void ReturnWidget_RunsOnHideBeforeDestroying()
        {
            var service = CreateWidgetService(BuildWidgetPrefab<CountingWidget>());
            var widget = service.GetWidget<CountingWidget>();

            service.ReturnWidget(widget);

            Assert.AreEqual(1, HideLog.Count, UICallLog.Describe());
            Assert.AreSame(widget, HideLog[0], UICallLog.Describe());
        }

        [Test]
        public void ReturnWidget_DestroysTheWidget()
        {
            var service = CreateWidgetService(BuildWidgetPrefab<CountingWidget>());
            var widget = service.GetWidget<CountingWidget>();

            service.ReturnWidget(widget);

            Assert.IsTrue(widget == null, "The returned widget was not destroyed.");
        }

        [Test]
        public void ReturnWidget_UntracksTheWidget()
        {
            var service = CreateWidgetService(BuildWidgetPrefab<CountingWidget>());
            var widget = service.GetWidget<CountingWidget>();

            service.ReturnWidget(widget);

            Assert.AreEqual(0, service.ActiveWindow.SpawnedWidgets.Count);
        }

        [Test]
        public void ReturnWidget_ForeignWidget_ThrowsUIBindingException()
        {
            var service = CreateWidgetService(BuildWidgetPrefab<CountingWidget>());
            var foreign = BuildWidget<CountingWidget>();

            Assert.Throws<UIBindingException>(() => service.ReturnWidget(foreign));
        }

        // ---- Invisible to navigation -----------------------------------------

        [Test]
        public void GetWidget_EntersNoServiceList()
        {
            var service = CreateLoadedService(
                new[] { BuildUIPrefab<TestScreenA>() },
                BuildWidgets(BuildWidgetPrefab<TestWidget>()));
            var screen = service.ShowUI<TestScreenA>();

            service.GetWidget<TestWidget>();

            Assert.AreEqual(0, service.ActivePopups.Count);
            Assert.AreEqual(0, service.ActivePanels.Count);
            Assert.AreSame(screen, service.ActiveScreen);
        }

        [Test]
        public void GetWidget_DoesNotAffectBackNavigation()
        {
            var service = CreateLoadedService(
                new[] { BuildUIPrefab<TestScreenA>() },
                BuildWidgets(BuildWidgetPrefab<TestWidget>()));
            service.ShowUI<TestScreenA>();

            service.GetWidget<TestWidget>();

            Assert.IsFalse(service.Back(), UICallLog.Describe());
        }

        // ---- Window teardown -------------------------------------------------

        // Ownership is by tracking, not hierarchy: an externally parented widget is not a child of
        // the window, so it must be torn down explicitly on unload.
        [Test]
        public void SwitchWindow_TearsDownExternallyParentedWidgets()
        {
            var service = CreateWidgetService(BuildWidgetPrefab<TestWidget>());
            var externalParent = BuildEmptyPrefab("ExternalWidgetParent");
            var widget = service.GetWidget<TestWidget>(externalParent.transform);

            service.SwitchWindow(BuildWindowData(new GameObject[0]));

            Assert.IsTrue(widget == null, "A widget parented outside the window outlived its window.");
        }
    }
}
