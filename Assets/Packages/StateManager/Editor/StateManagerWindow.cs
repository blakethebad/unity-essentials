using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEssentials.States;

namespace UnityEssentials.States.Editor
{
    /// <summary>
    /// A live dashboard of every <see cref="StateManager{TState}"/> currently registered: one card per
    /// machine showing its current state, how long it has been there, where it came from, which
    /// transitions are legal from here, and a short history. Opened from <c>Essentials/StateManager</c>.
    /// </summary>
    /// <remarks>
    /// Machines register themselves when they initialize, so there is nothing to wire up: open the
    /// window, enter play mode, and every machine appears. Nothing here is a setting, a filter or a
    /// mode — the window is deliberately a read-only view, because a debug tool that can alter the
    /// thing it observes stops being trustworthy evidence about it.
    /// <para>
    /// The window itself holds no logic worth testing. Every number it prints comes from the registry
    /// or from a card; its whole job is to poll, diff, and hand entries to cards. That split is
    /// intentional — the registry is covered by EditMode tests, and an untested class that only wires
    /// things together cannot hide a bug that matters.
    /// </para>
    /// <para>
    /// Timings are wall clock (<c>Time.realtimeSinceStartupAsDouble</c>), so a paused editor keeps
    /// counting the time in the current state. That is the honest reading — the machine really has been
    /// in that state that long — but it does mean the elapsed number is not "game time" and will run
    /// away while you sit on a breakpoint.
    /// </para>
    /// </remarks>
    public sealed class StateManagerWindow : EditorWindow
    {
        // ---- Constants ----------------------------------------------------

        /// <summary>
        /// Polling period in milliseconds. Ten refreshes a second is fast enough that the elapsed
        /// readout looks continuous at one decimal place, and slow enough that a window left open in
        /// the background costs nothing measurable.
        /// </summary>
        private const long TickIntervalMs = 100;

        /// <summary>Minimum window size, chosen so a card's state strip has room before it wraps.</summary>
        private static readonly Vector2 MinimumSize = new Vector2(320f, 200f);

        // ---- State --------------------------------------------------------

        /// <summary>
        /// Reused across ticks so polling allocates nothing. The registry fills it rather than
        /// returning a fresh list for exactly this reason.
        /// </summary>
        private readonly List<StateMachineDebugEntry> _entries = new List<StateMachineDebugEntry>();

        /// <summary>
        /// Cards by entry id, so a rebuild reuses the card for a machine that was already on screen
        /// instead of rebuilding its element tree.
        /// </summary>
        private readonly Dictionary<int, StateManagerCard> _cardsById = new Dictionary<int, StateManagerCard>();

        /// <summary>Scratch map used while rebuilding, so the swap needs no second dictionary allocation.</summary>
        private readonly Dictionary<int, StateManagerCard> _rebuildScratch = new Dictionary<int, StateManagerCard>();

        /// <summary>
        /// The registry version the card list was last built from. Starts at a value the registry can
        /// never hold so the first tick always builds.
        /// </summary>
        private int _lastVersion = -1;

        /// <summary>The count readout in the toolbar.</summary>
        private Label _countLabel;

        /// <summary>Scrolling host for the cards.</summary>
        private ScrollView _scrollView;

        /// <summary>The hint shown while there is nothing to display.</summary>
        private Label _emptyLabel;

        // ---- Menu ---------------------------------------------------------

        /// <summary>
        /// Opens (or focuses) the window.
        /// </summary>
        /// <remarks>
        /// Lives under <c>Essentials/</c> with the rest of this library's tooling rather than under
        /// <c>Window/</c>, so everything the package adds to the menu bar is in one place.
        /// </remarks>
        [MenuItem("Essentials/StateManager")]
        public static void Open()
        {
            StateManagerWindow window = GetWindow<StateManagerWindow>("State Managers");
            window.minSize = MinimumSize;
        }

        // ---- Construction -------------------------------------------------

        /// <summary>
        /// Builds the chrome once and starts the poll loop.
        /// </summary>
        /// <remarks>
        /// <c>CreateGUI</c> rather than <c>OnEnable</c>: the root visual element is guaranteed to exist
        /// here, and the schedule attached to it is torn down with the window automatically, so there
        /// is no unsubscribe to forget.
        /// </remarks>
        private void CreateGUI()
        {
            Toolbar toolbar = new Toolbar();
            rootVisualElement.Add(toolbar);

            _countLabel = new Label();
            _countLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            _countLabel.style.marginLeft = 4f;
            _countLabel.style.flexGrow = 1f;
            toolbar.Add(_countLabel);

            ToolbarButton clearButton = new ToolbarButton(ClearAllHistory);
            clearButton.text = "Clear History";
            clearButton.tooltip = "Empty every machine's transition history. Does not touch the machines themselves.";
            toolbar.Add(clearButton);

            // Added above the ScrollView, not inside it: the scroll view's children are cleared on every
            // rebuild, and a hint that lives below a flex-grown scroll view would be pinned to the
            // bottom of an otherwise empty window.
            _emptyLabel = new Label();
            _emptyLabel.style.whiteSpace = WhiteSpace.Normal;
            _emptyLabel.style.marginTop = 8f;
            _emptyLabel.style.marginLeft = 8f;
            _emptyLabel.style.marginRight = 8f;
            _emptyLabel.style.opacity = 0.7f;
            rootVisualElement.Add(_emptyLabel);

            _scrollView = new ScrollView(ScrollViewMode.Vertical);
            _scrollView.style.flexGrow = 1f;
            _scrollView.style.paddingLeft = 6f;
            _scrollView.style.paddingRight = 6f;
            _scrollView.style.paddingTop = 6f;
            rootVisualElement.Add(_scrollView);

            rootVisualElement.schedule.Execute(Tick).Every(TickIntervalMs);
        }

