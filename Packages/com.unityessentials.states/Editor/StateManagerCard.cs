using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEssentials.States;

namespace UnityEssentials.States.Editor
{
    internal sealed class StateManagerCard : VisualElement
    {
        private const float AccentBarWidth = 3f;
        private const float CardCornerRadius = 6f;
        private const float PillCornerRadius = 4f;
        private const float DotSize = 8f;

        private static Color CardBackground
        {
            get
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(0.22f, 0.22f, 0.24f, 1f)
                    : new Color(0.85f, 0.85f, 0.87f, 1f);
            }
        }

        private static Color DimText
        {
            get
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(0.62f, 0.62f, 0.66f, 1f)
                    : new Color(0.35f, 0.35f, 0.39f, 1f);
            }
        }

        private static Color PrimaryText
        {
            get
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(0.87f, 0.87f, 0.90f, 1f)
                    : new Color(0.12f, 0.12f, 0.15f, 1f);
            }
        }

        // Colorless on purpose: a machine with no current state must not look like it is in one.
        private static Color NeutralPill
        {
            get
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(0.32f, 0.32f, 0.35f, 1f)
                    : new Color(0.72f, 0.72f, 0.75f, 1f);
            }
        }

        private readonly StateMachineDebugEntry _entry;
        private readonly VisualElement _accentBar;
        private readonly Button _nameButton;
        private readonly Label _nameLabel;
        private readonly Label _typeLabel;
        private readonly Label _currentPill;
        private readonly Label _elapsedLabel;
        private readonly VisualElement _previousRow;
        private readonly VisualElement _previousDot;
        private readonly Label _previousLabel;
        private readonly VisualElement _stateStrip;
        private readonly List<Label> _stateChips = new List<Label>();
        private readonly VisualElement _historyContainer;
        private readonly List<HistoryRow> _historyRows = new List<HistoryRow>();
        private readonly Label _historyEmptyLabel;

        private string _cachedCurrentStateName;
        private bool _hasCachedCurrentStateName;

        internal StateManagerCard(StateMachineDebugEntry entry)
        {
            _entry = entry;

            style.flexDirection = FlexDirection.Row;
            style.marginBottom = 6f;
            style.backgroundColor = CardBackground;
            style.borderTopLeftRadius = CardCornerRadius;
            style.borderTopRightRadius = CardCornerRadius;
            style.borderBottomLeftRadius = CardCornerRadius;
            style.borderBottomRightRadius = CardCornerRadius;
            style.overflow = Overflow.Hidden;

            _accentBar = new VisualElement();
            _accentBar.style.width = AccentBarWidth;
            _accentBar.style.flexShrink = 0f;
            _accentBar.style.backgroundColor = StateDebugPalette.AccentColor(entry);
            Add(_accentBar);

            VisualElement body = new VisualElement();
            body.style.flexGrow = 1f;
            body.style.paddingLeft = 8f;
            body.style.paddingRight = 8f;
            body.style.paddingTop = 6f;
            body.style.paddingBottom = 6f;
            Add(body);

            VisualElement header = CreateRow();
            body.Add(header);

            if (entry.HasOwner)
            {
                // A button, not a label, because the name doubles as a locator: clicking it pings the
                // owning object in the Hierarchy or Project window.
                _nameButton = new Button(PingOwner);
                _nameButton.text = entry.DisplayName;
                StyleAsLink(_nameButton);
                header.Add(_nameButton);
            }
            else
            {
                // Bare managers have nothing to select, so the name is inert rather than a button that
                // would look clickable and do nothing.
                _nameLabel = new Label(entry.DisplayName);
                _nameLabel.style.color = PrimaryText;
                _nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                header.Add(_nameLabel);
            }

            _typeLabel = new Label(entry.EnumType != null ? entry.EnumType.Name : "?");
            _typeLabel.style.color = DimText;
            _typeLabel.style.marginLeft = 6f;
            _typeLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            header.Add(_typeLabel);

            VisualElement currentRow = CreateRow();
            currentRow.style.marginTop = 4f;
            body.Add(currentRow);

            _currentPill = new Label();
            _currentPill.style.paddingLeft = 6f;
            _currentPill.style.paddingRight = 6f;
            _currentPill.style.paddingTop = 1f;
            _currentPill.style.paddingBottom = 1f;
            _currentPill.style.borderTopLeftRadius = PillCornerRadius;
            _currentPill.style.borderTopRightRadius = PillCornerRadius;
            _currentPill.style.borderBottomLeftRadius = PillCornerRadius;
            _currentPill.style.borderBottomRightRadius = PillCornerRadius;
            _currentPill.style.unityFontStyleAndWeight = FontStyle.Bold;
            _currentPill.style.unityTextAlign = TextAnchor.MiddleCenter;
            currentRow.Add(_currentPill);

            _elapsedLabel = new Label();
            _elapsedLabel.style.color = DimText;
            _elapsedLabel.style.marginLeft = 6f;
            _elapsedLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            currentRow.Add(_elapsedLabel);

            _previousRow = CreateRow();
            _previousRow.style.marginTop = 2f;
            _previousRow.style.display = DisplayStyle.None;
            body.Add(_previousRow);

            Label fromLabel = new Label("from");
            fromLabel.style.color = DimText;
            _previousRow.Add(fromLabel);

            _previousDot = new VisualElement();
            _previousDot.style.width = DotSize;
            _previousDot.style.height = DotSize;
            _previousDot.style.flexShrink = 0f;
            _previousDot.style.marginLeft = 5f;
            _previousDot.style.borderTopLeftRadius = DotSize * 0.5f;
            _previousDot.style.borderTopRightRadius = DotSize * 0.5f;
            _previousDot.style.borderBottomLeftRadius = DotSize * 0.5f;
            _previousDot.style.borderBottomRightRadius = DotSize * 0.5f;
            _previousRow.Add(_previousDot);

            _previousLabel = new Label();
            _previousLabel.style.color = PrimaryText;
            _previousLabel.style.marginLeft = 4f;
            _previousRow.Add(_previousLabel);

            _stateStrip = CreateRow();
            _stateStrip.style.flexWrap = Wrap.Wrap;
            _stateStrip.style.marginTop = 6f;
            body.Add(_stateStrip);

            _historyContainer = new VisualElement();
            _historyContainer.style.marginTop = 6f;
            body.Add(_historyContainer);

            _historyEmptyLabel = new Label("no transitions yet");
            _historyEmptyLabel.style.color = DimText;
            _historyEmptyLabel.style.display = DisplayStyle.None;
            _historyContainer.Add(_historyEmptyLabel);
        }

        internal StateMachineDebugEntry Entry
        {
            get { return _entry; }
        }

        internal void Refresh(double now)
        {
            IStateMachineDebugSource machine;
            if (!_entry.TryGetMachine(out machine))
            {
                style.opacity = 0.35f;
                return;
            }

            style.opacity = 1f;

            bool stripJustBuilt = EnsureStateStrip(machine);

            string currentStateName = machine.CurrentStateName;
            RefreshCurrentState(currentStateName, now);
            RefreshPreviousState(machine);

            if (stripJustBuilt || !_hasCachedCurrentStateName || currentStateName != _cachedCurrentStateName)
            {
                _cachedCurrentStateName = currentStateName;
                _hasCachedCurrentStateName = true;
                RestyleStateStrip(machine, currentStateName);
            }

            RefreshHistory(now);
        }

        // A registered-but-uninitialized machine gets a neutral em-dash pill and no timer: that is a
        // real state of the world, so it is shown plainly rather than hidden.
        private void RefreshCurrentState(string currentStateName, double now)
        {
            if (string.IsNullOrEmpty(currentStateName))
            {
                _currentPill.text = "—";
                _currentPill.style.backgroundColor = NeutralPill;
                _currentPill.style.color = DimText;
                _elapsedLabel.style.display = DisplayStyle.None;
                return;
            }

            Color background = StateDebugPalette.StateColor(currentStateName);
            _currentPill.text = currentStateName;
            _currentPill.style.backgroundColor = background;
            _currentPill.style.color = StateDebugPalette.TextOn(background);

            _elapsedLabel.style.display = DisplayStyle.Flex;
            _elapsedLabel.text = "for " + FormatSeconds(now - _entry.CurrentStateEnteredAt);
        }

        private void RefreshPreviousState(IStateMachineDebugSource machine)
        {
            string previous = machine.PreviousStateName;
            if (string.IsNullOrEmpty(previous))
            {
                _previousRow.style.display = DisplayStyle.None;
                return;
            }

            _previousRow.style.display = DisplayStyle.Flex;
            if (_previousLabel.text != previous)
            {
                _previousLabel.text = previous;
                _previousDot.style.backgroundColor = StateDebugPalette.StateColor(previous);
            }
        }

        // Deferred out of the constructor because the card may be built in the same tick the machine
        // registers. Returns true when chips were created, which forces a restyle.
        private bool EnsureStateStrip(IStateMachineDebugSource machine)
        {
            if (_stateChips.Count > 0)
            {
                return false;
            }

            IReadOnlyList<string> names = machine.StateNames;
            if (names == null || names.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < names.Count; i++)
            {
                Label chip = new Label(names[i]);
                chip.style.marginRight = 4f;
                chip.style.marginTop = 2f;
                chip.style.paddingLeft = 5f;
                chip.style.paddingRight = 5f;
                chip.style.borderTopLeftRadius = PillCornerRadius;
                chip.style.borderTopRightRadius = PillCornerRadius;
                chip.style.borderBottomLeftRadius = PillCornerRadius;
                chip.style.borderBottomRightRadius = PillCornerRadius;
                chip.style.borderTopWidth = 1f;
                chip.style.borderRightWidth = 1f;
                chip.style.borderBottomWidth = 1f;
                chip.style.borderLeftWidth = 1f;
                chip.style.unityTextAlign = TextAnchor.MiddleCenter;
                _stateStrip.Add(chip);
                _stateChips.Add(chip);
            }

            return true;
        }

        // Filled for the current state, bright outline for everything reachable from it, dim for the
        // rest. With no current state every chip is drawn dim: nothing is reachable from nowhere.
        private void RestyleStateStrip(IStateMachineDebugSource machine, string currentStateName)
        {
            IReadOnlyList<string> names = machine.StateNames;
            if (names == null)
            {
                return;
            }

            int currentIndex = IndexOf(names, currentStateName);

            for (int i = 0; i < _stateChips.Count && i < names.Count; i++)
            {
                Label chip = _stateChips[i];
                Color stateColor = StateDebugPalette.StateColor(names[i]);

                if (i == currentIndex)
                {
                    chip.style.backgroundColor = stateColor;
                    chip.style.color = StateDebugPalette.TextOn(stateColor);
                    chip.style.borderTopColor = stateColor;
                    chip.style.borderRightColor = stateColor;
                    chip.style.borderBottomColor = stateColor;
                    chip.style.borderLeftColor = stateColor;
                    chip.style.unityFontStyleAndWeight = FontStyle.Bold;
                    continue;
                }

                bool reachable = currentIndex >= 0 && machine.IsTransitionAllowed(currentIndex, i);

                // Unreachable chips keep their hue but lose most of their presence: the strip should
                // still read as "these are the states", with the legal moves standing out from it.
                Color outline = reachable ? stateColor : Fade(stateColor, 0.28f);
                chip.style.backgroundColor = Color.clear;
                chip.style.color = reachable ? stateColor : Fade(stateColor, 0.45f);
                chip.style.borderTopColor = outline;
                chip.style.borderRightColor = outline;
                chip.style.borderBottomColor = outline;
                chip.style.borderLeftColor = outline;
                chip.style.unityFontStyleAndWeight = FontStyle.Normal;
            }
        }

        // Rows are pooled and surplus ones hidden rather than removed, so a machine transitioning
        // every frame does not churn the visual tree. Ages are relative, newest first.
        private void RefreshHistory(double now)
        {
            int count = HistoryCount;
            if (count <= 0)
            {
                _historyEmptyLabel.style.display = DisplayStyle.Flex;
                for (int i = 0; i < _historyRows.Count; i++)
                {
                    _historyRows[i].Root.style.display = DisplayStyle.None;
                }

                return;
            }

            _historyEmptyLabel.style.display = DisplayStyle.None;

            for (int i = 0; i < count; i++)
            {
                HistoryRow row = GetOrCreateHistoryRow(i);
                StateTransitionRecord record = GetHistoryRecord(i);

                row.Root.style.display = DisplayStyle.Flex;
                if (row.From.text != record.From)
                {
                    row.From.text = record.From;
                    row.From.style.color = StateDebugPalette.StateColor(record.From);
                }

                if (row.To.text != record.To)
                {
                    row.To.text = record.To;
                    row.To.style.color = StateDebugPalette.StateColor(record.To);
                }

                row.Age.text = FormatSeconds(now - record.Timestamp) + " ago";
            }

            for (int i = count; i < _historyRows.Count; i++)
            {
                _historyRows[i].Root.style.display = DisplayStyle.None;
            }
        }

        private HistoryRow GetOrCreateHistoryRow(int index)
        {
            while (_historyRows.Count <= index)
            {
                HistoryRow row = new HistoryRow();
                _historyContainer.Add(row.Root);
                _historyRows.Add(row);
            }

            return _historyRows[index];
        }

        // The two members below are the card's only contact with the entry's ring-buffer accessors, so
        // the newest-first ordering the UI wants is asserted here rather than assumed throughout.
        private int HistoryCount
        {
            get { return _entry.HistoryCount; }
        }

        private StateTransitionRecord GetHistoryRecord(int indexFromNewest)
        {
            return _entry.GetHistory(indexFromNewest);
        }

        // Pings rather than selects: selecting would replace whatever the user has open in the
        // Inspector. The null check covers the window where Owner is already fake-null but the entry
        // has not been pruned yet.
        private void PingOwner()
        {
            if (_entry.Owner != null)
            {
                EditorGUIUtility.PingObject(_entry.Owner);
            }
        }

        private static VisualElement CreateRow()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        // Strips a Button of its chrome so it reads as a clickable name, keeping the hit target and
        // keyboard focus behavior.
        private static void StyleAsLink(Button button)
        {
            button.style.backgroundColor = Color.clear;
            button.style.borderTopWidth = 0f;
            button.style.borderRightWidth = 0f;
            button.style.borderBottomWidth = 0f;
            button.style.borderLeftWidth = 0f;
            button.style.marginLeft = 0f;
            button.style.marginRight = 0f;
            button.style.marginTop = 0f;
            button.style.marginBottom = 0f;
            button.style.paddingLeft = 0f;
            button.style.paddingRight = 0f;
            button.style.paddingTop = 0f;
            button.style.paddingBottom = 0f;
            button.style.color = PrimaryText;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
        }

        // Clamped at zero: a timestamp can briefly sit in the future after a domain reload or a manual
        // clock change, and "0.0s" beats a nonsensical negative age.
        private static string FormatSeconds(double seconds)
        {
            if (seconds < 0d)
            {
                seconds = 0d;
            }

            return seconds.ToString("0.0") + "s";
        }

        private static Color Fade(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        // Hand-rolled because IReadOnlyList<T> has no IndexOf and the LINQ equivalent would allocate
        // on every state change, for every card.
        private static int IndexOf(IReadOnlyList<string> names, string value)
        {
            if (value == null)
            {
                return -1;
            }

            for (int i = 0; i < names.Count; i++)
            {
                if (names[i] == value)
                {
                    return i;
                }
            }

            return -1;
        }

        // One pooled history line: From → To in the states' own colors, with a right-aligned relative
        // age. Holds its three labels rather than looking children up by index on every tick.
        private sealed class HistoryRow
        {
            internal readonly VisualElement Root;

            internal readonly Label From;

            internal readonly Label To;

            internal readonly Label Age;

            internal HistoryRow()
            {
                Root = CreateRow();
                Root.style.marginTop = 1f;

                From = new Label();
                Root.Add(From);

                Label arrow = new Label(" → ");
                arrow.style.color = DimText;
                Root.Add(arrow);

                To = new Label();
                Root.Add(To);

                // A grow-only spacer, rather than absolute positioning, so the age stays on the right
                // edge at any window width without the row needing a layout callback.
                VisualElement spacer = new VisualElement();
                spacer.style.flexGrow = 1f;
                Root.Add(spacer);

                Age = new Label();
                Age.style.color = DimText;
                Age.style.unityTextAlign = TextAnchor.MiddleRight;
                Root.Add(Age);
            }
        }
    }
}
