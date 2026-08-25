using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace UnityEssentials.UI
{
    /// <summary>
    /// The navigation bookkeeping behind <see cref="WindowService"/>: the visible screen/popup stack,
    /// the screen back-trail, and the <see cref="IUIData"/> each entry was opened with so back
    /// navigation can restore a screen with its original payload. Pure C#, testable without Unity.
    /// </summary>
    internal sealed class UIHistory<TEntry> where TEntry : class
    {
        // Data lists run parallel to their entry lists — always mutate both at the same index.
        private readonly List<TEntry> _visible = new List<TEntry>();
        private readonly List<IUIData> _visibleData = new List<IUIData>();
        private readonly List<TEntry> _screens = new List<TEntry>();
        private readonly List<IUIData> _screenData = new List<IUIData>();
        private readonly ReadOnlyCollection<TEntry> _visibleView;
        private readonly ReadOnlyCollection<TEntry> _screensView;

        internal UIHistory()
        {
            _visibleView = new ReadOnlyCollection<TEntry>(_visible);
            _screensView = new ReadOnlyCollection<TEntry>(_screens);
        }

        internal IReadOnlyList<TEntry> VisibleEntries => _visibleView;

        internal IReadOnlyList<TEntry> ScreenTrail => _screensView;

        internal TEntry Top => _visible.Count > 0 ? _visible[_visible.Count - 1] : null;

        internal TEntry TopScreen => _screens.Count > 0 ? _screens[_screens.Count - 1] : null;

        internal void PushScreen(TEntry screen, IUIData data = null)
        {
            if (ReferenceEquals(screen, null))
            {
                return;
            }

            // Move-to-top dedup keeps menu ping-pong bounded: A → B → A leaves [B, A].
            RemoveByIdentity(_visible, _visibleData, screen);
            _visible.Add(screen);
            _visibleData.Add(data);

            RemoveByIdentity(_screens, _screenData, screen);
            _screens.Add(screen);
            _screenData.Add(data);
        }

        internal void PushOverlay(TEntry overlay, IUIData data = null)
        {
            if (ReferenceEquals(overlay, null))
            {
                return;
            }

            // Overlays never touch the back-trail: closing a popup is not a navigation step.
            RemoveByIdentity(_visible, _visibleData, overlay);
            _visible.Add(overlay);
            _visibleData.Add(data);
        }

        internal void Hide(TEntry entry, bool isScreen)
        {
            if (ReferenceEquals(entry, null))
            {
                return;
            }

            RemoveByIdentity(_visible, _visibleData, entry);

            if (!isScreen)
            {
                return;
            }

            // The conditional prune: a superseded screen is not the trail top (the incoming screen
            // was pushed first) so it stays as the previous screen; a screen closed on its own is
            // the top and retreats the trail.
            if (ReferenceEquals(TopScreen, entry))
            {
                _screens.RemoveAt(_screens.Count - 1);
                _screenData.RemoveAt(_screenData.Count - 1);
            }
        }

        internal void UpdateData(TEntry entry, IUIData data)
        {
            if (ReferenceEquals(entry, null))
            {
                return;
            }

            for (var i = 0; i < _screens.Count; i++)
            {
                if (ReferenceEquals(_screens[i], entry))
                {
                    _screenData[i] = data;
                    break;
                }
            }

            for (var i = 0; i < _visible.Count; i++)
            {
                if (ReferenceEquals(_visible[i], entry))
                {
                    _visibleData[i] = data;
                    break;
                }
            }
        }

        internal TEntry PreviousScreen(TEntry current)
        {
            return PreviousScreen(current, out _);
        }

        internal TEntry PreviousScreen(TEntry current, out IUIData data)
        {
            data = null;

            var count = _screens.Count;
            if (count == 0)
            {
                return null;
            }

            if (ReferenceEquals(_screens[count - 1], current))
            {
                if (count > 1)
                {
                    data = _screenData[count - 2];
                    return _screens[count - 2];
                }

                return null;
            }

            // current may be on the trail without being its top; skip it so a screen can never be
            // its own previous screen.
            for (var i = count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(_screens[i], current))
                {
                    data = _screenData[i];
                    return _screens[i];
                }
            }

            return null;
        }

        internal bool TruncateTopScreen(TEntry current)
        {
            if (ReferenceEquals(current, null) || !ReferenceEquals(TopScreen, current))
            {
                return false;
            }

            _screens.RemoveAt(_screens.Count - 1);
            _screenData.RemoveAt(_screenData.Count - 1);
            return true;
        }

        internal void Clear()
        {
            _visible.Clear();
            _visibleData.Clear();
            _screens.Clear();
            _screenData.Clear();
        }

        // Identity only: Unity objects override equality so a destroyed one equals null, which
        // would let equality-based removal take out the wrong entry.
        private static void RemoveByIdentity(List<TEntry> list, List<IUIData> data, TEntry entry)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], entry))
                {
                    list.RemoveAt(i);
                    data.RemoveAt(i);
                    return;
                }
            }
        }
    }
}