        // ---- Poll ---------------------------------------------------------

        /// <summary>
        /// One poll: collect the live entries, rebuild the card list if the registry changed, and
        /// refresh what is on screen.
        /// </summary>
        /// <remarks>
        /// The version check is what keeps this cheap. Registration, unregistration and pruning all bump
        /// the registry's version, so an unchanged version means the same machines in the same order and
        /// the existing cards can simply be refreshed. Comparing the collected list against the card map
        /// every tick would work too, but it would do that work ten times a second to discover nothing
        /// happened, which is the common case.
        /// <para>
        /// The version is read after collection, not before: <c>CollectAlive</c> prunes dead entries and
        /// bumps the version itself, so reading afterwards folds that prune into the same tick instead
        /// of leaving a stale card on screen for another 100 ms.
        /// </para>
        /// </remarks>
        private void Tick()
        {
            StateMachineDebugRegistry.CollectAlive(_entries);

            int version = StateMachineDebugRegistry.Version;
            if (version != _lastVersion)
            {
                _lastVersion = version;
                RebuildCards();
            }

            double now = Time.realtimeSinceStartupAsDouble;
            for (int i = 0; i < _entries.Count; i++)
            {
                StateManagerCard card;
                if (_cardsById.TryGetValue(_entries[i].Id, out card))
                {
                    card.Refresh(now);
                }
            }

            UpdateHeader();
        }

        /// <summary>
        /// Re-seats the card list to match the collected entries, reusing cards by entry id.
        /// </summary>
        /// <remarks>
        /// Cards are keyed by <see cref="StateMachineDebugEntry.Id"/> rather than by the entry object so
        /// the map is a value lookup, and reused rather than rebuilt so that a machine appearing or
        /// disappearing does not reset the scroll position or discard the element trees of every other
        /// card on screen.
        /// </remarks>
        private void RebuildCards()
        {
            _scrollView.Clear();
            _rebuildScratch.Clear();

            for (int i = 0; i < _entries.Count; i++)
            {
                StateMachineDebugEntry entry = _entries[i];

                StateManagerCard card;
                if (!_cardsById.TryGetValue(entry.Id, out card))
                {
                    card = new StateManagerCard(entry);
                }

                _rebuildScratch[entry.Id] = card;
                _scrollView.Add(card);
            }

            // Whatever is left in the old map belonged to entries that are gone; dropping the map drops
            // those cards with it, and their elements are already detached by the Clear above.
            _cardsById.Clear();
            foreach (KeyValuePair<int, StateManagerCard> pair in _rebuildScratch)
            {
                _cardsById[pair.Key] = pair.Value;
            }

            _rebuildScratch.Clear();
        }

        /// <summary>
        /// Updates the toolbar count and the empty-state hint.
        /// </summary>
        /// <remarks>
        /// The hint differs by play mode because the two empty states have completely different causes:
        /// outside play mode there is simply nothing running, while inside it an empty list means the
        /// machines exist but have not initialized — usually a deferred <c>Start</c> or an
        /// <c>Initialize()</c> that never got called, since a machine configures itself from there.
        /// Entries created by EditMode tests show up normally; the registry does not care whether the
        /// editor is playing.
        /// </remarks>
        private void UpdateHeader()
        {
            int count = _entries.Count;
            _countLabel.text = count == 1 ? "1 active machine" : count + " active machines";

            if (count > 0)
            {
                _emptyLabel.style.display = DisplayStyle.None;
                return;
            }

            _emptyLabel.style.display = DisplayStyle.Flex;
            _emptyLabel.text = EditorApplication.isPlaying
                ? "No StateManager has been initialized yet."
                : "Enter Play Mode — active StateManagers appear here automatically.";
        }

        // ---- Commands -----------------------------------------------------

        /// <summary>
        /// Empties every live entry's transition history.
        /// </summary>
        /// <remarks>
        /// Useful right before reproducing something: clear, perform the action, and the history holds
        /// only what the action caused. It touches the recorded history and nothing else — no machine is
        /// stopped, reset or otherwise disturbed.
        /// </remarks>
        private void ClearAllHistory()
        {
            StateMachineDebugRegistry.CollectAlive(_entries);
            for (int i = 0; i < _entries.Count; i++)
            {
                _entries[i].ClearHistory();
            }

            Tick();
        }
    }
}
