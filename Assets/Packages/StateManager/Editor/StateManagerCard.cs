using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEssentials.States;

namespace UnityEssentials.States.Editor
{
    /// <summary>
    /// The visual for one registered state machine: header, current state, previous state, a strip of
    /// every state with its reachability, and a short transition history. One card is built per
    /// <see cref="StateMachineDebugEntry"/> and then refreshed ten times a second.
    /// </summary>
    /// <remarks>
    /// Built once, refreshed cheaply. The window's tick runs every 100 ms for as long as the window is
    /// open, so <see cref="Refresh"/> must not allocate elements or restyle the whole tree: the
    /// hierarchy is created in the constructor (plus the state strip, which needs the machine and is
    /// therefore built on the first refresh that finds one), history rows are pooled, and the state
    /// strip is only restyled when the current state actually changes. What is left per tick is a
    /// handful of text assignments — the price of a live view without a per-frame repaint.
    /// <para>
    /// All styling is set through <see cref="VisualElement.style"/> in code rather than through USS.
    /// This package must be copyable into another project as a folder; a stylesheet asset would add a
    /// GUID reference that has to survive the copy, and a missing one degrades into an unreadable
    /// window rather than a compile error. Inline styles cost a little verbosity here and remove that
    /// failure mode entirely.
    /// </para>
    /// <para>
    /// The card never reaches into the machine for anything but display, and never subscribes to it.
    /// Everything it shows is either a snapshot read through
    /// <see cref="IStateMachineDebugSource"/> or history the registry already recorded, so a card that
    /// is destroyed or never refreshed cannot affect the machine it describes.
    /// </para>
    /// </remarks>
    internal sealed class StateManagerCard : VisualElement
    {
        // ---- Metrics ------------------------------------------------------

        /// <summary>Width of the per-machine accent bar down the left edge, in pixels.</summary>
        private const float AccentBarWidth = 3f;

        /// <summary>Corner radius shared by the card, the current-state pill and the strip chips.</summary>
        private const float CardCornerRadius = 6f;

        /// <summary>Corner radius for the small pills and chips, kept below <see cref="CardCornerRadius"/>.</summary>
        private const float PillCornerRadius = 4f;

        /// <summary>Diameter of the colored dot marking the previous state.</summary>
        private const float DotSize = 8f;

        // ---- Skin ---------------------------------------------------------

        /// <summary>
        /// Card background. Chosen per skin rather than as a translucent overlay so the card keeps the
        /// same contrast against the window whether or not something is drawn behind it.
        /// </summary>
        private static Color CardBackground
        {
            get
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(0.22f, 0.22f, 0.24f, 1f)
                    : new Color(0.85f, 0.85f, 0.87f, 1f);
            }
        }

