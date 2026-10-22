using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace UnityEssentials.Collections.Editor
{
    /// <summary>
    /// Skin-aware colors, cached contents and lazily built styles for the serialized dictionary drawer.
    /// Icons and styles are rebuilt whenever the editor skin changes.
    /// </summary>
    internal static class SerializedDictionaryStyles
    {
        internal static readonly GUIContent KeyColumnContent = new GUIContent("Key");
        internal static readonly GUIContent ValueColumnContent = new GUIContent("Value");
        internal static readonly GUIContent EmptyContent = new GUIContent("Empty");
        internal static readonly GUIContent MultiObjectContent = new GUIContent("Multi-object editing is not supported for dictionaries.");
        internal static readonly GUIContent PreviousPageContent = new GUIContent("◂", "Previous page");
        internal static readonly GUIContent NextPageContent = new GUIContent("▸", "Next page");
        internal static readonly GUIContent ClearMenuContent = new GUIContent("Clear");
        internal static readonly GUIContent RemoveInvalidMenuContent = new GUIContent("Remove Invalid Entries");

        private static readonly GUIContent _badgeContent = new GUIContent();
        private static readonly GUIContent _pageContent = new GUIContent();
        private static readonly Dictionary<int, string> _countTexts = new Dictionary<int, string>();
        private static readonly Dictionary<(int, int), string> _pageTexts = new Dictionary<(int, int), string>();

        private static bool _initialized;
        private static bool _initializedForProSkin;
        private static GUIContent _addContent;
        private static GUIContent _removeContent;
        private static GUIContent _nullKeyContent;
        private static GUIContent _duplicateKeyContent;
        private static GUIStyle _iconButton;
        private static GUIStyle _pageArrow;
        private static GUIStyle _pageLabel;
        private static GUIStyle _badgeLabel;
        private static GUIStyle _columnLabel;
        private static GUIStyle _messageLabel;
        private static GUIStyle _warningIcon;

        internal static Color HeaderBackground
        {
            get
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(0.165f, 0.165f, 0.165f, 1f)
                    : new Color(0.72f, 0.72f, 0.72f, 1f);
            }
        }

        internal static Color BodyBackground
        {
            get
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(0.205f, 0.205f, 0.205f, 1f)
                    : new Color(0.81f, 0.81f, 0.81f, 1f);
            }
        }

        internal static Color ColumnHeaderBackground
        {
            get
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(0.185f, 0.185f, 0.185f, 1f)
                    : new Color(0.765f, 0.765f, 0.765f, 1f);
            }
        }

        internal static Color Border
        {
            get
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(0.10f, 0.10f, 0.10f, 1f)
                    : new Color(0.52f, 0.52f, 0.52f, 1f);
            }
        }

        internal static Color Divider
        {
            get
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(0.14f, 0.14f, 0.14f, 1f)
                    : new Color(0.64f, 0.64f, 0.64f, 1f);
            }
        }

        internal static Color Stripe
        {
            get
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.025f)
                    : new Color(0f, 0f, 0f, 0.035f);
            }
        }

        internal static Color Hover
        {
            get
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(1f, 1f, 1f, 0.045f)
                    : new Color(1f, 1f, 1f, 0.30f);
            }
        }

        internal static Color InvalidOverlay
        {
            get
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(0.86f, 0.22f, 0.20f, 0.16f)
                    : new Color(0.90f, 0.20f, 0.18f, 0.20f);
            }
        }

        internal static Color BadgeBackground
        {
            get
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(0.29f, 0.29f, 0.29f, 1f)
                    : new Color(0.62f, 0.62f, 0.62f, 1f);
            }
        }

        internal static Color DimText
        {
            get
            {
                return EditorGUIUtility.isProSkin
                    ? new Color(0.62f, 0.62f, 0.64f, 1f)
                    : new Color(0.30f, 0.30f, 0.33f, 1f);
            }
        }

        internal static GUIContent AddContent
        {
            get
            {
                EnsureInitialized();
                return _addContent;
            }
        }

        internal static GUIContent RemoveContent
        {
            get
            {
                EnsureInitialized();
                return _removeContent;
            }
        }

        internal static GUIContent NullKeyContent
        {
            get
            {
                EnsureInitialized();
                return _nullKeyContent;
            }
        }

        internal static GUIContent DuplicateKeyContent
        {
            get
            {
                EnsureInitialized();
                return _duplicateKeyContent;
            }
        }

        internal static GUIStyle IconButton
        {
            get
            {
                EnsureInitialized();
                return _iconButton;
            }
        }

        internal static GUIStyle PageArrow
        {
            get
            {
                EnsureInitialized();
                return _pageArrow;
            }
        }

        internal static GUIStyle PageLabel
        {
            get
            {
                EnsureInitialized();
                return _pageLabel;
            }
        }

        internal static GUIStyle BadgeLabel
        {
            get
            {
                EnsureInitialized();
                return _badgeLabel;
            }
        }

        internal static GUIStyle ColumnLabel
        {
            get
            {
                EnsureInitialized();
                return _columnLabel;
            }
        }

        internal static GUIStyle MessageLabel
        {
            get
            {
                EnsureInitialized();
                return _messageLabel;
            }
        }

        internal static GUIStyle WarningIcon
        {
            get
            {
                EnsureInitialized();
                return _warningIcon;
            }
        }

        internal static GUIContent BadgeContent(int count)
        {
            if (!_countTexts.TryGetValue(count, out string text))
            {
                text = count.ToString(CultureInfo.InvariantCulture);
                _countTexts.Add(count, text);
            }

            _badgeContent.text = text;
            return _badgeContent;
        }

        internal static GUIContent PageContent(int page, int pageCount)
        {
            if (!_pageTexts.TryGetValue((page, pageCount), out string text))
            {
                text = (page + 1).ToString(CultureInfo.InvariantCulture) + " / " + pageCount.ToString(CultureInfo.InvariantCulture);
                _pageTexts.Add((page, pageCount), text);
            }

            _pageContent.text = text;
            return _pageContent;
        }

        // EditorStyles is not ready in static constructors, and IconContent resolves the skin's icon
        // variant at call time, so everything here is built on first use and again after a skin switch.
        private static void EnsureInitialized()
        {
            bool proSkin = EditorGUIUtility.isProSkin;
            if (_initialized && _initializedForProSkin == proSkin)
            {
                return;
            }

            _initialized = true;
            _initializedForProSkin = proSkin;

            _addContent = new GUIContent(EditorGUIUtility.IconContent("Toolbar Plus").image, "Add entry");
            _removeContent = new GUIContent(EditorGUIUtility.IconContent("Toolbar Minus").image, "Remove entry");

            Texture warning = EditorGUIUtility.IconContent("console.warnicon.sml").image;
            _nullKeyContent = new GUIContent(warning, "Null key — this entry is ignored at runtime");
            _duplicateKeyContent = new GUIContent(warning, "Duplicate key — this entry is ignored at runtime");

            Color dimText = DimText;

            _iconButton = new GUIStyle(EditorStyles.iconButton);
            _iconButton.fixedWidth = 0f;
            _iconButton.fixedHeight = 0f;
            _iconButton.margin = new RectOffset();
            _iconButton.padding = new RectOffset();
            _iconButton.alignment = TextAnchor.MiddleCenter;

            _pageArrow = new GUIStyle(_iconButton);
            _pageArrow.fontSize = 12;
            _pageArrow.normal.textColor = EditorStyles.label.normal.textColor;
            _pageArrow.hover.textColor = EditorStyles.label.normal.textColor;

            _pageLabel = new GUIStyle(EditorStyles.miniLabel);
            _pageLabel.alignment = TextAnchor.MiddleCenter;
            _pageLabel.padding = new RectOffset();
            _pageLabel.normal.textColor = dimText;

            _badgeLabel = new GUIStyle(EditorStyles.miniLabel);
            _badgeLabel.alignment = TextAnchor.MiddleCenter;
            _badgeLabel.padding = new RectOffset();
            _badgeLabel.normal.textColor = dimText;

            _columnLabel = new GUIStyle(EditorStyles.miniBoldLabel);
            _columnLabel.alignment = TextAnchor.MiddleLeft;
            _columnLabel.padding = new RectOffset();
            _columnLabel.normal.textColor = dimText;

            _messageLabel = new GUIStyle(EditorStyles.label);
            _messageLabel.alignment = TextAnchor.MiddleCenter;
            _messageLabel.fontStyle = FontStyle.Italic;
            _messageLabel.clipping = TextClipping.Clip;
            _messageLabel.normal.textColor = dimText;

            _warningIcon = new GUIStyle(GUIStyle.none);
            _warningIcon.alignment = TextAnchor.MiddleCenter;
            _warningIcon.imagePosition = ImagePosition.ImageOnly;
        }
    }
}
