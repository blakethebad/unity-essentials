using System;
using System.Collections.Generic;

namespace UnityEssentials.UI.Tests
{
    /// <summary>
    /// A recognisable payload: a label and a number, so assertions can prove the package routed the
    /// exact <see cref="IUIData"/> instance through to <c>OnShow</c> rather than an equal-looking one.
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
    /// OnShow/OnHide sequence an operation produced — window load and unload order above all.
    /// Static because the doubles are instantiated by the window under test, not by the test;
    /// <see cref="UITestFixture"/> clears it in both SetUp and TearDown.
    /// </summary>
    public static class UICallLog
    {
        public static readonly List<UIBase> Entries = new List<UIBase>();
        public static readonly List<UIBase> Shows = new List<UIBase>();
        public static readonly List<UIBase> Hides = new List<UIBase>();
        public static readonly List<string> Markers = new List<string>();

        public static void Clear()
        {
            Entries.Clear();
            Shows.Clear();
            Hides.Clear();
            Markers.Clear();
        }

        public static void RecordShow(UIBase element)
        {
            if (element == null)
            {
                return;
            }

            Entries.Add(element);
            Shows.Add(element);
            Markers.Add($"Show:{element.GetType().Name}");
        }

        public static void RecordHide(UIBase element)
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
    /// What every recording double exposes: hook counts and the last payload received. Assertions
    /// written against this interface keep working when kind-specific element bases return and the
    /// doubles are split across several roots again.
    /// </summary>
    public interface ICountingUI
    {
        int ShowCount { get; }

        int HideCount { get; }

        IUIData LastData { get; }

        void ResetCounts();
    }

    /// <summary>
    /// The single recording double base: counts <c>OnShow</c>/<c>OnHide</c>, remembers the payload,
    /// and appends every hook to <see cref="UICallLog"/> so cross-element ordering is observable.
    /// </summary>
    public abstract class RecordingUIBase : UIBase, ICountingUI
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

    /// <summary>Interchangeable element type, for tests that need several distinct types in one window.</summary>
    public sealed class TestElementA : RecordingUIBase
    {
    }

    /// <summary>Interchangeable element type, for tests that need several distinct types in one window.</summary>
    public sealed class TestElementB : RecordingUIBase
    {
    }

    /// <summary>Interchangeable element type, for tests that need several distinct types in one window.</summary>
    public sealed class TestElementC : RecordingUIBase
    {
    }

    /// <summary>Interchangeable element type, for tests that need several distinct types in one window.</summary>
    public sealed class TestElementD : RecordingUIBase
    {
    }

    /// <summary>Interchangeable element type, for tests that need several distinct types in one window.</summary>
    public sealed class TestElementE : RecordingUIBase
    {
    }

    /// <summary>
    /// The element to reach for when a test is about hook counts rather than about which type was
    /// resolved — named for what it is being used for at the call site.
    /// </summary>
    public sealed class CountingElement : RecordingUIBase
    {
    }

    /// <summary>
    /// An element type deliberately never listed in any <see cref="WindowData"/> the suite builds,
    /// so resolving it proves <see cref="UIElementNotFoundException"/> and <c>TryGetUI</c>'s false.
    /// </summary>
    public sealed class UnregisteredElement : RecordingUIBase
    {
    }

    /// <summary>
    /// An element whose transitions never complete on their own: each captures its completion
    /// callback instead of invoking it, so a test can park the element in <c>Showing</c> or
    /// <c>Hiding</c>, assert on that state, and finish the transition by hand.
    /// </summary>
    /// <remarks>
    /// Invoking a captured callback is also how the stale-token guard in <see cref="UIBase"/> is
    /// tested: hold one completion, start a second transition, then fire the held one and assert it
    /// did nothing. <see cref="ClearPending"/> exists so a test can tell "never captured" apart from
    /// "captured during an earlier step".
    /// </remarks>
    public sealed class DeferredTransitionElement : RecordingUIBase
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