        /// <summary>Text color for secondary information: type names, timings, the arrow in history rows.</summary>
        private static Color DimText
        {
            get
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(0.62f, 0.62f, 0.66f, 1f)
                    : new Color(0.35f, 0.35f, 0.39f, 1f);
            }
        }

        /// <summary>Text color for primary information that is not printed on a colored background.</summary>
        private static Color PrimaryText
        {
            get
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(0.87f, 0.87f, 0.90f, 1f)
                    : new Color(0.12f, 0.12f, 0.15f, 1f);
            }
        }

        /// <summary>
        /// Fill for the placeholder pill shown while a machine is registered but has no current state.
        /// Deliberately colorless: an uninitialized machine must not look like it is in some state.
        /// </summary>
        private static Color NeutralPill
        {
            get
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(0.32f, 0.32f, 0.35f, 1f)
                    : new Color(0.72f, 0.72f, 0.75f, 1f);
            }
        }

        // ---- Model --------------------------------------------------------

        /// <summary>The registry entry this card visualizes, for its whole lifetime.</summary>
        private readonly StateMachineDebugEntry _entry;

        // ---- Elements -----------------------------------------------------

        /// <summary>The colored bar identifying the machine, filled once from the entry's accent.</summary>
        private readonly VisualElement _accentBar;

        /// <summary>Clickable name, present only when the entry has a Unity object to ping.</summary>
        private readonly Button _nameButton;

        /// <summary>Static name, present only when the entry has no owner to ping.</summary>
        private readonly Label _nameLabel;

        /// <summary>The dim <c>StateManager&lt;GameState&gt;</c>-style type hint next to the name.</summary>
        private readonly Label _typeLabel;

        /// <summary>The filled, rounded badge naming the current state.</summary>
        private readonly Label _currentPill;

        /// <summary>The live "for 12.3s" readout beside the current-state pill.</summary>
        private readonly Label _elapsedLabel;

        /// <summary>Row holding the previous-state dot and name; hidden until a transition happens.</summary>
        private readonly VisualElement _previousRow;

        /// <summary>The color dot identifying the previous state.</summary>
        private readonly VisualElement _previousDot;

        /// <summary>The previous state's name.</summary>
        private readonly Label _previousLabel;

        /// <summary>Container for the per-state chips; populated on the first refresh with a machine.</summary>
        private readonly VisualElement _stateStrip;

        /// <summary>The chips inside <see cref="_stateStrip"/>, index-aligned with the machine's state names.</summary>
        private readonly List<Label> _stateChips = new List<Label>();

        /// <summary>Container for the pooled history rows.</summary>
        private readonly VisualElement _historyContainer;

        /// <summary>Pooled history rows, grown on demand up to the entry's ring-buffer capacity.</summary>
        private readonly List<HistoryRow> _historyRows = new List<HistoryRow>();

        /// <summary>Shown in place of the rows while the machine has not transitioned yet.</summary>
        private readonly Label _historyEmptyLabel;

        // ---- Refresh caches -----------------------------------------------

        /// <summary>
        /// Last current-state name applied to the strip. The strip is the expensive part of a refresh —
        /// one reachability query and up to three style writes per state — so it is only redone when
        /// this stops matching.
        /// </summary>
        private string _cachedCurrentStateName;

        /// <summary>
        /// Whether <see cref="_cachedCurrentStateName"/> holds a value that was actually applied. Needed
        /// because <see langword="null"/> is a legitimate current state (machine registered but not yet
        /// initialized) and would otherwise be indistinguishable from "never styled".
        /// </summary>
        private bool _hasCachedCurrentStateName;

        // ---- Construction -------------------------------------------------

        /// <summary>
        /// Builds the whole card except the state strip, which needs a live machine and is deferred to
        /// the first <see cref="Refresh"/>.
        /// </summary>
        /// <param name="entry">The registry entry to visualize. Retained for the card's lifetime.</param>
        /// <remarks>
        /// Everything read here is entry metadata that cannot change while the entry lives — accent,
        /// display name, owner, enum type — so the header is styled once and never touched again.
        /// </remarks>
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

            // ---- Header: who this machine belongs to, and of what type.
            VisualElement header = CreateRow();
            body.Add(header);

            if (entry.HasOwner)
            {
                // A button, not a label, because the name doubles as a locator: clicking it pings the
                // owning object in the Hierarchy or Project window, which is the fastest way to get
                // from "this card is misbehaving" to the object responsible.
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

            // ---- Current state: the one line most sessions actually watch.
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

            // ---- Previous state: context for the current one, hidden until it exists.
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

            // ---- State strip: the machine's shape at a glance.
            _stateStrip = CreateRow();
            _stateStrip.style.flexWrap = Wrap.Wrap;
            _stateStrip.style.marginTop = 6f;
            body.Add(_stateStrip);

            // ---- History: what just happened, newest first.
            _historyContainer = new VisualElement();
            _historyContainer.style.marginTop = 6f;
            body.Add(_historyContainer);

            _historyEmptyLabel = new Label("no transitions yet");
            _historyEmptyLabel.style.color = DimText;
            _historyEmptyLabel.style.display = DisplayStyle.None;
            _historyContainer.Add(_historyEmptyLabel);
        }

        /// <summary>
        /// The entry this card was built for, so the window can match cards to entries without keeping
        /// a parallel map.
        /// </summary>
        internal StateMachineDebugEntry Entry
        {
            get { return _entry; }
        }

        // ---- Refresh ------------------------------------------------------

        /// <summary>
        /// Updates every live part of the card from the machine's current snapshot.
        /// </summary>
        /// <param name="now">
        /// The window's clock reading for this tick, in the same units the registry timestamps with
        /// (<c>Time.realtimeSinceStartupAsDouble</c>). Passed in rather than sampled here so every card
        /// in one tick shows ages relative to the same instant.
        /// </param>
        /// <remarks>
        /// A card whose machine has been collected or destroyed dims itself and stops updating instead
        /// of removing itself: the entry is pruned by the registry on the next
        /// <c>CollectAlive</c>, and the window rebuilds from that. Self-removal would mean a child
        /// mutating its parent's list mid-iteration for no visual gain.
        /// </remarks>
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

        /// <summary>
        /// Applies the current state to the pill and the elapsed readout.
        /// </summary>
        /// <param name="currentStateName">The machine's current state, or <see langword="null"/> before initialization.</param>
        /// <param name="now">This tick's clock reading.</param>
        /// <remarks>
        /// A registered-but-uninitialized machine gets a neutral em-dash pill and no timer. It is a real
        /// state of the world — the entry is created inside <c>Initialize</c> before the initial state
        /// is entered, and a machine can also be registered by a behaviour whose <c>Start</c> was
        /// deferred — so showing it plainly beats hiding the card or inventing a state name.
        /// </remarks>
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

        /// <summary>
        /// Shows or hides the previous-state line.
        /// </summary>
        /// <param name="machine">The live machine to read from.</param>
        /// <remarks>
        /// Hidden rather than blanked while the machine has never transitioned, so the card is one line
        /// shorter instead of showing an empty row with a stray dot.
        /// </remarks>
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

        // ---- State strip --------------------------------------------------

        /// <summary>
        /// Creates one chip per registered state, the first time a machine with states is available.
        /// </summary>
        /// <param name="machine">The live machine to read state names from.</param>
        /// <returns><see langword="true"/> if chips were created by this call, which forces a restyle.</returns>
        /// <remarks>
        /// Deferred rather than done in the constructor because the card may be built in the same tick
        /// the machine registers, and because the set of states is fixed once configuration is done —
        /// there is no scenario where the strip needs rebuilding, only restyling.
        /// </remarks>
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

        /// <summary>
        /// Repaints the strip for a new current state: filled for the current one, bright outline for
        /// everything reachable from it, dim for everything else.
        /// </summary>
        /// <param name="machine">The live machine, queried for transition legality.</param>
        /// <param name="currentStateName">The state the machine is in, or <see langword="null"/>.</param>
        /// <remarks>
        /// This is the card's most useful readout and its most expensive one: it answers "where can this
        /// go next" without opening the configuration code. It runs only on an actual state change,
        /// which for a typical machine is a few times a second at worst.
        /// <para>
        /// With no current state every chip is drawn dim — nothing is reachable from nowhere, and
        /// guessing at the configured initial state here would show a reachability that the machine
        /// itself has not committed to yet.
        /// </para>
        /// </remarks>
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

        // ---- History ------------------------------------------------------

        /// <summary>
        /// Fills the history rows from the entry's ring buffer, newest first, and hides the surplus.
        /// </summary>
        /// <param name="now">This tick's clock reading, for the relative ages.</param>
        /// <remarks>
        /// Rows are pooled: the list only ever grows to the ring buffer's capacity, and rows beyond the
        /// current count are hidden rather than removed, so a machine transitioning every frame does not
        /// churn the visual tree.
        /// <para>
        /// Ages are relative ("3.2s ago") rather than absolute. The absolute number is
        /// <c>realtimeSinceStartup</c>, which means nothing to a reader; the useful question during
        /// debugging is always how long ago something happened relative to now.
        /// </para>
        /// </remarks>
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

        /// <summary>
        /// Returns the pooled row at <paramref name="index"/>, creating it on first use.
        /// </summary>
        /// <param name="index">Row position, 0 being the newest transition.</param>
        /// <returns>A row ready to be filled.</returns>
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

        // ---- Entry history adapter ----------------------------------------
        //
        // The two members below are the card's only contact with the entry's ring-buffer accessors.
        // They are isolated so the card can follow a change in that shape from one place, and so the
        // newest-first ordering the UI wants is asserted here rather than assumed throughout.

        /// <summary>How many transitions the entry currently holds, capped by its ring-buffer capacity.</summary>
        private int HistoryCount
        {
            get { return _entry.HistoryCount; }
        }

        /// <summary>
        /// Reads one recorded transition, newest first.
        /// </summary>
        /// <param name="indexFromNewest">0 for the most recent transition.</param>
        /// <returns>The recorded transition.</returns>
        private StateTransitionRecord GetHistoryRecord(int indexFromNewest)
        {
            return _entry.GetHistory(indexFromNewest);
        }

        // ---- Interaction --------------------------------------------------

        /// <summary>
        /// Flashes the owning object in the Hierarchy or Project window.
        /// </summary>
        /// <remarks>
        /// Pings rather than selects: selecting would replace whatever the user has open in the
        /// Inspector, which during debugging is often the thing they are watching. The null check
        /// covers the window between the object being destroyed and the entry being pruned, where
        /// <c>Owner</c> is already fake-null.
        /// </remarks>
        private void PingOwner()
        {
            if (_entry.Owner != null)
            {
                EditorGUIUtility.PingObject(_entry.Owner);
            }
        }

        // ---- Helpers ------------------------------------------------------

        /// <summary>Creates the horizontal, vertically centered row used throughout the card.</summary>
        /// <returns>An empty row element.</returns>
        private static VisualElement CreateRow()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        /// <summary>
        /// Strips a <see cref="Button"/> of its chrome so it reads as a clickable name rather than a
        /// control, while keeping the button's hit target and keyboard focus behavior.
        /// </summary>
        /// <param name="button">The button to restyle.</param>
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

        /// <summary>
        /// Formats a duration for display, clamped at zero.
        /// </summary>
        /// <param name="seconds">The duration; negative values are treated as zero.</param>
        /// <returns>The duration to one decimal, suffixed with <c>s</c>.</returns>
        /// <remarks>
        /// Negatives are possible for a single tick after a domain reload or a manual clock change,
        /// where a timestamp can briefly sit in the future relative to the window's sample. Showing
        /// "0.0s" for that frame is preferable to a nonsensical negative age.
        /// </remarks>
        private static string FormatSeconds(double seconds)
        {
            if (seconds < 0d)
            {
                seconds = 0d;
            }

            return seconds.ToString("0.0") + "s";
        }

        /// <summary>Returns <paramref name="color"/> at the given alpha, for the dimmed strip chips.</summary>
        /// <param name="color">The base color.</param>
        /// <param name="alpha">The alpha to apply.</param>
        /// <returns>The faded color.</returns>
        private static Color Fade(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        /// <summary>
        /// Finds <paramref name="value"/> in <paramref name="names"/> without allocating an enumerator.
        /// </summary>
        /// <param name="names">The machine's state names.</param>
        /// <param name="value">The name to locate; <see langword="null"/> yields -1.</param>
        /// <returns>The index, or -1 if absent.</returns>
        /// <remarks>
        /// Hand-rolled because <see cref="IReadOnlyList{T}"/> has no <c>IndexOf</c> and the LINQ
        /// equivalent would allocate on every state change, in a loop that runs for every card.
        /// </remarks>
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

        // ---- Nested types -------------------------------------------------

        /// <summary>
        /// One pooled history line: <c>From → To</c> in the states' own colors, with a right-aligned
        /// relative age.
        /// </summary>
        /// <remarks>
        /// A small class holding the three labels rather than a query into the row's children: history
        /// rows are refreshed for every card on every tick, and looking children up by index or name
        /// each time would be both slower and easy to break by inserting an element.
        /// </remarks>
        private sealed class HistoryRow
        {
            /// <summary>The row element, added to the history container once.</summary>
            internal readonly VisualElement Root;

            /// <summary>The state transitioned out of, in that state's color.</summary>
            internal readonly Label From;

            /// <summary>The state transitioned into, in that state's color.</summary>
            internal readonly Label To;

            /// <summary>The relative age, pushed to the right edge by a flexible spacer.</summary>
            internal readonly Label Age;

            /// <summary>Builds the row and its three labels, styled once.</summary>
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
