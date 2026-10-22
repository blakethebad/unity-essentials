using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UnityEssentials.Collections.Editor
{
    /// <summary>
    /// Odin-style drawer for <see cref="SerializedDictionary{TKey,TValue}"/>: a boxed header with count badge, pager
    /// and add button over a resizable key/value table that flags null and duplicate keys.
    /// IMGUI on purpose, so it renders in UI Toolkit inspectors, IMGUI editors and Odin alike.
    /// </summary>
    [CustomPropertyDrawer(typeof(SerializedDictionary<,>), true)]
    internal sealed class SerializedDictionaryDrawer : PropertyDrawer
    {
        internal const string EntriesField = "_entries";
        internal const string KeyField = "_key";
        internal const string ValueField = "_value";

        private const int PageSize = 20;
        private const float HeaderHeight = 22f;
        private const float ColumnHeaderHeight = 18f;
        private const float BorderWidth = 1f;
        private const float HeaderPadding = 4f;
        private const float HeaderSpacing = 4f;
        private const float IconButtonWidth = 20f;
        private const float IconButtonHeight = 18f;
        private const float PageArrowWidth = 16f;
        private const float PageLabelSpacing = 2f;
        private const float BadgeHeight = 16f;
        private const float BadgeHorizontalPadding = 6f;
        private const float BodyPadding = 6f;
        private const float RowPadding = 3f;
        private const float ColumnGap = 4f;
        private const float WarningIconSize = 16f;
        private const float WarningIconWidth = 18f;
        private const float SplitterHandleWidth = 8f;
        private const float DefaultSplitRatio = 0.35f;
        private const float MinSplitRatio = 0.2f;
        private const float MaxSplitRatio = 0.8f;
        private const float MinInlineLabelWidth = 60f;
        private const float InlineLabelWidthRatio = 0.4f;
        private const string SplitSessionKeyPrefix = "UnityEssentials.Collections.SerializedDictionary.Split:";

        private static readonly int SplitterControlHash = "UnityEssentials.Collections.SerializedDictionary.Splitter".GetHashCode();
        private static readonly Dictionary<(EntityId, string), DrawerState> _states = new Dictionary<(EntityId, string), DrawerState>();
        private static readonly Dictionary<(Type, string), float> _splitRatios = new Dictionary<(Type, string), float>();
        private static readonly Stack<SerializedKeyStatus[]> _statusPool = new Stack<SerializedKeyStatus[]>();

        private static SerializedObject _contextMenuObject;
        private static string _contextMenuPath;

        private static float MessageRowHeight
        {
            get { return EditorGUIUtility.singleLineHeight + (RowPadding * 2f); }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded)
            {
                return HeaderHeight;
            }

            if (property.serializedObject.isEditingMultipleObjects)
            {
                return HeaderHeight + MessageRowHeight + BorderWidth;
            }

            SerializedProperty entries = property.FindPropertyRelative(EntriesField);
            int count = entries.arraySize;
            float height = HeaderHeight + ColumnHeaderHeight + BorderWidth;
            if (count == 0)
            {
                return height + MessageRowHeight;
            }

            GetPageRange(GetState(property), count, out int start, out int end);
            SerializedDictionaryTypeInfo types = GetTypeInfo();
            for (int i = start; i < end; i++)
            {
                SerializedProperty element = entries.GetArrayElementAtIndex(i);
                float keyHeight = GetFieldHeight(element.FindPropertyRelative(KeyField), types.KeyHasCustomDrawer);
                float valueHeight = GetFieldHeight(element.FindPropertyRelative(ValueField), types.ValueHasCustomDrawer);
                height += GetRowHeight(keyHeight, valueHeight);
            }

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            int indentLevel = EditorGUI.indentLevel;
            float labelWidth = EditorGUIUtility.labelWidth;
            position = EditorGUI.IndentedRect(position);
            EditorGUI.indentLevel = 0;

            try
            {
                DrawDictionary(position, property, label);
            }
            finally
            {
                EditorGUI.indentLevel = indentLevel;
                EditorGUIUtility.labelWidth = labelWidth;
            }
        }

        [InitializeOnLoadMethod]
        private static void RegisterContextMenu()
        {
            EditorApplication.contextualPropertyMenu += OnContextualPropertyMenu;
        }

        private void DrawDictionary(Rect position, SerializedProperty property, GUIContent label)
        {
            bool multipleObjects = property.serializedObject.isEditingMultipleObjects;
            bool expanded = property.isExpanded;
            SerializedProperty entries = property.FindPropertyRelative(EntriesField);
            int count = entries.arraySize;
            DrawerState state = GetState(property);
            GetPageRange(state, count, out int start, out int end);

            Rect headerRect = new Rect(position.x, position.y, position.width, HeaderHeight);
            bool addRequested = DrawHeader(headerRect, property, label, count, state, multipleObjects);

            // The body uses the expanded state the layout was measured with, not one toggled this event.
            if (expanded)
            {
                Rect bodyRect = new Rect(position.x, headerRect.yMax, position.width, position.height - HeaderHeight);
                if (multipleObjects)
                {
                    DrawMultiObjectBody(bodyRect);
                }
                else
                {
                    DrawBody(bodyRect, property, entries, count, start, end, state);
                }
            }

            if (addRequested)
            {
                AddEntry(property, entries, state);
            }
        }

        private static bool DrawHeader(Rect rect, SerializedProperty property, GUIContent label, int count, DrawerState state, bool multipleObjects)
        {
            Event evt = Event.current;
            label = EditorGUI.BeginProperty(rect, label, property);

            // BeginProperty turns this on for differing multi-object values; Foldout would then draw a stray dash.
            EditorGUI.showMixedValue = false;

            // EndProperty opens Unity's property menu for this rect; flagging the target lets
            // OnContextualPropertyMenu append the dictionary actions to it.
            if (!multipleObjects && evt.type == EventType.ContextClick && rect.Contains(evt.mousePosition))
            {
                _contextMenuObject = property.serializedObject;
                _contextMenuPath = property.propertyPath;
            }

            if (evt.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, SerializedDictionaryStyles.Border);
                EditorGUI.DrawRect(Inset(rect), SerializedDictionaryStyles.HeaderBackground);
            }

            float centerY = rect.y + (rect.height * 0.5f);
            float right = rect.xMax - HeaderPadding;
            bool addRequested = false;
            if (!multipleObjects)
            {
                Rect addRect = new Rect(right - IconButtonWidth, centerY - (IconButtonHeight * 0.5f), IconButtonWidth, IconButtonHeight);
                addRequested = GUI.Button(addRect, SerializedDictionaryStyles.AddContent, SerializedDictionaryStyles.IconButton);
                right = addRect.x - HeaderSpacing;

                int pageCount = GetPageCount(count);
                if (pageCount > 1)
                {
                    right = DrawPager(right, centerY, state, pageCount) - HeaderSpacing;
                }

                right = DrawBadge(right, centerY, count) - HeaderSpacing;
            }

            // In hierarchy mode (every inspector) Foldout pulls its rect left by this offset to hang the arrow.
            float foldoutOffset = EditorGUIUtility.hierarchyMode ? EditorStyles.foldout.padding.left - EditorStyles.label.padding.left : 0f;
            float foldoutX = rect.x + HeaderPadding + foldoutOffset;
            float lineHeight = EditorGUIUtility.singleLineHeight;
            Rect foldoutRect = new Rect(foldoutX, centerY - (lineHeight * 0.5f), Mathf.Max(0f, right - foldoutX), lineHeight);

            // Foldout clips its text to labelWidth, so widen it to the free header space.
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = foldoutRect.width + foldoutOffset;
            bool expanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);
            EditorGUIUtility.labelWidth = labelWidth;
            if (expanded != property.isExpanded)
            {
                property.isExpanded = expanded;
            }

            EditorGUI.EndProperty();
            _contextMenuObject = null;
            _contextMenuPath = null;
            return addRequested;
        }

        private static float DrawPager(float right, float centerY, DrawerState state, int pageCount)
        {
            GUIContent pageContent = SerializedDictionaryStyles.PageContent(state.Page, pageCount);
            float labelWidth = SerializedDictionaryStyles.PageLabel.CalcSize(pageContent).x;
            float y = centerY - (IconButtonHeight * 0.5f);
            Rect nextRect = new Rect(right - PageArrowWidth, y, PageArrowWidth, IconButtonHeight);
            Rect labelRect = new Rect(nextRect.x - PageLabelSpacing - labelWidth, y, labelWidth, IconButtonHeight);
            Rect previousRect = new Rect(labelRect.x - PageLabelSpacing - PageArrowWidth, y, PageArrowWidth, IconButtonHeight);

            // Paging only changes the view, so it stays usable in read-only inspectors.
            bool enabled = GUI.enabled;
            GUI.enabled = state.Page > 0;
            if (GUI.Button(previousRect, SerializedDictionaryStyles.PreviousPageContent, SerializedDictionaryStyles.PageArrow))
            {
                state.Page--;
            }

            GUI.enabled = state.Page < pageCount - 1;
            if (GUI.Button(nextRect, SerializedDictionaryStyles.NextPageContent, SerializedDictionaryStyles.PageArrow))
            {
                state.Page++;
            }

            GUI.enabled = enabled;
            GUI.Label(labelRect, pageContent, SerializedDictionaryStyles.PageLabel);
            return previousRect.x;
        }

        private static float DrawBadge(float right, float centerY, int count)
        {
            GUIContent content = SerializedDictionaryStyles.BadgeContent(count);
            float width = Mathf.Max(BadgeHeight, SerializedDictionaryStyles.BadgeLabel.CalcSize(content).x + (BadgeHorizontalPadding * 2f));
            Rect badgeRect = new Rect(right - width, centerY - (BadgeHeight * 0.5f), width, BadgeHeight);

            if (Event.current.type == EventType.Repaint)
            {
                GUI.DrawTexture(badgeRect, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, SerializedDictionaryStyles.BadgeBackground, 0f, BadgeHeight * 0.5f);
                SerializedDictionaryStyles.BadgeLabel.Draw(badgeRect, content, false, false, false, false);
            }

            return badgeRect.x;
        }

        private void DrawBody(Rect rect, SerializedProperty property, SerializedProperty entries, int count, int start, int end, DrawerState state)
        {
            Event evt = Event.current;
            bool repaint = evt.type == EventType.Repaint;
            if (repaint)
            {
                DrawBodyBackground(rect);
            }

            ColumnLayout layout = new ColumnLayout(rect, GetSplitRatio(property.propertyPath));
            Rect columnHeaderRect = new Rect(rect.x + BorderWidth, rect.y, rect.width - (BorderWidth * 2f), ColumnHeaderHeight);
            DrawColumnHeader(columnHeaderRect, layout, repaint);
            HandleSplitter(columnHeaderRect, layout, property.propertyPath);

            float y = columnHeaderRect.yMax;
            if (count == 0)
            {
                DrawMessageRow(rect, y, SerializedDictionaryStyles.EmptyContent);
                return;
            }

            SerializedKeyStatus[] statuses = RentStatuses(count);
            try
            {
                SerializedDictionaryKeyUtility.ValidateKeys(entries, count, statuses);
                SerializedDictionaryTypeInfo types = GetTypeInfo();
                float lineHeight = EditorGUIUtility.singleLineHeight;
                int removeIndex = -1;
                int hoveredRow = -1;

                for (int i = start; i < end; i++)
                {
                    SerializedProperty element = entries.GetArrayElementAtIndex(i);
                    SerializedProperty key = element.FindPropertyRelative(KeyField);
                    SerializedProperty value = element.FindPropertyRelative(ValueField);
                    float keyHeight = GetFieldHeight(key, types.KeyHasCustomDrawer);
                    float valueHeight = GetFieldHeight(value, types.ValueHasCustomDrawer);
                    Rect rowRect = new Rect(rect.x + BorderWidth, y, rect.width - (BorderWidth * 2f), GetRowHeight(keyHeight, valueHeight));
                    SerializedKeyStatus status = statuses[i];

                    bool hovered = rowRect.Contains(evt.mousePosition);
                    if (hovered)
                    {
                        hoveredRow = i;
                    }

                    if (repaint)
                    {
                        DrawRowBackground(rowRect, layout.SplitX, (i - start) % 2 == 1, hovered, status != SerializedKeyStatus.Valid);
                    }

                    float contentY = y + RowPadding;
                    Rect keyRect = new Rect(layout.KeyX, contentY, layout.KeyWidth, keyHeight);
                    if (status != SerializedKeyStatus.Valid)
                    {
                        Rect iconRect = new Rect(keyRect.x, contentY + ((lineHeight - WarningIconSize) * 0.5f), WarningIconSize, WarningIconSize);
                        GUIContent warning = status == SerializedKeyStatus.NullKey
                            ? SerializedDictionaryStyles.NullKeyContent
                            : SerializedDictionaryStyles.DuplicateKeyContent;
                        GUI.Label(iconRect, warning, SerializedDictionaryStyles.WarningIcon);
                        keyRect.xMin += WarningIconWidth;
                    }

                    DrawField(keyRect, key, types.KeyHasCustomDrawer);
                    DrawField(new Rect(layout.ValueX, contentY, layout.ValueWidth, valueHeight), value, types.ValueHasCustomDrawer);

                    Rect removeRect = new Rect(layout.RemoveX, contentY + ((lineHeight - IconButtonHeight) * 0.5f), IconButtonWidth, IconButtonHeight);
                    if (GUI.Button(removeRect, SerializedDictionaryStyles.RemoveContent, SerializedDictionaryStyles.IconButton))
                    {
                        removeIndex = i;
                    }

                    y = rowRect.yMax;
                }

                // Using the move event makes the container repaint, which is what moves the hover highlight.
                if (evt.type == EventType.MouseMove && hoveredRow != state.HoveredRow)
                {
                    state.HoveredRow = hoveredRow;
                    evt.Use();
                }

                // Deleted after the loop so every row of this event is laid out against the measured heights.
                if (removeIndex >= 0)
                {
                    entries.DeleteArrayElementAtIndex(removeIndex);
                    GUI.changed = true;
                }
            }
            finally
            {
                _statusPool.Push(statuses);
            }
        }

        private static void DrawMultiObjectBody(Rect rect)
        {
            if (Event.current.type == EventType.Repaint)
            {
                DrawBodyBackground(rect);
            }

            DrawMessageRow(rect, rect.y, SerializedDictionaryStyles.MultiObjectContent);
        }

        private static void DrawBodyBackground(Rect rect)
        {
            EditorGUI.DrawRect(rect, SerializedDictionaryStyles.Border);
            EditorGUI.DrawRect(new Rect(rect.x + BorderWidth, rect.y, rect.width - (BorderWidth * 2f), rect.height - BorderWidth), SerializedDictionaryStyles.BodyBackground);
        }

        private static void DrawColumnHeader(Rect rect, ColumnLayout layout, bool repaint)
        {
            if (repaint)
            {
                EditorGUI.DrawRect(rect, SerializedDictionaryStyles.ColumnHeaderBackground);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - BorderWidth, rect.width, BorderWidth), SerializedDictionaryStyles.Border);
                EditorGUI.DrawRect(new Rect(layout.SplitX, rect.y, BorderWidth, rect.height - BorderWidth), SerializedDictionaryStyles.Divider);
            }

            float labelHeight = rect.height - BorderWidth;
            GUI.Label(new Rect(layout.KeyX, rect.y, layout.KeyWidth, labelHeight), SerializedDictionaryStyles.KeyColumnContent, SerializedDictionaryStyles.ColumnLabel);
            GUI.Label(new Rect(layout.ValueX, rect.y, layout.ValueWidth, labelHeight), SerializedDictionaryStyles.ValueColumnContent, SerializedDictionaryStyles.ColumnLabel);
        }

        private void HandleSplitter(Rect columnHeaderRect, ColumnLayout layout, string propertyPath)
        {
            Rect handleRect = new Rect(layout.SplitX - (SplitterHandleWidth * 0.5f), columnHeaderRect.y, SplitterHandleWidth, columnHeaderRect.height);
            int controlId = GUIUtility.GetControlID(SplitterControlHash, FocusType.Passive, handleRect);
            EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeHorizontal, controlId);

            Event evt = Event.current;
            EventType type = evt.GetTypeForControl(controlId);

            // Resizing only changes the view, so like Foldout it reads raw mouse events in read-only inspectors.
            if (!GUI.enabled && (evt.rawType == EventType.MouseDown || evt.rawType == EventType.MouseDrag || evt.rawType == EventType.MouseUp))
            {
                type = evt.rawType;
            }

            switch (type)
            {
                case EventType.MouseDown:
                    if (evt.button == 0 && handleRect.Contains(evt.mousePosition))
                    {
                        GUIUtility.hotControl = controlId;
                        evt.Use();
                    }

                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlId)
                    {
                        float ratio = (evt.mousePosition.x - layout.ColumnsX) / layout.ColumnsWidth;
                        _splitRatios[(fieldInfo.DeclaringType, propertyPath)] = Mathf.Clamp(ratio, MinSplitRatio, MaxSplitRatio);
                        evt.Use();
                    }

                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                        (Type, string) key = (fieldInfo.DeclaringType, propertyPath);
                        SessionState.SetFloat(GetSplitSessionKey(key), _splitRatios[key]);
                        evt.Use();
                    }

                    break;
            }
        }

        private static void DrawRowBackground(Rect rowRect, float splitX, bool striped, bool hovered, bool invalid)
        {
            if (striped)
            {
                EditorGUI.DrawRect(rowRect, SerializedDictionaryStyles.Stripe);
            }

            if (hovered)
            {
                EditorGUI.DrawRect(rowRect, SerializedDictionaryStyles.Hover);
            }

            if (invalid)
            {
                EditorGUI.DrawRect(rowRect, SerializedDictionaryStyles.InvalidOverlay);
            }

            EditorGUI.DrawRect(new Rect(splitX, rowRect.y, BorderWidth, rowRect.height), SerializedDictionaryStyles.Divider);
        }

        private static void DrawMessageRow(Rect bodyRect, float y, GUIContent content)
        {
            Rect rowRect = new Rect(bodyRect.x + BorderWidth, y, bodyRect.width - (BorderWidth * 2f), MessageRowHeight);
            GUI.Label(rowRect, content, SerializedDictionaryStyles.MessageLabel);
        }

        private static float GetRowHeight(float keyHeight, float valueHeight)
        {
            return Mathf.Max(keyHeight, valueHeight) + (RowPadding * 2f);
        }

        private static float GetFieldHeight(SerializedProperty property, bool hasCustomDrawer)
        {
            if (IsInlineGeneric(property, hasCustomDrawer))
            {
                float height = 0f;
                SerializedProperty child = property.Copy();
                SerializedProperty end = property.GetEndProperty();
                bool enterChildren = true;
                while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
                {
                    if (!enterChildren)
                    {
                        height += EditorGUIUtility.standardVerticalSpacing;
                    }

                    enterChildren = false;
                    height += EditorGUI.GetPropertyHeight(child, true);
                }

                return height;
            }

            if (IsCollection(property))
            {
                return EditorGUI.GetPropertyHeight(property, true);
            }

            return EditorGUI.GetPropertyHeight(property, GUIContent.none, true);
        }

        private static void DrawField(Rect rect, SerializedProperty property, bool hasCustomDrawer)
        {
            if (IsInlineGeneric(property, hasCustomDrawer))
            {
                float labelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = Mathf.Max(MinInlineLabelWidth, rect.width * InlineLabelWidthRatio);

                float y = rect.y;
                SerializedProperty child = property.Copy();
                SerializedProperty end = property.GetEndProperty();
                bool enterChildren = true;
                while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
                {
                    enterChildren = false;
                    float height = EditorGUI.GetPropertyHeight(child, true);
                    EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, height), child, true);
                    y += height + EditorGUIUtility.standardVerticalSpacing;
                }

                EditorGUIUtility.labelWidth = labelWidth;
                return;
            }

            if (IsCollection(property))
            {
                EditorGUI.PropertyField(rect, property, true);
                return;
            }

            EditorGUI.PropertyField(rect, property, GUIContent.none, true);
        }

        // Plain structs and classes are drawn Odin-style as their stacked children; types with their own
        // drawer (including a nested dictionary) keep it.
        private static bool IsInlineGeneric(SerializedProperty property, bool hasCustomDrawer)
        {
            return !hasCustomDrawer && property.propertyType == SerializedPropertyType.Generic && !property.isArray && property.hasVisibleChildren;
        }

        private static bool IsCollection(SerializedProperty property)
        {
            return property.isArray && property.propertyType != SerializedPropertyType.String;
        }

        private void AddEntry(SerializedProperty property, SerializedProperty entries, DrawerState state)
        {
            int index = entries.arraySize;
            entries.arraySize = index + 1;

            // Resizing copied the previous row; replace it with a fresh key and value when the types are known.
            SerializedDictionaryTypeInfo types = GetTypeInfo();
            if (types.KeyType != null)
            {
                SerializedProperty element = entries.GetArrayElementAtIndex(index);
                AssignSuggestedKey(element.FindPropertyRelative(KeyField), types.KeyType, entries, index);
                AssignDefaultValue(element.FindPropertyRelative(ValueField), types.ValueType);
            }

            property.isExpanded = true;
            state.Page = index / PageSize;
            GUI.changed = true;
        }

        private static void AssignSuggestedKey(SerializedProperty key, Type keyType, SerializedProperty entries, int count)
        {
            // Any failure keeps the copied key, which then shows as a duplicate to fix by hand.
            try
            {
                if (SerializedDictionaryKeyUtility.TrySuggestUniqueKey(keyType, entries, count, out object suggestion))
                {
                    Assign(key, suggestion);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void AssignDefaultValue(SerializedProperty value, Type valueType)
        {
            // Any failure keeps the copied value.
            try
            {
                if (SerializedDictionaryKeyUtility.TryCreateDefaultValue(valueType, out object defaultValue))
                {
                    Assign(value, defaultValue);
                }
            }
            catch (Exception)
            {
            }
        }

        private static void Assign(SerializedProperty property, object value)
        {
            // boxedValue cannot write arrays; an empty collection is their default.
            if (IsCollection(property))
            {
                property.arraySize = 0;
                return;
            }

            property.boxedValue = value;
        }

        private static void OnContextualPropertyMenu(GenericMenu menu, SerializedProperty property)
        {
            if (property.serializedObject != _contextMenuObject || property.propertyPath != _contextMenuPath)
            {
                return;
            }

            // Menu callbacks run after OnGUI, when the drawn SerializedProperty is no longer valid.
            SerializedObject serializedObject = property.serializedObject;
            string propertyPath = property.propertyPath;
            menu.AddItem(SerializedDictionaryStyles.ClearMenuContent, false, () => ClearEntries(serializedObject, propertyPath));
            menu.AddItem(SerializedDictionaryStyles.RemoveInvalidMenuContent, false, () => RemoveInvalidEntries(serializedObject, propertyPath));
        }

        private static void ClearEntries(SerializedObject serializedObject, string propertyPath)
        {
            serializedObject.Update();
            serializedObject.FindProperty(propertyPath).FindPropertyRelative(EntriesField).ClearArray();
            serializedObject.ApplyModifiedProperties();
        }

        private static void RemoveInvalidEntries(SerializedObject serializedObject, string propertyPath)
        {
            serializedObject.Update();
            SerializedDictionaryKeyUtility.RemoveInvalidEntries(serializedObject.FindProperty(propertyPath).FindPropertyRelative(EntriesField));
            serializedObject.ApplyModifiedProperties();
        }

        private SerializedDictionaryTypeInfo GetTypeInfo()
        {
            return SerializedDictionaryTypeUtility.GetTypeInfo(fieldInfo.FieldType);
        }

        private float GetSplitRatio(string propertyPath)
        {
            (Type, string) key = (fieldInfo.DeclaringType, propertyPath);
            if (!_splitRatios.TryGetValue(key, out float ratio))
            {
                ratio = Mathf.Clamp(SessionState.GetFloat(GetSplitSessionKey(key), DefaultSplitRatio), MinSplitRatio, MaxSplitRatio);
                _splitRatios.Add(key, ratio);
            }

            return ratio;
        }

        private static string GetSplitSessionKey((Type, string) key)
        {
            return SplitSessionKeyPrefix + key.Item1.FullName + ":" + key.Item2;
        }

        private static DrawerState GetState(SerializedProperty property)
        {
            // Drawer instances are shared across targets and array elements, so view state lives here instead.
            (EntityId, string) key = (property.serializedObject.targetObject.GetEntityId(), property.propertyPath);
            if (!_states.TryGetValue(key, out DrawerState state))
            {
                state = new DrawerState();
                _states.Add(key, state);
            }

            return state;
        }

        private static int GetPageCount(int count)
        {
            return count > PageSize ? (count + PageSize - 1) / PageSize : 1;
        }

        private static void GetPageRange(DrawerState state, int count, out int start, out int end)
        {
            state.Page = Mathf.Clamp(state.Page, 0, GetPageCount(count) - 1);
            start = state.Page * PageSize;
            end = Mathf.Min(start + PageSize, count);
        }

        private static SerializedKeyStatus[] RentStatuses(int count)
        {
            // Pooled rather than one shared buffer: a value can itself be a dictionary that re-enters this drawer mid-loop.
            while (_statusPool.Count > 0)
            {
                SerializedKeyStatus[] buffer = _statusPool.Pop();
                if (buffer.Length >= count)
                {
                    return buffer;
                }
            }

            return new SerializedKeyStatus[Mathf.NextPowerOfTwo(Mathf.Max(count, 32))];
        }

        private static Rect Inset(Rect rect)
        {
            return new Rect(rect.x + BorderWidth, rect.y + BorderWidth, rect.width - (BorderWidth * 2f), rect.height - (BorderWidth * 2f));
        }

        private sealed class DrawerState
        {
            internal int Page { get; set; }
            internal int HoveredRow { get; set; } = -1;
        }

        private readonly struct ColumnLayout
        {
            internal ColumnLayout(Rect bodyRect, float splitRatio)
            {
                ColumnsX = bodyRect.x + BorderWidth + BodyPadding;
                RemoveX = bodyRect.xMax - HeaderPadding - IconButtonWidth;
                float columnsRight = RemoveX - ColumnGap;
                ColumnsWidth = Mathf.Max(1f, columnsRight - ColumnsX);
                SplitX = Mathf.Round(ColumnsX + (ColumnsWidth * splitRatio));
                KeyX = ColumnsX;
                KeyWidth = Mathf.Max(0f, SplitX - ColumnGap - KeyX);
                ValueX = SplitX + BorderWidth + ColumnGap;
                ValueWidth = Mathf.Max(0f, columnsRight - ValueX);
            }

            internal float ColumnsX { get; }
            internal float ColumnsWidth { get; }
            internal float SplitX { get; }
            internal float KeyX { get; }
            internal float KeyWidth { get; }
            internal float ValueX { get; }
            internal float ValueWidth { get; }
            internal float RemoveX { get; }
        }
    }
}
