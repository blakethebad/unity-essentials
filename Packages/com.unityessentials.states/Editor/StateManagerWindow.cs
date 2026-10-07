using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEssentials.States;

namespace UnityEssentials.States.Editor
{
    public sealed class StateManagerWindow : EditorWindow
    {
        private const long TickIntervalMs = 100;
        private static readonly Vector2 MinimumSize = new Vector2(320f, 200f);

        // Reused across ticks so polling allocates nothing.
        private readonly List<StateMachineDebugEntry> _entries = new List<StateMachineDebugEntry>();
        private readonly Dictionary<int, StateManagerCard> _cardsById = new Dictionary<int, StateManagerCard>();
        private readonly Dictionary<int, StateManagerCard> _rebuildScratch = new Dictionary<int, StateManagerCard>();

        private int _lastVersion = -1;

        private Label _countLabel;
        private ScrollView _scrollView;
        private Label _emptyLabel;

        [MenuItem("Essentials/StateManager")]
        public static void Open()
        {
            StateManagerWindow window = GetWindow<StateManagerWindow>("State Managers");
            window.minSize = MinimumSize;
        }

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

            // Added above the ScrollView, not inside it: the scroll view's children are cleared on
            // every rebuild, and a hint below a flex-grown scroll view would be pinned to the bottom.
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

            // Whatever is left in the old map belonged to entries that are gone; dropping the map
            // drops those cards with it, and their elements are already detached by the Clear above.
            _cardsById.Clear();
            foreach (KeyValuePair<int, StateManagerCard> pair in _rebuildScratch)
            {
                _cardsById[pair.Key] = pair.Value;
            }

            _rebuildScratch.Clear();
        }

        private void UpdateHeader()
        {
            int count = _entries.Count;
            _countLabel.text = count == 1 ? "1 active machine" : count + " active machines";

            if (count > 0)
            {
                _emptyLabel.style.display = DisplayStyle.None;
                return;
            }

            // The two empty states have different causes: outside play mode nothing is running, while
            // inside it the machines exist but have not initialized.
            _emptyLabel.style.display = DisplayStyle.Flex;
            _emptyLabel.text = EditorApplication.isPlaying
                ? "No StateManager has been initialized yet."
                : "Enter Play Mode — active StateManagers appear here automatically.";
        }

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
