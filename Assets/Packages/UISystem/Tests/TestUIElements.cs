using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEssentials.UI.Tests
{
    /// <summary>
    /// A recognisable payload: a label and a number, so assertions can prove the package routed the
    /// exact instance rather than an equal-looking one.
    /// </summary>
    public sealed class TestUIData : IUIData
    {
        public TestUIData()
        {
        }

        public TestUIData(string label, int value = 0)
        {
            Label = label;
            Value = value;
        }

        public string Label { get; }

        public int Value { get; }

        public override string ToString()
        {
            return $"TestUIData('{Label}', {Value})";
        }
    }

    /// <summary>
    /// The ordered sink every recording double writes to, so fixtures can assert the exact
    /// OnShow/OnHide sequence an operation produced. Static because the doubles are instantiated by
    /// the window under test; the fixture clears it in SetUp and TearDown.
    /// </summary>
    public static class UICallLog
    {
        // MonoBehaviour rather than UIBase: widgets live in their own hierarchy rooted at UIWidget,
        // and the log records both.
        public static readonly List<MonoBehaviour> Entries = new List<MonoBehaviour>();
        public static readonly List<MonoBehaviour> Shows = new List<MonoBehaviour>();
        public static readonly List<MonoBehaviour> Hides = new List<MonoBehaviour>();
        public static readonly List<string> Markers = new List<string>();

        public static void Clear()
        {
            Entries.Clear();
            Shows.Clear();
            Hides.Clear();
            Markers.Clear();
        }

        public static void RecordShow(MonoBehaviour element)
        {
            if (element == null)
            {
                return;
            }

            Entries.Add(element);
            Shows.Add(element);
            Markers.Add($"Show:{element.GetType().Name}");
        }

        public static void RecordHide(MonoBehaviour element)
        {
            if (element == null)
            {
                return;
            }

            Entries.Add(element);
            Hides.Add(element);
            Markers.Add($"Hide:{element.GetType().Name}");
        }

        public static string Describe()
        {
            return Markers.Count == 0 ? "(empty)" : string.Join(", ", Markers.ToArray());
        }
    }

    /// <summary>
    /// What every recording double exposes regardless of which element base it derives from: hook
    /// counts and the last payload. The counting bodies repeat once per base — the four bases are
    /// separate roots, so there is nothing to inherit them from.
    /// </summary>
    public interface ICountingUI
    {
        int ShowCount { get; }

        int HideCount { get; }

        IUIData LastData { get; }

        void ResetCounts();
    }

    /// <summary>Base for every screen double: counts its hooks and writes them to <see cref="UICallLog"/>.</summary>
    public abstract class RecordingScreenBase : ScreenBase, ICountingUI
    {
        public int ShowCount { get; private set; }

        public int HideCount { get; private set; }

        public IUIData LastData { get; private set; }

        public void ResetCounts()
        {
            ShowCount = 0;
            HideCount = 0;
            LastData = null;
        }

        protected override void OnShow(IUIData uiData)
        {
            ShowCount++;
            LastData = uiData;
            UICallLog.RecordShow(this);
        }

        protected override void OnHide()
        {
            HideCount++;
            UICallLog.RecordHide(this);
        }
    }

    public sealed class TestScreenA : RecordingScreenBase
    {
    }

    public sealed class TestScreenB : RecordingScreenBase
    {
    }

    public sealed class TestScreenC : RecordingScreenBase
    {
    }

    public sealed class CountingScreen : RecordingScreenBase
    {
    }

    /// <summary>A screen type deliberately never listed in any WindowData the suite builds.</summary>
    public sealed class UnregisteredScreen : RecordingScreenBase
    {
    }

    /// <summary>
    /// A screen whose transitions never complete on their own: each captures its callback instead
    /// of invoking it, so a test can hold the element in Showing/Hiding and finish by hand.
    /// </summary>
    public sealed class DeferredTransitionScreen : RecordingScreenBase
    {
        public bool DeferShow = true;
        public bool DeferHide = true;
        public Action PendingShowComplete;
        public Action PendingHideComplete;

        public bool InvokePendingShow()
        {
            if (PendingShowComplete == null)
            {
                return false;
            }

            PendingShowComplete();
            return true;
        }

        public bool InvokePendingHide()
        {
            if (PendingHideComplete == null)
            {
                return false;
            }

            PendingHideComplete();
            return true;
        }

        public void ClearPending()
        {
            PendingShowComplete = null;
            PendingHideComplete = null;
        }

        protected override void OnShowTransition(Action complete)
        {
            PendingShowComplete = complete;

            if (!DeferShow)
            {
                complete();
            }
        }

        protected override void OnHideTransition(Action complete)
        {
            PendingHideComplete = complete;

            if (!DeferHide)
            {
                complete();
            }
        }
    }

    /// <summary>The well-formed auto-panel case: declares TestPanelA then TestPanelB and hands each a recognisable payload.</summary>
    public sealed class AutoPanelScreen : RecordingScreenBase
    {
        public const string AutoPanelDataPrefix = "auto:";

        private static readonly Type[] Panels = { typeof(TestPanelA), typeof(TestPanelB) };

        public static string ExpectedDataLabel(Type panelType)
        {
            return AutoPanelDataPrefix + panelType.Name;
        }

        protected override Type[] AutoPanels => Panels;

        protected override IUIData GetAutoPanelData(Type panelType)
        {
            return new TestUIData(ExpectedDataLabel(panelType));
        }
    }

    /// <summary>Declares a popup type among its auto-panels — an illegal declaration no window can satisfy.</summary>
    public sealed class BadAutoPanelScreen : RecordingScreenBase
    {
        private static readonly Type[] Panels = { typeof(TestPopupA) };

        protected override Type[] AutoPanels => Panels;
    }

    /// <summary>Declares a valid panel type that no fixture ever lists in a WindowData.</summary>
    public sealed class MissingAutoPanelScreen : RecordingScreenBase
    {
        private static readonly Type[] Panels = { typeof(UnregisteredPanel) };

        protected override Type[] AutoPanels => Panels;
    }

    /// <summary>Declares an array containing a null entry — the shape a stale element leaves behind.</summary>
    public sealed class NullAutoPanelEntryScreen : RecordingScreenBase
    {
        private static readonly Type[] Panels = { null };

        protected override Type[] AutoPanels => Panels;
    }

    /// <summary>Returns null from AutoPanels, which the package must treat as "no panels".</summary>
    public sealed class NullAutoPanelArrayScreen : RecordingScreenBase
    {
        protected override Type[] AutoPanels => null;
    }

    /// <summary>Base for every popup double: counts its hooks and writes them to <see cref="UICallLog"/>.</summary>
    public abstract class RecordingPopupBase : PopupBase, ICountingUI
    {
        public int ShowCount { get; private set; }

        public int HideCount { get; private set; }

        public IUIData LastData { get; private set; }

        public void ResetCounts()
        {
            ShowCount = 0;
            HideCount = 0;
            LastData = null;
        }

        protected override void OnShow(IUIData uiData)
        {
            ShowCount++;
            LastData = uiData;
            UICallLog.RecordShow(this);
        }

        protected override void OnHide()
        {
            HideCount++;
            UICallLog.RecordHide(this);
        }
    }

    public sealed class TestPopupA : RecordingPopupBase
    {
    }

    public sealed class TestPopupB : RecordingPopupBase
    {
    }

    public sealed class CountingPopup : RecordingPopupBase
    {
    }

    /// <summary>A popup type deliberately never listed in any WindowData.</summary>
    public sealed class UnregisteredPopup : RecordingPopupBase
    {
    }

    /// <summary>Base for every panel double: counts its hooks and writes them to <see cref="UICallLog"/>.</summary>
    public abstract class RecordingPanelBase : PanelBase, ICountingUI
    {
        public int ShowCount { get; private set; }

        public int HideCount { get; private set; }

        public IUIData LastData { get; private set; }

        public void ResetCounts()
        {
            ShowCount = 0;
            HideCount = 0;
            LastData = null;
        }

        protected override void OnShow(IUIData uiData)
        {
            ShowCount++;
            LastData = uiData;
            UICallLog.RecordShow(this);
        }

        protected override void OnHide()
        {
            HideCount++;
            UICallLog.RecordHide(this);
        }
    }

    public sealed class TestPanelA : RecordingPanelBase
    {
    }

    public sealed class TestPanelB : RecordingPanelBase
    {
    }

    public sealed class CountingPanel : RecordingPanelBase
    {
    }

    /// <summary>A panel type deliberately never listed in any WindowData.</summary>
    public sealed class UnregisteredPanel : RecordingPanelBase
    {
    }

    /// <summary>Base for every widget double: counts its hooks and writes them to <see cref="UICallLog"/>.</summary>
    public abstract class RecordingWidgetBase : WidgetBase, ICountingUI
    {
        public int ShowCount { get; private set; }

        public int HideCount { get; private set; }

        public IUIData LastData { get; private set; }

        public void ResetCounts()
        {
            ShowCount = 0;
            HideCount = 0;
            LastData = null;
        }

        protected override void OnShow(IUIData uiData)
        {
            ShowCount++;
            LastData = uiData;
            UICallLog.RecordShow(this);
        }

        protected override void OnHide()
        {
            HideCount++;
            UICallLog.RecordHide(this);
        }
    }

    public sealed class TestWidget : RecordingWidgetBase
    {
    }

    public sealed class TestWidgetB : RecordingWidgetBase
    {
    }

    public sealed class CountingWidget : RecordingWidgetBase
    {
    }

    /// <summary>A widget type deliberately never listed in any WindowData.Widgets.</summary>
    public sealed class UnregisteredWidget : RecordingWidgetBase
    {
    }

    /// <summary>
    /// The unbound counterpart of <see cref="DeferredTransitionScreen"/>: the same captured-callback
    /// behaviour on an element that needs no window at all, for driving the state machine in isolation.
    /// </summary>
    public sealed class DeferredTransitionWidget : RecordingWidgetBase
    {
        public bool DeferShow = true;
        public bool DeferHide = true;
        public Action PendingShowComplete;
        public Action PendingHideComplete;

        public bool InvokePendingShow()
        {
            if (PendingShowComplete == null)
            {
                return false;
            }

            PendingShowComplete();
            return true;
        }

        public bool InvokePendingHide()
        {
            if (PendingHideComplete == null)
            {
                return false;
            }

            PendingHideComplete();
            return true;
        }

        public void ClearPending()
        {
            PendingShowComplete = null;
            PendingHideComplete = null;
        }

        protected override void OnShowTransition(Action complete)
        {
            PendingShowComplete = complete;

            if (!DeferShow)
            {
                complete();
            }
        }

        protected override void OnHideTransition(Action complete)
        {
            PendingHideComplete = complete;

            if (!DeferHide)
            {
                complete();
            }
        }
    }
}
